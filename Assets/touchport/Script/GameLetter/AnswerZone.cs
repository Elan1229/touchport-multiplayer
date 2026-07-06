using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 拼词垫：只在这一块里算「字母齐没齐」，然后交给 <see cref="LetterTaskState"/>。
/// 谁的任务由 <see cref="reportsFor"/> 决定（P0 垫 / P1 垫），没有第二次词判定。
/// </summary>
[RequireComponent(typeof(Collider))]
public class AnswerZone : NetworkBehaviour
{
    public enum ReportsForPlayer
    {
        Player0 = 0,
        Player1 = 1,
    }

    [Header("Task Config")]
    public ReportsForPlayer reportsFor = ReportsForPlayer.Player0;
    public string targetWord = "CAT";

    private readonly HashSet<LetterObject> _letters = new HashSet<LetterObject>();

    public override void OnNetworkSpawn()
    {
        var col = GetComponent<Collider>();
        if (col != null && !col.isTrigger)
            Debug.LogWarning($"[{nameof(AnswerZone)}] collider on {gameObject.name} isTrigger should be true.");
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;

        var letter = other.GetComponentInParent<LetterObject>();
        if (letter == null) return;

        if (_letters.Add(letter))
            Evaluate();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsServer) return;

        var letter = other.GetComponentInParent<LetterObject>();
        if (letter == null) return;

        if (_letters.Remove(letter))
            Evaluate();
    }

    private void Evaluate()
    {
        bool matched = MultisetEquals(
            BuildMultisetFromLetters(_letters),
            BuildMultiset(targetWord));

        var hub = LetterTaskState.Instance;
        if (hub == null) return;

        if (reportsFor == ReportsForPlayer.Player0)
            hub.ReportPlayer0WordMatchServer(matched);
        else
            hub.ReportPlayer1WordMatchServer(matched);
    }

    private static Dictionary<char, int> BuildMultisetFromLetters(HashSet<LetterObject> letters)
    {
        var dict = new Dictionary<char, int>();
        foreach (var lo in letters)
        {
            if (lo == null) continue;
            var c = lo.GetLetterChar();
            if (c == '\0') continue;

            if (!dict.TryGetValue(c, out int count))
                count = 0;
            dict[c] = count + 1;
        }
        return dict;
    }

    private static Dictionary<char, int> BuildMultiset(string word)
    {
        var dict = new Dictionary<char, int>();
        if (string.IsNullOrWhiteSpace(word))
            return dict;

        word = word.Trim();
        foreach (var raw in word)
        {
            if (char.IsWhiteSpace(raw)) continue;
            var c = char.ToUpperInvariant(raw);
            if (!dict.TryGetValue(c, out int count))
                count = 0;
            dict[c] = count + 1;
        }
        return dict;
    }

    private static bool MultisetEquals(Dictionary<char, int> a, Dictionary<char, int> b)
    {
        if (a.Count != b.Count)
            return false;

        foreach (var kv in a)
        {
            if (!b.TryGetValue(kv.Key, out int countB))
                return false;
            if (kv.Value != countB)
                return false;
        }

        return true;
    }
}
