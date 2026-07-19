using ithappy.Animals_FREE;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 挂在猫上，配合 IObjectXR + CreatureMover。
/// 被抓时禁用 CharacterController（由 NetworkTransform 同步位置）。
/// 落地后跑向 runTarget，到达后 idle。没有 target 则沿当前朝向跑 runDistance 米。
/// </summary>
[RequireComponent(typeof(IObjectXR))]
[RequireComponent(typeof(CreatureMover))]
[RequireComponent(typeof(CharacterController))]
public class CatMove : NetworkBehaviour
{
    [SerializeField] private float runDistance = 2f;
    [SerializeField] private float arriveThreshold = 0.3f;

    [Header("Target")]
    [SerializeField] private Transform runTarget;

    // 代码里动态设置目标
    public void SetRunTarget(Transform t) => runTarget = t;
    public Transform RunTarget => runTarget;

    private IObjectXR _grab;
    private CreatureMover _mover;
    private CharacterController _cc;

    private bool _wasHeld = false;
    private bool _shouldRunOnLand = false;
    private bool _isRunning = false;
    private Vector3 _runStartPos;
    private Vector3 _runDirection;

    public override void OnNetworkSpawn()
    {
        _grab = GetComponent<IObjectXR>();
        _mover = GetComponent<CreatureMover>();
        _cc = GetComponent<CharacterController>();

        if (IsServer && runTarget == null)
        {
            ulong opponentId = PlayerWorld.OtherPlayer(_grab.ownerPlayerId);
            foreach (var no in FindObjectsByType<NetworkObject>(FindObjectsSortMode.None))
                if (no.IsSpawned && no.IsPlayerObject && no.OwnerClientId == opponentId)
                { runTarget = no.transform; break; }
        }
    }

    private void Update()
    {
        if (!IsServer) return;

        bool isHeld = _grab.IsBeingHeld;

        // 刚被抓起
        if (isHeld && !_wasHeld)
        {
            _isRunning = false;
            _shouldRunOnLand = false;
            _cc.enabled = false;
        }

        // 刚被放下
        if (!isHeld && _wasHeld)
        {
            _cc.enabled = true;
            if (runTarget != null)
            {
                Vector3 toTarget = runTarget.position - transform.position;
                toTarget.y = 0f;
                _runDirection = toTarget.sqrMagnitude > 0.001f ? toTarget.normalized : transform.forward;
            }
            else
            {
                _runDirection = new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;
                if (_runDirection == Vector3.zero) _runDirection = Vector3.forward;
            }
            _shouldRunOnLand = true;
        }

        _wasHeld = isHeld;

        // 被抓着：idle 动画
        if (isHeld)
        {
            _mover.SetInput(Vector2.zero, transform.position + transform.forward, false, false);
            return;
        }

        // 落地检测
        if (_shouldRunOnLand && _cc.isGrounded)
        {
            _shouldRunOnLand = false;
            _isRunning = true;
            _runStartPos = transform.position;
        }

        // 跑步
        if (_isRunning)
        {
            bool arrived;
            if (runTarget != null)
            {
                Vector3 toTarget = runTarget.position - transform.position;
                toTarget.y = 0f;
                arrived = toTarget.magnitude < arriveThreshold;
                if (!arrived)
                    _runDirection = toTarget.sqrMagnitude > 0.001f ? toTarget.normalized : _runDirection;
            }
            else
            {
                float traveled = Vector2.Distance(
                    new Vector2(transform.position.x, transform.position.z),
                    new Vector2(_runStartPos.x, _runStartPos.z));
                arrived = traveled >= runDistance;
            }

            if (!arrived)
            {
                Vector3 lookTarget = transform.position + _runDirection * 5f;
                _mover.SetInput(Vector2.up, lookTarget, false, false);
            }
            else
            {
                _isRunning = false;
                _mover.SetInput(Vector2.zero, transform.position + transform.forward, false, false);
            }
        }
        else
        {
            _mover.SetInput(Vector2.zero, transform.position + transform.forward, false, false);
        }
    }
}
