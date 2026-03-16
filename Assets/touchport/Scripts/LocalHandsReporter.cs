using Unity.Netcode;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;

namespace Anaglyph.Demo
{
    /// <summary>
    /// MonoBehaviour，挂在本地玩家的 Rig/CubeGrabber GO 上。
    /// 每帧读取本地 XR 手柄位置和握持状态，通过 ServerRpc 上报给 HandsManager。
    /// 替换原来的 CubeGrabber（抓取判断逻辑已移到 GrabbableObject 的 server 端）。
    /// </summary>
    public class LocalHandsReporter : MonoBehaviour
    {
        [SerializeField] private Transform leftHandTracker;
        [SerializeField] private Transform rightHandTracker;
        [SerializeField] private float reportInterval = 0.033f; // ~30次/秒，够用且不会撑爆队列

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

            // 重新获取设备（控制器关闭再开时会失效）
            if (!leftDevice.isValid)
                leftDevice  = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            if (!rightDevice.isValid)
                rightDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

            UpdateHandTracker(leftDevice,  leftHandTracker);
            UpdateHandTracker(rightDevice, rightHandTracker);

            // A 键上升沿 → 请求 server toggle（任意一方按都行）
            bool aButton = rightDevice.isValid &&
                           rightDevice.TryGetFeatureValue(CommonUsages.primaryButton, out bool a) && a;
            if (aButton && !_prevAButton)
                HandsManager.Instance.RequestToggleServerRpc();
            _prevAButton = aButton;

            // 两个设备都无效时不上报，避免 (0,0,0) 触发假阳性
            if (!leftDevice.isValid && !rightDevice.isValid) return;

            bool leftGrip  = GetGrip(leftDevice);
            bool rightGrip = GetGrip(rightDevice);

            HandsManager.Instance.ReportHandsServerRpc(
                leftHandTracker.position,
                rightHandTracker.position,
                leftGrip,
                rightGrip);

            _debugTimer -= Time.deltaTime;
            if (_debugTimer <= 0f)
            {
                _debugTimer = 2f;
                Debug.Log($"[LocalHandsReporter] clientId={NetworkManager.Singleton.LocalClientId} " +
                          $"L={leftHandTracker.position:F2} R={rightHandTracker.position:F2} " +
                          $"leftGrip={leftGrip} rightGrip={rightGrip}");
            }
        }

        private void UpdateHandTracker(InputDevice device, Transform tracker)
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
