using UnityEngine;

/// <summary>
/// 挂在永远 active 的父节点上。
/// 控制 wristPanel 的显示/隐藏和位置旋转。
/// </summary>
public class WristPanelFollow : MonoBehaviour
{
    [SerializeField] private GameObject wristPanel;
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

        bool show = _reporter.RightMode == InputMode.Hand;
        if (wristPanel.activeSelf != show)
            wristPanel.SetActive(show);

        if (!show) return;

        var tracker = _reporter.RightHandTracker;
        if (tracker == null) return;
        wristPanel.transform.position = tracker.position + tracker.TransformDirection(positionOffset);
        wristPanel.transform.rotation = tracker.rotation * Quaternion.Euler(rotationOffset);
    }
}
