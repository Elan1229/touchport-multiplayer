using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace DreamTouch
{
    // THE networking file. Server-authoritative dream state for both worlds.
    // Mirrors LetterTaskState: NetworkVariables replicate each world's current-dream index;
    // clients react by swapping visuals via ChangeDreamByGift. The pure-logic PlayerDreamBag
    // (one per world) runs on the SERVER only. Put this on GameManager (already a NetworkObject),
    // next to LetterTaskState.
    //
    // 换梦时间轴（礼物穿门 = T0）：
    //   T0            server 立即 bag 结算；跳梦则上缓冲锁 + 写 PendingXIndex（预告通道）
    //   T0~T0+delay   两端收到 pending -> Preload 目标房间（inactive、progress=1 待命）；
    //                 server 同时读预载房间的 marker，预实例化礼物真身（inactive、不 Spawn）
    //   T0+delay      server 写正式 DreamXIndex -> 两端 Switch：交叉溶解（新梦激活后旧梦
    //                 溶出、新梦凝入同时跑，portal 里全程有内容；旧礼物同时摊帧 despawn）
    //   凝入完成       OnTransitionComplete -> 解缓冲锁 + 新礼物摊帧 Spawn（每帧几个）；
    //                 旧房间的 Release 再延后 releaseDelay 秒错开卸载尖峰
    public class DreamNetworkManager : NetworkBehaviour
    {
        public static DreamNetworkManager Instance { get; private set; }

        [Header("Per world — [0] = World0/Player0, [1] = World1/Player1")]
        public PlayerDreamBag[] bags = new PlayerDreamBag[2];              // server logic
        public ChangeDreamByGift[] presenters = new ChangeDreamByGift[2];  // visual swap per world

        [Tooltip("Shared ordered dream list — BOTH clients must agree (the INDEX is what's " +
                 "networked). Leave empty to use bags[0].allDreams.")]
        public DefinitionDream[] allDreams;

        [Tooltip("Buffer window: seconds between the bag settling a jump (gift crossed the " +
                 "portal) and the actual dream change. Both ends preload during this window.")]
        public float changeDelay = 15f;

        [Tooltip("礼物要等【所有客户端】的房间都凝入完成才 spawn，否则 server 一凝完就广播 spawn，" +
                 "还在加载房间的那端会先看到一堆礼物浮在空中、过很久环境才溶出来。" +
                 "这里是等待上限（秒），超时就照常 spawn，免得某端加载失败把流程卡死。")]
        public float giftSpawnAckTimeout = 10f;

        [Header("Gift")]
        [Tooltip("礼物穿门触发换梦时，挂在这个礼物身上的发光/脉冲 prefab。VFX Graph 或 " +
                 "ParticleSystem 都行，要求 SetActive 就自己播、不需要外部驱动。整个 changeDelay " +
                 "窗口都亮着，然后世界才开始溶解。留空 = 不发光。" +
                 "注意它会被自动设到礼物所在的世界层，材质得是透明队列才会被 stencil 管线画出来。")]
        public GameObject giftHeraldPrefab;

        [Tooltip("和发光同时播的那一声「咚」。留空 = 不发声。")]
        public AudioClip giftHeraldSound;

        [Tooltip("DEBUG ONLY: turn off to test whether gift networking (spawn/despawn of " +
                 "ObjectGift children) is causing a problem, without touching dream loading itself. " +
                 "The Inspector's component-enable checkbox does NOT work for this — Netcode calls " +
                 "OnNetworkSpawn on disabled NetworkBehaviours regardless, this flag is a real gate.")]
        public bool spawnGifts = true;

        // Replicated current-dream index per world (-1 = none yet).
        public NetworkVariable<int> Dream0Index = new(-1,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> Dream1Index = new(-1,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // 预告通道：正式换梦前 changeDelay 秒先广播目标梦的 index，两端收到后各自 Preload。
        // -1 = 无预告。用 NetworkVariable 而不是 RPC：缓冲期内新连入的 client 靠 initial
        // sync 也能拿到 pending 值补做预载，RPC 会错过；语义上预载是"状态"不是"事件"。
        public NetworkVariable<int> Pending0Index = new(-1,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> Pending1Index = new(-1,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // server：per-world 缓冲期锁。bag 结算跳梦后上锁，presenter 过渡完成解锁；锁定期间
        // Deliver/TakeBack 直接跳过 bag 结算（礼物的 currentDream/ownerPlayerId 更新在
        // GiftDeliveryTrigger / PortalDirectionTrigger，照常发生，礼物会随旧梦一起消失）。
        readonly bool[] worldLocked = new bool[2];

        // server：账本——所有经本类 Spawn 出去的礼物，替代原先 FindObjectsByType 的全场景
        // 扫描。despawn 时按"当时的" ownerPlayerId 过滤（礼物穿门会翻转 owner，所以不能按
        // spawn 时的 world 分两本账）。
        readonly List<NetworkObject> spawnedGifts = new List<NetworkObject>();

        // server：预实例化好（inactive、未 Spawn）、等本 world 凝入完成后摊帧 Spawn 的礼物。
        readonly List<GameObject>[] pendingSpawn = { new List<GameObject>(), new List<GameObject>() };

        // server：本轮过渡里已经报告「我这边房间凝入完成了」的 clientId。每次 ApplyVisual
        // 开新一轮过渡时清空，集齐所有在线 client 才放行礼物 Spawn（见 SpawnPendingGiftsWhenReady）。
        readonly HashSet<ulong>[] transitionAcks = { new HashSet<ulong>(), new HashSet<ulong>() };

        // server：每个 world 的换梦代次，ApplyVisual 开新一轮就 +1。摊帧 Spawn 的协程每帧
        // 核对代次，一变就放弃剩下的。没有这个的话：上一轮的 spawn 协程会在新一轮的清场
        // 扫描【跑完之后】才把旧礼物 Spawn 出来——而清场是倒序遍历账本的，后追加到尾部的
        // 条目索引比当时的 i 大，永远扫不到，旧梦的礼物就永久留在新梦里。
        readonly int[] transitionGen = new int[2];

        // server：本次 DeliverGift 是哪个礼物送进来的（NetworkObjectId，0 = 未知）。
        // bag.ReceiveGift -> OnDreamChanged -> HandleBagJump 是同步调用链，HandleBagJump
        // 本身拿不到礼物引用（它只收到 DefinitionDream），靠这个字段把礼物身份带过去。
        ulong _heraldGiftId;

        void Awake() => Instance = this;

        public override void OnNetworkSpawn()
        {
            // DIAGNOSTIC: if this ever prints more than once for the same InstanceID, OnNetworkSpawn
            // is firing twice on the same DreamNetworkManager and every subscription below doubles up.
            Debug.Log($"[DreamNet] OnNetworkSpawn id={GetInstanceID()} IsServer={IsServer} IsClient={IsClient}");

            if ((allDreams == null || allDreams.Length == 0) &&
                bags != null && bags.Length > 0 && bags[0] != null)
                allDreams = bags[0].allDreams;

            Dream0Index.OnValueChanged += (_, v) => ApplyVisual(0, v);
            Dream1Index.OnValueChanged += (_, v) => ApplyVisual(1, v);
            Pending0Index.OnValueChanged += (_, v) => ApplyPreload(0, v);
            Pending1Index.OnValueChanged += (_, v) => ApplyPreload(1, v);

            // 房间实例（含它带的 ObjectGift 子物件）就绪之后（可能还 inactive），把礼物 marker
            // 变成待命的网络物体（server）或者删掉本地重影（client）。见 HandleDreamInstanceReady。
            // 凝入完成后（OnTransitionComplete）解缓冲锁 + 摊帧 Spawn。
            // DIAGNOSTIC: 打印 presenters[0]/[1] 的 InstanceID——如果两个 id 一样，说明 Inspector 里
            // World0/World1 的 presenter 槽位被错误地指向了同一个 ChangeDreamByGift。
            if (presenters.Length > 0 && presenters[0] != null)
            {
                Debug.Log($"[DreamNet] subscribe presenters[0]='{presenters[0].name}' id={presenters[0].GetInstanceID()}");
                presenters[0].OnInstanceReady += (instance, dream) => HandleDreamInstanceReady(0, instance, dream);
                presenters[0].OnTransitionComplete += (instance, dream) => HandleTransitionComplete(0, instance, dream);
            }
            if (presenters.Length > 1 && presenters[1] != null)
            {
                Debug.Log($"[DreamNet] subscribe presenters[1]='{presenters[1].name}' id={presenters[1].GetInstanceID()}");
                presenters[1].OnInstanceReady += (instance, dream) => HandleDreamInstanceReady(1, instance, dream);
                presenters[1].OnTransitionComplete += (instance, dream) => HandleTransitionComplete(1, instance, dream);
            }

            if (IsServer)
            {
                // A bag jump (server-only) opens the buffer window: pending index first, the
                // real DreamXIndex lands changeDelay later. See HandleBagJump.
                if (bags.Length > 0 && bags[0] != null)
                    bags[0].OnDreamChanged += (p, from, to) => HandleBagJump(0, to);
                if (bags.Length > 1 && bags[1] != null)
                    bags[1].OnDreamChanged += (p, from, to) => HandleBagJump(1, to);

                // 初始梦不走缓冲，直接生效。Setting these fires OnValueChanged above -> ApplyVisual once.
                Dream0Index.Value = IndexOf(bags.Length > 0 ? bags[0]?.Current : null);
                Dream1Index.Value = IndexOf(bags.Length > 1 ? bags[1]?.Current : null);
            }
            else
            {
                // Clients get NO OnValueChanged for the already-synced initial values -> apply once.
                ApplyVisual(0, Dream0Index.Value);
                ApplyVisual(1, Dream1Index.Value);
                // 缓冲期内连入的 client：initial sync 带到的 pending 值也要补做预载。
                ApplyPreload(0, Pending0Index.Value);
                ApplyPreload(1, Pending1Index.Value);
            }
        }

        // Called on the SERVER when a gift crosses the portal into `targetPlayer`'s world.
        // `origin` = the dream the gift came from (the giver's world); also the excluded partner.
        // 结算立即发生（不再延迟）；跳梦时由 HandleBagJump 进缓冲期，changeDelay 挪到了
        // "结算 -> 正式换梦"之间（预载窗口）。
        // giftNetId：送进来的那个礼物的 NetworkObjectId，用来在换梦预告期间让它发光+响。
        // 0 = 不知道是哪个礼物（单机/测试路径），那就不发光，其余流程照旧。
        public void DeliverGift(int targetPlayer, DefinitionGift gift, DefinitionDream origin,
                                ulong giftNetId = 0)
        {
            if (!IsServer || gift == null) return;
            if (targetPlayer < 0 || targetPlayer > 1 || bags[targetPlayer] == null) return;
            if (worldLocked[targetPlayer])           // 缓冲期/过渡中：跳过结算
            {
                Debug.Log($"[DreamNet] DeliverGift '{gift.giftName}' -> world{targetPlayer} SKIPPED (buffer lock).");
                return;
            }
            Debug.Log($"[DreamNet] DeliverGift '{gift.giftName}' -> world{targetPlayer} " +
                      $"origin='{(origin ? origin.dreamId : "null")}', settling bag now.");
            var bag = bags[targetPlayer];
            var other = bags[PlayerWorld.OtherWorld(targetPlayer)];
            var partner = other != null ? other.Current : origin;

            // 同步调用链：ReceiveGift 里跳梦会当场 Invoke OnDreamChanged -> HandleBagJump，
            // 后者从 _heraldGiftId 取走礼物身份。调用返回后立刻清掉，别污染下一次结算。
            _heraldGiftId = giftNetId;
            bag.ReceiveGift(gift, origin, partner);
            _heraldGiftId = 0;
        }

        // Called on the SERVER when a gift that was delivered into `sourceWorld` gets carried
        // back out (GiftDeliveryTrigger decides this by comparing the gift's origin against
        // where it currently sits) — undoes the weight boost it added to sourceWorld's bag.
        public void TakeBackGift(int sourceWorld, DefinitionGift gift)
        {
            if (!IsServer || gift == null) return;
            if (sourceWorld < 0 || sourceWorld > 1 || bags[sourceWorld] == null) return;
            if (worldLocked[sourceWorld])            // 缓冲期/过渡中：跳过结算
            {
                Debug.Log($"[DreamNet] TakeBackGift '{gift.giftName}' out of world{sourceWorld} SKIPPED (buffer lock).");
                return;
            }
            Debug.Log($"[DreamNet] TakeBackGift '{gift.giftName}' out of world{sourceWorld}, settling bag now.");
            var bag = bags[sourceWorld];
            var other = bags[PlayerWorld.OtherWorld(sourceWorld)];
            bag.TakeBackGift(gift, other != null ? other.Current : null);
        }

        public float ChangeDelay => changeDelay;

        // server：bag 结算得出跳梦 -> 上锁进缓冲期：先写预告 index 让两端开始预载，
        // changeDelay 后写正式 index 触发两端换梦。解锁在 HandleTransitionComplete。
        void HandleBagJump(int world, DefinitionDream to)
        {
            int index = IndexOf(to);
            Debug.Log($"[DreamNet] bag jump world={world} -> '{(to ? to.dreamId : "null")}' (index {index}), " +
                      $"buffering {changeDelay}s before the switch.");
            worldLocked[world] = true;
            if (world == 0) Pending0Index.Value = index;
            else Pending1Index.Value = index;

            // 换梦预告：让触发这次换梦的礼物在整个缓冲窗口里发光+响一声，给「是这个礼物
            // 干的、马上要变天了」一个明确的因果提示。take-back 不走这里（_heraldGiftId=0）。
            if (_heraldGiftId != 0) HeraldGiftClientRpc(_heraldGiftId, changeDelay);

            StartCoroutine(CommitAfterBuffer(world, index));
        }

        // 各端在礼物身上挂发光 prefab + 播一声。纯表现，不影响任何状态。
        [ClientRpc]
        void HeraldGiftClientRpc(ulong giftNetId, float duration)
        {
            if (giftHeraldPrefab == null && giftHeraldSound == null) return;
            if (NetworkManager.SpawnManager == null ||
                !NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(giftNetId, out var no) ||
                no == null)
            {
                Debug.LogWarning($"[DreamNet] herald: gift netId={giftNetId} not found on this client.");
                return;
            }

            if (giftHeraldPrefab != null)
            {
                var fx = Instantiate(giftHeraldPrefab, no.transform.position, no.transform.rotation,
                                     no.transform);
                // 礼物在某个世界层上，发光也必须跟到同一层——否则 portal 的 stencil 管线
                // 根本不会画它（base renderer 的 layer mask 不含世界层）。
                SetLayerRecursively(fx.transform, no.gameObject.layer);
                Destroy(fx, duration);
            }

            if (giftHeraldSound != null)
                AudioSource.PlayClipAtPoint(giftHeraldSound, no.transform.position);
        }

        static void SetLayerRecursively(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++)
                SetLayerRecursively(t.GetChild(i), layer);
        }

        IEnumerator CommitAfterBuffer(int world, int index)
        {
            if (changeDelay > 0f) yield return new WaitForSeconds(changeDelay);
            if (world == 0) { Dream0Index.Value = index; Pending0Index.Value = -1; }
            else { Dream1Index.Value = index; Pending1Index.Value = -1; }
        }

        // Which world (0/1) is CURRENTLY showing exactly this dream. Returns -1 if neither
        // (e.g. a stale reference from before a dream changed). Used by GiftDeliveryTrigger to
        // find where a gift physically sits right now via ObjectGift.currentDream — NOT the
        // same question as "which world doesn't show the origin" (that can't tell a delivery
        // apart from a take-back once a gift has already crossed once).
        public int WorldShowing(DefinitionDream dream)
        {
            if (dream == null) return -1;
            if (bags.Length > 0 && bags[0] != null && bags[0].Current == dream) return 0;
            if (bags.Length > 1 && bags[1] != null && bags[1].Current == dream) return 1;
            return -1;
        }

        // 预告收到（server 本地回调 + client 复制都走这）：开始预载目标梦。-1 = 预告清除，无动作。
        void ApplyPreload(int world, int index)
        {
            if (index < 0) return;
            bool hasPresenter = presenters != null && world < presenters.Length && presenters[world] != null;
            var dream = (allDreams != null && index < allDreams.Length) ? allDreams[index] : null;
            Debug.Log($"[DreamNet] ApplyPreload world={world} index={index} dream={(dream ? dream.dreamId : "null")} " +
                      $"presenter={hasPresenter} IsServer={IsServer}");
            if (!hasPresenter || dream == null) return;
            presenters[world].Preload(dream);
        }

        void ApplyVisual(int world, int index)
        {
            // 这个 world 要换梦了（Switch 里紧接着开始 DissolveOut）：把这个 owner 名下现存的
            // 礼物摊帧清掉——不管是这个梦自己带的，还是穿门从对面拿过来的，跟旧梦一起消失。
            if (IsServer && spawnGifts) StartCoroutine(DespawnGiftsOwnedBy(world));

            // 新一轮过渡开始：代次 +1（让上一轮还在摊帧 Spawn 的协程立刻自弃），
            // 上一轮的「凝入完成」报到也一并作废。
            // 注意这里【不能】清 pendingSpawn——此刻它装的是【新梦】预载时就预实例化好的
            // 礼物（T0 就开始预载了），清掉的话新梦一个礼物都不会有。
            if (IsServer && world >= 0 && world < 2)
            {
                transitionGen[world]++;
                transitionAcks[world].Clear();
            }

            // 溶解开始的同时把 portal 优雅收掉：反向缩回 → despawn → 各端 stencil 回家
            // （串门的玩家视角送回自己世界）。没有 portal 时是 no-op；初始加载也走这，同样 no-op。
            if (IsServer && PortalSpawner.Instance != null) PortalSpawner.Instance.ClosePortal();

            bool hasPresenter = presenters != null && world < presenters.Length && presenters[world] != null;
            var dream = (allDreams != null && index >= 0 && index < allDreams.Length) ? allDreams[index] : null;
            Debug.Log($"[DreamNet] ApplyVisual world={world} index={index} dream={(dream ? dream.dreamId : "null")} " +
                      $"presenter={hasPresenter} IsServer={IsServer} IsClient={IsClient}");
            if (IsClient && !IsServer)   // surface the CLIENT's result to the HOST console for debugging
                ReportClientServerRpc(world, index, dream != null, hasPresenter,
                                      allDreams != null ? allDreams.Length : -1);
            if (!hasPresenter || dream == null) return;
            presenters[world].Switch(dream);
        }

        [ServerRpc(RequireOwnership = false)]
        void ReportClientServerRpc(int world, int index, bool dreamResolved, bool hasPresenter,
                                   int allDreamsLen, ServerRpcParams p = default)
        {
            Debug.Log($"[DreamNet<-Client{p.Receive.SenderClientId}] ApplyVisual world={world} index={index} " +
                      $"dreamResolved={dreamResolved} presenter={hasPresenter} allDreamsLen={allDreamsLen}");
        }

        // 房间实例就绪之后（预载完成或兜底加载完成，此时房间可能还 inactive），处理它带的
        // ObjectGift 子物件。每台机器（server + 每个 client）各自触发一次。
        void HandleDreamInstanceReady(int world, GameObject roomInstance, DefinitionDream dream)
        {
            if (!spawnGifts) return; // debug kill-switch, see tooltip on the field
            if (roomInstance == null) return;
            var gifts = roomInstance.GetComponentsInChildren<ObjectGift>(true);
            // DIAGNOSTIC: if the same room InstanceID shows up in two log lines, this method ran
            // twice for the same room load (double subscription, or presenters[0]==presenters[1]).
            Debug.Log($"[DreamNet] HandleDreamInstanceReady world={world} room='{roomInstance.name}' " +
                      $"id={roomInstance.GetInstanceID()} dream={dream?.dreamId} giftCount={gifts.Length} IsServer={IsServer}");
            if (!IsServer)
            {
                // 房间里这份只是本地视觉重影——真正能看见的网络物体由 server 的 Spawn 广播
                // 过来，这份本地副本直接删掉，避免重影。
                foreach (var obj in gifts) Destroy(obj.gameObject);
                return;
            }

            // 这一刻 pendingSpawn 的归属权换手：接下来要装的是【新房间】的礼物。代次 +1，
            // 让上一轮可能还在摊帧 Spawn 的协程当场停手，别把新房间的礼物提前 Spawn 出去
            // （代次只在 ApplyVisual 那里 +1 是不够的——预载完成早于正式换梦，中间这段
            // 窗口旧协程照样能吃到新礼物）。
            transitionGen[world]++;

            // 上一轮没来得及 Spawn 的预实例已经过时（新的房间实例来了）——兜底清掉，
            // 正常流程走到这里时列表是空的。
            foreach (var go in pendingSpawn[world]) if (go != null) Destroy(go);
            pendingSpawn[world].Clear();

            foreach (var obj in gifts) PreInstantiateGift(obj, world, dream);
        }

        // server-only：房间里烤的这份 gift 只是个 marker，不直接上网——它是 Addressable 房间
        // prefab 里的 nested prefab instance，GlobalObjectIdHash 跟登记在 NetworkPrefabs list 里
        // 的独立 prefab 对不上；client 收到 spawn 消息后查表查不到，新 client 连入时的 connection
        // sync 就卡死在这一个物体上，整个连接进不来（表现为黑屏、host 也跟着卡住）。
        // 做法：读走 marker 的位置/朝向/gift 数据，销毁 marker，改用它指向的 networkPrefab
        // （已经注册过的独立 prefab，见 ObjectGift.networkPrefab）原地重新生成一个待命实例
        // （inactive、不 Spawn）——真正的 net.Spawn() 在凝入完成后摊帧执行，见 SpawnPendingGifts。
        void PreInstantiateGift(ObjectGift marker, int world, DefinitionDream dream)
        {
            if (marker.networkPrefab == null)
            {
                Debug.LogWarning($"[DreamNet] '{marker.name}' has no networkPrefab assigned, skipping spawn. " +
                                 "Run Tools/DreamTouch/Backfill Gift Network Prefabs, or assign it by hand.", marker);
                return;
            }

            var pos = marker.transform.position;
            var rot = marker.transform.rotation;
            var scale = marker.transform.lossyScale;   // world scale — marker's room-ancestors may also be scaled
            var giftDef = marker.gift;
            Destroy(marker.gameObject);

            var inst = Instantiate(marker.networkPrefab, pos, rot);
            // Instantiate(prefab, pos, rot) only takes position/rotation — scale falls back to the
            // prefab's own default unless set explicitly. inst has no parent at this point, so its
            // lossyScale == localScale.
            inst.transform.localScale = scale;
            var net = inst.GetComponent<NetworkObject>();
            if (net == null)
            {
                Debug.LogWarning($"[DreamNet] networkPrefab '{marker.networkPrefab.name}' has no NetworkObject, " +
                                 "cannot spawn. Add NetworkObject + IObjectScreen/IObjectXR and register it in " +
                                 "DefaultNetworkPrefabs.", inst);
                Destroy(inst);
                return;
            }
            inst.SetActive(false);   // 凝入完成前不可见也不上网

            var obj = inst.GetComponent<ObjectGift>();
            if (obj != null)
            {
                obj.gift = giftDef;
                // origin dream 不信任 prefab 里手动配的值——同一个 gift prefab 会在不同梦里重复
                // 出现，一律以"这次是在哪个梦里生成的"为准。currentDream 同步设成一样的值：
                // 刚生成时"当前在哪"="老家在哪"，还没被送出去过。
                obj.originDream = dream;
                obj.currentDream = dream;
            }

            var xr = inst.GetComponent<IObjectXR>();
            if (xr != null) xr.ownerPlayerId = PlayerWorld.PlayerOf(world);
            var screen = inst.GetComponent<IObjectScreen>();
            if (screen != null) screen.ownerPlayerId = PlayerWorld.PlayerOf(world);

            pendingSpawn[world].Add(inst);
        }

        // 换梦过渡完成（凝入结束；加载失败时 roomInstance=null 也会到这）。这个回调在
        // 【每台机器】上各自触发一次——每端先向 server 报到，server 集齐所有人才放行礼物。
        void HandleTransitionComplete(int world, GameObject roomInstance, DefinitionDream dream)
        {
            // 本端房间已经凝入完成，可以承接礼物了。host 上这个 ServerRpc 是本地直接调用。
            ReportTransitionCompleteServerRpc(world);

            if (!IsServer) return;

            // 没有礼物要落地（加载失败 / 关了 spawnGifts）：当场解锁。
            if (!spawnGifts || roomInstance == null)
            {
                foreach (var go in pendingSpawn[world]) if (go != null) Destroy(go);
                pendingSpawn[world].Clear();
                worldLocked[world] = false;
                return;
            }

            // 注意：这里【故意不解锁】。缓冲锁一直押到礼物真正 Spawn 完为止（见
            // SpawnPendingGiftsWhenReady 末尾）。以前在这里就解锁，导致「等 ack + 摊帧
            // Spawn」这段窗口里能放行新一轮换梦结算，于是新一轮的清场先跑完、上一轮的
            // 礼物后 Spawn，旧梦的礼物就永久留在了新梦里。
            StartCoroutine(SpawnPendingGiftsWhenReady(world, transitionGen[world]));
        }

        [ServerRpc(RequireOwnership = false)]
        void ReportTransitionCompleteServerRpc(int world, ServerRpcParams p = default)
        {
            if (world < 0 || world > 1) return;
            transitionAcks[world].Add(p.Receive.SenderClientId);
            Debug.Log($"[DreamNet] transition ack world={world} from client{p.Receive.SenderClientId} " +
                      $"({transitionAcks[world].Count}/{NetworkManager.ConnectedClientsIds.Count} in)");
        }

        // server-only：等所有在线 client 都报告本 world 凝入完成，再摊帧 Spawn 礼物。
        // 不等的话，server 一凝完就 Spawn，网络消息立刻送达还在加载房间的那一端——那边就会
        // 先看到一堆礼物浮在空中、过很久环境才溶出来（正是要修的问题）。
        // 超时兜底：某端加载失败/极慢时不能无限等，到点照常 Spawn。
        // gen = 启动时这个 world 的换梦代次。每次 yield 之后都要核对：代次变了说明又换了一轮梦，
        // 本轮剩下的礼物属于已经消失的旧梦，绝不能再 Spawn 出去（详见 transitionGen 的注释）。
        IEnumerator SpawnPendingGiftsWhenReady(int world, int gen)
        {
            float deadline = Time.time + giftSpawnAckTimeout;
            bool allReady = false;
            while (Time.time < deadline)
            {
                if (transitionGen[world] != gen) yield break;   // 换代了，整轮作废
                allReady = true;
                foreach (var id in NetworkManager.ConnectedClientsIds)
                    if (!transitionAcks[world].Contains(id)) { allReady = false; break; }
                if (allReady) break;
                yield return null;
            }

            if (!allReady)
                Debug.LogWarning($"[DreamNet] world={world} spawning gifts after {giftSpawnAckTimeout}s " +
                                 "without all client acks — someone's room load is slow or failed.");

            // 摊帧 Spawn：每帧几个，激活 + net.Spawn() + 登记账本。
            // NGO 要求 active 才能 Spawn——绝不在 inactive 阶段 spawn。
            const int perFrame = 3;
            int n = 0;
            var list = pendingSpawn[world];
            while (list.Count > 0)
            {
                // 换代了：立刻停手，且【不要动这个列表】——此刻里面装的很可能已经是新一代
                // 预实例化好的礼物了（HandleDreamInstanceReady refill 时就换了归属）。
                // 本轮没来得及 Spawn 的残留由 HandleDreamInstanceReady 负责销毁。
                if (transitionGen[world] != gen)
                {
                    Debug.Log($"[DreamNet] world={world} gift spawn ABORTED mid-way (dream changed again).");
                    yield break;
                }

                var inst = list[list.Count - 1];
                list.RemoveAt(list.Count - 1);
                if (inst == null) continue;
                inst.SetActive(true);
                var net = inst.GetComponent<NetworkObject>();
                net.Spawn();
                spawnedGifts.Add(net);
                if (++n >= perFrame) { n = 0; yield return null; }
            }

            // 礼物全部落地，这一轮换梦才算真的结束——现在才放缓冲锁。
            worldLocked[world] = false;
            Debug.Log($"[DreamNet] world={world} transition fully settled, buffer lock released.");
        }

        // server-only：这个 world 名下现存的礼物（自己梦带的 + 穿门拿过来的）全部销毁。
        // 账本遍历替代全场景扫描；摊帧削掉换梦帧的 CPU 尖峰，启动时机与 DissolveOut 同帧。
        //
        // 先对账本拍快照再遍历快照，而不是拿索引在活账本上倒着走。原来那种写法在 yield 期间
        // 只要有第二个协程动了同一个 List（另一个世界也在清场、或者 SpawnPendingGifts 在追加），
        // 索引就会失配、静默跳过元素——而且跳过谁取决于列表顺序，所以表现成「每次都是固定
        // 那几个清不掉」。快照遍历 + 按引用 Remove 则完全不受并发增删影响。
        IEnumerator DespawnGiftsOwnedBy(int world)
        {
            const int perFrame = 3;
            ulong ownerId = PlayerWorld.PlayerOf(world);
            int n = 0;
            var snapshot = new List<NetworkObject>(spawnedGifts);

            foreach (var no in snapshot)
            {
                if (no == null || !no.IsSpawned) { spawnedGifts.Remove(no); continue; }

                // 用 GetComponentInChildren 而不是 GetComponent：组件挂在子物体上的 prefab
                // 在根节点查不到，会被判成「不是我的」从而永远清不掉。
                var xr = no.GetComponentInChildren<IObjectXR>(true);
                var screen = no.GetComponentInChildren<IObjectScreen>(true);
                bool owned = (xr != null && xr.ownerPlayerId == ownerId) ||
                             (screen != null && screen.ownerPlayerId == ownerId);

                if (!owned)
                {
                    // [GiftDiag] 留在场上的礼物，逐个说明为什么没被认领——一眼看出是
                    // owner 对不上（被人穿门带走过）还是压根没有归属组件。
                    Debug.LogWarning($"[GiftDiag] KEEPING '{no.name}' during world{world} cleanup: " +
                                     $"xr={(xr != null ? xr.ownerPlayerId.ToString() : "NONE")} " +
                                     $"screen={(screen != null ? screen.ownerPlayerId.ToString() : "NONE")} " +
                                     $"(looking for owner {ownerId})", no);
                    continue;
                }

                spawnedGifts.Remove(no);
                no.Despawn(true);
                if (++n >= perFrame) { n = 0; yield return null; }
            }

            // [GiftDiag] 清场收尾对账：场上还实际存在的 ObjectGift vs 账本。出现在场上但不在
            // 账本里的，说明它根本没经过 SpawnPendingGifts 登记（比如场景里手摆的），
            // 清场逻辑永远碰不到它——这类只能靠这条日志发现。
            var live = FindObjectsByType<ObjectGift>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var sb = new System.Text.StringBuilder(
                $"[GiftDiag] world{world} cleanup done. ledger={spawnedGifts.Count} liveInScene={live.Length}");
            foreach (var g in live)
            {
                var gNo = g.GetComponentInParent<NetworkObject>();
                bool tracked = gNo != null && spawnedGifts.Contains(gNo);
                sb.Append($"\n  '{g.name}' net={(gNo != null ? gNo.NetworkObjectId.ToString() : "NONE")} " +
                          $"inLedger={tracked} dream={(g.currentDream ? g.currentDream.dreamId : "null")}");
            }
            Debug.Log(sb.ToString());
        }

        int IndexOf(DefinitionDream d)
        {
            if (d == null || allDreams == null) return -1;
            for (int i = 0; i < allDreams.Length; i++)
                if (allDreams[i] == d) return i;
            return -1;
        }
    }
}
