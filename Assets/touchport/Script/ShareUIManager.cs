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
    [SerializeField] private GameObject panelPeopleNearby; // 选人面板（演示用）
    [SerializeField] private GameObject panelIdle;     // Share? + Cancel
    [SerializeField] private GameObject panelWaiting;  // Waiting... + Cancel
    [SerializeField] private GameObject panelAccept;   // Accept Share? + Cancel
    [SerializeField] private GameObject panelAccepted;    // Accepted!（1秒后自动关）
    [SerializeField] private GameObject panelNotAccepted; // Not Accepted!（1秒后自动关）
    [SerializeField] private GameObject panelSharing;  // Sharing + Stop Sharing

    [Header("Root")]
    [SerializeField] private GameObject uiRoot; // 整个 UI 的根节点，Show/Hide 控制它

    [Header("XR Positioning")]
    [Tooltip("是否在 Show 时把 UI 定位到头部前方（XR 用）")]
    [SerializeField] private bool positionInFrontOfHead = false;
    [SerializeField] private float distanceToHead = 0.6f;
    [SerializeField] private float headHeightOffset = -0.1f;

    [Header("People Nearby")]
    [SerializeField] private GameObject[] highlightRings; // 3个，对应三个头像的高亮圈

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
        if (positionInFrontOfHead)
            PositionInFrontOfHead();
        uiRoot.SetActive(true);
        bool isSharing = SharedState.Instance != null && SharedState.Instance.IsShared;
        SetPanel(isSharing ? panelSharing : panelPeopleNearby);
    }

    private void PositionInFrontOfHead()
    {
        var cam = Camera.main;
        if (cam == null) return;
        var forward = cam.transform.forward;
        forward.y = 0f;
        forward.Normalize();
        var pos = cam.transform.position
                  + forward * distanceToHead
                  + Vector3.up * headHeightOffset;
        uiRoot.transform.position = pos;
        uiRoot.transform.rotation = Quaternion.LookRotation(forward);
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
        yield return new WaitForSeconds(2f);
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

    // ─── 按钮回调（People Nearby）────────────────────────────────

    public void OnClickPerson(int index)
    {
        // 高亮选中的圈，其余隐藏
        if (highlightRings != null)
            for (int i = 0; i < highlightRings.Length; i++)
                if (highlightRings[i] != null)
                    highlightRings[i].SetActive(i == index);
        SetPanel(panelIdle);
    }

    private void SetPanel(GameObject active)
    {
        if (panelPeopleNearby) panelPeopleNearby.SetActive(panelPeopleNearby == active);
        if (panelIdle)     panelIdle.SetActive(panelIdle      == active);
        if (panelWaiting)  panelWaiting.SetActive(panelWaiting  == active);
        if (panelAccept)   panelAccept.SetActive(panelAccept   == active);
        if (panelAccepted)    panelAccepted.SetActive(panelAccepted    == active);
        if (panelNotAccepted) panelNotAccepted.SetActive(panelNotAccepted == active);
        if (panelSharing)     panelSharing.SetActive(panelSharing     == active);
    }

    // ─── 按钮回调 ─────────────────────────────────────────────────

    public void OnClickShare()          { Debug.Log("[UI] OnClickShare called"); GameManager.FireUIRequestShare(); }
    public void OnClickAccept()         => GameManager.FireUIAcceptShare();
    public void OnClickStopSharing()    => GameManager.FireStopSharing();
    public void OnClickCloseUI()        => Hide();
    public void OnClickCancel()         => GameManager.FireCancelRequest();
    public void OnClickNotAccept()      => GameManager.FireNotAccept();
}
