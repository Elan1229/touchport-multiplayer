using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMoveNetwork : NetworkBehaviour
{
    public float speed = 5f;

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

        ApplyLocalAudioListeners();
        if (IsOwner)
            StartCoroutine(ApplyAudioListenersNextFrame());

        // Stencil 双世界：与 stencil multiplayer 场景一致，P1 本机初始 URP mask 与 P0 相反（物体 layer / Owner 不变）。
        if (IsOwner && OwnerClientId == 1)
        {
            var change = FindFirstObjectByType<ChangeLayer>();
            if (change != null)
            {
                change.ChangeRendererLayerMask("StencilThisWorld", "layer1");
                change.ChangeRendererLayerMask("StencilPortalWorld", "layer0");
            }
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

        float h = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
        float v = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
        if (h == 0f && v == 0f) return;

        MoveServerRpc(new Vector3(h, 0, v) * speed * Time.deltaTime);
    }

    [ServerRpc]
    void MoveServerRpc(Vector3 move)
    {
        transform.Translate(move, Space.World);
    }
}
