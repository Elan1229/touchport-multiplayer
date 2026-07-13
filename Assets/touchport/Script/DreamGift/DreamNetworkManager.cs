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

        // Replicated current-dream index per world (-1 = none yet).
        public NetworkVariable<int> Dream0Index = new(-1,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<int> Dream1Index = new(-1,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        void Awake() => Instance = this;

        public override void OnNetworkSpawn()
        {
            if ((allDreams == null || allDreams.Length == 0) &&
                bags != null && bags.Length > 0 && bags[0] != null)
                allDreams = bags[0].allDreams;

            Dream0Index.OnValueChanged += (_, v) => ApplyVisual(0, v);
            Dream1Index.OnValueChanged += (_, v) => ApplyVisual(1, v);

            if (IsServer)
            {
                // A bag jump (server-only) publishes the new index -> replicates to everyone.
                if (bags.Length > 0 && bags[0] != null)
                    bags[0].OnDreamChanged += (p, from, to) => Dream0Index.Value = IndexOf(to);
                if (bags.Length > 1 && bags[1] != null)
                    bags[1].OnDreamChanged += (p, from, to) => Dream1Index.Value = IndexOf(to);

                // Publish the starting dreams (bags set Current in their own Awake, before this).
                Dream0Index.Value = IndexOf(bags.Length > 0 ? bags[0]?.Current : null);
                Dream1Index.Value = IndexOf(bags.Length > 1 ? bags[1]?.Current : null);
            }

            // Show whatever the replicated values already are (clients + late joiners).
            ApplyVisual(0, Dream0Index.Value);
            ApplyVisual(1, Dream1Index.Value);
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

        public float ChangeDelay => changeDelay;

        // Which world should RECEIVE a gift whose origin dream is `origin`: the world NOT
        // currently showing `origin`. Returns -1 if it can't be told (fix the gift's originDream).
        public int ResolveTargetWorld(DefinitionDream origin)
        {
            if (origin == null) return -1;
            var d0 = (bags.Length > 0 && bags[0] != null) ? bags[0].Current : null;
            var d1 = (bags.Length > 1 && bags[1] != null) ? bags[1].Current : null;
            if (origin == d0) return 1;
            if (origin == d1) return 0;
            return -1;
        }

        void ApplyVisual(int world, int index)
        {
            if (presenters == null || world >= presenters.Length || presenters[world] == null) return;
            var dream = (allDreams != null && index >= 0 && index < allDreams.Length) ? allDreams[index] : null;
            if (dream != null) presenters[world].Switch(dream);
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
