using UnityEngine;
using UnityEngine.InputSystem;

public class HandInputDesktop : HandInputSource
{
    [SerializeField] private Transform leftSphere;
    [SerializeField] private Transform rightSphere;
    [SerializeField] private Key scaleKey = Key.F;

    public override PortalHandState LeftHand  => BuildState(leftSphere);
    public override PortalHandState RightHand => BuildState(rightSphere);

    private PortalHandState BuildState(Transform sphere)
    {
        if (sphere == null) return default;
        return new PortalHandState
        {
            isTracked     = true,
            isScaleIntent = Keyboard.current != null && Keyboard.current[scaleKey].isPressed,
            worldPosition = sphere.position,
            collider      = sphere.GetComponent<Collider>()
        };
    }
}