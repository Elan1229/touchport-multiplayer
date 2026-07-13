using System.Collections.Generic;
using UnityEngine;

namespace DreamTouch
{
    // Quick in-scene test. No prefabs, no networking needed.
    // Put this on any GameObject, drag a PlayerDreamBag into `bag`, press Play.
    // An on-screen panel shows the current dream and a button per gift.
    public class DreamTestDriver : MonoBehaviour
    {
        public PlayerDreamBag bag;

        [Tooltip("The 'other player' dream — excluded from jumps, and used as the gift's origin.")]
        public DefinitionDream partnerDream;

        readonly List<DefinitionGift> gifts = new List<DefinitionGift>();
        int partnerIdx = -1;

        void Start()
        {
            if (bag == null || bag.allDreams == null) return;
            foreach (var w in bag.allDreams)
                if (w != null && w.gifts != null)
                    foreach (var g in w.gifts)
                        if (g != null && !gifts.Contains(g)) gifts.Add(g);
        }

        void OnEnable()  { if (bag != null) bag.OnDreamChanged += OnChanged; }
        void OnDisable() { if (bag != null) bag.OnDreamChanged -= OnChanged; }

        void OnChanged(PlayerDreamBag p, DefinitionDream from, DefinitionDream to)
        {
            Debug.Log($"[Dream] jump: {(from ? from.dreamId : "?")} -> {to.dreamId}");
        }

        void OnGUI()
        {
            if (bag == null) { GUILayout.Label("Drag a PlayerDreamBag into 'bag'."); return; }

            GUILayout.BeginArea(new Rect(12, 12, 300, 660), GUI.skin.box);

            GUILayout.Label("Current dream:  " + (bag.Current ? bag.Current.dreamId : "?"));

            GUILayout.BeginHorizontal();
            GUILayout.Label("Partner (excluded):  " + (partnerDream ? partnerDream.dreamId : "none"));
            if (GUILayout.Button("cycle", GUILayout.Width(52)))
            {
                partnerIdx = (partnerIdx + 1) % (bag.allDreams.Length + 1);
                partnerDream = partnerIdx < bag.allDreams.Length ? bag.allDreams[partnerIdx] : null;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.Label("Bring back a gift:");
            foreach (var g in gifts)
            {
                if (GUILayout.Button(g.giftName))
                {
                    // origin = partnerDream (the gift came from the other player's dream)
                    bool jumped = bag.ReceiveGift(g, partnerDream, partnerDream);
                    if (!jumped)
                        Debug.Log($"[Dream] brought {g.giftName}, no jump (still {bag.Current.dreamId})");
                }
            }

            GUILayout.EndArea();
        }
    }
}
