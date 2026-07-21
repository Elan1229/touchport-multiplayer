using System.Collections.Generic;
using Unity.Netcode;
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

        [Tooltip("开门宽限期（秒）：trigger 启用后这么久之内碰到的礼物视为\"portal 开到了它头上\"，" +
                 "不算穿门，进忽略名单；先离开 trigger 再回来才会正常结算。" +
                 "要盖过 PortalSpawnAnim 的放大时长（默认 2s）。")]
        public float armDelay = 2.5f;

        readonly Dictionary<ObjectGift, float> _cooldown = new Dictionary<ObjectGift, float>();

        // 开门时就在门里被"吞"进来的礼物——OnTriggerExit 才把它们移出名单。
        readonly HashSet<ObjectGift> _swallowedAtSpawn = new HashSet<ObjectGift>();
        float _enabledAt;

        void Reset()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        void Awake()
        {
            if (manager == null) ResolveManager();
        }

        void OnEnable()
        {
            _enabledAt = Time.time;
            _swallowedAtSpawn.Clear();
        }

        void OnTriggerEnter(Collider other)
        {
            var obj = other.GetComponentInParent<ObjectGift>();
            if (obj == null) return;

            // 开门宽限期内碰到的礼物：是 portal 开在了它所在的位置（含放大动画期间长进去的），
            // 不是有人拿着它穿门——忽略，等它先出去一次。
            if (Time.time - _enabledAt < armDelay)
            {
                if (_swallowedAtSpawn.Add(obj))
                    Debug.Log($"[GiftDelivery] {obj.name} was inside the portal when it opened — " +
                              "ignored until it leaves the trigger once.", obj);
                return;
            }
            if (_swallowedAtSpawn.Contains(obj)) return;   // 开门吞进来的，还没出去过

            if (_cooldown.TryGetValue(obj, out var readyAt) && Time.time < readyAt) return;
            _cooldown[obj] = Time.time + reArmDelay;

            if (DreamNetworkManager.Instance != null)
                DeliverMultiplayer(obj);
            else
                DeliverSinglePlayer(obj);
        }

        void OnTriggerExit(Collider other)
        {
            var obj = other.GetComponentInParent<ObjectGift>();
            if (obj != null && _swallowedAtSpawn.Remove(obj))
                Debug.Log($"[GiftDelivery] {obj.name} left the portal — armed for normal delivery.", obj);
        }

        // 多人：先用 obj.currentDream（不是 ownerPlayerId——那个跟 PortalDirectionTrigger 翻转
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
            int destWorld = PlayerWorld.OtherWorld(sourceWorld);

            bool isReturning = obj.originDream != obj.currentDream;
            if (isReturning)
            {
                mgr.TakeBackGift(sourceWorld, obj.gift);
                Debug.Log($"[GiftDelivery] {obj.name} ({obj.gift.giftName}) taken back out of World{sourceWorld} " +
                          $"(weight removed in {mgr.ChangeDelay}s).", obj);
            }
            else
            {
                // 带上礼物自己的 NetworkObjectId：如果这一送触发了换梦，DreamNetworkManager
                // 会在缓冲窗口里让【这个】礼物发光+响，把因果关系演出来。
                var net = obj.GetComponentInParent<NetworkObject>();
                ulong netId = net != null && net.IsSpawned ? net.NetworkObjectId : 0UL;
                mgr.DeliverGift(destWorld, obj.gift, obj.originDream, netId);
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
