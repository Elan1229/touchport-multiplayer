using System.Collections;
using UnityEngine;

/// <summary>
/// 挂在小球上。撞到东西时发光 pulse。
/// 需要：球本身有 Renderer + Collider；子物体有 Point Light（拖到 Inspector）。
/// </summary>
public class OrbGlow : MonoBehaviour
{
    [Header("Light")]
    [SerializeField] private Light pointLight;
    [SerializeField] private float idleLightIntensity  = 0.3f;
    [SerializeField] private float pulseLightIntensity = 4f;
    [SerializeField] private float pulseDuration       = 0.6f;  // 从亮到暗的时间

    [Header("Emission")]
    [SerializeField] private Color glowColor = new Color(0.4f, 0.8f, 1f); // 淡蓝白
    [SerializeField] private float idleEmission  = 0.4f;
    [SerializeField] private float pulseEmission = 3f;

    private Material _mat;
    private Coroutine _pulseRoutine;

    private void Awake()
    {
        var rend = GetComponent<Renderer>();
        if (rend != null)
            _mat = rend.material; // 实例化 material，不影响其他球

        SetEmission(idleEmission);
        if (pointLight != null)
            pointLight.intensity = idleLightIntensity;
    }

    private void OnCollisionEnter(Collision collision)
    {
        Debug.Log($"[OrbGlow] 碰到了 {collision.gameObject.name}，mat={_mat != null}");
        if (_pulseRoutine != null) return; // 冷却中，忽略
        _pulseRoutine = StartCoroutine(Pulse());
    }

    private IEnumerator Pulse()
    {
        // 瞬间亮起
        SetEmission(pulseEmission);
        if (pointLight != null) pointLight.intensity = pulseLightIntensity;

        // 慢慢淡回 idle
        float t = 0f;
        while (t < pulseDuration)
        {
            t += Time.deltaTime;
            float lerp = t / pulseDuration;
            SetEmission(Mathf.Lerp(pulseEmission, idleEmission, lerp));
            if (pointLight != null)
                pointLight.intensity = Mathf.Lerp(pulseLightIntensity, idleLightIntensity, lerp);
            yield return null;
        }

        SetEmission(idleEmission);
        if (pointLight != null) pointLight.intensity = idleLightIntensity;
        _pulseRoutine = null; // 冷却结束，可以再次触发
    }

    private void SetEmission(float intensity)
    {
        if (_mat == null) return;
        _mat.EnableKeyword("_EMISSION");
        _mat.SetColor("_EmissionColor", glowColor * intensity);
    }
}
