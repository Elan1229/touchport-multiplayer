using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;

public class MultiDemoSetup
{
    [MenuItem("Tools/Build MultiDemo Scene")]
    static void BuildScene()
    {
        // ── 1. 新建空场景 ──────────────────────────────────────────
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ── 2. 方向光 ─────────────────────────────────────────────
        var lightGO = new GameObject("Directional Light");
        var light = lightGO.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        // ── 3. 地面 ───────────────────────────────────────────────
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(3f, 1f, 3f); // 30x30 单位

        // ── 4. 构建 Player 预制体 ─────────────────────────────────
        var playerGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
        playerGO.name = "Player";

        // 子摄像机：悬在玩家后上方，斜向下看
        var camGO = new GameObject("PlayerCamera");
        camGO.transform.SetParent(playerGO.transform);
        camGO.transform.localPosition = new Vector3(0f, 8f, -6f);
        camGO.transform.localRotation = Quaternion.Euler(50f, 0f, 0f);
        var cam = camGO.AddComponent<Camera>();
        cam.enabled = false;                  // PlayerMove.OnNetworkSpawn 按需开启
        camGO.AddComponent<AudioListener>(); // 每个场景需要一个 AudioListener

        // 网络组件
        playerGO.AddComponent<NetworkObject>();
        playerGO.AddComponent<NetworkTransform>();
        playerGO.AddComponent<PlayerMove>();

        // 保存预制体
        const string prefabDir = "Assets/Prefabs";
        if (!AssetDatabase.IsValidFolder(prefabDir))
            AssetDatabase.CreateFolder("Assets", "Prefabs");

        const string prefabPath = prefabDir + "/Player.prefab";
        var playerPrefab = PrefabUtility.SaveAsPrefabAsset(playerGO, prefabPath);
        Object.DestroyImmediate(playerGO);

        // ── 5. NetworkManager ─────────────────────────────────────
        var nmGO = new GameObject("NetworkManager");
        var nm = nmGO.AddComponent<NetworkManager>();
        var transport = nmGO.AddComponent<UnityTransport>();

        // 直接通过公开 API 赋值
        nm.NetworkConfig.PlayerPrefab = playerPrefab;
        nm.NetworkConfig.NetworkTransport = transport;
        EditorUtility.SetDirty(nm);

        // AutoConnect 负责编辑器内按 Playmode tag 自动连接
        nmGO.AddComponent<AutoConnect>();

        // ── 6. 保存场景 ───────────────────────────────────────────
        const string sceneDir = "Assets/Scenes";
        if (!AssetDatabase.IsValidFolder(sceneDir))
            AssetDatabase.CreateFolder("Assets", "Scenes");

        const string scenePath = sceneDir + "/MultiDemo.unity";
        EditorSceneManager.SaveScene(scene, scenePath);
        AssetDatabase.Refresh();

        Debug.Log($"[MultiDemoSetup] 场景已创建：{scenePath}，预制体：{prefabPath}");
    }
}
