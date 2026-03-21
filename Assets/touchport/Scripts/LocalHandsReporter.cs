using Unity.Netcode;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;

namespace Anaglyph.Demo
{
    public enum InputMode { Off = -1, Controller = 0, Hand = 1 }

    public class LocalHandsReporter : MonoBehaviour
    {
        [SerializeField] private Transform leftHandTracker;
        [SerializeField] private Transform rightHandTracker;
        [SerializeField] private float reportInterval = 0.033f;

        public InputMode LeftMode  { get; private set; } = InputMode.Off;
        public InputMode RightMode { get; private set; } = InputMode.Off;

        private InputDevice leftDevice;
        private InputDevice rightDevice;
        private XROrigin xrOrigin;
        private float _timer;
        private bool _prevAButton = false;
        private float _debugTimer = 0f;

        private void Start()
        {
            xrOrigin = FindObjectOfType<XROrigin>();
        }

        private void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = reportInterval;

            var netManager = NetworkManager.Singleton;
            if (netManager == null || !netManager.IsConnectedClient) return;

            if (HandsManager.Instance == null)
            {
                Debug.LogWarning("[LocalHandsReporter] HandsManager.Instance is null, skipping");
                return;
            }

            // 手势优先：直接走 OVRPlugin，不依赖 OVRHand GO 或其层级
            bool leftHandTracked  = TryGetOVRHandPos(OVRPlugin.Hand.HandLeft,  out Vector3 leftHandPos);
            bool rightHandTracked = TryGetOVRHandPos(OVRPlugin.Hand.HandRight, out Vector3 rightHandPos);

            // 手势活跃时清掉旧设备引用，确保切回控制器时重新获取
            if (leftHandTracked)
            {
                leftHandTracker.position = leftHandPos;
                leftDevice = default;
            }
            else
            {
                if (!leftDevice.isValid)
                    leftDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
                UpdateTrackerFromDevice(leftDevice, leftHandTracker);
            }

            if (rightHandTracked)
            {
                rightHandTracker.position = rightHandPos;
                rightDevice = default;
            }
            else
            {
                if (!rightDevice.isValid)
                    rightDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
                UpdateTrackerFromDevice(rightDevice, rightHandTracker);
            }

            LeftMode  = leftHandTracked  ? InputMode.Hand : (leftDevice.isValid  ? InputMode.Controller : InputMode.Off);
            RightMode = rightHandTracked ? InputMode.Hand : (rightDevice.isValid ? InputMode.Controller : InputMode.Off);

            // A 键上升沿 → 请求 server toggle
            bool aButton = rightDevice.isValid &&
                           rightDevice.TryGetFeatureValue(CommonUsages.primaryButton, out bool a) && a;
            if (aButton && !_prevAButton)
                HandsManager.Instance.RequestToggleServerRpc();
            _prevAButton = aButton;

            bool leftGrip  = GetGrip(leftDevice);
            bool rightGrip = GetGrip(rightDevice);

            // 每只手单独上报（isTracked=false 时服务器仍收到，用于清除旧数据）
            HandsManager.Instance.ReportHandServerRpc(
                true,  leftHandTracker.position,  LeftMode  != InputMode.Off, LeftMode,  leftGrip);
            HandsManager.Instance.ReportHandServerRpc(
                false, rightHandTracker.position, RightMode != InputMode.Off, RightMode, rightGrip);

            _debugTimer -= Time.deltaTime;
            if (_debugTimer <= 0f)
            {
                _debugTimer = 2f;
                Debug.Log($"[LocalHandsReporter] clientId={NetworkManager.Singleton.LocalClientId} " +
                          $"L={leftHandTracker.position:F2}[{LeftMode}] R={rightHandTracker.position:F2}[{RightMode}]");
            }
        }

        // 直接从 OVRPlugin 读手部 wrist 世界坐标，不依赖场景 GO 层级
        private bool TryGetOVRHandPos(OVRPlugin.Hand hand, out Vector3 worldPos)
        {
            worldPos = Vector3.zero;
            var state = new OVRPlugin.HandState();
            if (!OVRPlugin.GetHandState(OVRPlugin.Step.Render, hand, ref state))
                return false;
            if ((state.Status & OVRPlugin.HandStatus.HandTracked) == 0)
                return false;

            // OVRPlugin 坐标系 Z 与 Unity 相反，flip Z 转换
            var p = state.RootPose.Position;
            Vector3 trackingPos = new Vector3(p.x, p.y, -p.z);
            worldPos = xrOrigin != null
                ? xrOrigin.transform.TransformPoint(trackingPos)
                : trackingPos;
            return true;
        }

        private void UpdateTrackerFromDevice(InputDevice device, Transform tracker)
        {
            if (!device.isValid) return;
            if (!device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 localPos)) return;
            if (!device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion localRot)) return;

            if (xrOrigin != null)
            {
                tracker.position = xrOrigin.transform.TransformPoint(localPos);
                tracker.rotation = xrOrigin.transform.rotation * localRot;
            }
            else
            {
                tracker.position = localPos;
                tracker.rotation = localRot;
            }
        }

        private static bool GetGrip(InputDevice device)
        {
            if (device.isValid && device.TryGetFeatureValue(CommonUsages.gripButton, out bool gripping))
                return gripping;
            return false;
        }
    }
}
