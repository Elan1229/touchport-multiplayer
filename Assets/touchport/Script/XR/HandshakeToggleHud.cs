using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// 视野中心提示 "Handshake ON / OFF"。
/// 挂在一个 world-space Canvas（或它的根节点）上，把 Canvas 根拖到 hudRoot、TMP 文字拖到 label。
/// 订阅 GameManager.OnHandshakeEnabledChanged，开关一变就在头前弹出文字，displaySeconds 秒后收起。
/// 纯显示，不含任何逻辑；删掉本物体不影响握手开关本身。
/// </summary>
public class HandshakeToggleHud : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private GameObject hudRoot;   // 整块提示的根节点，Show/Hide 控制它
    [SerializeField] private TMP_Text   label;

    [Header("Placement")]
    [Tooltip("提示出现在头部正前方多远（米）")]
    [SerializeField] private float distanceToHead  = 1.0f;
    [Tooltip("相对视线高度的偏移，负数=偏下，0=正中")]
    [SerializeField] private float heightOffset    = 0f;
    [Tooltip("显示期间是否每帧跟随头部转动；不勾则弹出瞬间定住不动")]
    [SerializeField] private bool  followHead      = true;

    [Header("Timing")]
    [Tooltip("提示停留几秒后自动消失")]
    [SerializeField] private float displaySeconds  = 2f;
    [Tooltip("勾选后：关闭状态的 OFF 提示常驻不消失，直到重新打开；ON 提示仍然只闪一下")]
    [SerializeField] private bool  keepVisibleWhileOff = false;

    [Header("Text")]
    [SerializeField] private string onText  = "Handshake ON";
    [SerializeField] private string offText = "Handshake OFF";
    [SerializeField] private Color  onColor  = Color.white;
    [SerializeField] private Color  offColor = new Color(1f, 0.45f, 0.35f);

    private Coroutine _hideRoutine;
    private bool _isShowing;
    private bool _gotFirstState;   // 是否已收到过一次状态（用来吞掉 spawn 时的初始推送）
    private Canvas[] _selfCanvases; // hudRoot 就是本物体时的降级方案

    private void Awake()
    {
        // hudRoot 指向自己的话，SetActive(false) 会把本脚本一起关掉、退订事件后再也醒不过来。
        // 这种情况改成开关 Canvas.enabled。
        if (hudRoot == gameObject)
            _selfCanvases = GetComponentsInChildren<Canvas>(true);
    }

    private void OnEnable()
    {
        GameManager.OnHandshakeEnabledChanged += OnEnabledChanged;
        Hide();
    }

    private void OnDisable()
    {
        GameManager.OnHandshakeEnabledChanged -= OnEnabledChanged;
    }

    private void LateUpdate()
    {
        if (_isShowing && followHead)
            PlaceInFrontOfHead();
    }

    private void OnEnabledChanged(bool enabled)
    {
        // GameManager spawn 时会推一次当前值。开着的话说明是正常开局，不该闪一下字；
        // 关着的话（中途加入的客户端）要提示，否则他不知道握手是被关掉的。
        bool isInitialPush = !_gotFirstState;
        _gotFirstState = true;
        if (isInitialPush && enabled) { Hide(); return; }

        if (label != null)
        {
            label.text  = enabled ? onText  : offText;
            label.color = enabled ? onColor : offColor;
        }

        Show();

        if (_hideRoutine != null) StopCoroutine(_hideRoutine);

        // OFF 常驻模式：关着的时候不收起，一直提醒
        if (keepVisibleWhileOff && !enabled) return;

        _hideRoutine = StartCoroutine(HideAfterDelay());
    }

    private IEnumerator HideAfterDelay()
    {
        yield return new WaitForSeconds(displaySeconds);
        _hideRoutine = null;
        Hide();
    }

    private void Show()
    {
        PlaceInFrontOfHead();
        _isShowing = true;
        SetVisible(true);
    }

    private void Hide()
    {
        _isShowing = false;
        SetVisible(false);
    }

    private void SetVisible(bool visible)
    {
        if (hudRoot == null) return;

        if (_selfCanvases != null)
        {
            foreach (var c in _selfCanvases)
                if (c != null) c.enabled = visible;
            return;
        }

        hudRoot.SetActive(visible);
    }

    private void PlaceInFrontOfHead()
    {
        var cam = Camera.main;
        if (cam == null || hudRoot == null) return;

        var camT = cam.transform;
        hudRoot.transform.position = camT.position
                                     + camT.forward * distanceToHead
                                     + camT.up * heightOffset;
        // 面朝摄像机（背对视线方向，文字才是正的）
        hudRoot.transform.rotation = Quaternion.LookRotation(hudRoot.transform.position - camT.position, camT.up);
    }
}
