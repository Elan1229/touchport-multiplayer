using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attach to ScrollView root.
/// logText    -> ScrollView/Viewport/Content's TextMeshProUGUI
/// scrollRect -> ScrollRect on the same GameObject
/// filter     -> only show lines containing these prefixes (empty = show all)
/// </summary>
public class DebugLogPanel : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI logText;
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private int maxLines = 60;
    [SerializeField] private float refreshInterval = 0.15f;

    [Header("Filter (leave empty to show all)")]
    [SerializeField] private string[] showOnlyPrefixes = { "[GM]", "[SS]", "[touchport]" };

    private readonly Queue<string> _lines = new();
    private bool _dirty = false;
    private float _timer = 0f;

    private void OnEnable()  { Application.logMessageReceived += OnLog; }
    private void OnDisable() { Application.logMessageReceived -= OnLog; }
    private void OnDestroy() { Application.logMessageReceived -= OnLog; }

    private void OnLog(string message, string stackTrace, LogType type)
    {
        if (showOnlyPrefixes != null && showOnlyPrefixes.Length > 0)
        {
            bool match = false;
            foreach (var p in showOnlyPrefixes)
                if (message.Contains(p)) { match = true; break; }
            if (!match) return;
        }

        string prefix = type switch
        {
            LogType.Warning   => "[W] ",
            LogType.Error     => "[E] ",
            LogType.Exception => "[X] ",
            _                 => "",
        };
        _lines.Enqueue(prefix + message);
        while (_lines.Count > maxLines)
            _lines.Dequeue();
        _dirty = true;
    }

    private void LateUpdate()
    {
        if (!_dirty) return;
        _timer -= Time.deltaTime;
        if (_timer > 0f) return;
        _timer = refreshInterval;
        _dirty = false;

        logText.text = string.Join("\n", _lines);

        // force TMP to recalculate, then resize Content to match
        logText.ForceMeshUpdate();
        var contentRect = (RectTransform)logText.transform.parent;
        if (contentRect != null)
            contentRect.sizeDelta = new Vector2(contentRect.sizeDelta.x, logText.preferredHeight + 20f);

        if (scrollRect != null)
            scrollRect.verticalNormalizedPosition = 0f;
    }
}
