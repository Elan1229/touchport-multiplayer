using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DreamTouch
{
    // LOCAL logic: the player's current dream + bag + the evolution rule (方案 B，见 EvaluateJump).
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

        // 上一件礼物命中了哪些隐藏房间。连续两件都命中同一个 → 去那个隐藏房间。
        // 故意不随换梦清空（SeedBag 不碰它）：礼物本体已经随旧梦消失了，计数还在。
        readonly List<DefinitionDream> lastHiddenHits = new List<DefinitionDream>();

        [Header("Debug (runtime, read-only)")]
        [SerializeField] string debugCurrentDream;
        [SerializeField] string debugHiddenStreak;
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
            if (giftOrigin == Current)                          // own item -> inert
            {
                Debug.Log($"[DreamBag] '{name}' ignored '{gift.giftName}' — origin '{giftOrigin?.dreamId}' " +
                          "is the current dream (own item, inert).", this);
                return false;
            }
            foreach (var k in gift.keywords)
                if (k != null) bag[k] = (bag.TryGetValue(k, out var c) ? c : 0) + giftPush;
            bool jumped = EvaluateJump(partnerDream, gift);
            Debug.Log($"[DreamBag] '{name}' received '{gift.giftName}' (+{giftPush} weight on " +
                      $"[{KeywordList(gift)}]) -> jumped={jumped}, current='{Current?.dreamId}'.", this);
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
            bool jumped = EvaluateJump(partnerDream, null);   // 拿回去不算进「连续两件」
            Debug.Log($"[DreamBag] '{name}' took back '{gift.giftName}' (-{giftPush} weight on " +
                      $"[{KeywordList(gift)}]) -> jumped={jumped}, current='{Current?.dreamId}'.", this);
            RefreshDebug();
            return jumped;
        }

        static string KeywordList(DefinitionGift gift)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var k in gift.keywords)
                if (k != null) { if (sb.Length > 0) sb.Append(", "); sb.Append(k.Id); }
            return sb.ToString();
        }

        // 规则（方案 B：固定系列 + 隐藏房间）。旧的「自由跳」规则留档在
        // _DreamTouchForAI/Worlds/saved-schemes/v3-free-roam.json 和对应的模拟器 html 里。
        //  · 玩家的「家」= 起始梦所在的系列（DefinitionDream.series）。平时只在家里的房间之间换：
        //    排除自己和对方，第 1 名领先第 2 名 >= leadMargin 就跳；前两名咬得很紧（差 <= 1）
        //    但明显甩开第 3 名时也跳（bumper）；并列第 1 随机挑。
        //  · 隐藏房间：连续两件礼物都命中同一个隐藏房间的 hiddenTrigger → 去那个房间，计数清零。
        //    对方已经在那个房间时不去。
        //  · 人在隐藏房间时，下一件礼物【一定】把他送回家里契合分最高的房间；这件礼物算进
        //    「连续两件」的计数，所以最快再下一件又能去隐藏房间。
        //  · gift == null（拿回去）：只在家里重新结算，不碰计数。
        bool EvaluateJump(DefinitionDream partnerDream, DefinitionGift gift)
        {
            bool inHidden = Current != null && Current.IsHidden;
            DefinitionDream pick = null;
            string why = null;

            if (gift != null)
            {
                var hits = allDreams
                    .Where(w => w != null && w.IsHidden && gift.keywords.Any(k => k != null && w.hiddenTrigger.Contains(k)))
                    .OrderBy(w => w.hiddenPriority)
                    .ToList();
                if (!inHidden)
                    pick = hits.FirstOrDefault(w => lastHiddenHits.Contains(w) && w != partnerDream);

                lastHiddenHits.Clear();
                if (pick != null) why = "two matching gifts in a row -> hidden room";
                else lastHiddenHits.AddRange(hits);          // 这件算第 1 件
            }

            if (pick == null)
            {
                string home = startDream != null ? startDream.series : null;
                var cand = allDreams
                    .Where(w => w != null && w != Current && w != partnerDream && !w.IsHidden && w.series == home)
                    .Select(w => new { dream = w, s = Score(w) })
                    .OrderByDescending(x => x.s)
                    .ToList();
                if (cand.Count == 0) return false;

                int s0 = cand[0].s;                                       // 1st place score
                int s1 = cand.Count > 1 ? cand[1].s : int.MinValue / 2;   // 2nd place score
                int s2 = cand.Count > 2 ? cand[2].s : int.MinValue / 2;   // 3rd place score

                bool jump = inHidden && gift != null;                     // 从隐藏房间一定回家
                if (!jump) jump = (s0 - s1) >= leadMargin;                // clear leader
                if (!jump && (s0 - s1) <= 1 && (s1 - s2) >= leadMargin)   // bumper: top pair vs 3rd
                    jump = true;
                if (!jump) return false;

                var leaders = cand.Where(x => x.s == s0).Select(x => x.dream).ToList();
                pick = leaders[UnityEngine.Random.Range(0, leaders.Count)];
                why = inHidden ? "back home from hidden room" : $"home series, s0={s0}";
            }

            var from = Current;
            Current = pick;
            SeedBag();
            Debug.Log($"[DreamBag] '{name}' jump '{from?.dreamId}' -> '{pick.dreamId}' ({why}).", this);
            OnDreamChanged?.Invoke(this, from, pick);
            return true;
        }

        // Handy for a debug UI.
        public IReadOnlyDictionary<DefinitionKeyword, int> Bag => bag;

        // Mirror runtime state into the serialized debug fields so the Inspector shows it.
        void RefreshDebug()
        {
            debugCurrentDream = Current != null ? Current.dreamId : "(none)";
            debugHiddenStreak = lastHiddenHits.Count == 0 ? "(none)"
                : "1 of 2 toward: " + string.Join(", ", lastHiddenHits.Select(d => d.dreamId));

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
