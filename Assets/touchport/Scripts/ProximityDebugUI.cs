using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Demo
{
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

        private void Update()
        {
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

            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsConnectedClient)
            {
                debugText.text = "Not connected";
                return;
            }

            ulong localId = nm.LocalClientId;
            _sb.AppendLine($"ClientId={localId}  IsServer={nm.IsServer}");
            _sb.AppendLine("---");

            // 本地手部（直接读 tracker transform）
            Vector3 localL = localLeftHand  != null ? localLeftHand.position  : Vector3.zero;
            Vector3 localR = localRightHand != null ? localRightHand.position : Vector3.zero;
            string modeL = localReporter != null ? FmtMode(localReporter.LeftMode)  : "";
            string modeR = localReporter != null ? FmtMode(localReporter.RightMode) : "";
            _sb.AppendLine($"LocalL : {Fmt(localL)} {modeL}");
            _sb.AppendLine($"LocalR : {Fmt(localR)} {modeR}");

            if (hm == null)
            {
                _sb.AppendLine("HandsManager: null");
                debugText.text = _sb.ToString();
                return;
            }

            // 远端手部（NetworkVariable，server 在 ReportHandServerRpc 里写入）
            Vector3 remoteL, remoteR;
            InputMode remoteLMode, remoteRMode;
            if (localId == 0)
            {
                remoteL = hm.Debug1Left.Value;  remoteLMode = (InputMode)hm.Debug1LeftMode.Value;
                remoteR = hm.Debug1Right.Value; remoteRMode = (InputMode)hm.Debug1RightMode.Value;
                _sb.AppendLine($"RemoteL[1]: {Fmt(remoteL)} {FmtMode(remoteLMode)}");
                _sb.AppendLine($"RemoteR[1]: {Fmt(remoteR)} {FmtMode(remoteRMode)}");
            }
            else
            {
                remoteL = hm.Debug0Left.Value;  remoteLMode = (InputMode)hm.Debug0LeftMode.Value;
                remoteR = hm.Debug0Right.Value; remoteRMode = (InputMode)hm.Debug0RightMode.Value;
                _sb.AppendLine($"RemoteL[0]: {Fmt(remoteL)} {FmtMode(remoteLMode)}");
                _sb.AppendLine($"RemoteR[0]: {Fmt(remoteR)} {FmtMode(remoteRMode)}");
            }

            // 4 对组合中的最短距离
            float minDist = Mathf.Min(
                Vector3.Distance(localL, remoteL),
                Vector3.Distance(localL, remoteR),
                Vector3.Distance(localR, remoteL),
                Vector3.Distance(localR, remoteR));

            bool remoteHasData = remoteL != Vector3.zero || remoteR != Vector3.zero;
            _sb.AppendLine("---");
            _sb.AppendLine(remoteHasData ? $"MinDist  : {minDist:F3} m" : "MinDist  : no remote data");
            _sb.AppendLine($"Threshold: {hm.ProximityMinThreshold:F2} ~ {hm.ProximityThreshold:F2} m");
            _sb.AppendLine($"AreClose : {hm.HandsAreClose.Value}");
            _sb.AppendLine("---");
            _sb.AppendLine($"MergeEvt : {_mergeLog}");

            var sL = new OVRPlugin.HandState();
            var sR = new OVRPlugin.HandState();
            bool ovrL = OVRPlugin.GetHandState(OVRPlugin.Step.Render, OVRPlugin.Hand.HandLeft,  ref sL) && (sL.Status & OVRPlugin.HandStatus.HandTracked) != 0;
            bool ovrR = OVRPlugin.GetHandState(OVRPlugin.Step.Render, OVRPlugin.Hand.HandRight, ref sR) && (sR.Status & OVRPlugin.HandStatus.HandTracked) != 0;
            bool validL = leftSkeleton  != null && leftSkeleton.IsDataValid;
            bool validR = rightSkeleton != null && rightSkeleton.IsDataValid;
            _sb.AppendLine($"OVR L={ovrL} R={ovrR}");
            _sb.AppendLine($"Skel L=valid:{validL} R=valid:{validR}");

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
}
