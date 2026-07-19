using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 挂在 portal prefab 上。spawn 后从 scale=0 缓动到原始 scale，时长 animDuration 秒。
/// 关门走 CloseClientRpc：所有端一起反向缩回（closeDuration 秒），随后由 server despawn
/// （见 PortalSpawner.ClosePortal）。
/// </summary>
public class PortalSpawnAnim : NetworkBehaviour
{
    [SerializeField] private float animDuration = 2f;
    [Tooltip("关门反向缩回的时长。换梦触发的关门要在新梦稳定前完成，" +
             "建议 ≤ ChangeDreamByGift 的 dissolve 时长（默认 1.5s）。")]
    [SerializeField] private float closeDuration = 2.5f;
    [SerializeField] private Vector3 targetScale = Vector3.one;

    public float CloseDuration => closeDuration;

    private Coroutine scaleCo;

    public override void OnNetworkSpawn()
    {
        Debug.Log($"[PortalSpawnAnim] OnNetworkSpawn IsServer={IsServer} IsClient={IsClient}");
        transform.localScale = Vector3.zero;
        scaleCo = StartCoroutine(ScaleUp());
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

    // server 调一次，所有端（含 host）各自本地播放缩回动画。开门动画还没放完也能接手（从当前 scale 缩）。
    [ClientRpc]
    public void CloseClientRpc()
    {
        if (scaleCo != null) StopCoroutine(scaleCo);
        StartCoroutine(ScaleDown());
    }

    private IEnumerator ScaleDown()
    {
        Vector3 from = transform.localScale;
        float t = 0f;
        while (t < closeDuration)
        {
            t += Time.deltaTime;
            float p = Mathf.SmoothStep(0f, 1f, t / closeDuration);
            transform.localScale = from * (1f - p);
            yield return null;
        }
        transform.localScale = Vector3.zero;
    }
}
