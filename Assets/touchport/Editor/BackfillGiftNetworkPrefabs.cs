using System.Collections.Generic;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace DreamTouch.EditorTools
{
    // 一次性工具：扫 Assets/touchport/Dreams 下所有房间 prefab，把已经烤在里面、还没填过
    // networkPrefab 的 ObjectGift marker 自动补上（读它链接的源 prefab，跟 ObjectGift.OnValidate
    // 干的是同一件事）。历史上已经放好的 marker 不会自动触发 OnValidate，所以补一次；以后新拖进去
    // 的 marker 保存时会自己填，不用再跑这个。可以反复跑，已经填过的会跳过。
    //
    // 顺带两个校验，跑完都会在 console 汇总:
    // - 找源 prefab 时一层层往上找，直到找到第一个带 NetworkObject 的——不会把一个不带
    //   NetworkObject 的 prefab 写进 networkPrefab 字段。
    // - 把所有实际用到的 networkPrefab 去重后，核对是否都在 NetworkPrefabsList 里注册过；
    //   没注册的打印清单（不假设"应该都注册过了"，跑一遍确认）。
    public static class BackfillGiftNetworkPrefabs
    {
        const string DreamsFolder = "Assets/touchport/Dreams";
        const string NetworkPrefabsListPath = "Assets/Settings/DefaultNetworkPrefabs.asset";

        [MenuItem("Tools/DreamTouch/Backfill Gift Network Prefabs")]
        static void Run()
        {
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { DreamsFolder });
            int filled = 0, alreadySet = 0, unresolved = 0, prefabsSaved = 0, roomsScanned = 0;
            var usedPrefabs = new HashSet<GameObject>();

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(path);
                bool dirty = false;
                roomsScanned++;

                foreach (var marker in root.GetComponentsInChildren<ObjectGift>(true))
                {
                    if (marker.networkPrefab != null)
                    {
                        alreadySet++;
                        usedPrefabs.Add(marker.networkPrefab);
                        continue;
                    }

                    var resolved = ResolveNetworkPrefab(marker.gameObject);
                    if (resolved == null)
                    {
                        Debug.LogWarning($"[BackfillGiftNetworkPrefabs] '{marker.name}' in '{path}' has no " +
                                         "source prefab with a NetworkObject on it, skipping — assign " +
                                         "networkPrefab by hand.", marker);
                        unresolved++;
                        continue;
                    }

                    marker.networkPrefab = resolved;
                    usedPrefabs.Add(resolved);
                    filled++;
                    dirty = true;
                }

                if (dirty)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    prefabsSaved++;
                }
                PrefabUtility.UnloadPrefabContents(root);
            }

            CheckRegistration(usedPrefabs);

            Debug.Log($"[BackfillGiftNetworkPrefabs] Done: rooms scanned {roomsScanned}, filled {filled}, " +
                      $"already set {alreadySet}, unresolved {unresolved}, prefabs saved {prefabsSaved}.");
        }

        // 有些 marker 嵌套了不止一层 prefab variant——一层层往上找源 prefab，
        // 找到第一个自带 NetworkObject 的就是登记表里那个真身；找到底都没有就放弃。
        static GameObject ResolveNetworkPrefab(GameObject marker)
        {
            var current = marker;
            for (int i = 0; i < 8; i++)
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(current);
                if (source == null) return null;
                if (source.GetComponent<NetworkObject>() != null) return source;
                current = source;
            }
            return null;
        }

        static void CheckRegistration(HashSet<GameObject> usedPrefabs)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsListPath);
            if (list == null)
            {
                Debug.LogError($"[BackfillGiftNetworkPrefabs] Could not find {NetworkPrefabsListPath}, " +
                               "skipped registration check.");
                return;
            }

            var missing = new List<string>();
            foreach (var prefab in usedPrefabs)
                if (!list.Contains(prefab))
                    missing.Add(prefab.name);

            if (missing.Count > 0)
                Debug.LogWarning($"[BackfillGiftNetworkPrefabs] {missing.Count} networkPrefab(s) NOT " +
                                 $"registered in {NetworkPrefabsListPath}: {string.Join(", ", missing)}");
            else
                Debug.Log($"[BackfillGiftNetworkPrefabs] All {usedPrefabs.Count} distinct networkPrefab(s) " +
                          "are registered in the NetworkPrefabsList.");
        }
    }
}
