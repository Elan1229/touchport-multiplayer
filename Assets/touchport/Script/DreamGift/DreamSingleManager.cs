using UnityEngine;

namespace DreamTouch
{
    // Single-player orchestration. NO networking. It is the offline twin of
    // DreamNetworkManager: it feeds delivered gifts into the local bag and swaps
    // visuals when the dream changes. Once the single-player loop feels right,
    // port the useful bits into DreamNetworkManager (the NetworkBehaviour) for
    // multiplayer. Keep only ONE manager per scene.
    public class DreamSingleManager : MonoBehaviour
    {
        [Header("Wiring")]
        public PlayerDreamBag localBag;
        public ChangeDreamByGift switcher;

        [Tooltip("The 'other room' dream in single-player: excluded from jumps, and " +
                 "used as a gift's origin when the gift object doesn't set one itself.")]
        [UnityEngine.Serialization.FormerlySerializedAs("partnerWorld")] public DefinitionDream partnerDream;

        void OnEnable()  { if (localBag != null) localBag.OnDreamChanged += HandleLocalChanged; }
        void OnDisable() { if (localBag != null) localBag.OnDreamChanged -= HandleLocalChanged; }

        void Start()
        {
            // Show the starting dream at launch (PlayerDreamBag sets Current in Awake).
            if (switcher != null && localBag != null && localBag.Current != null)
                switcher.Switch(localBag.Current);
        }

        // Called by GiftDeliveryTrigger when a gift object crosses into this room.
        public void DeliverGift(ObjectGift obj)
        {
            if (obj == null || localBag == null) return;
            var origin = obj.originDream != null ? obj.originDream : partnerDream;
            localBag.ReceiveGift(obj.gift, origin, partnerDream);
        }

        void HandleLocalChanged(PlayerDreamBag p, DefinitionDream from, DefinitionDream to)
        {
            if (switcher != null) switcher.Switch(to);
        }
    }
}
