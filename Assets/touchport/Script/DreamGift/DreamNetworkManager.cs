using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace DreamTouch
{
    // THE networking file. Server-authoritative dream state for both worlds.
    // Mirrors LetterTaskState: NetworkVariables replicate each world's current-dream index;
    // clients react by swapping visuals via ChangeDreamByGift. The pure-logic PlayerDreamBag
    // (one per world) runs on the SERVER only. Put this on GameManager (already a NetworkObject),
    // next to LetterTaskState.
    public class DreamNetworkManager : NetworkBehaviour
    {
        public static DreamNetworkManager Instance { get; private set; }

        [Header("Per world — [0] = World0/Player0, [1] = World1/Player1")]
        public PlayerDreamBag[] bags = new PlayerDreamBag[2];              // server logic
        public ChangeDreamByGift[] presenters = new ChangeDreamByGift[2];  // visual swap per world

        [Tooltip("Shared ordered dream list — BOTH clients must agree (the INDEX is what's " +
                 "networked). Leave empty to use bags[0].allDreams.")]
        public DefinitionDream[] allDreams;

        [Tooltip("Seconds after a gift crosses the portal before that world's dream changes.")]
        public float changeDelay = 2f;

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

            // 房间实例（含它带的 ObjectGift 子物件）加载完之后，把礼物物体变成真正的网络物体
            // （server）或者删掉本地重影（client）。见 HandleDreamInstanceReady。
            // DIAGNOSTIC: 打印 presenters[0]/[1] 的 InstanceID——如果两个 id 一样，说明 Inspector 里
            // World0/World1 的 presenter 槽位被错误地指向了同一个 ChangeDreamByGift。
            if (presenters.Length > 0 && presenters[0] != null)
            {
                Debug.Log($"[DreamNet] subscribe presenters[0]='{presenters[0].name}' id={presenters[0].GetInstanceID()}");
                presenters[0].OnInstanceReady += (instance, dream) => HandleDreamInstanceReady(0, instance, dream);
            }
            if (presenters.Length > 1 && presenters[1] != null)
            {
                Debug.Log($"[DreamNet] subscribe presenters[1]='{presenters[1].name}' id={presenters[1].GetInstanceID()}");
                presenters[1].OnInstanceReady += (instance, dream) => HandleDreamInstanceReady(1, instance, dream);
            }

            if (IsServer)
            {
                // A bag jump (server-only) publishes the new index -> replicates to everyone.
                if (bags.Length > 0 && bags[0] != null)
                    bags[0].OnDreamChanged += (p, from, to) => Dream0Index.Value = IndexOf(to);
                if (bags.Length > 1 && bags[1] != null)
                    bags[1].OnDreamChanged += (p, from, to) => Dream1Index.Value = IndexOf(to);

                // Setting these fires OnValueChanged above -> ApplyVisual once (server side).
                Dream0Index.Value = IndexOf(bags.Length > 0 ? bags[0]?.Current : null);
                Dream1Index.Value = IndexOf(bags.Length > 1 ? bags[1]?.Current : null);
            }
            else
            {
                // Clients get NO OnValueChanged for the already-synced initial value -> apply once.
                ApplyVisual(0, Dream0Index.Value);
                ApplyVisual(1, Dream1Index.Value);
            }
        }

        // Called on the SERVER when a gift crosses the portal into `targetPlayer`'s world.
        // `origin` = the dream the gift came from (the giver's world); also the excluded partner.
        public void DeliverGift(int targetPlayer, DefinitionGift gift, DefinitionDream origin)
        {
            if (!IsServer || gift == null) return;
            if (targetPlayer < 0 || targetPlayer > 1 || bags[targetPlayer] == null) return;
            StartCoroutine(DeliverAfterDelay(targetPlayer, gift, origin));
        }

        IEnumerator DeliverAfterDelay(int targetPlayer, DefinitionGift gift, DefinitionDream origin)
        {
            if (changeDelay > 0f) yield return new WaitForSeconds(changeDelay);
            var bag = bags[targetPlayer];
            if (bag == null) yield break;
            var other = bags[1 - targetPlayer];
            var partner = other != null ? other.Current : origin;
            bag.ReceiveGift(gift, origin, partner);   // jump -> OnDreamChanged -> sets NetworkVariable
        }

        // Called on the SERVER when a gift that was delivered into `sourceWorld` gets carried
        // back out (GiftDeliveryTrigger decides this by comparing the gift's origin against
        // where it currently sits) — undoes the weight boost it added to sourceWorld's bag.
        public void TakeBackGift(int sourceWorld, DefinitionGift gift)
        {
            if (!IsServer || gift == null) return;
            if (sourceWorld < 0 || sourceWorld > 1 || bags[sourceWorld] == null) return;
            StartCoroutine(TakeBackAfterDelay(sourceWorld, gift));
        }

        IEnumerator TakeBackAfterDelay(int sourceWorld, DefinitionGift gift)
        {
            if (changeDelay > 0f) yield return new WaitForSeconds(changeDelay);
            var bag = bags[sourceWorld];
            if (bag == null) yield break;
            var other = bags[1 - sourceWorld];
            bag.TakeBackGift(gift, other != null ? other.Current : null);
        }

        public float ChangeDelay => changeDelay;

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

        void ApplyVisual(int world, int index)
        {
            // 这个 world 要换梦了：先把这个 owner 名下现存的礼物物体全部清掉——不管是这个梦
            // 自己带的，还是穿门从对面拿过来的，跟旧梦一起清空，再让新梦（和它带的礼物）加载。
            if (IsServer && spawnGifts) DespawnGiftsOwnedBy(world);

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

        // 房间实例加载完之后，处理它带的 ObjectGift 子物件。每台机器（server + 每个 client）
        // 各自本地 Switch() 完成后都会调用这个方法一次。
        void HandleDreamInstanceReady(int world, GameObject roomInstance, DefinitionDream dream)
        {
            if (!spawnGifts) return; // debug kill-switch, see tooltip on the field
            if (roomInstance == null) return;
            var gifts = roomInstance.GetComponentsInChildren<ObjectGift>(true);
            // DIAGNOSTIC: if the same room InstanceID shows up in two log lines, this method ran
            // twice for the same room load (double subscription, or presenters[0]==presenters[1]).
            Debug.Log($"[DreamNet] HandleDreamInstanceReady world={world} room='{roomInstance.name}' " +
                      $"id={roomInstance.GetInstanceID()} dream={dream?.dreamId} giftCount={gifts.Length} IsServer={IsServer}");
            foreach (var obj in gifts)
            {
                if (!IsServer)
                {
                    // 房间里这份只是本地视觉重影——真正能看见的网络物体由 server 的
                    // Spawn 广播过来（见 SpawnGift），这份本地副本直接删掉，避免重影。
                    Destroy(obj.gameObject);
                    continue;
                }
                SpawnGift(obj, world, dream);
            }
        }

        // server-only：房间里烤的这份 gift 只是个 marker，不直接上网——它是 Addressable 房间
        // prefab 里的 nested prefab instance，GlobalObjectIdHash 跟登记在 NetworkPrefabs list 里
        // 的独立 prefab 对不上；client 收到 spawn 消息后查表查不到，新 client 连入时的 connection
        // sync 就卡死在这一个物体上，整个连接进不来（表现为黑屏、host 也跟着卡住）。
        // 做法：读走 marker 的位置/朝向/gift 数据，销毁 marker，改用它指向的 networkPrefab
        // （已经注册过的独立 prefab，见 ObjectGift.networkPrefab）原地重新生成一个、Spawn 那一个——
        // hash 保证对得上，因为它就是登记表里那个 prefab 本体。
        void SpawnGift(ObjectGift marker, int world, DefinitionDream dream)
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
            if (xr != null) xr.gameOwnerId = (ulong)world;
            var screen = inst.GetComponent<IObjectScreen>();
            if (screen != null) screen.gameOwnerId = (ulong)world;

            Debug.Log($"[DreamNet] SpawnGift '{inst.name}' id={inst.GetInstanceID()} world={world} dream={dream?.dreamId}");
            net.Spawn();
        }

        // server-only：这个 world 名下现存的礼物物体（自己梦带的 + 穿门拿过来的）全部销毁。
        void DespawnGiftsOwnedBy(int world)
        {
            ulong ownerId = (ulong)world;
            foreach (var no in FindObjectsByType<NetworkObject>(FindObjectsSortMode.None))
            {
                if (!no.IsSpawned) continue;
                var xr = no.GetComponent<IObjectXR>();
                var screen = no.GetComponent<IObjectScreen>();
                bool owned = (xr != null && xr.gameOwnerId == ownerId) ||
                             (screen != null && screen.gameOwnerId == ownerId);
                if (owned) no.Despawn(true);
            }
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
