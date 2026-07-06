using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 挂在使用 NeonGlow_Pulse 材质的物体上。
/// 需要：本物体有 Rigidbody（Kinematic），子物体有 Trigger Collider。
/// 碰撞进入 → PulseSpeed/GlowStrength 切换到激活值；
/// 所有接触离开后 cooldownAfterExit 秒恢复闲置值。
/// </summary>
public class GlowNeonController : MonoBehaviour
{
    [Header("Idle State")]
    [SerializeField] private float idlePulseSpeed   = 1f;
    [SerializeField] private float idleGlowStrength = 1f;

    [Header("Active State")]
    [SerializeField] private float activePulseSpeed    = 3f;
    [SerializeField] private float activeGlowStrength  = 3f;
    [SerializeField] private float cooldownAfterExit   = 5f;
    [SerializeField] private float deactivateDuration  = 1.5f;  // 渐变回闲置的时长

    private Material _mat;
    private readonly HashSet<Collider> _contacts = new HashSet<Collider>();
    private Coroutine _cooldownRoutine;

    private void Awake()
    {
        var rend = GetComponent<Renderer>();
        if (rend != null)
            _mat = rend.material;
    }

    private void OnDisable()
    {
        _contacts.Clear();
        CancelCooldown();
        SetIdle();
    }

    // ── Trigger 检测（子物体 Trigger + 本物体 Rigidbody，事件自动上传）──

    private void OnTriggerEnter(Collider other)
    {
        _contacts.Add(other);
        CancelCooldown();
        SetActive();
    }

    private void OnTriggerExit(Collider other)
    {
        _contacts.Remove(other);
        if (_contacts.Count == 0)
        {
            CancelCooldown();
            _cooldownRoutine = StartCoroutine(CooldownRoutine());
        }
    }

    // ── 状态切换 ───────────────────────────────────────────────

    private void SetActive()
    {
        if (_mat == null) return;
        _mat.SetFloat("_PulseSpeed",   activePulseSpeed);
        _mat.SetFloat("_GlowStrength", activeGlowStrength);
    }

    private void SetIdle()
    {
        if (_mat == null) return;
        _mat.SetFloat("_PulseSpeed",   idlePulseSpeed);
        _mat.SetFloat("_GlowStrength", idleGlowStrength);
    }

    // ── Cooldown 协程 ──────────────────────────────────────────

    private IEnumerator CooldownRoutine()
    {
        yield return new WaitForSeconds(cooldownAfterExit);

        // PulseSpeed 直接 snap 回闲置（lerp 频率会导致波形相位跳变闪烁）
        if (_mat != null) _mat.SetFloat("_PulseSpeed", idlePulseSpeed);

        // 只对 GlowStrength 做渐变
        float fromGlow = _mat != null ? _mat.GetFloat("_GlowStrength") : activeGlowStrength;
        float elapsed  = 0f;

        while (elapsed < deactivateDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / deactivateDuration;
            _mat.SetFloat("_GlowStrength", Mathf.Lerp(fromGlow, idleGlowStrength, t));
            yield return null;
        }

        SetIdle();
        _cooldownRoutine = null;
    }

    private void CancelCooldown()
    {
        if (_cooldownRoutine != null)
        {
            StopCoroutine(_cooldownRoutine);
            _cooldownRoutine = null;
        }
    }
}
