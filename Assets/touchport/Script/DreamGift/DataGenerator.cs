#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DreamTouch
{
    // Menu: DreamTouch > Generate Default Assets
    // Creates all Keyword / Gift / Dream assets pre-filled with the tuned prototype weights.
    // Safe to re-run: it updates existing assets in place.
    public static class DataGenerator
    {
        const string Root = "Assets/DreamTouch/Data";

        [MenuItem("DreamTouch/Generate Default Assets")]
        public static void Generate()
        {
            EnsureFolder("Assets/DreamTouch");
            EnsureFolder(Root);
            EnsureFolder(Root + "/Keywords");
            EnsureFolder(Root + "/Gifts");
            EnsureFolder(Root + "/Dreams");

            // ---- 1) Keywords ----
            string[] kwNames = { "Toy", "Computer", "Animal", "Balloon", "Dark",
                                 "Flower", "Glass", "Sunlight", "Halo", "Cloud", "Booth" };
            var kw = new Dictionary<string, DefinitionKeyword>();
            foreach (var n in kwNames)
                kw[n] = GetOrCreate<DefinitionKeyword>($"{Root}/Keywords/{n}.asset", n);

            // ---- 2) Gifts (name -> carrier keywords) ----
            var giftDefs = new (string name, string[] keys)[]
            {
                ("GoldenFish",       new[]{ "Animal" }),
                ("Toy",              new[]{ "Toy" }),
                ("TV",               new[]{ "Computer" }),
                ("Balloon",          new[]{ "Balloon" }),
                ("TeddyBear",        new[]{ "Animal" }),
                ("DirtToy",          new[]{ "Toy", "Dark" }),
                ("Flower",           new[]{ "Flower", "Sunlight" }),
                ("TelephoneHandset", new[]{ "Computer" }),
                ("Raccoon",          new[]{ "Animal" }),
                ("FlowerAtNight",    new[]{ "Flower", "Dark" }),
                ("Moon",             new[]{ "Halo", "Sunlight" }),
                ("SunsetCloud",      new[]{ "Cloud", "Dark" }),
                ("SunnyCloud",       new[]{ "Cloud", "Sunlight" }),
                ("Lantern",          new[]{ "Halo", "Sunlight" }),
                ("FlowerInterior",   new[]{ "Flower", "Dark" }),
                ("GameCartridges",   new[]{ "Computer", "Toy" }),
            };
            var gifts = new Dictionary<string, DefinitionGift>();
            foreach (var g in giftDefs)
            {
                var asset = GetOrCreate<DefinitionGift>($"{Root}/Gifts/{g.name}.asset", g.name);
                asset.giftName = g.name;
                var keys = new List<DefinitionKeyword>();
                foreach (var k in g.keys) keys.Add(kw[k]);
                asset.keywords = keys.ToArray();
                EditorUtility.SetDirty(asset);
                gifts[g.name] = asset;
            }

            // ---- 3) Dreams (id -> signature + gift names) ----
            var dreams = new (string id, (string k, int w)[] sig, string[] gifts)[]
            {
                ("ChildRoom",
                    new[]{ ("Toy",3), ("Computer",2), ("Animal",1), ("Balloon",1) },
                    new[]{ "GoldenFish", "Toy", "TV", "Balloon" }),
                ("SubmarineRoom",
                    new[]{ ("Toy",2), ("Computer",3), ("Animal",1), ("Dark",3), ("Balloon",1) },
                    new[]{ "TeddyBear", "DirtToy", "TV" }),
                ("PhoneBooth",
                    new[]{ ("Flower",2), ("Computer",2), ("Glass",5), ("Sunlight",1), ("Animal",2), ("Booth",10) },
                    new[]{ "Flower", "TelephoneHandset", "Raccoon" }),
                ("SunsetPhoneBooth",
                    new[]{ ("Computer",2), ("Glass",5), ("Halo",1), ("Flower",2), ("Sunlight",1), ("Booth",10), ("Dark",1) },
                    new[]{ "FlowerAtNight", "TelephoneHandset", "Moon", "SunsetCloud" }),
                ("CloudRoom",
                    new[]{ ("Cloud",3), ("Toy",2), ("Balloon",2), ("Sunlight",2) },
                    new[]{ "SunnyCloud", "Balloon" }),
                ("AsianAlley",
                    new[]{ ("Toy",2), ("Computer",3), ("Flower",2), ("Halo",2), ("Dark",1) },
                    new[]{ "Lantern", "FlowerInterior", "GameCartridges" }),
                ("Greenhouse",
                    new[]{ ("Flower",3), ("Sunlight",3), ("Animal",2), ("Glass",3) },
                    new[]{ "Flower", "TeddyBear" }),
            };
            foreach (var w in dreams)
            {
                var asset = GetOrCreate<DefinitionDream>($"{Root}/Dreams/{w.id}.asset", w.id);
                asset.dreamId = w.id;

                var sig = new List<WeightedKeyword>();
                foreach (var pair in w.sig)
                    sig.Add(new WeightedKeyword { keyword = kw[pair.k], weight = pair.w });
                asset.signature = sig.ToArray();

                var gl = new List<DefinitionGift>();
                foreach (var gn in w.gifts) gl.Add(gifts[gn]);
                asset.gifts = gl.ToArray();

                EditorUtility.SetDirty(asset);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"DreamTouch: generated {kwNames.Length} keywords, {giftDefs.Length} gifts, {dreams.Length} dreams under {Root}");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace("\\", "/");
            var leaf = Path.GetFileName(path);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        static T GetOrCreate<T>(string path, string assetName) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a == null)
            {
                a = ScriptableObject.CreateInstance<T>();
                a.name = assetName;
                AssetDatabase.CreateAsset(a, path);
            }
            return a;
        }
    }
}
#endif
