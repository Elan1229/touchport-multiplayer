using Anaglyph.Demo;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Anaglyph.Demo.Editor
{
    public static class DemoSetup
    {
        [MenuItem("Demo/Build Demo Scene")]
        public static void BuildDemoScene()
        {
            // Ensure output directories exist
            System.IO.Directory.CreateDirectory("Assets/touchport/Scripts");
            System.IO.Directory.CreateDirectory("Assets/touchport/Editor");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ── Networking (NetworkManager) ───────────────────────────────────────
            var networkingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Anaglyph/LaserTag/Networking.prefab");
            if (networkingPrefab != null)
                PrefabUtility.InstantiatePrefab(networkingPrefab);
            else
                Debug.LogWarning("[DemoSetup] Networking.prefab not found — add it manually.");

            // ── ColocationManager (contains MetaSessionDiscovery for auto-connect) ──
            // Host advertises its IP via OVRColocationSession; Guests auto-discover and connect
            var colocationPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Anaglyph/LaserTag/ColocationManager.prefab");
            if (colocationPrefab != null)
                PrefabUtility.InstantiatePrefab(colocationPrefab);
            else
                Debug.LogWarning("[DemoSetup] ColocationManager.prefab not found — add it manually.");

            // ── XR Rig ───────────────────────────────────────────────────────────
            var xrRigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Anaglyph/LaserTag/XR Rig.prefab");
            if (xrRigPrefab != null)
            {
                var xrRigInstance = (GameObject)PrefabUtility.InstantiatePrefab(xrRigPrefab);
                // Disable lasertag weapons — this demo only needs bare controllers
                DisableWeaponsOnRig(xrRigInstance);
            }
            else
                Debug.LogWarning("[DemoSetup] XR Rig.prefab not found — add it manually.");

            // ── Directional Light ────────────────────────────────────────────────
            var lightGO = new GameObject("Directional Light");
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // ── Floor ────────────────────────────────────────────────────────────
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(2f, 1f, 2f);

            // ── Shared Cube ──────────────────────────────────────────────────────
            var cube = CreateSharedCube();

            // ── UI Canvas ────────────────────────────────────────────────────────
            var uiScript = CreateDemoUI(out var grabberRef);

            // ── CubeGrabber ──────────────────────────────────────────────────────
            CreateCubeGrabber(grabberRef);

            // ── EventSystem (XR-compatible) ──────────────────────────────────────
            var evGO = new GameObject("EventSystem");
            evGO.AddComponent<EventSystem>();
            evGO.AddComponent<XRUIInputModule>();

            // ── Save ─────────────────────────────────────────────────────────────
            EditorSceneManager.SaveScene(scene, "Assets/touchport/Demo.unity");
            AssetDatabase.Refresh();

            Debug.Log("[DemoSetup] Done. Open Assets/touchport/Demo.unity to start.");
        }

        // ─────────────────────────────────────────────────────────────────────────

        private static GameObject CreateSharedCube()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "SharedCube";
            // Position it ~1.5 m in front of the player at waist height
            cube.transform.position = new Vector3(0f, 1f, 1.5f);
            cube.transform.localScale = new Vector3(0.3f, 0.3f, 0.3f);

            // Colour
            var rend = cube.GetComponent<MeshRenderer>();
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            if (mat.shader == null || mat.shader.name == "Hidden/InternalErrorShader")
                mat = new Material(Shader.Find("Standard")); // fallback for Editor without URP active
            mat.color = new Color(0.2f, 0.6f, 1f);
            rend.sharedMaterial = mat;
            AssetDatabase.CreateAsset(mat, "Assets/touchport/SharedCubeMat.mat");

            // Rigidbody — kinematic so it floats until grabbed
            var rb = cube.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            // Netcode components
            cube.AddComponent<NetworkObject>();
            cube.AddComponent<NetworkTransform>();
            cube.AddComponent<NetworkGrab>();

            // Save as prefab asset and keep the scene instance linked to it
            PrefabUtility.SaveAsPrefabAssetAndConnect(
                cube, "Assets/touchport/SharedCube.prefab",
                InteractionMode.AutomatedAction);

            return cube;
        }

        // ─────────────────────────────────────────────────────────────────────────

        private static DemoNetworkUI CreateDemoUI(out GameObject grabberParent)
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                    ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

            // Root canvas — small, off to the left, not blocking center view ─────
            var canvasGO = new GameObject("Demo UI");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            var canvasRT = canvasGO.GetComponent<RectTransform>();
            canvasRT.sizeDelta = new Vector2(500f, 300f);
            canvasRT.localScale = Vector3.one * 0.001f;
            canvasGO.transform.position = new Vector3(-0.4f, 1.3f, 1.0f);
            canvasGO.transform.rotation = Quaternion.Euler(0f, 20f, 0f);

            var uiScript = canvasGO.AddComponent<DemoNetworkUI>();

            // Semi-transparent background ─────────────────────────────────────────
            var bg = MakePanel(canvasGO.transform, "BG", new Color(0f, 0f, 0f, 0.7f), font);
            StretchToParent(bg);

            // Single status label — shows "Looking for host..." / "Connecting..." etc.
            var statusLbl = MakeLabel(canvasGO.transform, "StatusText",
                "Looking for host...", 44, font,
                new Vector2(0, 0), new Vector2(460, 80));

            // CubeGrabber lives at root level ─────────────────────────────────────
            grabberParent = new GameObject("CubeGrabber");

            // Wire serialized fields ──────────────────────────────────────────────
            var so = new SerializedObject(uiScript);
            so.FindProperty("statusText").objectReferenceValue = statusLbl;
            so.ApplyModifiedProperties();

            return uiScript;
        }

        private static void CreateCubeGrabber(GameObject grabberGO)
        {
            var grabber = grabberGO.AddComponent<CubeGrabber>();

            var leftTracker = new GameObject("LeftHandTracker");
            leftTracker.transform.SetParent(grabberGO.transform, false);

            var rightTracker = new GameObject("RightHandTracker");
            rightTracker.transform.SetParent(grabberGO.transform, false);

            var so = new SerializedObject(grabber);
            so.FindProperty("leftHandTracker").objectReferenceValue = leftTracker.transform;
            so.FindProperty("rightHandTracker").objectReferenceValue = rightTracker.transform;
            so.ApplyModifiedProperties();
        }

        // ─── Weapon removal ──────────────────────────────────────────────────────

        // Search by component type name so we don't need to reference the lasertag assembly
        private static readonly string[] WeaponTypeNames = { "Blaster", "Automatic", "ToolPalette", "Tool Palette" };

        private static void DisableWeaponsOnRig(GameObject rig)
        {
            foreach (var mb in rig.GetComponentsInChildren<MonoBehaviour>(true))
            {
                var typeName = mb.GetType().Name;
                foreach (var weaponName in WeaponTypeNames)
                {
                    if (typeName == weaponName)
                    {
                        mb.enabled = false; // disable shooting logic, keep the mesh visible
                        break;
                    }
                }
            }
        }

        // ─── UI helpers ──────────────────────────────────────────────────────────

        private static void StretchToParent(GameObject go)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static GameObject MakePanel(Transform parent, string name, Color color, Font font)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var img = go.AddComponent<Image>();
            img.color = color;
            return go;
        }

        private static Text MakeLabel(Transform parent, string name, string text,
            int fontSize, Font font, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var t = go.AddComponent<Text>();
            t.text = text;
            t.fontSize = fontSize;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            t.font = font;
            return t;
        }

    }
}
