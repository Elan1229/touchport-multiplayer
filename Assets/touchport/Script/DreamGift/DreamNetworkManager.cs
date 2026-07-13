using UnityEngine;

namespace DreamTouch
{
    // The "logistics center". It holds NO rule — it just moves data in and out:
    //   - keeps the partner's current dream in sync (from your networking layer)
    //   - delivers incoming gifts into the local bag
    //   - when the local dream changes, tells the switcher + (TODO) broadcasts to the peer
    public class DreamNetworkManager : MonoBehaviour
    {
        public PlayerDreamBag localBag;
        public ChangeDreamByGift switcher;

        [Tooltip("Which dream the OTHER player is in right now. Kept in sync by your networking layer.")]
        public DefinitionDream partnerCurrentDream;

        void OnEnable()  { if (localBag != null) localBag.OnDreamChanged += HandleLocalChanged; }
        void OnDisable() { if (localBag != null) localBag.OnDreamChanged -= HandleLocalChanged; }

        // Call this when a gift object is dropped into the local player's dream.
        public void DeliverGift(ObjectGift obj)
        {
            if (obj == null || localBag == null) return;
            localBag.ReceiveGift(obj.gift, obj.originDream, partnerCurrentDream);
        }

        // Call this from the networking layer when the peer tells you they changed dream.
        public void SetPartnerDream(DefinitionDream dream)
        {
            partnerCurrentDream = dream;
        }

        void HandleLocalChanged(PlayerDreamBag p, DefinitionDream from, DefinitionDream to)
        {
            if (switcher != null) switcher.Switch(to);   // (1) swap visuals locally
            // (2) TODO networking: broadcast `to` so the peer calls SetPartnerDream(to) on their side.
        }
    }
}
