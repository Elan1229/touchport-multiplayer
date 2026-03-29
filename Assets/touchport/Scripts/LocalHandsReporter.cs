using Unity.Netcode;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;

namespace Anaglyph.Demo
{
    // 本机输入模式：没有设备 / 手柄 / 手追踪
    public enum InputMode { Off = -1, Controller = 0, Hand = 1 }

    /// <summary>
    /// 只跑在本机客户端。
    /// 每帧采集本机双手的位置、追踪状态、握持/捏合手势、A键，
    /// 打包发给服务端的 HandsManager。
    /// 不做任何判断，只管采集和上报。
    /// </summary>
    public class LocalHandsReporter : MonoBehaviour
    {
        // 挂在手上的 Transform，用来记录手的世界坐标
        [SerializeField] private Transform leftHandTracker;
        [SerializeField] private Transform rightHandTracker;

        [Header("Hand Tracking (OVRSkeleton)")]
        // OVR 手追踪组件，有手追踪时用这个拿位置和骨骼
        [SerializeField] private OVRHand leftOVRHand;
        [SerializeField] private OVRHand rightOVRHand;
        [SerializeField] private OVRSkeleton leftOVRSkeleton;
        [SerializeField] private OVRSkeleton rightOVRSkeleton;

        // 上报频率，默认 30Hz（0.033s 一次）
        [SerializeField] private float reportInterval = 0.033f;

        // 当前左右手的输入模式，HandJointVisualizer 和 ProximityDebugUI 会读这个来显示
        public InputMode LeftMode  { get; private set; } = InputMode.Off;
        public InputMode RightMode { get; private set; } = InputMode.Off;

        // 当前左右手的世界坐标，给 HandJointVisualizer 的 fallback 显示用
        public Vector3 LeftHandPosition  => leftHandTracker  != null ? leftHandTracker.position  : Vector3.zero;
        public Vector3 RightHandPosition => rightHandTracker != null ? rightHandTracker.position : Vector3.zero;

        private InputDevice leftDevice;   // 左手柄设备
        private InputDevice rightDevice;  // 右手柄设备
        private XROrigin xrOrigin;        // 用来把 tracking space 坐标转成世界坐标
        private float _timer;             // 上报计时器
        private bool _prevAButton = false; // 上一帧 A 键状态，用来检测按下瞬间
        private float _debugTimer = 0f;   // debug log 计时器，防止每帧刷屏

        private void Start()
        {
            xrOrigin = FindObjectOfType<XROrigin>();
        }

