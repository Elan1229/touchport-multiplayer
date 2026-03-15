using Unity.Netcode;
using UnityEngine;

public class PlayerMove : NetworkBehaviour
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

        // 摄像机：只给本地玩家开启
        var cam = GetComponentInChildren<Camera>(true);
        if (cam != null) cam.enabled = IsOwner;
    }

    void Update()
    {
        if (!IsOwner) return;

        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        if (h == 0f && v == 0f) return;

        MoveServerRpc(new Vector3(h, 0, v) * speed * Time.deltaTime);
    }

    [ServerRpc]
    void MoveServerRpc(Vector3 move)
    {
        transform.Translate(move, Space.World);
    }
}
