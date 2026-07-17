#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class MeshyOcclusionMaterialBatcher
{
    private const string ShaderPath = "Meta/Depth/URP/Occlusion Lit";
    private const string GeneratedMaterialSuffix = "_Occlusion";
    private const string PackedMapSuffix = "_metallic_smoothness";

    private class TextureSet
    {
        public string FolderPath;
        public string AssetName;
        public Texture2D Base;
        public Texture2D Normal;
        public Texture2D Metallic;
        public Texture2D Roughness;
        public Texture2D Emission;
        public Material Material;
        public Texture2D PackedMetallicSmoothness;
    }

    [MenuItem("Tools/Meshy/Build Occlusion Materials From Selection")]
    public static void BuildFromSelection()
    {
        var shader = Shader.Find(ShaderPath);
        if (shader == null)
        {
            EditorUtility.DisplayDialog(
                "Meshy Material Batcher",
                "Could not find shader:\n" + ShaderPath,
                "OK"
            );
            return;
        }

        var folders = GetSelectedFolders();
        if (folders.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "Meshy Material Batcher",
                "Select one or more Meshy FBX folders in the Project panel, then run this again.",
                "OK"
            );
            return;
        }

        try
        {
            AssetDatabase.StartAssetEditing();
            foreach (var folder in folders)
            {
                ProcessFolder(folder, shader);
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        EditorUtility.DisplayDialog(
            "Meshy Material Batcher",
            "Done. Built occlusion materials for " + folders.Count + " selected folder(s).",
            "OK"
        );
    }

    private static List<string> GetSelectedFolders()
    {
        var folders = new HashSet<string>();
        foreach (var guid in Selection.assetGUIDs)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path))
                continue;

            if (AssetDatabase.IsValidFolder(path))
            {
                folders.Add(path);
                continue;
            }

            var parent = Path.GetDirectoryName(path)?.Replace("\\", "/");
            if (!string.IsNullOrEmpty(parent) && AssetDatabase.IsValidFolder(parent))
                folders.Add(parent);
        }

        return folders.OrderBy(path => path).ToList();
    }

    private static void ProcessFolder(string folderPath, Shader shader)
    {
        var textureSet = FindTextureSet(folderPath);
        if (textureSet.Base == null)
        {
            Debug.LogWarning("[Meshy] No base texture found in " + folderPath);
            return;
        }

        textureSet.Material = CreateOrUpdateMaterial(textureSet, shader);
        AssignMaterialToModels(folderPath, textureSet.Material);
        Debug.Log("[Meshy] Built material: " + AssetDatabase.GetAssetPath(textureSet.Material));
    }

    private static TextureSet FindTextureSet(string folderPath)
    {
        var textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { folderPath });
        var textures = textureGuids
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => !string.IsNullOrEmpty(path))
            .OrderBy(path => path)
            .ToList();

        var set = new TextureSet
        {
            FolderPath = folderPath,
            AssetName = SanitizeName(Path.GetFileName(folderPath))
        };

        foreach (var path in textures)
        {
            var lower = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null)
                continue;

            if (IsNormalName(lower))
            {
                set.Normal = tex;
            }
            else if (IsRoughnessName(lower))
            {
                set.Roughness = tex;
            }
            else if (IsMetallicName(lower))
            {
                set.Metallic = tex;
            }
            else if (IsEmissionName(lower))
            {
                set.Emission = tex;
            }
            else if (IsBaseName(lower))
            {
                set.Base = tex;
            }
        }

        if (set.Normal != null)
            ConfigureNormalTexture(AssetDatabase.GetAssetPath(set.Normal));

        if (set.Metallic != null || set.Roughness != null)
            set.PackedMetallicSmoothness = CreatePackedMetallicSmoothness(set);

        return set;
    }

    private static bool IsBaseName(string lower)
    {
        return lower.Contains("_texture")
            && !IsNormalName(lower)
            && !IsRoughnessName(lower)
            && !IsMetallicName(lower)
            && !IsEmissionName(lower)
            && !lower.Contains(PackedMapSuffix);
    }

    private static bool IsNormalName(string lower)
    {
        return lower.Contains("normal");
    }

    private static bool IsRoughnessName(string lower)
    {
        return lower.Contains("roughness") || lower.Contains("rough");
    }

    private static bool IsMetallicName(string lower)
    {
        return lower.Contains("metallic") || lower.Contains("metalness");
    }

    private static bool IsEmissionName(string lower)
    {
        return lower.Contains("emission") || lower.Contains("emissive");
    }

    private static void ConfigureNormalTexture(string texturePath)
    {
        var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
        if (importer == null)
            return;

        if (importer.textureType == TextureImporterType.NormalMap)
            return;

        importer.textureType = TextureImporterType.NormalMap;
        importer.sRGBTexture = false;
        importer.SaveAndReimport();
    }

    private static Texture2D CreatePackedMetallicSmoothness(TextureSet set)
    {
        var source = set.Metallic != null ? set.Metallic : set.Roughness;
        if (source == null)
            return null;

        var metallicPath = set.Metallic != null ? AssetDatabase.GetAssetPath(set.Metallic) : null;
        var roughnessPath = set.Roughness != null ? AssetDatabase.GetAssetPath(set.Roughness) : null;

        var metallicPixels = ReadPixelsOrWhite(metallicPath, source.width, source.height, 0f);
        var roughnessPixels = ReadPixelsOrWhite(roughnessPath, source.width, source.height, 0.5f);

        var packed = new Texture2D(source.width, source.height, TextureFormat.RGBA32, true, true);
        var pixels = new Color[source.width * source.height];

        for (var i = 0; i < pixels.Length; i++)
        {
            var metallic = Grayscale(metallicPixels[i]);
            var smoothness = 1f - Grayscale(roughnessPixels[i]);
            pixels[i] = new Color(metallic, metallic, metallic, smoothness);
        }

        packed.SetPixels(pixels);
        packed.Apply();

        var outputPath = set.FolderPath + "/" + set.AssetName + PackedMapSuffix + ".png";
        File.WriteAllBytes(outputPath, packed.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(packed);
        AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);

        var importer = AssetImporter.GetAtPath(outputPath) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath);
    }

    private static Color[] ReadPixelsOrWhite(string texturePath, int fallbackWidth, int fallbackHeight, float fallbackValue)
    {
        if (string.IsNullOrEmpty(texturePath))
            return Enumerable.Repeat(new Color(fallbackValue, fallbackValue, fallbackValue, fallbackValue), fallbackWidth * fallbackHeight).ToArray();

        var absolutePath = Path.Combine(Directory.GetCurrentDirectory(), texturePath);
        if (!File.Exists(absolutePath))
            return Enumerable.Repeat(new Color(fallbackValue, fallbackValue, fallbackValue, fallbackValue), fallbackWidth * fallbackHeight).ToArray();

        var bytes = File.ReadAllBytes(absolutePath);
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        tex.LoadImage(bytes);

        if (tex.width != fallbackWidth || tex.height != fallbackHeight)
        {
            var resized = ResizeTexture(tex, fallbackWidth, fallbackHeight);
            UnityEngine.Object.DestroyImmediate(tex);
            tex = resized;
        }

        var pixels = tex.GetPixels();
        UnityEngine.Object.DestroyImmediate(tex);
        return pixels;
    }

    private static Texture2D ResizeTexture(Texture2D source, int width, int height)
    {
        var old = RenderTexture.active;
        var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        Graphics.Blit(source, rt);
        RenderTexture.active = rt;

        var resized = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
        resized.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        resized.Apply();

        RenderTexture.active = old;
        RenderTexture.ReleaseTemporary(rt);
        return resized;
    }

    private static float Grayscale(Color color)
    {
        return color.r * 0.299f + color.g * 0.587f + color.b * 0.114f;
    }

    private static Material CreateOrUpdateMaterial(TextureSet set, Shader shader)
    {
        var materialPath = set.FolderPath + "/" + set.AssetName + GeneratedMaterialSuffix + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, materialPath);
        }
        else
        {
            material.shader = shader;
        }

        material.name = set.AssetName + GeneratedMaterialSuffix;
        SetTexture(material, set.Base, "_BaseMap", "_MainTex", "_BaseColorMap", "_BaseColorTex");
        SetTexture(material, set.Normal, "_BumpMap", "_NormalMap", "_NormalTex");
        SetTexture(material, set.PackedMetallicSmoothness, "_MetallicGlossMap", "_MetallicMap", "_MetallicRoughnessMap", "_MetallicRoughnessTex");
        SetTexture(material, set.Emission, "_EmissionMap", "_EmissiveColorMap", "_EmissionTex");

        SetFloat(material, "_WorkflowMode", 1f);
        SetFloat(material, "_Metallic", GuessMetallic(set.AssetName));
        SetFloat(material, "_Smoothness", GuessSmoothness(set.AssetName));
        SetColor(material, "_BaseColor", Color.white);
        SetColor(material, "_Color", Color.white);

        if (set.Emission != null)
        {
            SetColor(material, "_EmissionColor", GuessEmissionColor(set.AssetName));
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        else
        {
            SetColor(material, "_EmissionColor", Color.black);
            material.DisableKeyword("_EMISSION");
        }

        EditorUtility.SetDirty(material);
        return material;
    }

    private static float GuessMetallic(string assetName)
    {
        return 0f;
    }

    private static float GuessSmoothness(string assetName)
    {
        var lower = assetName.ToLowerInvariant();
        if (lower.Contains("balloon") || lower.Contains("sphere") || lower.Contains("goldfish") || lower.Contains("fish"))
            return 0.82f;
        if (lower.Contains("lego") || lower.Contains("block") || lower.Contains("toy"))
            return 0.58f;
        if (lower.Contains("lantern") || lower.Contains("cloud"))
            return 0.25f;
        if (lower.Contains("raccoon") || lower.Contains("bear") || lower.Contains("teddy"))
            return 0.2f;
        if (lower.Contains("flower") || lower.Contains("cherry"))
            return 0.35f;
        return 0.5f;
    }

    private static Color GuessEmissionColor(string assetName)
    {
        var lower = assetName.ToLowerInvariant();
        if (lower.Contains("lantern"))
            return new Color(1.5f, 0.65f, 0.75f, 1f);
        if (lower.Contains("cloud"))
            return new Color(1.1f, 1.05f, 0.9f, 1f);
        return Color.black;
    }

    private static void AssignMaterialToModels(string folderPath, Material material)
    {
        var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { folderPath });
        foreach (var guid in prefabGuids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            AssignMaterialToPrefabAsset(path, material);
        }

        var modelGuids = AssetDatabase.FindAssets("t:Model", new[] { folderPath });
        foreach (var guid in modelGuids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            AssignMaterialToModelImporter(path, material);
        }
    }

    private static void AssignMaterialToPrefabAsset(string prefabPath, Material material)
    {
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (var i = 0; i < materials.Length; i++)
                    materials[i] = material;
                renderer.sharedMaterials = materials;
                EditorUtility.SetDirty(renderer);
            }
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void AssignMaterialToModelImporter(string modelPath, Material material)
    {
        var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
        if (importer == null)
            return;

        var embeddedMaterials = AssetDatabase
            .LoadAllAssetsAtPath(modelPath)
            .OfType<Material>()
            .ToList();

        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.materialLocation = ModelImporterMaterialLocation.External;

        foreach (var embeddedMaterial in embeddedMaterials)
        {
            var identifier = new AssetImporter.SourceAssetIdentifier(typeof(Material), embeddedMaterial.name);
            importer.AddRemap(identifier, material);
        }

        importer.SaveAndReimport();
    }

    private static void SetTexture(Material material, Texture texture, params string[] propertyNames)
    {
        if (texture == null)
            return;

        foreach (var propertyName in propertyNames)
        {
            if (!material.HasProperty(propertyName))
                continue;
            material.SetTexture(propertyName, texture);
            return;
        }

        Debug.LogWarning("[Meshy] No matching texture property on shader for " + texture.name);
    }

    private static void SetFloat(Material material, string propertyName, float value)
    {
        if (material.HasProperty(propertyName))
            material.SetFloat(propertyName, value);
    }

    private static void SetColor(Material material, string propertyName, Color value)
    {
        if (material.HasProperty(propertyName))
            material.SetColor(propertyName, value);
    }

    private static string SanitizeName(string raw)
    {
        var chars = raw.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_').ToArray();
        return new string(chars).Trim('_');
    }
}
#endif
