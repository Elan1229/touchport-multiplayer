using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMoveNetwork : NetworkBehaviour
{
    public float speed = 5f;
    [Tooltip("鼠标转向灵敏度（可在 Inspector 拖）。鼠标 delta 已是每帧位移，不再乘 deltaTime。")]
    public float lookSensitivity = 0.2f;

    private Transform camT;
    private float yaw;
    private float pitch;

    static readonly Color[] PlayerColors =
    {
        Color.red,
        new Color(0.2f, 0.5f, 1f),
        Color.green,
        Color.yellow,
    };

    public override void OnNetworkSpawn()
    {
        var col = PlayerColors[OwnerClientId % (ulong)PlayerColors.Length];

        // 旧结构（根节点是 Cube）：直接设根节点颜色
        var r = GetComponent<Renderer>();
        if (r != null)
        {
            r.material.SetColor("_BaseColor", col);
            r.material.SetColor("_Color", col);
        }
        else
        {
            // 新结构（根节点是空节点）：设所有子 Renderer
            foreach (var cr in GetComponentsInChildren<Renderer>(true))
            {
                cr.material.SetColor("_BaseColor", col);
                cr.material.SetColor("_Color", col);
            }
        }

        // 形状切换：有 CubeVisual/SphereVisual 子物体时才执行
        var cubeVisual = transform.Find("CubeVisual");
        var sphereVisual = transform.Find("SphereVisual");
        if (cubeVisual != null)  cubeVisual.gameObject.SetActive(OwnerClientId == 0);
        if (sphereVisual != null) sphereVisual.gameObject.SetActive(OwnerClientId != 0);

        // 摄像机：只给本地玩家
        var cam = GetComponentInChildren<Camera>(true);
        if (cam != null) cam.enabled = IsOwner;
        camT = cam != null ? cam.transform : null;
        yaw = transform.eulerAngles.y;
        if (IsOwner) Cursor.lockState = CursorLockMode.Locked;

        ApplyLocalAudioListeners();
        if (IsOwner)
            StartCoroutine(ApplyAudioListenersNextFrame());

        // Stencil 双世界：出生时视角设为自己的老家世界（P0 与场景默认一致，P1 等价于旧的"反转"）。
        if (IsOwner)
        {
            var change = ChangeLayer.Instance != null ? ChangeLayer.Instance : FindFirstObjectByType<ChangeLayer>();
            change?.ViewHomeWorld();
        }
    }

    IEnumerator ApplyAudioListenersNextFrame()
    {
        yield return null;
        if (IsOwner)
            ApplyLocalAudioListeners();
    }

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
        if (!IsOwner) return;
        var kb = Keyboard.current;
        if (kb == null) return;

        // 第一人称转头：鼠标 X 转身体(yaw)、鼠标 Y 抬头低头(相机 pitch，纯本地视角)
        var md = Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
        yaw  += md.x * lookSensitivity;
        pitch = Mathf.Clamp(pitch - md.y * lookSensitivity, -80f, 80f);
        if (camT != null) camT.localRotation = Quaternion.Euler(pitch, 0f, 0f);

        // 朝向相关的 WASD 移动
        float h = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
        float v = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
        var face = Quaternion.Euler(0f, yaw, 0f);
        Vector3 move = (face * Vector3.right * h + face * Vector3.forward * v) * speed * Time.deltaTime;

        // server 权威：把朝向和位移交给 server（NetworkTransform 再同步给所有人）
        if (md.sqrMagnitude > 0f || h != 0f || v != 0f)
            MoveLookServerRpc(yaw, move);
    }

    [ServerRpc]
    void MoveLookServerRpc(float yawDeg, Vector3 move)
    {
        transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);
        transform.Translate(move, Space.World);
    }
}
