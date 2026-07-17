using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 桌面场景专用，对应 XR 场景里的 HandsManager。
/// Server 每帧检测两个玩家距离，靠近时 fire GameManager.FireInteract()，
/// GameManager 不需要区分来源是 XR 还是桌面。
/// 靠近只负责触发开始共享；已经在共享中时靠近不再重复触发/解除。
/// U 键：未共享时切换 ShareUI；已共享时按 U 解除共享。
/// </summary>
public class ScreenPlayerManager : NetworkBehaviour
{
    [SerializeField] private float triggerDistance = 2f;
    [SerializeField] private float holdSeconds = 2f;
    [SerializeField] private float cooldown = 1.5f;

    public static float InterPlayerDistance { get; private set; } = -1f;

    public NetworkVariable<float> SyncedDistance = new(
        -1f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private bool _wasClose = false;
    private float _closeTimer = 0f;
    private float _cooldownTimer = 0f;

    private void Update()
    {
        // U 键（所有客户端都响应，不限 Server）：
        // 已共享 → 解除共享；未共享 → 照常切换 ShareUI 面板
        if (Keyboard.current != null && Keyboard.current[Key.U].wasPressedThisFrame)
        {
            if (SharedState.Instance != null && SharedState.Instance.IsShared)
                GameManager.FireStopSharing();
            else
                ShareUIManager.Instance?.Toggle();
        }

        if (!IsServer) return;

        if (_cooldownTimer > 0f) _cooldownTimer -= Time.deltaTime;

        if (!TryGetBothPlayers(out Transform p0, out Transform p1)) return;

        InterPlayerDistance = Vector3.Distance(p0.position, p1.position);
        SyncedDistance.Value = InterPlayerDistance;
        bool nowClose = InterPlayerDistance < triggerDistance;

        if (nowClose)
        {
            bool alreadyShared = SharedState.Instance != null && SharedState.Instance.IsShared;
            if (alreadyShared)
            {
                // 共享中：靠近不再重复计时/不再触发解除，解除交给 U 键
                _closeTimer = 0f;
            }
            else
            {
                _closeTimer += Time.deltaTime;
                if (_closeTimer >= holdSeconds && _cooldownTimer <= 0f)
                {
                    _cooldownTimer = cooldown;
                    _closeTimer = 0f;
                    GameManager.FireHandshake();
                }
            }
        }
        else
        {
            _closeTimer = 0f;
        }

        _wasClose = nowClose;
    }

    // PortalSpawner 的 fallback 也用这个拿玩家位置
    public static bool TryGetBothPlayers(out Transform p0, out Transform p1)
    {
        p0 = null;
        p1 = null;
        foreach (var no in FindObjectsByType<NetworkObject>(FindObjectsSortMode.None))
        {
            if (!no.IsSpawned || !no.IsPlayerObject) continue;
            if (no.OwnerClientId == 0) p0 = no.transform;
            else if (no.OwnerClientId == 1) p1 = no.transform;
        }
        return p0 != null && p1 != null;
    }
}
