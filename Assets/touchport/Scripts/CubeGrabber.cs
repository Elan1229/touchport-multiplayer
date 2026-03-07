using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;

namespace Anaglyph.Demo
{
    /// <summary>
    /// Detects when either hand closes (grip) near the SharedCube and grabs it.
    /// Uses raw XR input — no XR Interaction Toolkit dependency needed.
    /// </summary>
    public class CubeGrabber : MonoBehaviour
    {
        [SerializeField] private Transform leftHandTracker;
        [SerializeField] private Transform rightHandTracker;
        [SerializeField] private float grabRadius = 0.15f;

        private NetworkedCube grabbedCube;
        private Transform grabbingHand;

        private InputDevice leftDevice;
        private InputDevice rightDevice;
        private bool leftWasGripping;
        private bool rightWasGripping;

        private XROrigin xrOrigin;

        private void Start()
        {
            xrOrigin = FindObjectOfType<XROrigin>();
        }

        private void Update()
        {
            // Re-acquire devices if lost (e.g. controller turned off and back on)
            if (!leftDevice.isValid)
                leftDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            if (!rightDevice.isValid)
                rightDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

            UpdateHandTracker(leftDevice, leftHandTracker);
            UpdateHandTracker(rightDevice, rightHandTracker);

            bool leftGrip = GetGrip(leftDevice);
            bool rightGrip = GetGrip(rightDevice);

            HandleHand(leftHandTracker, leftGrip, leftWasGripping);
            HandleHand(rightHandTracker, rightGrip, rightWasGripping);

            leftWasGripping = leftGrip;
            rightWasGripping = rightGrip;
        }

        // Convert tracking-space pose to world space using the XROrigin transform
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

        private void HandleHand(Transform hand, bool isGripping, bool wasGripping)
        {
            bool gripPressed = isGripping && !wasGripping;
            bool gripReleased = !isGripping && wasGripping;

            // Try to grab something
            if (gripPressed && grabbedCube == null)
            {
                var cube = FindNearestCubeInRange(hand.position);
                if (cube != null)
                {
                    grabbedCube = cube;
                    grabbingHand = hand;
                    cube.OnGrabbed(hand);
                }
            }

            // Release
            if (gripReleased && grabbedCube != null && grabbingHand == hand)
            {
                grabbedCube.OnReleased();
                grabbedCube = null;
                grabbingHand = null;
            }
        }

        // Use transform-based distance check instead of Physics.OverlapSphere.
        // NetworkTransform updates transform.position directly; the physics collider
        // only catches up at the next FixedUpdate, so physics queries can miss the cube.
        private NetworkedCube FindNearestCubeInRange(Vector3 handPos)
        {
            foreach (var cube in FindObjectsByType<NetworkedCube>(FindObjectsSortMode.None))
            {
                var c = cube.transform.position;
                var h = cube.transform.localScale * 0.5f; // half-extents

                // Nearest point on the cube's AABB to the hand
                var nearest = new Vector3(
                    Mathf.Clamp(handPos.x, c.x - h.x, c.x + h.x),
                    Mathf.Clamp(handPos.y, c.y - h.y, c.y + h.y),
                    Mathf.Clamp(handPos.z, c.z - h.z, c.z + h.z));

                if (Vector3.Distance(handPos, nearest) < grabRadius)
                    return cube;
            }
            return null;
        }
    }
}
