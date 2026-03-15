using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;

public class MultiDemo2Setup
{
    [MenuItem("Tools/Build MultiDemo2 Scene")]
    static void BuildScene()
    {
        // ── 1. 新建空场景 ─────────────────────────────────────────
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ── 2. 方向光 ──────────────────────────────────────────────
        var lightGO = new GameObject("Directional Light");
        var light = lightGO.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        // ── 3. 地面（50x50）────────────────────────────────────────
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(5f, 1f, 5f);

        // ── 4. Prefabs 文件夹 ──────────────────────────────────────
        const string prefabDir = "Assets/Prefabs";
        if (!AssetDatabase.IsValidFolder(prefabDir))
            AssetDatabase.CreateFolder("Assets", "Prefabs");

        // ── 5. 玩家预制体 ──────────────────────────────────────────
        //   根节点：空 GO，挂 NetworkObject / NetworkTransform / PlayerMove
        //   子物体：CubeVisual（默认显示）、SphereVisual（默认隐藏）、PlayerCamera
        var playerRoot = new GameObject("Player");

        // CubeVisual
        var cubeVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cubeVisual.name = "CubeVisual";
        cubeVisual.transform.SetParent(playerRoot.transform);
        cubeVisual.transform.localPosition = Vector3.zero;
        cubeVisual.transform.localScale = Vector3.one;

        // SphereVisual（默认隐藏）
        var sphereVisual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphereVisual.name = "SphereVisual";
        sphereVisual.transform.SetParent(playerRoot.transform);
        sphereVisual.transform.localPosition = Vector3.zero;
        sphereVisual.transform.localScale = Vector3.one;
        sphereVisual.SetActive(false);

        // PlayerCamera
        var camGO = new GameObject("PlayerCamera");
        camGO.transform.SetParent(playerRoot.transform);
        camGO.transform.localPosition = new Vector3(0f, 8f, -6f);
        camGO.transform.localRotation = Quaternion.Euler(50f, 0f, 0f);
        var cam = camGO.AddComponent<Camera>();
        cam.enabled = false;
        camGO.AddComponent<AudioListener>();

        // 网络组件挂在根节点
        playerRoot.AddComponent<NetworkObject>();
        playerRoot.AddComponent<NetworkTransform>();
        playerRoot.AddComponent<PlayerMove>();

        var playerPrefab = PrefabUtility.SaveAsPrefabAsset(playerRoot, prefabDir + "/Player2.prefab");
        Object.DestroyImmediate(playerRoot);

        // ── 6. 小物体：方块（左侧，gameOwnerId=0）───────────────
        for (int i = 0; i < 5; i++)
            PlaceStickyObject(PrimitiveType.Cube, $"StickyCube_{i}", 0,
                new Vector3(-10f, 0.3f, (i - 2) * 2.5f));

        // ── 7. 小物体：球球（右侧，gameOwnerId=1）───────────────
        for (int i = 0; i < 5; i++)
            PlaceStickyObject(PrimitiveType.Sphere, $"StickySphere_{i}", 1,
                new Vector3(10f, 0.3f, (i - 2) * 2.5f));

        // ── 8. NetworkManager ──────────────────────────────────────
        var nmGO = new GameObject("NetworkManager");
        var nm = nmGO.AddComponent<NetworkManager>();
        var transport = nmGO.AddComponent<UnityTransport>();
        nm.NetworkConfig.NetworkTransport = transport;
        // 注册玩家预制体（动态 Spawn 需要注册）
        nm.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = playerPrefab });
        EditorUtility.SetDirty(nm);
        nmGO.AddComponent<AutoConnect>();

        // ── 9. GameManager（PlayerSpawner）────────────────────────
        var gmGO = new GameObject("GameManager");
        gmGO.AddComponent<NetworkObject>();
        var spawner = gmGO.AddComponent<PlayerSpawner>();
        spawner.playerPrefab = playerPrefab;
        EditorUtility.SetDirty(gmGO);

        // ── 10. 保存场景 ───────────────────────────────────────────
        const string sceneDir = "Assets/Scenes";
        if (!AssetDatabase.IsValidFolder(sceneDir))
            AssetDatabase.CreateFolder("Assets", "Scenes");

        const string scenePath = sceneDir + "/MultiDemo2.unity";
        EditorSceneManager.SaveScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene(), scenePath);
        AssetDatabase.Refresh();

        Debug.Log("[MultiDemo2Setup] 完成：" + scenePath);
    }

    static void PlaceStickyObject(PrimitiveType shape, string goName, ulong ownerId, Vector3 pos)
    {
        var go = GameObject.CreatePrimitive(shape);
        go.name = goName;
        go.transform.position = pos;
        go.transform.localScale = Vector3.one * 0.5f;

        // 触发器 + 刚体（让 Unity 物理系统触发 OnTriggerEnter）
        go.GetComponent<Collider>().isTrigger = true;
        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        go.AddComponent<NetworkObject>();
        go.AddComponent<NetworkTransform>();
        var sticky = go.AddComponent<StickyObject>();
        sticky.gameOwnerId = ownerId;
    }
}
