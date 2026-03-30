using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// 挂在按钮上。检测本机右手食指指尖靠近时触发：
/// 优先触发同物体上的 UI Button（Screen/XR 共用），
/// 没有 Button 则触发 OnPoke 自定义事件。
/// </summary>
public class WristButton : MonoBehaviour
{
    private float pokeRadius = 0.04f;
    [SerializeField] private UnityEvent onPoke; // 没有 UI Button 时的回调

    private Button _button;
    private bool _wasInside = false;

    private void Awake()
    {
        _button = GetComponent<Button>();
    }

    private void Update()
    {
        var hm = HandsManager.Instance;
        if (hm == null) return;

        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsConnectedClient) return;

        Vector3 indexTip = nm.LocalClientId == 0
            ? hm.KP0R.Value.indexTip
            : hm.KP1R.Value.indexTip;

        if (indexTip == Vector3.zero) return;

        bool inside = Vector3.Distance(indexTip, transform.position) < pokeRadius;
        if (inside && !_wasInside)
        {
            if (onPoke.GetPersistentEventCount() > 0)
                onPoke.Invoke();
            else if (_button != null)
                _button.onClick.Invoke();
        }

        _wasInside = inside;
    }
}
