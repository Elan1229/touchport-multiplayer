using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 挂在 player prefab 上。
/// Owner 在 Inspector 填名字，OnNetworkSpawn 时同步给所有客户端。
/// 名牌每帧朝向本机摄像机。
/// </summary>
public class PlayerNameTag : NetworkBehaviour
{
    [SerializeField] private string playerName = "Player";
    [SerializeField] private TMP_Text nameText;

    private NetworkVariable<FixedString64Bytes> _syncedName = new(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    public override void OnNetworkSpawn()
    {
        _syncedName.OnValueChanged += OnNameChanged;

        if (IsOwner)
            _syncedName.Value = new FixedString64Bytes(playerName);
        else
            UpdateText(_syncedName.Value.ToString());
    }

    public override void OnNetworkDespawn()
    {
        _syncedName.OnValueChanged -= OnNameChanged;
    }

    private void OnNameChanged(FixedString64Bytes prev, FixedString64Bytes next)
        => UpdateText(next.ToString());

    private void UpdateText(string name)
    {
        if (nameText != null)
            nameText.text = name;
    }

    private void Update()
    {
        var cam = Camera.main;
        if (cam == null) return;
        transform.LookAt(transform.position + cam.transform.rotation * Vector3.forward,
                         cam.transform.rotation * Vector3.up);
    }
}
