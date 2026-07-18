using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DreamTouch
{
    // LOCAL logic: the player's current dream + bag + the evolution rule.
    // This is your "bag with a rule". It knows nothing about networking or visuals.
    // The Manager feeds gifts in; when the dream changes, this fires OnDreamChanged.
    public class PlayerDreamBag : MonoBehaviour
    {
        [Header("Data")]
        [Tooltip("Every dream in the game (drag all Dream assets here).")]
        [UnityEngine.Serialization.FormerlySerializedAs("allWorlds")] public DefinitionDream[] allDreams;
        [UnityEngine.Serialization.FormerlySerializedAs("startWorld")] public DefinitionDream startDream;

        [Header("Tuning (same as the prototype sliders)")]
        [Range(1, 8)]  public int giftPush   = 4;   // 单礼物推力
        [Range(0, 20)] public int leadMargin = 2;   // 领先差

        public DefinitionDream Current { get; private set; }

        // Live bag: keyword -> count. Runtime only; never authored by hand.
        readonly Dictionary<DefinitionKeyword, int> bag = new Dictionary<DefinitionKeyword, int>();

        // ── Debug mirror (runtime, read-only). Unity can't serialize the live Dictionary,
        //    so these fields are refreshed on every bag/dream change just for the Inspector. ──
        [System.Serializable] public struct DebugKeyword { public string keyword; public int weight; public int count; }
        [System.Serializable] public struct DebugScore   { public string dream;   public int score; }

        [Header("Debug (runtime, read-only)")]
        [SerializeField] string debugCurrentDream;
        [SerializeField] List<DebugKeyword> debugBag    = new List<DebugKeyword>();
        [SerializeField] List<DebugScore>   debugScores = new List<DebugScore>();

        // (player, from, to) — the Manager listens to this to switch visuals + broadcast.
        public event Action<PlayerDreamBag, DefinitionDream, DefinitionDream> OnDreamChanged;

        void Awake()
        {
            Current = startDream;
            SeedBag();
        }

        // Reset bag to the current dream: each of its keywords at count 1 (presence, NOT weight).
        void SeedBag()
        {
            bag.Clear();
            if (Current != null)
                foreach (var wk in Current.signature)
                    if (wk.weight > 0 && wk.keyword != null) bag[wk.keyword] = 1;
            RefreshDebug();
        }

        // score(dream) = sum over keywords of  bag[count] * dream.weight
        int Score(DefinitionDream w)
        {
            int total = 0;
            foreach (var wk in w.signature)
                if (wk.keyword != null && bag.TryGetValue(wk.keyword, out var c))
                    total += c * wk.weight;
            return total;
        }

        // Called by the Manager when a gift is delivered into this player's dream.
        // Returns true if the dream changed.
        public bool ReceiveGift(DefinitionGift gift, DefinitionDream giftOrigin, DefinitionDream partnerDream)
        {
            if (gift == null) return false;
            if (giftOrigin == Current) return false;            // own item -> inert
            foreach (var k in gift.keywords)
                if (k != null) bag[k] = (bag.TryGetValue(k, out var c) ? c : 0) + giftPush;
            bool jumped = EvaluateJump(partnerDream);
            RefreshDebug();
            return jumped;
        }

        // Called by the Manager when a gift that was previously delivered here gets carried
        // back OUT — undoes exactly the weight ReceiveGift added. Mirrors ReceiveGift but
        // subtracts; no "own item" guard needed here (the caller only invokes this when the
        // gift is NOT at its own origin dream, i.e. it really was delivered-in earlier).
        public bool TakeBackGift(DefinitionGift gift, DefinitionDream partnerDream)
        {
            if (gift == null) return false;
            foreach (var k in gift.keywords)
                if (k != null) bag[k] = Mathf.Max(0, (bag.TryGetValue(k, out var c) ? c : 0) - giftPush);
            bool jumped = EvaluateJump(partnerDream);
            RefreshDebug();
            return jumped;
        }

        // The rule: exclude self + partner.
        //  · Normal: jump when 1st beats 2nd by >= leadMargin.
        //  · Bumper: if 1st & 2nd are neck-and-neck (equal or differ by 1) but the top pair
        //    is decisively ahead of 3rd (2nd - 3rd >= leadMargin), commit to 1st anyway.
        //  · On a tie for 1st, pick one of the co-leaders at random.
        bool EvaluateJump(DefinitionDream partnerDream)
        {
            var cand = allDreams
                .Where(w => w != null && w != Current && w != partnerDream)
                .Select(w => new { dream = w, s = Score(w) })
                .OrderByDescending(x => x.s)
                .ToList();
            if (cand.Count < 2) return false;

            int s0 = cand[0].s;                                       // 1st place score
            int s1 = cand[1].s;                                       // 2nd place score
            int s2 = cand.Count > 2 ? cand[2].s : int.MinValue / 2;   // 3rd place score
            var leaders = cand.Where(x => x.s == s0).Select(x => x.dream).ToList();

            bool jump = (s0 - s1) >= leadMargin;                      // clear leader
            if (!jump && (s0 - s1) <= 1 && (s1 - s2) >= leadMargin)   // bumper: top pair vs 3rd
                jump = true;
            if (!jump) return false;

            var pick = leaders[UnityEngine.Random.Range(0, leaders.Count)];
            var from = Current;
            Current = pick;
            SeedBag();
            OnDreamChanged?.Invoke(this, from, pick);
            return true;
        }

        // Handy for a debug UI.
        public IReadOnlyDictionary<DefinitionKeyword, int> Bag => bag;

        // Mirror runtime state into the serialized debug fields so the Inspector shows it.
        void RefreshDebug()
        {
            debugCurrentDream = Current != null ? Current.dreamId : "(none)";

            debugBag.Clear();
            foreach (var kv in bag)
                debugBag.Add(new DebugKeyword
                {
                    keyword = kv.Key != null ? kv.Key.Id : "?",
                    weight  = WeightInCurrent(kv.Key),   // this keyword's weight in the CURRENT dream's signature
                    count   = kv.Value                   // runtime bag count (presence; +giftPush per gift)
                });

            debugScores.Clear();
            if (allDreams != null)
                foreach (var w in allDreams)
                    if (w != null)
                        debugScores.Add(new DebugScore { dream = w.dreamId, score = Score(w) });
        }

        // The weight of a keyword in the current dream's signature (0 if not part of it).
        int WeightInCurrent(DefinitionKeyword k)
        {
            if (Current == null || k == null) return 0;
            foreach (var wk in Current.signature)
                if (wk.keyword == k) return wk.weight;
            return 0;
        }
    }
}
