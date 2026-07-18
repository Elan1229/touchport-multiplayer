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

        // 多人：先用 obj.currentDream（不是 gameOwnerId——那个跟 PortalDirectionTrigger 翻转
        // owner 是同一次穿门触发的两个独立 OnTriggerEnter，谁先跑不该被这里依赖）找到这个礼物
        // 现在实际在哪个 world，再判断这趟是"送出去"还是"拿回去"：
        //   origin == currentDream（还在老家）  → 送出去 → 对面 world 的 bag 加权重
        //   origin != currentDream（不在老家）  → 拿回去 → 现在这个 world 的 bag 减权重
        // 不额外查 IsServer——DeliverGift/TakeBackGift 内部自己会查，client 调用是安全空操作。
        void DeliverMultiplayer(ObjectGift obj)
        {
            if (obj.gift == null) return;
            var mgr = DreamNetworkManager.Instance;

            int sourceWorld = mgr.WorldShowing(obj.currentDream);
            if (sourceWorld < 0)
            {
                Debug.LogWarning($"[GiftDelivery] can't place {obj.name} — its currentDream ('" +
                                 $"{(obj.currentDream ? obj.currentDream.dreamId : "null")}') doesn't match " +
                                 "either world's current dream (dream probably changed underneath it).", obj);
                return;
            }
            int destWorld = 1 - sourceWorld;

            bool isReturning = obj.originDream != obj.currentDream;
            if (isReturning)
            {
                mgr.TakeBackGift(sourceWorld, obj.gift);
                Debug.Log($"[GiftDelivery] {obj.name} ({obj.gift.giftName}) taken back out of World{sourceWorld} " +
                          $"(weight removed in {mgr.ChangeDelay}s).", obj);
            }
            else
            {
                mgr.DeliverGift(destWorld, obj.gift, obj.originDream);
                Debug.Log($"[GiftDelivery] {obj.name} ({obj.gift.giftName}) -> World{destWorld} " +
                          $"(dream changes in {mgr.ChangeDelay}s).", obj);
            }

            // 不管是送出去还是拿回去，穿门之后这个礼物物理上都进了对面那个 world。
            obj.currentDream = mgr.bags != null && destWorld < mgr.bags.Length && mgr.bags[destWorld] != null
                ? mgr.bags[destWorld].Current
                : obj.currentDream;
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
