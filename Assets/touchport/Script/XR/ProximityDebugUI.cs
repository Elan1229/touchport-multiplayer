using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// World-space debug panel，挂在 DebugCanvas GO 上。
/// 跟随相机偏右显示，不遮挡主视野。
/// 显示：本地/远端手部世界坐标、最小距离、阈值、HandsAreClose 状态、merge 事件记录。
/// </summary>
public class ProximityDebugUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TextMeshProUGUI debugText;
    [SerializeField] private Transform localLeftHand;
    [SerializeField] private Transform localRightHand;
    [SerializeField] private LocalHandsReporter localReporter;
    [SerializeField] private OVRSkeleton leftSkeleton;
    [SerializeField] private OVRSkeleton rightSkeleton;

    [Header("Camera Follow")]
    [SerializeField] private Transform followCamera;
    [SerializeField] private Vector3 followOffset = new Vector3(0.5f, -0.1f, 1.0f);

    [Header("Refresh Interval (s)")]
    [SerializeField] private float refreshInterval = 0.1f;

    private bool _subscribed = false;
    private string _mergeLog = "none";
    private float _refreshTimer = 0f;
    private readonly StringBuilder _sb = new();

    private CanvasGroup _canvasGroup;
    private bool _visible = true;
    private bool _prevBButton = false;
    private InputDevice _rightDevice;

    private void Awake()
    {
        _canvasGroup = GetComponent<CanvasGroup>();
        if (_canvasGroup == null)
            _canvasGroup = gameObject.AddComponent<CanvasGroup>();
        ApplyVisibility();
    }

    private void ApplyVisibility()
    {
        _canvasGroup.alpha = _visible ? 1f : 0f;
        _canvasGroup.interactable = _visible;
        _canvasGroup.blocksRaycasts = _visible;
    }

    private void Update()
    {
        // B键检测（纯本地，不走网络）
        if (!_rightDevice.isValid)
            _rightDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

        bool bButton = _rightDevice.isValid &&
                       _rightDevice.TryGetFeatureValue(CommonUsages.secondaryButton, out bool b) && b;
        if (bButton && !_prevBButton)
        {
            _visible = !_visible;
            ApplyVisibility();
        }
        _prevBButton = bButton;

        if (followCamera != null)
        {
            transform.position = followCamera.TransformPoint(followOffset);
            transform.rotation = followCamera.rotation;
        }

        var hm = HandsManager.Instance;
        if (!_subscribed && hm != null && hm.IsSpawned)
        {
            hm.HandsAreClose.OnValueChanged += OnMergeChanged;
            _subscribed = true;
        }

        _refreshTimer -= Time.deltaTime;
        if (_refreshTimer > 0f) return;
        _refreshTimer = refreshInterval;

        RefreshText(hm);
    }

    private void RefreshText(HandsManager hm)
    {
        if (debugText == null) return;
        _sb.Clear();

        _sb.AppendLine("Press B (right controller) to show/hide Debug UI");
        _sb.AppendLine("---");

        var nm = NetworkManager.Singleton;
        bool connected = nm != null && nm.IsConnectedClient;
        ulong localId  = connected ? nm.LocalClientId : 0;

        _sb.AppendLine($"ClientId : {(connected ? localId.ToString() : "--")}  IsServer={( connected ? nm.IsServer.ToString() : "--")}");
        _sb.AppendLine("---");

        // 本地手部
        Vector3 localL = localLeftHand  != null ? localLeftHand.position  : Vector3.zero;
        Vector3 localR = localRightHand != null ? localRightHand.position : Vector3.zero;
        string modeL = localReporter != null ? FmtMode(localReporter.LeftMode)  : "--";
        string modeR = localReporter != null ? FmtMode(localReporter.RightMode) : "--";
        _sb.AppendLine($"LocalL   : {Fmt(localL)} {modeL}");
        _sb.AppendLine($"LocalR   : {Fmt(localR)} {modeR}");

        // 远端手部
        Vector3 remoteL = Vector3.zero, remoteR = Vector3.zero;
        if (connected && hm != null)
        {
            InputMode remoteLMode, remoteRMode;
            if (localId == 0)
            {
                remoteL = hm.Debug1Left.Value;  remoteLMode = (InputMode)hm.Debug1LeftMode.Value;
                remoteR = hm.Debug1Right.Value; remoteRMode = (InputMode)hm.Debug1RightMode.Value;
            }
            else
            {
                remoteL = hm.Debug0Left.Value;  remoteLMode = (InputMode)hm.Debug0LeftMode.Value;
                remoteR = hm.Debug0Right.Value; remoteRMode = (InputMode)hm.Debug0RightMode.Value;
            }
            _sb.AppendLine($"RemoteL  : {Fmt(remoteL)} {FmtMode(remoteLMode)}");
            _sb.AppendLine($"RemoteR  : {Fmt(remoteR)} {FmtMode(remoteRMode)}");
        }
        else
        {
            _sb.AppendLine("RemoteL  : --");
            _sb.AppendLine("RemoteR  : --");
        }

        _sb.AppendLine("---");

        // 距离和阈值
        if (connected && hm != null)
        {
            bool remoteHasData = remoteL != Vector3.zero || remoteR != Vector3.zero;
            float minDist = Mathf.Min(
                Vector3.Distance(localL, remoteL),
                Vector3.Distance(localL, remoteR),
                Vector3.Distance(localR, remoteL),
                Vector3.Distance(localR, remoteR));
            _sb.AppendLine(remoteHasData ? $"MinDist  : {minDist:F3} m" : "MinDist  : --");
            _sb.AppendLine($"Threshold: {hm.ProximityMinThreshold:F2} ~ {hm.ProximityThreshold:F2} m");
            _sb.AppendLine($"AreClose : {hm.HandsAreClose.Value}");
            _sb.AppendLine($"IsShared : {(SharedState.Instance != null ? SharedState.Instance.IsShared.Value.ToString() : "--")}");
        }
        else
        {
            _sb.AppendLine("MinDist  : --");
            _sb.AppendLine("Threshold: --");
            _sb.AppendLine("AreClose : --");
            _sb.AppendLine("IsShared : --");
        }

        _sb.AppendLine("---");
        _sb.AppendLine($"MergeEvt : {(connected ? _mergeLog : "--")}");

        // OVR 手追踪状态
        var sL = new OVRPlugin.HandState();
        var sR = new OVRPlugin.HandState();
        bool ovrL  = OVRPlugin.GetHandState(OVRPlugin.Step.Render, OVRPlugin.Hand.HandLeft,  ref sL) && (sL.Status & OVRPlugin.HandStatus.HandTracked) != 0;
        bool ovrR  = OVRPlugin.GetHandState(OVRPlugin.Step.Render, OVRPlugin.Hand.HandRight, ref sR) && (sR.Status & OVRPlugin.HandStatus.HandTracked) != 0;
        bool validL = leftSkeleton  != null && leftSkeleton.IsDataValid;
        bool validR = rightSkeleton != null && rightSkeleton.IsDataValid;
        _sb.AppendLine($"OVR      : L={ovrL} R={ovrR}");
        _sb.AppendLine($"Skeleton : L={validL} R={validR}");

        debugText.text = _sb.ToString();
    }

    private void OnMergeChanged(bool oldVal, bool newVal)
    {
        _mergeLog = $"{oldVal}->{newVal} @{Time.time:F1}s";
        Debug.Log($"[touchport] MergeEvent {oldVal}->{newVal} clientId={NetworkManager.Singleton.LocalClientId}");
    }

    private void OnDestroy()
    {
        if (_subscribed && HandsManager.Instance != null)
            HandsManager.Instance.HandsAreClose.OnValueChanged -= OnMergeChanged;
    }

    private static string Fmt(Vector3 v) => $"({v.x:F2},{v.y:F2},{v.z:F2})";

    private static string FmtMode(InputMode m) => m switch
    {
        InputMode.Hand       => "[hand]",
        InputMode.Controller => "[ctrl]",
        _                    => "[off]"
    };
}
