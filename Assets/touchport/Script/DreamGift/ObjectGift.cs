using UnityEngine;

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
    }
}
