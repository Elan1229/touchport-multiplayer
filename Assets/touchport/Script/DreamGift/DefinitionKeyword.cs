using UnityEngine;

namespace DreamTouch
{
    // A keyword is just an asset. Its file name IS its id (e.g. "Toy", "Booth").
    // Add a new keyword by creating a new asset — no code change / recompile needed.
    [CreateAssetMenu(menuName = "DreamTouch/Keyword", fileName = "DefinitionKeyword")]
    public class DefinitionKeyword : ScriptableObject
    {
        public string Id => name;
    }
}
