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

        [Header("Hand Tracking (OVRSkeleton)")]
        [SerializeField] private OVRHand leftOVRHand;
        [SerializeField] private OVRHand rightOVRHand;
        [SerializeField] private OVRSkeleton leftOVRSkeleton;
        [SerializeField] private OVRSkeleton rightOVRSkeleton;

        [SerializeField] private float reportInterval = 0.033f;

        public InputMode LeftMode  { get; private set; } = InputMode.Off;
        public InputMode RightMode { get; private set; } = InputMode.Off;

        public Vector3 LeftHandPosition  => leftHandTracker  != null ? leftHandTracker.position  : Vector3.zero;
        public Vector3 RightHandPosition => rightHandTracker != null ? rightHandTracker.position : Vector3.zero;

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

            bool aButton = rightDevice.isValid &&
                           rightDevice.TryGetFeatureValue(CommonUsages.primaryButton, out bool a) && a;
            if (aButton && !_prevAButton)
                HandsManager.Instance.RequestToggleServerRpc();
            _prevAButton = aButton;

            bool leftGrip  = leftHandTracked  ? GetPinch(OVRPlugin.Hand.HandLeft)  : GetGrip(leftDevice);
            bool rightGrip = rightHandTracked ? GetPinch(OVRPlugin.Hand.HandRight) : GetGrip(rightDevice);

            var leftKP  = leftHandTracked  && leftOVRSkeleton  != null ? ReadKeyPoints(leftOVRSkeleton)  : default;
            var rightKP = rightHandTracked && rightOVRSkeleton != null ? ReadKeyPoints(rightOVRSkeleton) : default;

            HandsManager.Instance.ReportHandServerRpc(
                true,  leftHandTracker.position,  LeftMode  != InputMode.Off, LeftMode,  leftGrip,  leftKP);
            HandsManager.Instance.ReportHandServerRpc(
                false, rightHandTracker.position, RightMode != InputMode.Off, RightMode, rightGrip, rightKP);

            _debugTimer -= Time.deltaTime;
            if (_debugTimer <= 0f)
            {
                _debugTimer = 2f;
                Debug.Log($"[LocalHandsReporter] clientId={NetworkManager.Singleton.LocalClientId} " +
                          $"L={leftHandTracker.position:F2}[{LeftMode}] R={rightHandTracker.position:F2}[{RightMode}]");
            }
        }

        private bool TryGetOVRHandPos(OVRPlugin.Hand hand, out Vector3 worldPos, out Quaternion worldRot)
        {
            worldPos = Vector3.zero;
            worldRot = Quaternion.identity;
            var state = new OVRPlugin.HandState();
            if (!OVRPlugin.GetHandState(OVRPlugin.Step.Render, hand, ref state))
                return false;
            if ((state.Status & OVRPlugin.HandStatus.HandTracked) == 0)
                return false;

            var p = state.RootPose.Position;
            Vector3 trackingPos = new Vector3(p.x, p.y, -p.z);

            var q = state.RootPose.Orientation;
            Quaternion trackingRot = new Quaternion(-q.x, -q.y, q.z, q.w);

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

        private static HandsManager.HandKeyPoints ReadKeyPoints(OVRSkeleton sk)
        {
            if (!sk.IsDataValid || sk.Bones == null || sk.Bones.Count < 23)
                return default;

            Vector3 B(OVRSkeleton.BoneId id) => sk.Bones[(int)id].Transform.position;

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

        private static bool GetPinch(OVRPlugin.Hand hand)
        {
            var state = new OVRPlugin.HandState();
            if (!OVRPlugin.GetHandState(OVRPlugin.Step.Render, hand, ref state)) return false;
            if ((state.Status & OVRPlugin.HandStatus.HandTracked) == 0) return false;
            return (state.Pinches & OVRPlugin.HandFingerPinch.Index) != 0;
        }
    }
}
