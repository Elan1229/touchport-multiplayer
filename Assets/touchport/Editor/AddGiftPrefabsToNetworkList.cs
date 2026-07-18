using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace DreamTouch.EditorTools
{
    // 把当前在 Project 窗口里选中的 prefab（可以多选）批量加进 DefaultNetworkPrefabs.asset。
    // 只加带 NetworkObject 的；已经在列表里的会跳过，不会重复加，可以反复跑。
    public static class AddGiftPrefabsToNetworkList
    {
        const string NetworkPrefabsListPath = "Assets/Settings/DefaultNetworkPrefabs.asset";

        [MenuItem("Tools/DreamTouch/Add Selected Prefabs To Network List")]
        static void Run()
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsListPath);
            if (list == null)
            {
                Debug.LogError($"[AddGiftPrefabsToNetworkList] Could not find {NetworkPrefabsListPath}");
                return;
            }

            var selected = Selection.GetFiltered<GameObject>(SelectionMode.Assets);
            if (selected.Length == 0)
            {
                Debug.LogWarning("[AddGiftPrefabsToNetworkList] No prefabs selected in the Project window.");
                return;
            }

            int added = 0, skippedNoNetObj = 0, skippedDup = 0;

            foreach (var prefab in selected)
            {
                if (prefab.GetComponent<NetworkObject>() == null)
                {
                    Debug.Log($"[AddGiftPrefabsToNetworkList] Skipped '{prefab.name}': missing NetworkObject.");
                    skippedNoNetObj++;
                    continue;
                }

                if (list.Contains(prefab))
                {
                    skippedDup++;
                    continue;
                }

                list.Add(new NetworkPrefab { Prefab = prefab });
                added++;
            }

            EditorUtility.SetDirty(list);
            AssetDatabase.SaveAssets();

            Debug.Log($"[AddGiftPrefabsToNetworkList] Done: added {added}, " +
                      $"skipped {skippedNoNetObj} (missing NetworkObject), skipped {skippedDup} (already in list).");
        }
    }
}
