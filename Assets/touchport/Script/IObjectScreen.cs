using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class IObjectScreen : NetworkBehaviour
{
    public ulong gameOwnerId;

    private NetworkVariable<ulong> _networkOwnerId = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private Transform _followTarget;
    private Vector3 _offset;
    private Rigidbody _rb;

    private NetworkVariable<ulong> _heldByClientId = new NetworkVariable<ulong>(
        ulong.MaxValue,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    [Header("F Grab")]
    [SerializeField] private float grabDistance = 1f;
    [SerializeField] private Key grabKey = Key.F;

    public override void OnNetworkSpawn()
    {
        _rb = GetComponent<Rigidbody>();
        if (IsServer) _networkOwnerId.Value = gameOwnerId;
        _networkOwnerId.OnValueChanged += (_, newVal) =>
        {
            gameOwnerId = newVal;
            string layerName = newVal == 0 ? "layer0" : "layer1";
            ChangeLayer.Instance?.ChangeObjectLayer(gameObject, LayerMask.GetMask(layerName));
            Debug.Log($"[touchport] {gameObject.name} layer→{layerName} gameOwnerId→{newVal}");
        };

        // Make movement deterministic when held (network transform will sync position).
        if (_rb != null)
            _rb.isKinematic = true;

        // Ensure transform sync exists for held/follow movement.
        TryEnsureNetworkTransform();

        StartCoroutine(BindSharedStateWhenReady());
    }

    public override void OnNetworkDespawn()
    {
        SharedState.OnSharedChanged -= HandleSharedChanged;
    }

    void HandleSharedChanged(bool isShared)
    {
        ApplyVisibility(isShared);

        // isShared 关掉时：
        // - 如果当前是非主人正在握持（heldBy != gameOwnerId），要同时清掉 held 状态，
        //   否则会出现“跟随已断开但 heldBy 仍然占用，导致对方无法正常 Drop”的状态不一致。
        if (IsServer && !isShared)
        {
            if (_heldByClientId.Value != ulong.MaxValue && _heldByClientId.Value != gameOwnerId)
            {
                _heldByClientId.Value = ulong.MaxValue;
                _followTarget = null;
                return;
            }
        }
    }

    System.Collections.IEnumerator BindSharedStateWhenReady()
    {
        while (SharedState.Instance == null)
            yield return null;
        SharedState.OnSharedChanged += HandleSharedChanged;
        if (IsServer)
            ApplyVisibility(SharedState.Instance.IsShared);
        else
            yield return null;
            ApplyVisibility(SharedState.Instance.IsShared);
    }

    void ApplyVisibility(bool isShared)
    {
        bool iAmNonOwner = NetworkManager.LocalClientId != gameOwnerId;
        bool show = !iAmNonOwner || isShared;
        foreach (var r in GetComponentsInChildren<Renderer>())
            r.enabled = show;
    }

    void Update()
    {
        // Client handles F input only.
        if (IsClient)
            HandleFInput();

        // Server drives the transform while held/following.
        if (!IsServer) return;
        if (_followTarget != null)
            transform.position = _followTarget.position + _offset;
    }

    private void HandleFInput()
    {
        if (SharedState.Instance == null) return;

        if (_heldByClientId.Value != ulong.MaxValue && _heldByClientId.Value != NetworkManager.LocalClientId)
            return;

        var localClientId = NetworkManager.LocalClientId;
        bool canGrab = SharedState.Instance.IsShared || localClientId == gameOwnerId;
        if (!canGrab) return;

        var localPlayer = GetPlayerTransformByClientId(localClientId);
        if (localPlayer == null) return;

        bool inRange = Vector3.Distance(localPlayer.position, transform.position) <= grabDistance;
        if (!inRange) return;

        if (Keyboard.current != null && Keyboard.current[grabKey].wasPressedThisFrame)
            Debug.Log($"[IObjectScreen] {name} F pressed inRange={inRange} canGrab={canGrab} clientId={localClientId} gameOwnerId={gameOwnerId}");
        if (Keyboard.current != null && Keyboard.current[grabKey].wasPressedThisFrame)
        {
            // Not held yet -> grab
            if (_heldByClientId.Value == ulong.MaxValue)
            {
                var offset = transform.position - localPlayer.position;
                GrabServerRpc(offset);
            }
            // Held by me -> drop
            else if (_heldByClientId.Value == localClientId)
            {
                DropServerRpc();
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void GrabServerRpc(Vector3 offset, ServerRpcParams rpcParams = default)
    {
        if (_heldByClientId.Value != ulong.MaxValue) return;
        if (!CanServerClientGrab(rpcParams.Receive.SenderClientId)) return;

        var player = GetPlayerTransformByClientId(rpcParams.Receive.SenderClientId);
        if (player == null) return;

        _heldByClientId.Value = rpcParams.Receive.SenderClientId;
        _followTarget = player;
        _offset = offset;
    }

    [ServerRpc(RequireOwnership = false)]
    private void DropServerRpc(ServerRpcParams rpcParams = default)
    {
        var sender = rpcParams.Receive.SenderClientId;
        if (_heldByClientId.Value != sender) return;

        _heldByClientId.Value = ulong.MaxValue;
        _followTarget = null;
    }

    private bool CanServerClientGrab(ulong clientId)
    {
        if (SharedState.Instance == null) return clientId == gameOwnerId;
        return SharedState.Instance.IsShared || clientId == gameOwnerId;
    }

    private Transform GetPlayerTransformByClientId(ulong clientId)
    {
        foreach (var no in FindObjectsByType<NetworkObject>(FindObjectsSortMode.None))
        {
            if (no.IsSpawned && no.IsPlayerObject && no.OwnerClientId == clientId)
                return no.transform;
        }
        return null;
    }

    // PortalDirectionTrigger 调用
    public void TransferToOther()
    {
        if (!IsServer) return;
        ulong newOwner = gameOwnerId == 0 ? 1UL : 0UL;
        Debug.Log($"[touchport] IObject Collided! {gameObject.name} owner {gameOwnerId}→{newOwner}");
        _networkOwnerId.Value = newOwner;
    }

    /// <summary>
    /// Used when the match ends: stop following so letters stay where they are.
    /// </summary>
    public void ForceDrop()
    {
        if (!IsServer) return;
        _heldByClientId.Value = ulong.MaxValue;
        _followTarget = null;
    }

    private void TryEnsureNetworkTransform()
    {
        // If you already added a NetworkTransform/ClientNetworkTransform in the editor, this does nothing.
        var existing = GetComponent<Unity.Netcode.Components.NetworkTransform>();
        if (existing == null)
            gameObject.AddComponent<Unity.Netcode.Components.NetworkTransform>();
    }
}
