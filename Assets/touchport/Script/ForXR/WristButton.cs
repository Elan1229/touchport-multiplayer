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

        var kpL = nm.LocalClientId == 0 ? hm.KP0L.Value : hm.KP1L.Value;
        var kpR = nm.LocalClientId == 0 ? hm.KP0R.Value : hm.KP1R.Value;

        Vector3 tipL = kpL.indexTip;
        Vector3 tipR = kpR.indexTip;

        bool insideL = tipL != Vector3.zero && Vector3.Distance(tipL, transform.position) < pokeRadius;
        bool insideR = tipR != Vector3.zero && Vector3.Distance(tipR, transform.position) < pokeRadius;
        bool inside = insideL || insideR;
        if (inside && !_wasInside)
        {
            Debug.Log($"[WristButton] 戳到了 {gameObject.name}");
            if (onPoke.GetPersistentEventCount() > 0)
                onPoke.Invoke();
            else if (_button != null)
                _button.onClick.Invoke();
        }

        _wasInside = inside;
    }
}
