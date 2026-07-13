using UnityEngine;

namespace DreamTouch
{
    // A "gift card". One asset = one gift type. Keywords are set ONCE here,
    // not on every scene object. Origin is NOT stored here — it's contextual
    // (which dream the physical object came from) and lives on ObjectGift.
    [CreateAssetMenu(menuName = "DreamTouch/Gift", fileName = "DefinitionGift")]
    public class DefinitionGift : ScriptableObject
    {
        public string giftName;        // "Telephone Handset"

        [Tooltip("Carrier keywords this gift injects into the bag, e.g. [Computer].")]
        public DefinitionKeyword[] keywords;

        [Header("Presentation (optional)")]
        [Tooltip("The physical object to spawn for this gift.")]
        public GameObject giftPrefab;
    }
}