        private void Update()
        {
            // 按 reportInterval 节流，不是每帧都上报
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

            // 优先用手追踪，失败了用手柄
            bool leftHandTracked  = TryGetOVRHandPos(OVRPlugin.Hand.HandLeft,  out Vector3 leftHandPos,  out Quaternion leftHandRot);
            bool rightHandTracked = TryGetOVRHandPos(OVRPlugin.Hand.HandRight, out Vector3 rightHandPos, out Quaternion rightHandRot);

            if (leftHandTracked)
            {
                leftHandTracker.position = leftHandPos;
                if (leftOVRHand != null)
                {
                    leftOVRHand.transform.position = leftHandPos;
                    leftOVRHand.transform.rotation = leftHandRot;
                }
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
                if (rightOVRHand != null)
                {
                    rightOVRHand.transform.position = rightHandPos;
                    rightOVRHand.transform.rotation = rightHandRot;
                }
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

            // A键（右手柄 primaryButton），只上报按下的那一帧，不持续上报
            bool aButton = rightDevice.isValid &&
                           rightDevice.TryGetFeatureValue(CommonUsages.primaryButton, out bool a) && a;
            bool aPressed = aButton && !_prevAButton;
            _prevAButton = aButton;

            // 握持/捏合：手追踪用捏合，手柄用 grip 键
            bool leftGrip  = leftHandTracked  ? GetPinch(OVRPlugin.Hand.HandLeft)  : GetGrip(leftDevice);
            bool rightGrip = rightHandTracked ? GetPinch(OVRPlugin.Hand.HandRight) : GetGrip(rightDevice);

            // 手指关键点，有手追踪才有，用于远端手部可视化
            var leftKP  = leftHandTracked  && leftOVRSkeleton  != null ? ReadKeyPoints(leftOVRSkeleton)  : default;
            var rightKP = rightHandTracked && rightOVRSkeleton != null ? ReadKeyPoints(rightOVRSkeleton) : default;

            // 把所有采集到的数据一次性发给服务端，左右手分两条
            HandsManager.Instance.ReportHandServerRpc(
                true,  leftHandTracker.position,  LeftMode  != InputMode.Off, LeftMode,  leftGrip,  leftKP,  false);
            HandsManager.Instance.ReportHandServerRpc(
                false, rightHandTracker.position, RightMode != InputMode.Off, RightMode, rightGrip, rightKP, aPressed);

            _debugTimer -= Time.deltaTime;
            if (_debugTimer <= 0f)
            {
                _debugTimer = 2f;
                Debug.Log($"[LocalHandsReporter] clientId={NetworkManager.Singleton.LocalClientId} " +
                          $"L={leftHandTracker.position:F2}[{LeftMode}] R={rightHandTracker.position:F2}[{RightMode}]");
            }
        }

        // OVR 手追踪：从 OVRPlugin 拿手的世界坐标和朝向，失败返回 false
        private bool TryGetOVRHandPos(OVRPlugin.Hand hand, out Vector3 worldPos, out Quaternion worldRot)
        {
            worldPos = Vector3.zero;
            worldRot = Quaternion.identity;
            var state = new OVRPlugin.HandState();
            if (!OVRPlugin.GetHandState(OVRPlugin.Step.Render, hand, ref state))
                return false;
            if ((state.Status & OVRPlugin.HandStatus.HandTracked) == 0)
                return false;

            // OVR 坐标系和 Unity 坐标系有差异，Z轴需要取反
            var p = state.RootPose.Position;
            Vector3 trackingPos = new Vector3(p.x, p.y, -p.z);

            var q = state.RootPose.Orientation;
            Quaternion trackingRot = new Quaternion(-q.x, -q.y, q.z, q.w);

            // 转成世界坐标（XR Rig 可能有偏移）
            if (xrOrigin != null)
            {
                worldPos = xrOrigin.transform.TransformPoint(trackingPos);
                worldRot = xrOrigin.transform.rotation * trackingRot;
            }
            else
            {
                worldPos = trackingPos;
                worldRot = trackingRot;
            }
            return true;
        }

        // 从 OVRSkeleton 提取 7 个关键点（手腕、手掌、5个指尖），用于远端手部可视化
        private static HandsManager.HandKeyPoints ReadKeyPoints(OVRSkeleton sk)
        {
            if (!sk.IsDataValid || sk.Bones == null || sk.Bones.Count < 23)
                return default;

            Vector3 B(OVRSkeleton.BoneId id) => sk.Bones[(int)id].Transform.position;

            // 手掌中心取四个掌骨根部的平均位置
            var palm = (B(OVRSkeleton.BoneId.Hand_Index1)  +
                        B(OVRSkeleton.BoneId.Hand_Middle1) +
                        B(OVRSkeleton.BoneId.Hand_Ring1)   +
                        B(OVRSkeleton.BoneId.Hand_Pinky0)) * 0.25f;

            return new HandsManager.HandKeyPoints
            {
                wrist     = B(OVRSkeleton.BoneId.Hand_WristRoot),
                palm      = palm,
                thumbTip  = B(OVRSkeleton.BoneId.Hand_ThumbTip),
                indexTip  = B(OVRSkeleton.BoneId.Hand_IndexTip),
                middleTip = B(OVRSkeleton.BoneId.Hand_MiddleTip),
                ringTip   = B(OVRSkeleton.BoneId.Hand_RingTip),
                pinkyTip  = B(OVRSkeleton.BoneId.Hand_PinkyTip)
            };
        }

        // 手柄模式：从 XR 设备拿位置和朝向，更新 tracker Transform
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

        // 手柄 grip 键是否按下
        private static bool GetGrip(InputDevice device)
        {
            if (device.isValid && device.TryGetFeatureValue(CommonUsages.gripButton, out bool gripping))
                return gripping;
            return false;
        }

        // 手追踪模式下，用食指捏合代替 grip
        private static bool GetPinch(OVRPlugin.Hand hand)
        {
            var state = new OVRPlugin.HandState();
            if (!OVRPlugin.GetHandState(OVRPlugin.Step.Render, hand, ref state)) return false;
            if ((state.Status & OVRPlugin.HandStatus.HandTracked) == 0) return false;
            return (state.Pinches & OVRPlugin.HandFingerPinch.Index) != 0;
        }
    }
}
