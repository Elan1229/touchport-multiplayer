using System;
using UnityEngine;

/// <summary>
/// 业务逻辑层。维护 OnInteract 事件，决定何时调用 SharedState.ToggleShare。
/// XR 场景由 HandsManager fire，桌面场景由 ScreenPlayerManager fire。
/// </summary>
public class GameManager : MonoBehaviour
{
    // 触发交互事件，HandsManager 和 ScreenPlayerManager 都通过这里 fire
    public static event Action OnInteract;

    public static void FireInteract() => OnInteract?.Invoke();

    private void OnEnable()  => OnInteract += HandleInteract;
    private void OnDisable() => OnInteract -= HandleInteract;

    private void HandleInteract()
    {
        var session = SharedState.Instance;
        if (session == null || !session.IsServer) return;
        session.ToggleShare();
    }
}
