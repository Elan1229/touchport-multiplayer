using System;
using Unity.Netcode;

namespace Anaglyph.Demo
{
    /// <summary>
    /// 共享状态权威。维护 IsShared NetworkVariable，广播 OnSharedChanged 给下游所有系统。
    /// 下游系统只依赖这个类，不需要知道触发原因。
    /// </summary>
    public class SharedSession : NetworkBehaviour
    {
        public static SharedSession Instance { get; private set; }

        public static event Action<bool> OnSharedChanged;

        public NetworkVariable<bool> IsShared = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public override void OnNetworkSpawn()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            IsShared.OnValueChanged += (_, next) => OnSharedChanged?.Invoke(next);
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
        }

        // Server 直接调用
        public void StartShare()
        {
            if (IsServer) IsShared.Value = true;
        }

        public void StopShare()
        {
            if (IsServer) IsShared.Value = false;
        }

        public void ToggleShare()
        {
            if (IsServer) IsShared.Value = !IsShared.Value;
        }
    }
}
