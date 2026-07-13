using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace DreamTouch
{
    // Put on the portal's trigger collider (Portal/PortalTrigger on the runtime-spawned portal).
    // SERVER-only: when a gift crosses the portal, routes it to the OTHER world's bag via
    // DreamNetworkManager (target = the world NOT showing the gift's originDream).
    // This is the multiplayer counterpart of GiftDeliveryTrigger (which targets DreamSingleManager).
    [RequireComponent(typeof(Collider))]
    public class GiftPortalDelivery : MonoBehaviour
    {
        [Tooltip("Ignore the same gift for this many seconds after a delivery, so one crossing " +
                 "counts once (covers the change-delay window too).")]
        public float reArmDelay = 3f;

        readonly Dictionary<ObjectGift, float> _cooldown = new Dictionary<ObjectGift, float>();

        static bool IsServer => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

        void OnTriggerEnter(Collider other)
        {
            if (!IsServer) return;

            var obj = other.GetComponentInParent<ObjectGift>();
            if (obj == null || obj.gift == null) return;

            var mgr = DreamNetworkManager.Instance;
            if (mgr == null) return;

            if (_cooldown.TryGetValue(obj, out var readyAt) && Time.time < readyAt) return;
            _cooldown[obj] = Time.time + reArmDelay;

            int target = mgr.ResolveTargetWorld(obj.originDream);
            if (target < 0)
            {
                Debug.LogWarning($"[GiftPortalDelivery] can't route {obj.name} — set its ObjectGift." +
                                 $"originDream to the dream of the world it came from " +
                                 $"(got '{(obj.originDream ? obj.originDream.dreamId : "null")}').", obj);
                return;
            }

            mgr.DeliverGift(target, obj.gift, obj.originDream);
            Debug.Log($"[GiftPortalDelivery] {obj.name} ({obj.gift.giftName}) -> World{target} " +
                      $"(dream changes in {mgr.ChangeDelay}s).", obj);
        }
    }
}
