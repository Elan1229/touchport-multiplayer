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
        var kb = Keyboard.current;
        return new PortalHandState
        {
            isTracked          = true,
            isScaleIntent      = kb != null && kb[scaleKey].isPressed,
            isScaleJustPressed = kb != null && kb[scaleKey].wasPressedThisFrame,
            worldPosition      = sphere.position,
            collider           = sphere.GetComponent<Collider>()
        };
    }
}