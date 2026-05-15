using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class DreamManager : MonoBehaviour
{
    public DreamElementDatabase elementDB;
    public DreamCore dreamCore;
    public Transform spawnRoot;
    public Transform[] objectSpawnPoints;

    [Header("World")]
    [SerializeField] private bool startWithRandomWorld = true;
    [SerializeField] private float expandDelay = 1.5f;

    [Header("Input")]
    [SerializeField] private Key expandKey = Key.F;
    [SerializeField] private bool expandWhenCorePlacedInAvatar = true;

    private GameObject currentRoom;
    private GameObject currentSkybox;
    private readonly List<GameObject> currentObjects = new List<GameObject>();
    private Coroutine expandRoutine;

    // 添加组件时给 spawnRoot 和 dreamCore 自动填默认值，减少 Inspector 手工步骤。
    private void Reset()
    {
        spawnRoot = transform;
        dreamCore = FindDreamCore();
    }

    // 启动前补齐必要引用；DreamManager 只读 DreamCore，不让 DreamCore 反向依赖它。
    private void Awake()
    {
        if (spawnRoot == null)
            spawnRoot = transform;

        if (dreamCore == null)
            dreamCore = FindDreamCore();
    }

    // 每帧由 DreamManager 统一判断是否展开世界：按 F 或 DreamCore 碰到 Head。
    private void Update()
    {
        if (ShouldExpand())
            RequestExpandFromDreamCore();
    }

    // 初始世界：先随机给玩家 3 个 slots，再按规则补第 4 个系统词条并生成。
    private void Start()
    {
        if (!startWithRandomWorld) return;
        if (!HasDatabase()) return;

        List<string> randomSlots = elementDB.GetRandomIDs(3);
        string systemSlot = ChooseSystemSlot(randomSlots);
        SwitchWorld(randomSlots, systemSlot);
    }

    // 外部或 Update 请求展开时进入这里；同一时间只允许一个展开协程在跑。
    public bool RequestExpand(IReadOnlyList<string> slots)
    {
        if (!HasDatabase())
            return false;

        if (expandRoutine != null)
            return false;

        Debug.Log($"[DreamManager] Expanding with slots: {string.Join(", ", slots ?? Array.Empty<string>())}", this);
        expandRoutine = StartCoroutine(TriggerExpand(slots));
        return true;
    }

    // 展开流程：复制玩家 slots，挑系统词条，等待动效时间，然后切换世界。
    public IEnumerator TriggerExpand(IReadOnlyList<string> slots)
    {
        var playerSlots = slots != null ? new List<string>(slots) : new List<string>();
        string systemSlot = ChooseSystemSlot(playerSlots);

        yield return new WaitForSeconds(expandDelay);

        SwitchWorld(playerSlots, systemSlot);

        expandRoutine = null;
    }

    // 判断这一帧是否要展开：F 键由 Manager 处理，头像触发由 DreamCore 提供一次性标记。
    private bool ShouldExpand()
    {
        if (WasPressedThisFrame(expandKey))
            return true;

        return expandWhenCorePlacedInAvatar
            && dreamCore != null
            && dreamCore.ConsumeAvatarExpandRequest();
    }

    // 从 DreamCore 拉取当前 slots，并请求展开；这是 DreamCore -> Manager 的单向读取点。
    private void RequestExpandFromDreamCore()
    {
        if (dreamCore == null)
            dreamCore = FindDreamCore();

        if (dreamCore == null)
        {
            Debug.LogWarning("[DreamManager] dreamCore is not assigned.", this);
            return;
        }

        RequestExpand(dreamCore.slots);
    }

    // 根据玩家 3 个 slots 和系统词条生成完整世界：Skybox、Room、Objects 分开处理。
    public void SwitchWorld(IReadOnlyList<string> playerSlots, string systemSlot)
    {
        if (!HasDatabase()) return;

        var allSlots = new List<string>();
        if (playerSlots != null)
            allSlots.AddRange(playerSlots);
        if (!string.IsNullOrWhiteSpace(systemSlot))
            allSlots.Add(systemSlot);

        string skyboxID = null;
        string roomID = null;
        var objectIDs = new List<string>();

        foreach (var id in allSlots)
        {
            if (!elementDB.TryGetType(id, out var type))
            {
                Debug.LogWarning($"[DreamManager] Unknown element ID: {id}", this);
                continue;
            }

            if (type == DreamElement.ElementType.Skybox && skyboxID == null)
                skyboxID = id;
            else if (type == DreamElement.ElementType.Room && roomID == null)
                roomID = id;
            else if (type == DreamElement.ElementType.Object)
                objectIDs.Add(id);
        }

        ClearCurrentWorld();

        currentSkybox = SpawnWorldPart(
            skyboxID != null ? elementDB.GetByID(skyboxID) : elementDB.GetRandomOfType(DreamElement.ElementType.Skybox),
            spawnRoot,
            "Skybox");

        currentRoom = SpawnWorldPart(
            roomID != null ? elementDB.GetByID(roomID) : elementDB.GetRandomOfType(DreamElement.ElementType.Room),
            spawnRoot,
            "Room");

        SpawnObjects(objectIDs);
    }

    // 按 objectSpawnPoints 顺序生成 Object 类型词条，有几个 object slot 就放几个。
    private void SpawnObjects(IReadOnlyList<string> objectIDs)
    {
        if (objectIDs == null || objectIDs.Count == 0) return;

        int spawnCount = Mathf.Min(objectIDs.Count, objectSpawnPoints != null ? objectSpawnPoints.Length : 0);
        if (spawnCount == 0)
        {
            Debug.LogWarning("[DreamManager] Object slots exist, but objectSpawnPoints is empty.", this);
            return;
        }

        for (int i = 0; i < spawnCount; i++)
        {
            var prefab = elementDB.GetByID(objectIDs[i]);
            var point = objectSpawnPoints[i];
            if (prefab == null || point == null) continue;

            var go = Instantiate(prefab, point.position, point.rotation, spawnRoot);
            currentObjects.Add(go);
        }
    }

    // 实例化 Skybox 或 Room；prefab 缺失时只报警，不中断整个流程。
    private GameObject SpawnWorldPart(GameObject prefab, Transform parent, string label)
    {
        if (prefab == null)
        {
            Debug.LogWarning($"[DreamManager] Missing prefab for {label}.", this);
            return null;
        }

        return Instantiate(prefab, parent);
    }

    // 切世界前销毁上一轮生成的 Room、Skybox 和 Objects。
    private void ClearCurrentWorld()
    {
        if (currentRoom != null)
            Destroy(currentRoom);

        if (currentSkybox != null)
            Destroy(currentSkybox);

        foreach (var go in currentObjects)
        {
            if (go != null)
                Destroy(go);
        }

        currentRoom = null;
        currentSkybox = null;
        currentObjects.Clear();
    }

    // 检查数据库引用是否存在；缺失时给出明确 warning。
    private bool HasDatabase()
    {
        if (elementDB != null)
            return true;

        Debug.LogWarning("[DreamManager] elementDB is not assigned.", this);
        return false;
    }

    // 决定第 4 个系统词条：优先补 Room，再补 Skybox，都有时补随机 Object。
    private string ChooseSystemSlot(IReadOnlyList<string> playerSlots)
    {
        var usedSlots = playerSlots != null ? new List<string>(playerSlots) : new List<string>();
        bool hasSkybox = HasSlotOfType(usedSlots, DreamElement.ElementType.Skybox);
        bool hasRoom = HasSlotOfType(usedSlots, DreamElement.ElementType.Room);

        if (!hasRoom)
            return elementDB.GetRandomIDOfTypeExcluding(DreamElement.ElementType.Room, usedSlots)
                ?? elementDB.GetRandomIDExcluding(usedSlots);

        if (!hasSkybox)
            return elementDB.GetRandomIDOfTypeExcluding(DreamElement.ElementType.Skybox, usedSlots)
                ?? elementDB.GetRandomIDExcluding(usedSlots);

        return elementDB.GetRandomIDOfTypeExcluding(DreamElement.ElementType.Object, usedSlots)
            ?? elementDB.GetRandomIDExcluding(usedSlots);
    }

    // 检查一组 slots 里是否已经包含指定类型。
    private bool HasSlotOfType(IEnumerable<string> slots, DreamElement.ElementType type)
    {
        foreach (var id in slots)
        {
            if (elementDB.TryGetType(id, out var slotType) && slotType == type)
                return true;
        }

        return false;
    }

    // 用 Input System 检查键盘按键是否在本帧按下。
    private static bool WasPressedThisFrame(Key key)
    {
        var keyboard = Keyboard.current;
        return keyboard != null && key != Key.None && keyboard[key].wasPressedThisFrame;
    }

    // 场景里没手动拖 dreamCore 时，自动找一个 DreamCore。
    private static DreamCore FindDreamCore()
    {
#if UNITY_2023_1_OR_NEWER || UNITY_6000_0_OR_NEWER
        return FindFirstObjectByType<DreamCore>();
#else
        return FindObjectOfType<DreamCore>();
#endif
    }
}
