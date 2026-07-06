using UnityEngine;

public struct PortalHandState
{
    public bool     isTracked;
    public bool     isScaleIntent;       // 持续按住（留给 Quest grip 等连续信号）
    public bool     isScaleJustPressed;  // 按下瞬间（toggle 用）
    public Vector3  worldPosition;
    public Collider collider;
}

public abstract class HandInputSource : MonoBehaviour
{
    public abstract PortalHandState LeftHand  { get; }
    public abstract PortalHandState RightHand { get; }
}