using System.Text;
using Unity.Netcode;
using UnityEngine;
using TMPro;

/// <summary>
/// 字母拼词：P0/P1 各自一块垫子 <see cref="AnswerZone"/> 上报「词凑齐没」；可选 HUD；Server 上完成时可结束共享。
/// </summary>
public class LetterTaskState : NetworkBehaviour
{
    public static LetterTaskState Instance { get; private set; }

    [Header("Policy (server)")]
    [Tooltip("凑齐时调用 SharedState.StopShareAfterSeconds()（无参；秒数只在 SharedState Inspector 改）。")]
    [SerializeField] private bool stopSharedWhenAnyWordMatches = true;

    [SerializeField] private TMP_Text uiText;

    public NetworkVariable<bool> Player0Done = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> Player1Done = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private SharedState _sharedState;

    void Awake() => Instance = this;

    void OnEnable() => BindUI();

    void OnDisable() => UnbindUI();

    public override void OnDestroy()
    {
        UnbindUI();
        if (Instance == this)
            Instance = null;
        base.OnDestroy();
    }

    /// <summary>P0 的垫子算完字母后调用（仅 Server）。</summary>
    public void ReportPlayer0WordMatchServer(bool lettersMatch)
    {
        if (!IsServer) return;
        Player0Done.Value = lettersMatch;
        if (lettersMatch && stopSharedWhenAnyWordMatches)
            SharedState.Instance?.StopShareAfterSeconds();
    }

    /// <summary>P1 的垫子算完字母后调用（仅 Server）。</summary>
    public void ReportPlayer1WordMatchServer(bool lettersMatch)
    {
        if (!IsServer) return;
        Player1Done.Value = lettersMatch;
        if (lettersMatch && stopSharedWhenAnyWordMatches)
            SharedState.Instance?.StopShareAfterSeconds();
    }

    void BindUI()
    {
        UnbindUI();

        Player0Done.OnValueChanged += OnAnyChanged;
        Player1Done.OnValueChanged += OnAnyChanged;

        _sharedState = SharedState.Instance ?? FindFirstObjectByType<SharedState>();
        SharedState.OnSharedChanged += OnSharedChangedHandler;

        RefreshUI();
    }

    void UnbindUI()
    {
        Player0Done.OnValueChanged -= OnAnyChanged;
        Player1Done.OnValueChanged -= OnAnyChanged;

        SharedState.OnSharedChanged -= OnSharedChangedHandler;
        _sharedState = null;
    }

    void OnAnyChanged(bool _, bool __) => RefreshUI();
    void OnSharedChangedHandler(bool _) => RefreshUI();

    void RefreshUI()
    {
        if (uiText == null) return;

        var sb = new StringBuilder();
        if (_sharedState != null)
            sb.AppendLine(_sharedState.IsShared ? "Shared: ON" : "Shared: OFF");
        if (Player0Done.Value)
            sb.AppendLine("P0 word OK");
        if (Player1Done.Value)
            sb.AppendLine("P1 word OK");

        uiText.text = sb.ToString().TrimEnd();
    }
}
