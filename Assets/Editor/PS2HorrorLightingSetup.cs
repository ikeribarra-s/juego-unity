#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class PS2HorrorLightingSetup
{
    private static readonly Color AmbientColor = new Color(0.095f, 0.1f, 0.12f);
    private static readonly Color FogColor = new Color(0.055f, 0.06f, 0.072f);
    private static readonly Color DirectionalColor = new Color(0.36f, 0.42f, 0.55f);

    [MenuItem("Fluffterror/Setup/Apply PS2 Horror Lighting")]
    public static void ApplyPS2HorrorLighting()
    {
        ApplyRenderSettings();
        SetupDirectionalFill();
        SetupDungeonMaterials();

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log("Applied PS2 horror lighting setup: dark ambient, no environment reflections, weak directional fill, matte dungeon materials.");
    }

    private static void ApplyRenderSettings()
    {
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = AmbientColor;
        RenderSettings.ambientIntensity = 0.75f;

        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        RenderSettings.customReflection = null;
        RenderSettings.reflectionIntensity = 0f;
        RenderSettings.reflectionBounces = 1;

        RenderSettings.skybox = null;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = FogColor;
        RenderSettings.fogDensity = 0.028f;
    }

    private static void SetupDirectionalFill()
    {
        Light directional = null;
        Light[] lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i].type != LightType.Directional) continue;
            directional = lights[i];
            break;
        }

        if (directional == null)
        {
            var go = new GameObject("PS2 Horror Directional Fill");
            Undo.RegisterCreatedObjectUndo(go, "Create PS2 Horror Directional Fill");
            directional = go.AddComponent<Light>();
            directional.type = LightType.Directional;
            go.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
        }
        else
        {
            Undo.RecordObject(directional, "Apply PS2 Horror Directional Fill");
        }

        directional.color = DirectionalColor;
        directional.intensity = 0.14f;
        directional.shadows = LightShadows.Soft;
        directional.shadowStrength = 0.45f;
        directional.shadowBias = 0.08f;
        directional.shadowNormalBias = 0.4f;
    }

    private static void SetupDungeonMaterials()
    {
        string[] guids = AssetDatabase.FindAssets("t:Material", new[] { "Assets" });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || !ShouldTuneMaterial(path, mat)) continue;

            Undo.RecordObject(mat, "Tune PS2 Horror Material");
            TuneMaterial(mat);
            EditorUtility.SetDirty(mat);
        }
    }

    private static bool ShouldTuneMaterial(string path, Material mat)
    {
        string lowerPath = path.ToLowerInvariant();
        string lowerName = mat.name.ToLowerInvariant();

        if (lowerName.Contains("glow") || lowerName.Contains("evidence"))
            return false;

        return lowerPath.Contains("/materials/floors/") ||
               lowerName.Contains("floor") ||
               lowerName.Contains("wall") ||
               lowerName.Contains("brick") ||
               lowerName.Contains("dungeon") ||
               lowerName.Contains("stone") ||
               lowerName.Contains("model_basecolor") ||
               lowerName.Contains("textureskeleton");
    }

    private static void TuneMaterial(Material mat)
    {
        if (mat.HasProperty("_Metallic"))
            mat.SetFloat("_Metallic", 0f);

        if (mat.HasProperty("_Smoothness"))
            mat.SetFloat("_Smoothness", 0f);

        if (mat.HasProperty("_Glossiness"))
            mat.SetFloat("_Glossiness", 0f);

        if (mat.HasProperty("_EnvironmentReflections"))
            mat.SetFloat("_EnvironmentReflections", 0f);

        if (mat.HasProperty("_SpecularHighlights"))
            mat.SetFloat("_SpecularHighlights", 0f);

        if (mat.HasProperty("_SpecColor"))
            mat.SetColor("_SpecColor", Color.black);

        if (mat.HasProperty("_EmissionColor"))
            mat.SetColor("_EmissionColor", Color.black);
    }
}
#endif
