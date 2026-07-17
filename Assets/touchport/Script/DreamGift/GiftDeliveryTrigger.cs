using System.Collections.Generic;
using UnityEngine;

namespace DreamTouch
{
    // Put this on a trigger collider (e.g. a small box zone, or PortalTrigger in multiplayer).
    // When an object carrying an ObjectGift crosses it, delivers to whichever manager fits
    // the scene:
    //   - DreamNetworkManager.Instance present → multiplayer path. DeliverGift() itself
    //     no-ops on non-server clients (it checks IsServer internally), so this trigger
    //     doesn't need its own identity check before calling it.
    //   - otherwise                            → single-player path (DreamSingleManager).
    [RequireComponent(typeof(Collider))]
    public class GiftDeliveryTrigger : MonoBehaviour
    {
        [Tooltip("单人场景用。留空会自动在场景里找 DreamSingleManager。" +
                 "多人场景不用配这个，自动走 DreamNetworkManager.Instance。")]
        public DreamSingleManager manager;

        [Tooltip("Ignore the same gift for this many seconds after delivery, so one " +
                 "crossing counts as one delivery.")]
        public float reArmDelay = 1f;

        readonly Dictionary<ObjectGift, float> _cooldown = new Dictionary<ObjectGift, float>();

        void Reset()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        void Awake()
        {
            if (manager == null) ResolveManager();
        }

        void OnTriggerEnter(Collider other)
        {
            var obj = other.GetComponentInParent<ObjectGift>();
            if (obj == null) return;

            if (_cooldown.TryGetValue(obj, out var readyAt) && Time.time < readyAt) return;
            _cooldown[obj] = Time.time + reArmDelay;

            if (DreamNetworkManager.Instance != null)
                DeliverMultiplayer(obj);
            else
                DeliverSinglePlayer(obj);
        }

        // 多人：算出目标 World（origin 没在显示的那边），调 DreamNetworkManager.DeliverGift。
        // 不额外查 IsServer——client 调用是安全空操作，见上面类注释。
        void DeliverMultiplayer(ObjectGift obj)
        {
            if (obj.gift == null) return;
            var mgr = DreamNetworkManager.Instance;

            int target = mgr.ResolveTargetWorld(obj.originDream);
            if (target < 0)
            {
                Debug.LogWarning($"[GiftDelivery] can't route {obj.name} — set its ObjectGift." +
                                 $"originDream to the dream of the world it came from " +
                                 $"(got '{(obj.originDream ? obj.originDream.dreamId : "null")}').", obj);
                return;
            }

            mgr.DeliverGift(target, obj.gift, obj.originDream);
            Debug.Log($"[GiftDelivery] {obj.name} ({obj.gift.giftName}) -> World{target} " +
                      $"(dream changes in {mgr.ChangeDelay}s).", obj);
        }

        void DeliverSinglePlayer(ObjectGift obj)
        {
            if (manager == null) ResolveManager();
            if (manager == null) return;

            manager.DeliverGift(obj);
            Debug.Log($"[GiftDelivery] {obj.name} " +
                      $"({(obj.gift ? obj.gift.giftName : "no gift")}) delivered.", obj);
        }

        // The portal (and this trigger) can be spawned at runtime, so the manager usually
        // isn't wired in the Inspector — find it in the scene instead. Only relevant for
        // single-player; multiplayer always resolves via DreamNetworkManager.Instance.
        void ResolveManager()
        {
            manager = FindFirstObjectByType<DreamSingleManager>();
        }
    }
}
