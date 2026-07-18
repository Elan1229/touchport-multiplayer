using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DreamTouch
{
    // Put this on a pickable gift GameObject in the scene.
    // Just drag in which gift it is; set originDream when it is spawned in a dream.
    // The code reads keywords from `gift` — you never re-type keywords per object.
    public class ObjectGift : MonoBehaviour
    {
        [Tooltip("Which gift this object is (the gift card).")]
        public DefinitionGift gift;

        [Tooltip("The dream this object came from. Used for the 'own item is inert' rule.")]
        [UnityEngine.Serialization.FormerlySerializedAs("originWorld")] public DefinitionDream originDream;

        [Tooltip("Which dream this object is CURRENTLY sitting in, right now. Runtime-managed " +
                 "(set on spawn + updated on every portal crossing by GiftDeliveryTrigger) — " +
                 "don't author this by hand. originDream != currentDream means it's away from " +
                 "home (was delivered here); crossing out again is a 'take back', not a delivery.")]
        public DefinitionDream currentDream;

        [Tooltip("The registered NetworkObject prefab DreamNetworkManager.SpawnGift() Instantiates+Spawns " +
                 "in this marker's place (this marker itself, baked into a dream room prefab, is never " +
                 "Spawn()'d directly — see SpawnGift for why). Auto-filled on save from this instance's " +
                 "source prefab; leave empty and it fills itself.")]
        public GameObject networkPrefab;

#if UNITY_EDITOR
        void OnValidate()
        {
            if (networkPrefab != null) return;
            var source = PrefabUtility.GetCorrespondingObjectFromSource(gameObject);
            if (source != null) networkPrefab = source;
        }
#endif
    }
}
