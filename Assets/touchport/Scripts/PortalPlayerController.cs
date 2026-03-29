using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class PortalPlayerController : NetworkBehaviour
{
    public float speed = 5f;
    public float mouseSensitivity = 2f;

    private float xRotation = 0f;
    private Rigidbody rb;
    private Transform camTransform;

    public override void OnNetworkSpawn()
    {
        ApplyLocalAudioListeners();
        if (IsOwner)
            StartCoroutine(ReapplyLocalAudioNextFrame());

        rb = GetComponent<Rigidbody>();
        var cam = GetComponentInChildren<Camera>();
        if (cam) camTransform = cam.transform;

        if (!IsOwner)
        {
            if (cam) cam.enabled = false;
            enabled = false;
            return;
        }

        cam.enabled = true;
        Cursor.lockState = CursorLockMode.Locked;

        // Player1 出生在 WorldB，初始 renderer 状态与 Player0 相反
        if (OwnerClientId == 1)
        {
            var changeLayer = FindObjectOfType<ChangeLayer>();
            if (changeLayer != null)
            {
                changeLayer.ChangeRendererLayerMask("StencilThisWorld", "layer1");
                changeLayer.ChangeRendererLayerMask("StencilPortalWorld", "layer0");
            }
        }
    }

    IEnumerator ReapplyLocalAudioNextFrame()
    {
        yield return null;
        if (IsOwner)
            ApplyLocalAudioListeners();
    }

    /// <summary>本机：全场景只留自己层级下的 Listener；非本机：关掉自己身上的（避免和场景 Main Camera 叠两个）。</summary>
    void ApplyLocalAudioListeners()
    {
        if (IsOwner)
        {
            Transform root = transform;
            foreach (var al in FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                al.enabled = al.transform == root || al.transform.IsChildOf(root);
        }
        else
        {
            foreach (var al in GetComponentsInChildren<AudioListener>(true))
                al.enabled = false;
        }
    }

    void Update()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -80f, 80f);
        if (camTransform)
            camTransform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);

        transform.Rotate(Vector3.up * mouseX);
    }

    void FixedUpdate()
    {
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        Vector3 move = (transform.right * h + transform.forward * v) * speed;
        move.y = rb.linearVelocity.y;
        rb.linearVelocity = move;
    }
}
