using Anaglyph.Demo;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Anaglyph.Demo.Editor
{
    /// <summary>
    /// Demo/Setup Touchport Scene 菜单：
    ///   1. 把场景里所有带 NetworkGrab 的 GO 换成 GrabbableObject
    ///      - GO 名称包含 "cube" → gameOwnerId=0（host 方块）
    ///      - GO 名称包含 "sphere/ball/ball" → gameOwnerId=1（guest 球）
    ///      - 名称含 "shared" 或 alwaysVisible 标记 → alwaysVisible=true
    ///   2. CubeGrabber GO → 删掉 CubeGrabber，加 LocalHandsReporter
    ///      （保留 LeftHandTracker / RightHandTracker 子物件引用）
    ///   3. 加 HandsManager GO（NetworkObject + HandsManager）如果没有的话
    ///   4. 加 AutoConnect GO 如果没有的话
    /// </summary>
    public static class TouchportSetup
    {
        [MenuItem("Demo/Setup Touchport Scene")]
        public static void SetupTouchportScene()
        {
            int changed = 0;

            // ── 1. NetworkGrab → GrabbableObject ─────────────────────────────────
            var networkGrabs = Object.FindObjectsByType<NetworkGrab>(FindObjectsSortMode.None);
            foreach (var ng in networkGrabs)
            {
                var go = ng.gameObject;
                string lower = go.name.ToLowerInvariant();

                bool alwaysVisible = lower.Contains("shared");
                ulong ownerId = 0;
                if (!alwaysVisible)
                {
                    if (lower.Contains("sphere") || lower.Contains("ball") || lower.Contains("mushroom"))
                        ownerId = 1;
                    else if (lower.Contains("cube") || lower.Contains("box") || lower.Contains("block"))
                        ownerId = 0;
                    else
                    {
                        // Parent name fallback
                        var p = go.transform.parent;
                        if (p != null)
                        {
                            string pl = p.name.ToLowerInvariant();
                            if (pl.Contains("sphere") || pl.Contains("ball") || pl.Contains("mushroom") || pl.Contains("world1") || pl.Contains("guest"))
                                ownerId = 1;
                            else if (pl.Contains("cube") || pl.Contains("world0") || pl.Contains("host"))
                                ownerId = 0;
                        }
                    }
                }

                // Make sure NetworkTransform exists and is server-authoritative
                if (go.GetComponent<NetworkTransform>() == null)
                    go.AddComponent<NetworkTransform>();

                // Remove NetworkGrab
                Undo.DestroyObjectImmediate(ng);

                // Add GrabbableObject
                var grab = Undo.AddComponent<GrabbableObject>(go);
                grab.gameOwnerId = ownerId;
                grab.alwaysVisible = alwaysVisible;

                Debug.Log($"[TouchportSetup] {go.name}: NetworkGrab → GrabbableObject (ownerId={ownerId}, alwaysVisible={alwaysVisible})");
                changed++;
            }

            // ── 2. CubeGrabber → LocalHandsReporter ──────────────────────────────
            var cubeGrabbers = Object.FindObjectsByType<CubeGrabber>(FindObjectsSortMode.None);
            foreach (var cg in cubeGrabbers)
            {
                var go = cg.gameObject;

                // Try to find hand tracker children before destroying
                Transform leftTracker  = go.transform.Find("LeftHandTracker");
                Transform rightTracker = go.transform.Find("RightHandTracker");

                Undo.DestroyObjectImmediate(cg);

                var reporter = Undo.AddComponent<LocalHandsReporter>(go);

                // Wire via SerializedObject so Unity records the reference
                var so = new SerializedObject(reporter);
                if (leftTracker  != null) so.FindProperty("leftHandTracker").objectReferenceValue  = leftTracker;
                if (rightTracker != null) so.FindProperty("rightHandTracker").objectReferenceValue = rightTracker;
                so.ApplyModifiedProperties();

                Debug.Log($"[TouchportSetup] {go.name}: CubeGrabber → LocalHandsReporter");
            }

            // If no CubeGrabber existed, check for LocalHandsReporter already there
            // (idempotent — skip if already set up)

            // ── 3. HandsManager GO ────────────────────────────────────────────────
            if (Object.FindFirstObjectByType<HandsManager>() == null)
            {
                var hmGO = new GameObject("HandsManager");
                Undo.RegisterCreatedObjectUndo(hmGO, "Create HandsManager");
                hmGO.AddComponent<NetworkObject>();
                hmGO.AddComponent<HandsManager>();
                Debug.Log("[TouchportSetup] Created HandsManager GO");
            }
            else
            {
                Debug.Log("[TouchportSetup] HandsManager already exists — skipped");
            }

            // ── 4. AutoConnect GO ─────────────────────────────────────────────────
            if (Object.FindFirstObjectByType<AutoConnect>() == null)
            {
                var acGO = new GameObject("AutoConnect");
                Undo.RegisterCreatedObjectUndo(acGO, "Create AutoConnect");
                acGO.AddComponent<AutoConnect>();
                Debug.Log("[TouchportSetup] Created AutoConnect GO");
            }
            else
            {
                Debug.Log("[TouchportSetup] AutoConnect already exists — skipped");
            }

            // ── Done ──────────────────────────────────────────────────────────────
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log($"[TouchportSetup] Done. Converted {changed} NetworkGrab(s). Save the scene (Ctrl+S).");
        }
    }
}
