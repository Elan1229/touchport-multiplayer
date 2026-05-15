using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "DreamElementDatabase", menuName = "touchport/Dream Element Database")]
public class DreamElementDatabase : ScriptableObject
{
    public GameObject[] allPrefabs;

    private readonly Dictionary<DreamElement.ElementType, List<GameObject>> byType =
        new Dictionary<DreamElement.ElementType, List<GameObject>>();

    private readonly Dictionary<string, GameObject> byID = new Dictionary<string, GameObject>();
    private readonly List<GameObject> validPrefabs = new List<GameObject>();

    // ScriptableObject 加载时建立 ID/type 索引，运行时查询不用反复遍历 allPrefabs。
    private void OnEnable()
    {
        RebuildIndexes();
    }

    // Inspector 修改 allPrefabs 后立即重建索引，减少运行后才发现配置问题。
    private void OnValidate()
    {
        RebuildIndexes();
    }

    // 通过 elementID 找 prefab；DreamManager 生成世界时主要走这个入口。
    public GameObject GetByID(string id)
    {
        EnsureIndexes();
        return !string.IsNullOrWhiteSpace(id) && byID.TryGetValue(id, out var prefab)
            ? prefab
            : null;
    }

    // 从所有有效 prefab 里随机抽 count 个不重复 ID，用作初始世界的玩家 slots。
    public List<string> GetRandomIDs(int count)
    {
        EnsureIndexes();

        var shuffled = new List<GameObject>(validPrefabs);
        Shuffle(shuffled);

        var result = new List<string>();
        for (int i = 0; i < count && i < shuffled.Count; i++)
        {
            var element = shuffled[i].GetComponent<DreamElement>();
            if (element != null)
                result.Add(element.elementID);
        }

        return result;
    }

    // 随机抽一个不在 exclude 里的词条 ID，作为通用 fallback。
    public string GetRandomIDExcluding(IReadOnlyCollection<string> exclude)
    {
        EnsureIndexes();

        var excluded = exclude != null ? new HashSet<string>(exclude) : new HashSet<string>();
        var available = new List<GameObject>();

        foreach (var prefab in validPrefabs)
        {
            var element = prefab.GetComponent<DreamElement>();
            if (element != null && !excluded.Contains(element.elementID))
                available.Add(prefab);
        }

        if (available.Count == 0)
            return null;

        var selected = available[Random.Range(0, available.Count)];
        return selected.GetComponent<DreamElement>().elementID;
    }

    // 随机抽一个指定类型且不在 exclude 里的词条 ID，用来补 Room/Skybox/Object。
    public string GetRandomIDOfTypeExcluding(DreamElement.ElementType type, IReadOnlyCollection<string> exclude)
    {
        EnsureIndexes();

        if (!byType.TryGetValue(type, out var list) || list.Count == 0)
            return null;

        var excluded = exclude != null ? new HashSet<string>(exclude) : new HashSet<string>();
        var available = new List<GameObject>();

        foreach (var prefab in list)
        {
            var element = prefab.GetComponent<DreamElement>();
            if (element != null && !excluded.Contains(element.elementID))
                available.Add(prefab);
        }

        if (available.Count == 0)
            return null;

        var selected = available[Random.Range(0, available.Count)];
        return selected.GetComponent<DreamElement>().elementID;
    }

    // 随机取一个指定类型 prefab；当玩家 slots 没有房间或天空时作为生成 fallback。
    public GameObject GetRandomOfType(DreamElement.ElementType type)
    {
        EnsureIndexes();

        if (!byType.TryGetValue(type, out var list) || list.Count == 0)
            return null;

        return list[Random.Range(0, list.Count)];
    }

    // 查询某个 ID 对应的词条类型，用于 DreamManager 分类四个词条。
    public bool TryGetType(string id, out DreamElement.ElementType type)
    {
        type = default;

        var prefab = GetByID(id);
        if (prefab == null) return false;

        var element = prefab.GetComponent<DreamElement>();
        if (element == null) return false;

        type = element.elementType;
        return true;
    }

    // 如果索引为空但 allPrefabs 已有内容，就补建一次索引。
    private void EnsureIndexes()
    {
        if (validPrefabs.Count == 0 && allPrefabs != null && allPrefabs.Length > 0)
            RebuildIndexes();
    }

    // 重建 byID 和 byType，同时跳过空 prefab、缺 DreamElement、空 ID 和重复 ID。
    private void RebuildIndexes()
    {
        byType.Clear();
        byID.Clear();
        validPrefabs.Clear();

        if (allPrefabs == null) return;

        foreach (var prefab in allPrefabs)
        {
            if (prefab == null) continue;

            var element = prefab.GetComponent<DreamElement>();
            if (element == null || string.IsNullOrWhiteSpace(element.elementID))
            {
                Debug.LogWarning($"[DreamElementDatabase] {prefab.name} is missing DreamElement or elementID.", prefab);
                continue;
            }

            if (byID.ContainsKey(element.elementID))
            {
                Debug.LogWarning($"[DreamElementDatabase] Duplicate elementID {element.elementID}; skipped {prefab.name}.", prefab);
                continue;
            }

            validPrefabs.Add(prefab);
            byID[element.elementID] = prefab;

            if (!byType.TryGetValue(element.elementType, out var list))
            {
                list = new List<GameObject>();
                byType[element.elementType] = list;
            }

            list.Add(prefab);
        }
    }

    // Fisher-Yates 洗牌，用于随机抽取词条。
    private static void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            T temp = list[i];
            list[i] = list[j];
            list[j] = temp;
        }
    }
}
