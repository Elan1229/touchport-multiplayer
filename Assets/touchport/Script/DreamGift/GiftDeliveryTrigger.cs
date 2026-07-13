using System.Collections.Generic;
using UnityEngine;

namespace DreamTouch
{
    // Put this on a trigger collider (e.g. a small box zone on the Portal). When an
    // object carrying an ObjectGift crosses it, it reports the delivery to the manager.
    // The trigger CONDITION is identical for single-player and multiplayer; only the
    // manager it reports to differs. Kept separate for now; may merge into the portal
    // later.
    [RequireComponent(typeof(Collider))]
    public class GiftDeliveryTrigger : MonoBehaviour
    {
        [Tooltip("Who receives the gift. Leave empty — the portal is spawned at runtime, so " +
                 "this auto-finds the DreamSingleManager in the scene. (MP: DreamNetworkManager.)")]
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

        // The portal (and this trigger) can be spawned at runtime, so the manager usually
        // isn't wired in the Inspector — find it in the scene instead.
        void ResolveManager()
        {
            manager = FindFirstObjectByType<DreamSingleManager>();
        }

        void OnTriggerEnter(Collider other)
        {
            var obj = other.GetComponentInParent<ObjectGift>();
            if (obj == null) return;
            if (manager == null) ResolveManager();
            if (manager == null) return;

            if (_cooldown.TryGetValue(obj, out var readyAt) && Time.time < readyAt) return;
            _cooldown[obj] = Time.time + reArmDelay;

            manager.DeliverGift(obj);
            Debug.Log($"[GiftDelivery] {obj.name} " +
                      $"({(obj.gift ? obj.gift.giftName : "no gift")}) delivered.", obj);
        }
    }
}
