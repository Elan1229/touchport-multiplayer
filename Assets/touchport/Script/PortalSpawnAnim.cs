using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 挂在 portal prefab 上。spawn 后从 scale=0 缓动到原始 scale，时长 animDuration 秒。
/// </summary>
public class PortalSpawnAnim : NetworkBehaviour
{
    [SerializeField] private float animDuration = 2f;
    [SerializeField] private Vector3 targetScale = Vector3.one;

    public override void OnNetworkSpawn()
    {
        Debug.Log($"[PortalSpawnAnim] OnNetworkSpawn IsServer={IsServer} IsClient={IsClient}");
        transform.localScale = Vector3.zero;
        StartCoroutine(ScaleUp());
    }

    private IEnumerator ScaleUp()
    {
        float t = 0f;
        while (t < animDuration)
        {
            t += Time.deltaTime;
            float p = Mathf.SmoothStep(0f, 1f, t / animDuration);
            transform.localScale = targetScale * p;
            yield return null;
        }
        transform.localScale = targetScale;
    }
}
