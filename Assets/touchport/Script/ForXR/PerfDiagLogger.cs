using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// [PerfDiag] 每 3 秒打一条帧率统计到 logcat：
///   avg   = 窗口内平均 FPS
///   worst = 窗口内最差一帧的耗时（抓瞬时卡顿，如送礼时的房间实例化尖峰）
///   gpu   = 上一帧 App GPU 耗时（>帧预算 = GPU/shader 锅；远小于帧预算但 FPS 低 = CPU 锅）
///   target= 系统刷新率（确认跑 90 还是被锁 72）
/// 纯诊断用，demo 正式版把挂载物体禁用即可。
/// </summary>
public class PerfDiagLogger : MonoBehaviour
{
    [SerializeField] private float logInterval = 3f;

    private int _frames;
    private float _elapsed;
    private float _worstFrame;
    private XRDisplaySubsystem _display;

    private void Start()
    {
        var displays = new List<XRDisplaySubsystem>();
        SubsystemManager.GetSubsystems(displays);
        if (displays.Count > 0) _display = displays[0];
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        _frames++;
        _elapsed += dt;
        if (dt > _worstFrame) _worstFrame = dt;

        if (_elapsed < logInterval) return;

        float avgFps = _frames / _elapsed;
        float gpuMs = -1f;
        if (_display != null && _display.TryGetAppGPUTimeLastFrame(out float gpuSec))
            gpuMs = gpuSec * 1000f;
        float refresh = OVRPlugin.systemDisplayFrequency;

        Debug.Log($"[PerfDiag] avg={avgFps:F1}fps worst={_worstFrame * 1000f:F0}ms gpu={gpuMs:F1}ms target={refresh:F0}Hz");

        _frames = 0;
        _elapsed = 0f;
        _worstFrame = 0f;
    }
}
