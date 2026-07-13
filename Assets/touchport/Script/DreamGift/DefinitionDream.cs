using UnityEngine;
using UnityEngine.AddressableAssets;

namespace DreamTouch
{
    // One row of your weight table: a keyword + its 0..10 weight in a dream.
    [System.Serializable]
    public struct WeightedKeyword
    {
        public DefinitionKeyword keyword;
        [Range(0, 10)] public int weight;
    }

    // A "room card". One asset = one dream. Edit everything in the Inspector.
    [CreateAssetMenu(menuName = "DreamTouch/Dream", fileName = "DefinitionDream")]
    public class DefinitionDream : ScriptableObject
    {
        [UnityEngine.Serialization.FormerlySerializedAs("worldId")] public string dreamId;                 // e.g. "PhoneBooth"

        [Tooltip("This dream's row of the weight table (keyword + weight 0..10).")]
        public WeightedKeyword[] signature;

        [Tooltip("Gifts a visitor can carry OUT of this dream.")]
        public DefinitionGift[] gifts;

        [Header("Presentation — read by ChangeDreamByGift, never by the logic")]
        [Tooltip("The dream to load when the player becomes this dream. Drag a prefab here; " +
                 "Unity auto-marks it Addressable and only loads it on demand (Quest-friendly). " +
                 "To swap the dream, just change THIS slot — nothing else to update.")]
        [UnityEngine.Serialization.FormerlySerializedAs("worldAsset")] public AssetReferenceGameObject dreamAsset;
    }
}
