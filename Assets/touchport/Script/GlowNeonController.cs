using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 挂在使用 GlowNeon 材质的物体上。
/// 平时自动呼吸（由 Shader 内部 Time 驱动）。
/// 碰到任何有 Collider 的物体时激活；所有接触离开后 3s 自动恢复呼吸。
/// 外部代码也可以直接调用 Activate / Deactivate。
/// </summary>
public class GlowNeonController : MonoBehaviour
{
    [Header("Collision Activation")]
    [SerializeField] private float cooldownAfterExit  = 3f;  // 离开后多久恢复呼吸
    [SerializeField] private float collisionIntensity = 2f;

    [Header("Point Light")]
    [SerializeField] private Light pointLight;
    [SerializeField] private float idleLightIntensity     = 0.05f;   // 呼吸时的光强基准
    [SerializeField] private float activatedLightIntensity = 0.2f;    // 激活时的光强

    [Header("Transition")]
    [SerializeField] private float activateSpeed   = 4f;
    [SerializeField] private float deactivateSpeed = 2f;

    // 当前激活权重，由协程驱动，写入 Shader 的 _Activated 属性
    private float _activatedWeight;
    private float _activatedIntensity = 5f;
    private float _pulseSpeed         = 1f;   // 从材质读取，与 Shader 保持同步

    private Material _mat;
    private Coroutine _transitionRoutine;
    private Coroutine _autoDeactivateRoutine;

    // 正在接触的 Collider 集合；全部离开才开始倒计时
    private readonly HashSet<Collider> _contacts = new HashSet<Collider>();

    // ── 只读状态 ───────────────────────────────────────────────
    public bool IsActivated => _activatedWeight > 0.01f;

    // ── Unity 生命周期 ─────────────────────────────────────────
    private void Awake()
    {
        var rend = GetComponent<Renderer>();
        if (rend != null)
        {
            _mat = rend.material; // 实例化，不影响场景里其他物体
            _pulseSpeed = _mat.GetFloat("_PulseSpeed");
        }

        if (pointLight != null)
            pointLight.intensity = idleLightIntensity;
    }

    private void Update()
    {
        if (pointLight == null) return;

        // 与 Shader 同步的呼吸脉动（公式和 GlowNeon.shader 里一致）
        float pulse      = Mathf.Sin(Time.time * _pulseSpeed) * 0.45f + 0.55f;
        float idleLight  = idleLightIntensity * pulse;

        // 激活时过渡到 activatedLightIntensity，_activatedWeight 由协程平滑驱动
        pointLight.intensity = Mathf.Lerp(idleLight, activatedLightIntensity, _activatedWeight);
    }

    private void OnDisable()
    {
        _contacts.Clear();
        SetWeight(0f);
        if (pointLight != null)
            pointLight.intensity = 0f;
    }

    // ── 碰撞检测 ───────────────────────────────────────────────

    private void OnCollisionEnter(Collision collision)  => ContactEnter(collision.collider);
    private void OnCollisionExit(Collision collision)   => ContactExit(collision.collider);
    private void OnTriggerEnter(Collider other)         => ContactEnter(other);
    private void OnTriggerExit(Collider other)          => ContactExit(other);

    private void ContactEnter(Collider col)
    {
        _contacts.Add(col);
        // 新接触进来：取消冷却倒计时，立即激活
        CancelAutoDeactivate();
        Activate(collisionIntensity);
        Debug.Log("Glowing Neon Collide");
    }

    private void ContactExit(Collider col)
    {
        _contacts.Remove(col);
        if (_contacts.Count == 0)
        {
            // 所有接触都离开了，开始 cooldown 倒计时
            CancelAutoDeactivate();
            _autoDeactivateRoutine = StartCoroutine(AutoDeactivate(cooldownAfterExit));
        }
    }

    // ── 公开接口 ───────────────────────────────────────────────

    /// <summary>
    /// 激活：亮度切换到 intensity，颜色由 Material 的 _ActivatedColor 决定。
    /// </summary>
    public void Activate(float intensity)
    {
        _activatedIntensity = intensity;
        ApplyActivatedParams();
        StartTransition(1f, activateSpeed);
        CancelAutoDeactivate();
    }

    /// <summary>
    /// 激活并在 duration 秒后自动退出激活态。
    /// </summary>
    public void ActivateFor(float intensity, float duration)
    {
        Activate(intensity);
        CancelAutoDeactivate();
        _autoDeactivateRoutine = StartCoroutine(AutoDeactivate(duration));
    }

    /// <summary>
    /// 退出激活态，平滑过渡回呼吸状态。
    /// </summary>
    public void Deactivate()
    {
        CancelAutoDeactivate();
        StartTransition(0f, deactivateSpeed);
    }

    // ── 内部逻辑 ───────────────────────────────────────────────

    private void ApplyActivatedParams()
    {
        if (_mat == null) return;
        _mat.SetFloat("_ActivatedIntensity", _activatedIntensity);
    }

    private void SetWeight(float weight)
    {
        _activatedWeight = weight;
        if (_mat != null)
            _mat.SetFloat("_Activated", _activatedWeight);
    }

    private void StartTransition(float target, float speed)
    {
        if (_transitionRoutine != null)
            StopCoroutine(_transitionRoutine);
        _transitionRoutine = StartCoroutine(TransitionTo(target, speed));
    }

    private IEnumerator TransitionTo(float target, float speed)
    {
        while (!Mathf.Approximately(_activatedWeight, target))
        {
            SetWeight(Mathf.MoveTowards(_activatedWeight, target, speed * Time.deltaTime));
            yield return null;
        }
        SetWeight(target);
        _transitionRoutine = null;
    }

    private IEnumerator AutoDeactivate(float delay)
    {
        yield return new WaitForSeconds(delay);
        Deactivate();
        _autoDeactivateRoutine = null;
    }

    private void CancelAutoDeactivate()
    {
        if (_autoDeactivateRoutine != null)
        {
            StopCoroutine(_autoDeactivateRoutine);
            _autoDeactivateRoutine = null;
        }
    }
}
