using UnityEngine;

/// <summary>
/// 挂在 WristPanel 上，跟随右手位置和旋转。
/// Inspector 里调 positionOffset / rotationOffset 微调。
/// </summary>
public class WristPanelFollow : MonoBehaviour
{
    [SerializeField] private Vector3 positionOffset = Vector3.zero;
    [SerializeField] private Vector3 rotationOffset = Vector3.zero;

    private LocalHandsReporter _reporter;

    private void Start()
    {
        _reporter = FindObjectOfType<LocalHandsReporter>();
    }

    private void LateUpdate()
    {
        if (_reporter == null) return;
        var tracker = _reporter.RightHandTracker;
        if (tracker == null) return;
        transform.position = tracker.position + tracker.TransformDirection(positionOffset);
        transform.rotation = tracker.rotation * Quaternion.Euler(rotationOffset);
    }
}
