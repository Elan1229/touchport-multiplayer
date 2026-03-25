using Unity.Netcode;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;

namespace Anaglyph.Demo
{
    public enum InputMode { Off = -1, Controller = 0, Hand = 1 }

    public class LocalHandsReporter : MonoBehaviour
    {
        [Header("Hand Tracking (OVR Building Block)")]
        [SerializeField] private OVRHand leftOVRHand;
        [SerializeField] private OVRHand rightOVRHand;
        [SerializeField] private OVRSkeleton leftOVRSkeleton;
        [SerializeField] private OVRSkeleton rightOVRSkeleton;

        [SerializeField] private float reportInterval = 0.033f;

        public InputMode LeftMode  { get; private set; } = InputMode.Off;
        public InputMode RightMode { get; private set; } = InputMode.Off;
        public Vector3 LeftHandPosition  { get; private set; }
        public Vector3 RightHandPosition { get; private set; }

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

            bool leftHandTracked  = leftOVRHand  != null && leftOVRHand.IsTracked;
            bool rightHandTracked = rightOVRHand != null && rightOVRHand.IsTracked;

            if (leftHandTracked)
                leftDevice = default;
            else if (!leftDevice.isValid)
                leftDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);

            if (rightHandTracked)
                rightDevice = default;
            else if (!rightDevice.isValid)
                rightDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

            LeftHandPosition  = leftHandTracked  ? leftOVRHand.transform.position  : GetControllerWorldPos(leftDevice);
            RightHandPosition = rightHandTracked ? rightOVRHand.transform.position : GetControllerWorldPos(rightDevice);

            LeftMode  = leftHandTracked  ? InputMode.Hand : (leftDevice.isValid  ? InputMode.Controller : InputMode.Off);
            RightMode = rightHandTracked ? InputMode.Hand : (rightDevice.isValid ? InputMode.Controller : InputMode.Off);

            bool aButton = rightDevice.isValid &&
                           rightDevice.TryGetFeatureValue(CommonUsages.primaryButton, out bool a) && a;
            if (aButton && !_prevAButton)
                HandsManager.Instance.RequestToggleServerRpc();
            _prevAButton = aButton;

            bool leftGrip  = leftHandTracked  ? IsPinching(leftOVRHand)  : GetGrip(leftDevice);
            bool rightGrip = rightHandTracked ? IsPinching(rightOVRHand) : GetGrip(rightDevice);

            var leftKP  = leftHandTracked  && leftOVRSkeleton  != null ? ReadKeyPoints(leftOVRSkeleton)  : default;
            var rightKP = rightHandTracked && rightOVRSkeleton != null ? ReadKeyPoints(rightOVRSkeleton) : default;

            Vector3 leftGrabPos  = leftHandTracked  ? GetIndexTip(leftOVRSkeleton,  LeftHandPosition)  : LeftHandPosition;
            Vector3 rightGrabPos = rightHandTracked ? GetIndexTip(rightOVRSkeleton, RightHandPosition) : RightHandPosition;

            HandsManager.Instance.ReportHandServerRpc(
                true,  LeftHandPosition,  leftGrabPos,  LeftMode  != InputMode.Off, LeftMode,  leftGrip,  leftKP);
            HandsManager.Instance.ReportHandServerRpc(
                false, RightHandPosition, rightGrabPos, RightMode != InputMode.Off, RightMode, rightGrip, rightKP);

            _debugTimer -= Time.deltaTime;
            if (_debugTimer <= 0f)
            {
                _debugTimer = 2f;
                Debug.Log($"[LocalHandsReporter] clientId={NetworkManager.Singleton.LocalClientId} " +
                          $"L={LeftHandPosition:F2}[{LeftMode}] R={RightHandPosition:F2}[{RightMode}]");
            }
        }

        private Vector3 GetControllerWorldPos(InputDevice device)
        {
            if (!device.isValid) return Vector3.zero;
            if (!device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 localPos)) return Vector3.zero;
            return xrOrigin != null ? xrOrigin.transform.TransformPoint(localPos) : localPos;
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

        private static bool GetGrip(InputDevice device)
        {
            if (device.isValid && device.TryGetFeatureValue(CommonUsages.gripButton, out bool gripping))
                return gripping;
            return false;
        }

        private static bool IsPinching(OVRHand hand)
        {
            return hand != null && hand.IsTracked &&
                   hand.GetFingerIsPinching(OVRHand.HandFinger.Index);
        }

        private static Vector3 GetIndexTip(OVRSkeleton sk, Vector3 fallback)
        {
            if (sk != null && sk.IsDataValid && sk.Bones != null &&
                sk.Bones.Count > (int)OVRSkeleton.BoneId.Hand_IndexTip)
                return sk.Bones[(int)OVRSkeleton.BoneId.Hand_IndexTip].Transform.position;
            return fallback;
        }
    }
}
