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

        [Header("Series & hidden room — read by PlayerDreamBag")]
        [Tooltip("这个梦属于哪个系列（Playroom / Booth / Asian）。玩家平时只在【起始梦所在系列】的房间之间换。")]
        public string series;

        [Tooltip("只给隐藏房间填：连续两件礼物都带这里任意一个关键词，玩家就会被送来这个梦。普通房间留空。")]
        public DefinitionKeyword[] hiddenTrigger;

        [Tooltip("一件礼物同时命中多个隐藏房间时，数字小的优先。")]
        public int hiddenPriority;

        public bool IsHidden => hiddenTrigger != null && hiddenTrigger.Length > 0;

        [Header("Presentation — read by ChangeDreamByGift, never by the logic")]
        [Tooltip("The dream to load when the player becomes this dream. Drag a prefab here; " +
                 "Unity auto-marks it Addressable and only loads it on demand (Quest-friendly). " +
                 "To swap the dream, just change THIS slot — nothing else to update.")]
        [UnityEngine.Serialization.FormerlySerializedAs("worldAsset")] public AssetReferenceGameObject dreamAsset;
    }
}
