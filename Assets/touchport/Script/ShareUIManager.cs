using System.Collections;
using UnityEngine;

/// <summary>
/// 管理 Share UI 的三个面板状态。
/// Screen：U 键开关；XR：戳手腕板板打开。
/// 按钮回调直接调 GameManager.FireXxx()。
/// </summary>
public class ShareUIManager : MonoBehaviour
{
    public static ShareUIManager Instance { get; private set; }

    [Header("Panels")]
    [SerializeField] private GameObject panelIdle;     // Share? + Cancel
    [SerializeField] private GameObject panelWaiting;  // Waiting... + Cancel
    [SerializeField] private GameObject panelAccept;   // Accept Share? + Cancel
    [SerializeField] private GameObject panelAccepted;    // Accepted!（1秒后自动关）
    [SerializeField] private GameObject panelNotAccepted; // Not Accepted!（1秒后自动关）
    [SerializeField] private GameObject panelSharing;  // Sharing + Stop Sharing

    [Header("Root")]
    [SerializeField] private GameObject uiRoot; // 整个 UI 的根节点，Show/Hide 控制它

    [Header("Timeout")]
    [SerializeField] private float requestTimeout = 30f; // 等待对方 Accept 的超时秒数

    private float _timeoutTimer = 0f; // > 0 时倒计时，到 0 自动取消

    private void Awake()
    {
        Instance = this;
        Hide();
    }

    private void Update()
    {
        if (_timeoutTimer <= 0f) return;
        _timeoutTimer -= Time.deltaTime;
        if (_timeoutTimer <= 0f)
            GameManager.FireCancelRequest();
    }

    private void OnEnable()
    {
        GameManager.OnUIWaiting      += ShowWaiting;
        GameManager.OnUIRequestShare += ShowAccept;
        GameManager.OnUIAcceptShare  += ShowSharing;
        GameManager.OnStopSharing    += Hide;
        GameManager.OnCancelRequest  += Hide;
        GameManager.OnNotAccept      += ShowNotAccepted;
        SharedState.OnSharedChanged  += OnSharedChanged;
    }

    private void OnDisable()
    {
        GameManager.OnUIWaiting      -= ShowWaiting;
        GameManager.OnUIRequestShare -= ShowAccept;
        GameManager.OnUIAcceptShare  -= ShowSharing;
        GameManager.OnStopSharing    -= Hide;
        GameManager.OnCancelRequest  -= Hide;
        GameManager.OnNotAccept      -= ShowNotAccepted;
        SharedState.OnSharedChanged  -= OnSharedChanged;
    }

    private void OnSharedChanged(bool isShared)
    {
        if (!isShared) Hide();
    }

    // ─── 公开接口 ─────────────────────────────────────────────────

    public void Show()
    {
        uiRoot.SetActive(true);
        bool isSharing = SharedState.Instance != null && SharedState.Instance.IsShared.Value;
        SetPanel(isSharing ? panelSharing : panelIdle);
    }

    public void Hide()
    {
        _timeoutTimer = 0f;
        if (uiRoot != null) uiRoot.SetActive(false);
    }

    public void Toggle()
    {
        if (uiRoot.activeSelf) Hide();
        else Show();
    }

    // ─── 状态切换 ─────────────────────────────────────────────────

    private void ShowWaiting()
    {
        uiRoot.SetActive(true);
        SetPanel(panelWaiting);
        _timeoutTimer = requestTimeout;
    }

    private void ShowAccept()
    {
        uiRoot.SetActive(true);
        SetPanel(panelAccept);
    }

    private void ShowSharing()
    {
        _timeoutTimer = 0f;
        StartCoroutine(ShowAcceptedBrief());
    }

    private IEnumerator ShowAcceptedBrief()
    {
        uiRoot.SetActive(true);
        SetPanel(panelAccepted);
        yield return new WaitForSeconds(1f);
        Hide();
    }

    private void ShowNotAccepted()
    {
        StartCoroutine(ShowNotAcceptedBrief());
    }

    private IEnumerator ShowNotAcceptedBrief()
    {
        uiRoot.SetActive(true);
        SetPanel(panelNotAccepted);
        yield return new WaitForSeconds(1f);
        Hide();
    }

    private void SetPanel(GameObject active)
    {
        if (panelIdle)     panelIdle.SetActive(panelIdle      == active);
        if (panelWaiting)  panelWaiting.SetActive(panelWaiting  == active);
        if (panelAccept)   panelAccept.SetActive(panelAccept   == active);
        if (panelAccepted)    panelAccepted.SetActive(panelAccepted    == active);
        if (panelNotAccepted) panelNotAccepted.SetActive(panelNotAccepted == active);
        if (panelSharing)     panelSharing.SetActive(panelSharing     == active);
    }

    // ─── 按钮回调 ─────────────────────────────────────────────────

    public void OnClickShare()          => GameManager.FireUIRequestShare();
    public void OnClickAccept()         => GameManager.FireUIAcceptShare();
    public void OnClickStopSharing()    => GameManager.FireStopSharing();
    public void OnClickCloseUI()        => Hide();
    public void OnClickCancel()         => GameManager.FireCancelRequest();
    public void OnClickNotAccept()      => GameManager.FireNotAccept();
}
