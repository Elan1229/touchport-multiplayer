using UnityEngine;

public class DreamElement : MonoBehaviour
{
    // 词条类型：DreamManager 会按这个分类生成天空、房间或物件。
    public enum ElementType
    {
        Skybox,
        Room,
        Object
    }

    [Header("Element")]
    public ElementType elementType;
    public string elementID;
    public string displayName;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? elementID : displayName;

    // Inspector 添加组件时自动填一个默认 ID 和显示名，方便先跑通流程。
    private void Reset()
    {
        if (string.IsNullOrWhiteSpace(elementID))
            elementID = gameObject.name;

        if (string.IsNullOrWhiteSpace(displayName))
            displayName = gameObject.name;
    }

    // Inspector 改字段时同步显示名和提示文本，避免 UI 文案忘记更新。
    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(displayName))
            displayName = elementID;
    }
}
