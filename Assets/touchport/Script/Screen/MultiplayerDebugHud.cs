using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 单机调试用 HUD：右侧显示两人距离与 <see cref="SharedState.IsShared"/>。
/// 只读 <see cref="SharedState"/>，其它脚本勿依赖本类；删掉本物体即无影响。
/// </summary>
[DisallowMultipleComponent]
public class MultiplayerDebugHud : MonoBehaviour
{
    public static MultiplayerDebugHud Instance { get; private set; }

    [Header("UI")]
    [SerializeField] private CanvasGroup panel;
    [SerializeField] private TMP_Text label;

    [Header("Display")]
    [Tooltip("勾选后：一进 Play 面板就展开（Editor / 打包都一样）。不勾选则默认隐藏，须按切换键才出现。")]
    [SerializeField] private bool visibleOnPlayStart = true;

    [Header("Input")]
    [SerializeField] private Key inputToggleKey = Key.I;
    [SerializeField] private KeyCode legacyToggleKey = KeyCode.I;

    [Header("Canvas")]
    [Tooltip("为 true 时 Play 开始把本 Canvas 的 Sort Order 拉高，避免被别的 UI 盖住")]
    [SerializeField] private bool bumpSortOrderOnAwake = true;
    [SerializeField] private int debugSortOrder = 200;

    [Header("Title")]
    [SerializeField] private string panelTitle = "Debug UI";

    bool _visible;
    bool _warnedNoLabel;

    void Awake()
    {
        Instance = this;
        if (panel == null)
            panel = GetComponent<CanvasGroup>();
        if (panel == null)
            panel = gameObject.AddComponent<CanvasGroup>();
        if (label == null)
            label = GetComponentInChildren<TMP_Text>(true);
        if (label == null && !_warnedNoLabel)
        {
            _warnedNoLabel = true;
            Debug.LogWarning("[MultiplayerDebugHud] 没找到 TMP_Text：请在子物体上放 TextMeshPro，并把组件拖到 Label。", this);
        }

        if (bumpSortOrderOnAwake)
        {
            var c = GetComponent<Canvas>();
            if (c != null) c.sortingOrder = debugSortOrder;
            if (c == null)
            {
                c = GetComponentInParent<Canvas>();
                if (c != null) c.sortingOrder = debugSortOrder;
            }
        }

        _visible = visibleOnPlayStart;
        ApplyPanelAlpha();
        if (_visible)
            RefreshLabel();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Update()
    {
        if (WasTogglePressed())
        {
            _visible = !_visible;
            ApplyPanelAlpha();
            if (_visible)
                RefreshLabel();
        }
        else if (_visible)
            RefreshLabel();
    }

    bool WasTogglePressed()
    {
        if (Keyboard.current != null && Keyboard.current[inputToggleKey].wasPressedThisFrame)
            return true;
#if ENABLE_LEGACY_INPUT_MANAGER
        try
        {
            if (Input.GetKeyDown(legacyToggleKey))
                return true;
        }
        catch
        {
            // 仅新输入模式时旧 API 可能不可用
        }
#endif
        return false;
    }

    void ApplyPanelAlpha()
    {
        if (panel == null) return;
        panel.alpha = _visible ? 1f : 0f;
        panel.interactable = _visible;
        panel.blocksRaycasts = _visible;
    }

    void RefreshLabel()
    {
        if (label == null) return;

        // 整块关闭时（alpha=0）屏幕上不显示任何字；打开后才有下面内容。
        string footer = $"Press {inputToggleKey} to Show/Hide Debug UI";

        var ss = SharedState.Instance;
        if (ss == null)
        {
            label.text =
                $"{panelTitle}\n" +
                "────────\n" +
                "未运行：无 SharedState\n" +
                "（先 Host / 进联机场景）\n" +
                "Dist: —\n" +
                "IsShared: —\n" +
                footer;
            return;
        }

        var spm = FindFirstObjectByType<ScreenPlayerManager>();
        float dist = spm != null ? spm.SyncedDistance.Value : -1f;
        string distStr = dist < 0f ? "--" : $"{dist:F2}";

        ScreenPlayerManager.TryGetBothPlayers(out Transform p0, out Transform p1);
        string p0Str = p0 != null ? $"{p0.position:F1}" : "--";
        string p1Str = p1 != null ? $"{p1.position:F1}" : "--";

        label.text =
            $"{panelTitle}\n" +
            "────────\n" +
            $"IsShared: {ss.IsShared.Value}\n" +
            $"Dist: {distStr}\n" +
            $"P0: {p0Str}\n" +
            $"P1: {p1Str}\n" +
            footer;
    }
}
