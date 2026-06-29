using UnityEngine;

public struct PortalHandState
{
    public bool     isTracked;
    public bool     isScaleIntent;
    public Vector3  worldPosition;
    public Collider collider;
}

public abstract class HandInputSource : MonoBehaviour
{
    public abstract PortalHandState LeftHand  { get; }
    public abstract PortalHandState RightHand { get; }
}