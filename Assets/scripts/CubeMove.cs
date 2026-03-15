using Unity.Netcode;
using UnityEngine;

public class CubeMove : NetworkBehaviour
{
    public float speed = 5f;

    public override void OnNetworkSpawn()
    {
        Debug.Log($"玩家 {OwnerClientId} 出生了！是不是我自己？{IsOwner}");
    }

    void Update()
    {
        // 不是自己的角色，不控制
        if (!IsOwner) return;

        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        Vector3 move = new Vector3(h, 0, v) * speed * Time.deltaTime;

        // 请求服务器移动
        MoveServerRpc(move);
    }

    [ServerRpc]
    void MoveServerRpc(Vector3 move)
    {
        // 服务器上执行移动，位置会自动同步给所有客户端
        transform.Translate(move);
    }
}