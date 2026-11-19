using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Tools/Setup/Player Visuals: 옷자락 펄럭임 · 캐릭터 보조광 · 발밑 먼지/물 튐을 Player 프리팹에 붙이고 재질을 만듦
// 보조광이 플레이어에만 닿도록 URP 에셋의 렌더링 레이어(Light Layers)를 켬
public static class PlayerVisualsSetup
{
    const string PlayerPrefab = "Assets/2.Model/Prefabs/Player.prefab";
    const string Folder = "Assets/6.Materials/Player";
    const string SoftTexture = "Assets/6.Materials/Shrine/Fire_Soft.png";

    [MenuItem("Tools/Setup/Player Visuals")]
    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        EnableLightLayers();

        Material dust = Particle("Fx_Dust", false);
        Material splash = Particle("Fx_Splash", false);

        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefab);
        MakeClothMeshesReadable(root); // 옷자락이 실행 중 정점 · 뼈 가중치를 읽음 (빌드에서도)
        Ensure<PlayerClothFlutter>(root);
        Ensure<PlayerRimLight>(root);
        var ground = new SerializedObject(Ensure<PlayerGroundFx>(root));
        ground.FindProperty("dustMaterial").objectReferenceValue = dust;
        ground.FindProperty("splashMaterial").objectReferenceValue = splash;
        ground.ApplyModifiedPropertiesWithoutUndo();
        // [변경] 강림 화면 색도 뺌 → 붙어 있던 컴포넌트를 떼어 둠 (그 뒤 EnchantBodyGlow.cs를 지워도 됨)
        Component glow = root.GetComponent("EnchantBodyGlow");
        if (glow != null) Object.DestroyImmediate(glow, true);
        PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefab);
        PrefabUtility.UnloadPrefabContents(root);
        AssetDatabase.SaveAssets();
        Debug.Log("[PlayerVisuals] Player 프리팹에 옷자락 · 보조광 · 발밑 효과 · 강림 발광 추가 완료");
    }

    static void MakeClothMeshesReadable(GameObject root)
    {
        foreach (SkinnedMeshRenderer smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.sharedMesh == null) continue;
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(smr.sharedMesh)) as ModelImporter;
            if (importer == null || importer.isReadable) continue;
            importer.isReadable = true;
            importer.SaveAndReimport();
            Debug.Log($"[PlayerVisuals] {importer.assetPath}: Read/Write 켬 (옷자락용)");
        }
    }

    static T Ensure<T>(GameObject go) where T : Component
    {
        T c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }

    static void EnableLightLayers()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset"))
        {
            Object rp = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));
            var so = new SerializedObject(rp);
            SerializedProperty layers = so.FindProperty("m_SupportsLightLayers");
            if (layers == null) { Debug.LogWarning($"[PlayerVisuals] {rp.name}: 렌더링 레이어 설정을 찾지 못했습니다. URP 에셋에서 Use Rendering Layers를 직접 켜세요."); continue; }
            if (layers.boolValue) continue;
            layers.boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"[PlayerVisuals] {rp.name}: 렌더링 레이어 켬 (보조광이 플레이어에만 닿게)");
        }
    }

    // URP 파티클 재질 (부드러운 원 질감). additive = 더하기 합성(빛), 아니면 반투명
    static Material Particle(string name, bool additive)
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/6.Materials", "Player");
        string path = $"{Folder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = name };
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(SoftTexture));
        mat.SetColor("_BaseColor", additive ? new Color(2f, 2f, 2f, 1f) : Color.white);
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", additive ? 2f : 0f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(mat);
        return mat;
    }
}
