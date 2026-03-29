using UnityEngine;

namespace Anaglyph.Demo
{
    /// <summary>
    /// 业务逻辑层。订阅 HandsManager 的手势事件，决定何时调用 SharedSession.StartShare/StopShare。
    /// 以后要换触发手势或触发条件，只改这里。
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        private void OnEnable()
        {
            HandsManager.OnHandsTouched += OnHandsTouched;
        }

        private void OnDisable()
        {
            HandsManager.OnHandsTouched -= OnHandsTouched;
        }

        private void OnHandsTouched()
        {
            var session = SharedSession.Instance;
            if (session == null || !session.IsServer) return;
            session.ToggleShare();
        }
    }
}
