using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// 실사 숲(NatureManufacture Forest Environment) 적용: 구역 테마의 나무 · 바위 · 풀 · 지형 질감을 새 팩으로 바꾸고 그 구역을 다시 생성
// Tools/Map/Forest Pack/...  (InGame 씬을 열어 두고 실행)
//  - 카툰 테마 백업 · 되돌리기는 옛 맵 소스 정리와 함께 삭제
//  - 구역 이름 · 크기 · 문 · 웨이브 · 정자 같은 랜드마크 · 하늘 · 안개는 그대로 (나무 · 바위 · 풀 · 바닥만)
public static class ForestPackSetup
{
    const string Root = "Assets/NatureManufacture Assets/Forest Environment Dynamic Nature/";
    const string WindPrefabPath = "Assets/NatureManufacture Assets/NatureManufacture Wind/Prefab_Wind.prefab";
    const string ZoneFolder = "Assets/11.Data/Zone/";
    const string LayerFolder = ZoneFolder + "Forest Layers"; // 광택을 줄인 지형 레이어 사본
    const string LookFolder = ZoneFolder + "Forest Look"; // 하늘 재질 · 후처리 프로필
    const string LookName = "ForestLook";
    const string WindName = "NM_Wind";

    // ZoneGenerator 메뉴와 같은 순서
    static readonly (string theme, string menu)[] Zones =
    {
        ("Zone1_Sanmun", "Tools/Map/Generate Zone 1 (Sanmun)"),
        ("Zone2_Forest", "Tools/Map/Generate Zone 2 (Forest)"),
        ("Zone3_Temple", "Tools/Map/Generate Zone 3 (Temple)"),
        ("Zone4_Boss", "Tools/Map/Generate Boss Zone"),
        ("Zone3_Trial", "Tools/Map/Generate Trial"),
    };

    [MenuItem("Tools/Map/Forest Pack/Apply To Zone 1 (Sanmun)")]
    static void ApplyZone1() => Apply(0, 1);

    [MenuItem("Tools/Map/Forest Pack/Apply To All Zones")]
    static void ApplyAll() => Apply(0, Zones.Length);

    // 본편 외 씬: 씬마다 열어서 테마 교체 → 소품 · 지형 질감만 다시 생성 (결계 · 포탈 · 진행기 연결 유지) → 저장
    static readonly (string scene, string[] themes)[] OtherScenes =
    {
        ("Assets/1.Scene/Dungeon.unity", new[] { "Dungeon_West", "Dungeon_East", "Dungeon_North", "Dungeon_Hub" }),
        ("Assets/1.Scene/Village.unity", new[] { "Village_Square" }),
        ("Assets/1.Scene/Tutorial.unity", new[] { "Tutorial_Yard" }),
        ("Assets/1.Scene/Training.unity", new[] { "Training_Yard" }),
    };

    [MenuItem("Tools/Map/Forest Pack/Apply To Other Scenes (Dungeon · Village · Tutorial · Training)")]
    static void ApplyOtherScenes()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!AssetDatabase.IsValidFolder("Assets/NatureManufacture Assets"))
        {
            Debug.LogError("[ForestPack] NatureManufacture Assets 폴더가 없습니다.");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        string returnTo = EditorSceneManager.GetActiveScene().path;

        var set = Load();
        int zones = 0;
        foreach (var entry in OtherScenes)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(entry.scene) == null) { Debug.LogWarning($"[ForestPack] 씬 없음: {entry.scene}"); continue; }
            var scene = EditorSceneManager.OpenScene(entry.scene, OpenSceneMode.Single);
            foreach (string themeName in entry.themes)
            {
                ZoneTheme theme = PrepareTheme(themeName, set);
                if (theme != null && ZoneGenerator.RebuildProps(theme)) zones++;
            }
            EnsureWind();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        if (!string.IsNullOrEmpty(returnTo)) EditorSceneManager.OpenScene(returnTo, OpenSceneMode.Single);
        Debug.Log($"[ForestPack] 다른 씬 {OtherScenes.Length}개 · 구역 {zones}개에 실사 숲 적용 · 씬 저장까지 완료");
    }

    // 배치 목록 교체 → 저장 (카툰 원본 백업 삭제)
    static ZoneTheme PrepareTheme(string themeName, PackSet set)
    {
        string path = $"{ZoneFolder}{themeName}.asset";
        var theme = AssetDatabase.LoadAssetAtPath<ZoneTheme>(path);
        if (theme == null) { Debug.LogWarning($"[ForestPack] {path} 없음 · 건너뜀"); return null; }
        ApplyTo(theme, set);
        EditorUtility.SetDirty(theme);
        AssetDatabase.SaveAssets();
        return theme;
    }

    // 숲 분위기 전체 적용. 분위기 3종: 낮(본편 1~3) · 노을(보스 · 시련 · 수련장 · 연습장) · 밤(던전 · 마을)
    enum Mood { Day, Dusk, Night }

    static Mood MoodOf(string themeName)
    {
        if (themeName.StartsWith("Dungeon") || themeName.StartsWith("Village")) return Mood.Night;
        if (themeName == "Zone4_Boss" || themeName == "Zone3_Trial" || themeName.StartsWith("Tutorial") || themeName.StartsWith("Training")) return Mood.Dusk;
        return Mood.Day;
    }

    // 씬마다 주변광 세기 (밤 씬은 하늘이 어두워 주변광이 거의 없음 → 크게 올림)
    static readonly (string scene, float ambient)[] AmbientScenes =
    {
        ("Assets/1.Scene/InGame.unity", 1.15f),
        ("Assets/1.Scene/Dungeon.unity", 1.3f), // 1.6 → 1.3
        ("Assets/1.Scene/Village.unity", 1.3f),
        ("Assets/1.Scene/Tutorial.unity", 1.15f),
        ("Assets/1.Scene/Training.unity", 1.15f),
    };

    static void SetAmbient(string scenePath)
    {
        foreach (var entry in AmbientScenes)
            if (entry.scene == scenePath) RenderSettings.ambientIntensity = entry.ambient;
    }

    // 지형 · 소품은 그대로 두고 색보정 프로필 3종 + 씬별 주변광만 갱신 (다시 생성 없이 빠르게)
    [MenuItem("Tools/Map/Forest Pack/Forest Look/Update Brightness (All Scenes)")]
    static void UpdateBrightness()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        string returnTo = EditorSceneManager.GetActiveScene().path;
        EnsureFolders();
        LookProfile(Mood.Day);
        LookProfile(Mood.Dusk);
        LookProfile(Mood.Night);
        foreach (var entry in AmbientScenes)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(entry.scene) == null) continue;
            var scene = EditorSceneManager.OpenScene(entry.scene, OpenSceneMode.Single);
            RenderSettings.ambientIntensity = entry.ambient;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        if (!string.IsNullOrEmpty(returnTo)) EditorSceneManager.OpenScene(returnTo, OpenSceneMode.Single);
        Debug.Log("[ForestPack] 밝기 갱신 · 색보정 3종 + 씬 주변광 저장 완료");
    }

    [MenuItem("Tools/Map/Forest Pack/Forest Look/Zone 1 (Sanmun)")]
    static void LookZone1()
    {
        if (!InGameOpen()) return;
        ApplyLook(0);
        Debug.Log("[ForestPack] 구역 1 숲 분위기 적용 → 씬을 저장하세요");
    }

    [MenuItem("Tools/Map/Forest Pack/Forest Look/All Zones (InGame)")]
    static void LookAllZones()
    {
        if (!InGameOpen()) return;
        for (int i = 0; i < Zones.Length; i++) ApplyLook(i);
        SetAmbient(EditorSceneManager.GetActiveScene().path);
        Debug.Log($"[ForestPack] 본편 {Zones.Length}개 구역 숲 분위기 적용 → 씬을 저장하세요");
    }

    // 다른 씬은 결계 · 포탈 연결 때문에 소품만 다시 만들고(RebuildProps), 구역이 없는 씬 분위기는 에디터에서 RenderSettings로 저장
    [MenuItem("Tools/Map/Forest Pack/Forest Look/Other Scenes (Dungeon · Village · Tutorial · Training)")]
    static void LookOtherScenes()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        string returnTo = EditorSceneManager.GetActiveScene().path;
        EnsureFolders();

        var set = Load();
        int zones = 0;
        foreach (var entry in OtherScenes)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(entry.scene) == null) { Debug.LogWarning($"[ForestPack] 씬 없음: {entry.scene}"); continue; }
            var scene = EditorSceneManager.OpenScene(entry.scene, OpenSceneMode.Single);
            SetAmbient(entry.scene);
            ZoneTheme last = null;
            foreach (string themeName in entry.themes)
            {
                ZoneTheme theme = PrepareTheme(themeName, set);
                if (theme == null) continue;
                Mood mood = MoodOf(themeName);
                ApplyLookTo(theme, set, mood);
                EditorUtility.SetDirty(theme);
                AssetDatabase.SaveAssets();
                if (!ZoneGenerator.RebuildProps(theme)) continue;
                EnsureLookVolume(theme, mood);
                last = theme;
                zones++;
            }
            if (last != null) ZoneGenerator.ApplyAtmosphere(last); // 밤도 새 하늘 적용 · 씬 기본 분위기 = 마지막 구역
            EnsureWind();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        if (!string.IsNullOrEmpty(returnTo)) EditorSceneManager.OpenScene(returnTo, OpenSceneMode.Single);
        Debug.Log($"[ForestPack] 다른 씬 구역 {zones}개 숲 분위기 적용 · 저장 완료");
    }

    static void ApplyLook(int index)
    {
        EnsureFolders();
        var set = Load();
        ZoneTheme theme = PrepareTheme(Zones[index].theme, set);
        if (theme == null) return;
        Mood mood = MoodOf(Zones[index].theme);
        ApplyLookTo(theme, set, mood);
        // 본편만 지형까지 다시 만듦 → 자연스러운 언덕 · S자 길 · 완만한 길가 (다른 씬은 소품만 다시 만들어서 지형 값은 안 바꿈)
        theme.pathMeander = 7f;
        theme.corridorFade = 11f;
        EditorUtility.SetDirty(theme);
        AssetDatabase.SaveAssets();
        EditorApplication.ExecuteMenuItem(Zones[index].menu); // 웨이브 · 직접 둔 오브젝트 유지

        EnsureLookVolume(theme, mood);
        EnsureWind();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder(LookFolder)) AssetDatabase.CreateFolder(ZoneFolder.TrimEnd('/'), "Forest Look");
    }

    // 핵심: 화면 인상을 정하는 값들. 소품 교체(ApplyTo) 위에 덮음. 밤은 달빛 색 · 각도만 원래 테마
    static void ApplyLookTo(ZoneTheme theme, PackSet set, Mood mood)
    {
        if (mood == Mood.Day)
        {
            theme.skybox = SkyMaterial("Forest_Sky", new Color(0.42f, 0.5f, 0.56f), 0.85f, 0.035f, 1.35f);
            theme.fogColor = new Color(0.56f, 0.62f, 0.6f);   // 초록빛 도는 회색 숲 공기
            theme.fogDensity = 0.014f;
            theme.sunColor = new Color(1f, 0.9f, 0.74f);
            theme.sunEuler = new Vector3(38f, -40f, 0f);       // 낮은 해 → 나무 그림자가 공터로 길게
        }
        else if (mood == Mood.Dusk)
        {
            theme.skybox = SkyMaterial("Forest_Sky_Dusk", new Color(0.52f, 0.42f, 0.4f), 1.6f, 0.05f, 1.25f);
            theme.fogColor = new Color(0.6f, 0.5f, 0.45f);
            theme.fogDensity = 0.016f;
            theme.sunColor = new Color(1f, 0.7f, 0.48f);
            theme.sunEuler = new Vector3(16f, -50f, 0f);
        }
        else // 밤: 카툰 남색 하늘 대신 어두운 사실적 밤하늘 + 짙은 남색 안개 (달빛 색 · 각도는 원래 테마)
        {
            theme.skybox = SkyMaterial("Forest_Sky_Night", new Color(0.2f, 0.25f, 0.36f), 0.4f, 0.02f, 0.2f);
            theme.fogColor = new Color(0.07f, 0.09f, 0.13f);
            theme.fogDensity = 0.02f;
        }
        theme.fog = true;

        theme.innerTreeCount = 9;
        theme.treeMinDistance = 3.6f;
        theme.treeMaxCount = 200;

        theme.extraLayer = Layer("Terrain_Layer6_Moss", 0.3f);
        theme.extraAmount = 0.7f;

        // 협곡처럼 깎인 길 → 낮은 골짜기 + 불규칙한 언덕 + 비탈은 돌 질감 (모든 씬, 길 굽힘만 본편)
        theme.hillRoughness = 1f;
        theme.valleyWidth = 26f;
        theme.slopeLayer = Layer("Terrain_Layer8_Stones", 0.3f);

        // 바위 고리 사이사이 쓰러진 통나무 · 뿌리 (같은 크기 규칙으로 섞임)
        theme.wallRocks = Concat(set.wallRocks,
            Prefabs("Stumps Roots and Branches/Prefabs Moss Cover/prefab_{0}.prefab", "dead_log_01_Moss", "dead_log_03_Moss", "beech_old_roots_01_Moss"));
    }

    static Material SkyMaterial(string name, Color tint, float thickness, float sunSize, float exposure)
    {
        string path = $"{LookFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Skybox/Procedural")) { name = name };
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetFloat("_SunDisk", 2f);
        mat.SetFloat("_SunSize", sunSize);
        mat.SetFloat("_SunSizeConvergence", 6f);
        mat.SetFloat("_AtmosphereThickness", thickness);
        mat.SetColor("_SkyTint", tint);
        mat.SetColor("_GroundColor", new Color(0.3f, 0.3f, 0.27f));
        mat.SetFloat("_Exposure", exposure); // 하늘 = 주변광 → 그늘이 새까맣지 않게
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // 분위기별 색보정. 낮 값은 구역 1에서 확인한 값 그대로
    static VolumeProfile LookProfile(Mood mood)
    {
        string path = $"{LookFolder}/Forest_Volume{(mood == Mood.Day ? "" : "_" + mood)}.asset";
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
        }

        // 팩 데모 씬 값 참고: 채도는 거의 그대로, 대비 13, 밝은 쪽(gain)을 올려 숲이 칙칙하지 않게
        float exposure = 0.65f, contrast = 10f, saturation = -3f, temperature = -5f, bloomIntensity = 0.45f, vignetteIntensity = 0.22f;
        float gamma = 0.04f, gain = 0.1f;
        Vector4 shadows = new Vector4(0.92f, 1f, 1.06f, 0.04f), highlights = new Vector4(1.06f, 1f, 0.9f, 0f);
        if (mood == Mood.Dusk)
        {
            exposure = 0.75f; saturation = 0f; temperature = 8f; gamma = 0.06f;
            shadows = new Vector4(0.95f, 0.94f, 1.08f, 0.1f);
            highlights = new Vector4(1.1f, 0.98f, 0.85f, 0f);
        }
        else if (mood == Mood.Night)
        {
            exposure = 0.45f; contrast = 12f; saturation = -14f; temperature = -10f; bloomIntensity = 0.6f; vignetteIntensity = 0.3f; // 너무 밝아 낮처럼 보임 → 낮춤
            gamma = 0.06f; gain = 0.08f;
            shadows = new Vector4(0.9f, 0.95f, 1.1f, 0.08f); // 적 실루엣만 보일 만큼 그늘을 들어 올림
            highlights = new Vector4(1f, 1f, 1f, 0f);
        }

        Get<Tonemapping>(profile).mode.Override(TonemappingMode.ACES);
        var color = Get<ColorAdjustments>(profile);
        color.postExposure.Override(exposure);
        color.contrast.Override(contrast);
        color.saturation.Override(saturation);
        color.colorFilter.Override(new Color(1f, 0.98f, 0.94f));
        var white = Get<WhiteBalance>(profile);
        white.temperature.Override(temperature);
        white.tint.Override(-4f);
        var smh = Get<ShadowsMidtonesHighlights>(profile);
        smh.shadows.Override(shadows);       // 그늘은 차갑게
        smh.highlights.Override(highlights);
        var lgg = Get<LiftGammaGain>(profile);
        lgg.gamma.Override(new Vector4(1f, 1f, 1f, gamma));
        lgg.gain.Override(new Vector4(1f, 1f, 1f, gain));
        var bloom = Get<Bloom>(profile);
        bloom.threshold.Override(1.1f);
        bloom.intensity.Override(bloomIntensity);
        bloom.scatter.Override(0.6f);
        var vignette = Get<Vignette>(profile);
        vignette.intensity.Override(vignetteIntensity);
        vignette.smoothness.Override(0.45f);

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        return profile;
    }

    // 프로필 안 효과는 서브 에셋으로 저장해야 다시 열어도 남음
    static T Get<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (profile.TryGet(out T c)) return c;
        c = profile.Add<T>(true);
        c.name = typeof(T).Name;
        AssetDatabase.AddObjectToAsset(c, profile);
        return c;
    }

    static void EnsureLookVolume(ZoneTheme theme, Mood mood)
    {
        GameObject root = GameObject.Find(theme.zoneName);
        if (root == null) return;
        Transform t = root.transform.Find(LookName);
        GameObject go = t != null ? t.gameObject : new GameObject(LookName, typeof(Volume), typeof(ZoneLookVolume));
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = Vector3.zero;

        var volume = go.GetComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 5f; // 기본 전역 볼륨(0) 위, 궁극기 연출(200) 아래
        volume.sharedProfile = LookProfile(mood);
        var look = go.GetComponent<ZoneLookVolume>();
        if (look == null) look = go.AddComponent<ZoneLookVolume>();
        var so = new SerializedObject(look);
        so.FindProperty("radius").floatValue = theme.wallRadius + 6f;
        so.FindProperty("blend").floatValue = 15f;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void Apply(int from, int to)
    {
        if (!InGameOpen()) return;
        if (!AssetDatabase.IsValidFolder("Assets/NatureManufacture Assets"))
        {
            Debug.LogError("[ForestPack] NatureManufacture Assets 폴더가 없습니다. 팩과 URP 17.1 지원팩을 먼저 Import하세요.");
            return;
        }

        var set = Load();
        for (int i = from; i < to; i++)
        {
            if (PrepareTheme(Zones[i].theme, set) == null) continue; // 교체를 다른 씬 메뉴와 공용으로
            EditorApplication.ExecuteMenuItem(Zones[i].menu); // 이 구역만 다시 생성 (웨이브 · 직접 배치한 오브젝트는 유지)
        }
        EnsureWind();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"[ForestPack] {to - from}개 구역에 실사 숲 적용 · 나무 {set.trees.Length} · 바위 {set.wallRocks.Length} · 풀 {set.arenaCover.Length}/{set.outerCover.Length} → 씬을 저장하세요");
    }

    // 핵심: 테마의 배치 목록만 교체. 크기 범위는 실사 너도밤나무에 맞게 키우고 개수는 성능을 위해 조금 줄임
    static void ApplyTo(ZoneTheme theme, PackSet set)
    {
        theme.trees = set.trees;
        theme.treeHeight = new Vector2(14f, 22f);
        theme.treeMaxCount = Mathf.Min(theme.treeMaxCount, 160);
        theme.wallRocks = set.wallRocks;
        theme.wallRockSize = new Vector2(5f, 8.5f); // 실사 바위는 납작해서 크게 · 촘촘히 (벽 사이로 맨 언덕이 비치지 않게)
        theme.wallRockSpacing = 0.5f;
        theme.outerCoverCount = 220; // 언덕까지 덤불 · 고사리로 덮음
        theme.outerCoverReach = 24f;
        if (set.pathStones.Length > 0) // 작은 돌 → 이끼 낀 돌길 조각 (밟는 길 느낌)
        {
            theme.pathStones = set.pathStones;
            theme.pathStoneSize = 2.6f;
            theme.pathStoneSpacing = 2.4f;
        }
        theme.innerRocks = set.innerRocks;
        theme.arenaCover = set.arenaCover; // 전투 공간 안: 낮은 풀 · 낙엽만 (시야 가림 없음)
        theme.outerCover = set.outerCover; // 벽 바깥: 고사리 · 덤불 · 어린 나무 · 버섯 · 가지
        // 낙엽만 (Meadow Dust는 HDR 블룸에서 하얀 덩어리로 번져 뺌)
        if (set.leaves != null) theme.overheadParticles = new[] { set.leaves };

        // 지형 풀 · 비탈 절벽 · 반사 프로브 · 도리이 · 정자
        theme.detailGrass = set.detailGrass;
        theme.detailPlants = set.detailPlants;
        theme.detailDensity = 1f;
        theme.detailDistance = 70f;
        theme.slopeRocks = set.slopeRocks;
        theme.slopeRockCount = 14;
        theme.reflectionProbe = true;
        theme.gateMaterial = set.lacquer;
        theme.gateTrimMaterial = set.blackWood;
        theme.gateBaseMaterial = set.stone;
        if (theme.landmarks != null && theme.landmarks.Length > 0 && set.shrine != null) theme.landmarks = new[] { set.shrine };
        if (set.groundLayer != null) theme.groundLayer = set.groundLayer;
        if (set.arenaLayer != null) theme.arenaLayer = set.arenaLayer;
        if (set.roadLayer != null) theme.roadLayer = set.roadLayer;
    }

    class PackSet
    {
        public GameObject[] trees, wallRocks, innerRocks, arenaCover, outerCover, pathStones;
        public GameObject leaves, shrine;
        public GameObject[] detailGrass, detailPlants, slopeRocks;
        public Material lacquer, blackWood, stone;
        public TerrainLayer groundLayer, arenaLayer, roadLayer;
    }

    static PackSet Load()
    {
        var set = new PackSet
        {
            // 팩 설명서 권장: 숲에는 6 · 7 · 8번 (폴리곤 적고 빛이 잘 들게 최적화)
            trees = Prefabs("Beech Trees/Prefabs/prefab_beech_tree_{0}.prefab", "06", "07", "08"),
            wallRocks = Concat(
                Prefabs("Rocks/Prefabs/prefab_beech_forest_stones_01_{0}.prefab", "1", "2", "3", "4", "5", "6", "7", "8", "9", "10"),
                Prefabs("Rocks/Prefabs Leaves/prefab_beech_forest_stones_01_{0}_Leaves.prefab", "2", "4", "6", "8", "10")), // 작은 돌(11~14)은 벽에서 빼고 돌길로
            pathStones = Prefabs("Stumps Roots and Branches/Prefabs Moss Cover/prefab_stone_road_0{0}_Moss.prefab", "1", "2", "3", "4", "5", "6"),
            innerRocks = Concat(
                Prefabs("Rocks/Prefabs Leaves/prefab_beech_forest_stones_01_{0}_Leaves.prefab", "1", "3", "5", "7"),
                Prefabs("Stumps Roots and Branches/Prefabs Moss Cover/prefab_{0}.prefab",
                    "beech_forest_stump_01_1_Moss", "beech_old_roots_01_Moss", "dead_log_01_Moss", "dead_log_03_Moss")),
            arenaCover = Concat(
                Prefabs("Foliage and Grass/Prefabs/prefab_grass_{0}.prefab", "01_1", "01_2", "01_3", "02_1", "02_2", "03_1"),
                Prefabs("Details/Prefabs/prefab_detail_leaves_{0}.prefab", "01", "02", "03")),
            outerCover = Concat(
                Prefabs("Foliage and Grass/Prefabs/prefab_fern_01_{0}.prefab", "1", "2", "3", "4"),
                Prefabs("Bushes/Prefabs/Prefab_Forest_black_cherry_{0}.prefab", "00", "02", "04"),
                Prefabs("Beech Trees/Prefabs/prefab_beech_plant_{0}.prefab", "00", "01", "02"),
                Prefabs("Foliage and Grass/Prefabs/prefab_{0}.prefab", "plant_01_1", "plant_02_1", "shaggy_soldier_01_1"),
                Prefabs("Mushrooms/Prefabs/prefab_{0}.prefab", "Beech_mushroom_01", "Mushroom_Armillaria_01"),
                Prefabs("Stumps Roots and Branches/Prefabs/prefab_beech_forest_branch_01_{0}.prefab", "1", "3"),
                // 쓰러진 통나무 · 마른 가지 · 버섯 더
                Prefabs("Stumps Roots and Branches/Prefabs Leaves Cover/prefab_dead_log_0{0}_Leaves.prefab", "2", "4", "5"),
                Prefabs("Stumps Roots and Branches/Prefabs/prefab_beech_dry_bough_0{0}.prefab", "1", "2"),
                Prefabs("Mushrooms/Prefabs/prefab_{0}.prefab", "Beech_mushroom_02A", "Beech_mushroom_03A", "Mushroom_Lactarius_01", "Mushroom_Russula_01")),
            detailGrass = Prefabs("Foliage and Grass/Unity Terrain Prefabs/prefab_Unity_Terrain_grass_{0}.prefab", "01_1", "01_2", "02_1", "02_2", "03_1", "03_2"),
            detailPlants = Prefabs("Foliage and Grass/Unity Terrain Prefabs/prefab_Unity_Terrain_{0}.prefab",
                "fern_01_1", "fern_01_3", "plant_01_1", "plant_02_1", "shaggy_soldier_01_1", "lily_valley_01_1"),
            slopeRocks = Concat(
                Prefabs("Stumps Roots and Branches/Prefabs Leaves Cover/prefab_beech_forest_scarp_0{0}_Leaves.prefab", "1", "2", "1_U", "2_U"),
                Prefabs("Stumps Roots and Branches/Prefabs Moss Cover/prefab_sandy_slope _0{0}_Moss.prefab", "1", "3", "5")),
            leaves = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Particles/prefab_Leafs Forest.prefab"),
            groundLayer = Layer("Terrain_Layer5_Leaves", 0.3f), // 사본 · 광택 상한
            arenaLayer = Layer("Terrain_Layer2_Soil", 0.25f),
            roadLayer = Layer("Terrain_Layer3_Soil_Wet", 0.45f),
        };
        BuildStructures(set);
        return set;
    }

    // ===== 구조물: 실사 도리이 재질 · 숲 정자 프리팹 (카툰 정자 대신) =====
    const string StructureFolder = LookFolder + "/Structures";
    const string WoodTex = Root + "Small Architecture/Bridge/Textures/T_wood_bridge_";
    const string StoneTex = Root + "Rocks/Models/Textures/T_beech_forest_stones_01_";

    static void BuildStructures(PackSet set)
    {
        if (!AssetDatabase.IsValidFolder(LookFolder)) AssetDatabase.CreateFolder(ZoneFolder.TrimEnd('/'), "Forest Look");
        if (!AssetDatabase.IsValidFolder(StructureFolder)) AssetDatabase.CreateFolder(LookFolder, "Structures");
        set.lacquer = WoodMaterial("Torii_Lacquer", new Color(0.66f, 0.16f, 0.08f), 0.38f, new Vector2(1f, 3f));
        set.blackWood = WoodMaterial("Torii_Black", new Color(0.09f, 0.08f, 0.075f), 0.3f, new Vector2(3f, 1f));
        // [변경] 팩 돌 재질(Top Cover 셰이더)은 기본 상자 메시에서 NaN 픽셀 → 블룸이 하얀 덩어리로 번짐. 팩 돌 질감으로 URP Lit 재질을 따로 만듦
        set.stone = TexMaterial("Torii_Stone", new Color(0.85f, 0.85f, 0.82f), 0.25f, new Vector2(1f, 1f), StoneTex + "BC_SM.tga", StoneTex + "N.png");
        Material roof = WoodMaterial("Shrine_Roof", new Color(0.2f, 0.17f, 0.15f), 0.2f, new Vector2(4f, 4f));
        set.shrine = BuildShrine(set, roof);
    }

    // 다리 나무 질감에 색을 곱해 결이 보이는 옻칠 · 검은 나무
    static Material WoodMaterial(string name, Color color, float smoothness, Vector2 tiling) =>
        TexMaterial(name, color * 1.6f, smoothness, tiling, WoodTex + "BC.tga", WoodTex + "N.tga");

    static Material TexMaterial(string name, Color color, float smoothness, Vector2 tiling, string basePath, string normalPath)
    {
        string path = $"{StructureFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            AssetDatabase.CreateAsset(mat, path);
        }
        var baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>(basePath);
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
        color.a = 1f;
        mat.SetColor("_BaseColor", color);
        if (baseTex != null) { mat.SetTexture("_BaseMap", baseTex); mat.SetTextureScale("_BaseMap", tiling); }
        if (normal != null)
        {
            mat.SetTexture("_BumpMap", normal);
            mat.SetTextureScale("_BumpMap", tiling);
            mat.EnableKeyword("_NORMALMAP");
        }
        mat.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // 핵심: 돌 기단(땅에 2m 묻힘) · 옻칠 기둥 4 · 테두리 들보 · 제단 · 우진각 지붕(메시) · 용마루
    static GameObject BuildShrine(PackSet set, Material roof)
    {
        Material red = set.lacquer, black = set.blackWood, stone = set.stone != null ? set.stone : black;
        var root = new GameObject("Forest_Shrine");
        Box(root.transform, "Platform", new Vector3(0f, -0.75f, 0f), new Vector3(5.8f, 2.5f, 5.8f), stone, true);
        Box(root.transform, "Step", new Vector3(0f, 0.15f, -3.35f), new Vector3(2f, 0.3f, 0.9f), stone, true);
        foreach (float sx in new[] { -1f, 1f })
        foreach (float sz in new[] { -1f, 1f })
            Cylinder(root.transform, "Post", new Vector3(sx * 2.3f, 2f, sz * 2.3f), new Vector3(0.34f, 1.5f, 0.34f), red, true);
        Box(root.transform, "Beam_F", new Vector3(0f, 3.45f, -2.3f), new Vector3(5f, 0.28f, 0.28f), red, false);
        Box(root.transform, "Beam_B", new Vector3(0f, 3.45f, 2.3f), new Vector3(5f, 0.28f, 0.28f), red, false);
        Box(root.transform, "Beam_L", new Vector3(-2.3f, 3.45f, 0f), new Vector3(0.28f, 0.28f, 5f), red, false);
        Box(root.transform, "Beam_R", new Vector3(2.3f, 3.45f, 0f), new Vector3(0.28f, 0.28f, 5f), red, false);
        Box(root.transform, "Altar", new Vector3(0f, 1f, 1.2f), new Vector3(1.4f, 1f, 0.9f), black, true);
        Box(root.transform, "Altar_Top", new Vector3(0f, 1.55f, 1.2f), new Vector3(1.6f, 0.1f, 1.1f), red, false);

        var roofGo = new GameObject("Roof");
        roofGo.transform.SetParent(root.transform, false);
        roofGo.AddComponent<MeshFilter>().sharedMesh = RoofMesh();
        roofGo.AddComponent<MeshRenderer>().sharedMaterial = roof;
        Box(root.transform, "Ridge", new Vector3(0f, 5.42f, 0f), new Vector3(1.8f, 0.22f, 0.3f), black, false);

        string path = $"{StructureFolder}/Forest_Shrine.prefab";
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    // 처마(한 변 7.6m, 높이 3.6m) → 용마루(길이 1.6m, 높이 5.3m). 아래에서도 보이게 양면
    static Mesh RoofMesh()
    {
        string path = $"{StructureFolder}/Forest_Shrine_Roof.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh == null)
        {
            mesh = new Mesh { name = "Forest_Shrine_Roof" };
            AssetDatabase.CreateAsset(mesh, path);
        }
        const float e = 3.8f, eaveY = 3.6f, ridgeX = 0.8f, ridgeY = 5.3f;
        Vector3 a = new Vector3(-e, eaveY, -e), b = new Vector3(e, eaveY, -e), c = new Vector3(e, eaveY, e), d = new Vector3(-e, eaveY, e);
        Vector3 r0 = new Vector3(-ridgeX, ridgeY, 0f), r1 = new Vector3(ridgeX, ridgeY, 0f);
        var faces = new List<Vector3[]>
        {
            new[] { a, r0, r1, b }, // 앞
            new[] { c, r1, r0, d }, // 뒤
            new[] { d, r0, a },     // 왼쪽
            new[] { b, r1, c },     // 오른쪽
        };
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        foreach (bool back in new[] { false, true })
        foreach (Vector3[] f in faces)
        {
            int start = verts.Count;
            foreach (Vector3 v in f)
            {
                verts.Add(v);
                uvs.Add(new Vector2(v.x * 0.25f, (v.z + v.y) * 0.25f));
            }
            for (int i = 1; i < f.Length - 1; i++)
            {
                if (!back) { tris.Add(start); tris.Add(start + i); tris.Add(start + i + 1); }
                else { tris.Add(start); tris.Add(start + i + 1); tris.Add(start + i); }
            }
        }
        mesh.Clear();
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat, bool collider) =>
        Primitive(PrimitiveType.Cube, parent, name, pos, scale, mat, collider);

    static GameObject Cylinder(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat, bool collider) =>
        Primitive(PrimitiveType.Cylinder, parent, name, pos, scale, mat, collider);

    static GameObject Primitive(PrimitiveType type, Transform parent, string name, Vector3 pos, Vector3 scale, Material mat, bool collider)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    // 그래픽 품질 (목표 90fps 이상): 그림자 거리 40 → 60 · 캐스케이드 2 → 3. 직접 바꾼 값이 있으면 그대로 둠
    [MenuItem("Tools/Map/Forest Pack/Forest Look/Quality (90fps)")]
    static void Quality()
    {
        var asset = AssetDatabase.LoadMainAssetAtPath("Assets/2.Model/PIpeLine/Settings/PC_RPAsset.asset");
        if (asset == null) { Debug.LogWarning("[ForestPack] PC_RPAsset 없음"); return; }
        var so = new SerializedObject(asset);
        int n = 0;
        SerializedProperty distance = so.FindProperty("m_ShadowDistance");
        if (distance != null && distance.floatValue <= 40f) { distance.floatValue = 60f; n++; }
        SerializedProperty cascades = so.FindProperty("m_ShadowCascadeCount");
        if (cascades != null && cascades.intValue < 3) { cascades.intValue = 3; n++; }
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        Debug.Log($"[ForestPack] 그래픽 품질 · 바꾼 항목 {n}개 (그림자 거리 60 · 캐스케이드 3)");
    }

    static GameObject[] Prefabs(string pattern, params string[] ids)
    {
        var list = new List<GameObject>();
        foreach (string id in ids)
        {
            string path = Root + string.Format(pattern, id);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go != null) list.Add(go);
            else Debug.LogWarning($"[ForestPack] 없음: {path}");
        }
        return list.ToArray();
    }

    static GameObject[] Concat(params GameObject[][] groups)
    {
        var list = new List<GameObject>();
        foreach (GameObject[] g in groups) list.AddRange(g);
        return list.ToArray();
    }

    // 팩 레이어는 HDRP용 마스크라 URP 지형에서 금속 · 광택이 과해 바닥이 젖은 아스팔트처럼 번들거림
    // → 사본을 만들어 마스크의 금속 0, 광택 상한을 smoothMax로 눌러 씀 (팩 원본은 건드리지 않음)
    static TerrainLayer Layer(string name, float smoothMax)
    {
        var source = AssetDatabase.LoadAssetAtPath<TerrainLayer>($"{Root}Terrain Layers/{name}.terrainlayer");
        if (source == null) { Debug.LogWarning($"[ForestPack] 지형 레이어 없음: {name}"); return null; }

        if (!AssetDatabase.IsValidFolder(LayerFolder)) AssetDatabase.CreateFolder(ZoneFolder.TrimEnd('/'), "Forest Layers");
        string path = $"{LayerFolder}/{name}_Matte.terrainlayer";
        var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
        if (layer == null)
        {
            layer = Object.Instantiate(source);
            AssetDatabase.CreateAsset(layer, path);
        }
        else EditorUtility.CopySerialized(source, layer);
        layer.name = name + "_Matte";
        layer.metallic = 0f;
        layer.smoothness = smoothMax;
        Vector4 min = layer.maskMapRemapMin, max = layer.maskMapRemapMax;
        min.x = max.x = 0f;       // R 금속
        max.w = Mathf.Min(max.w, smoothMax); // A 광택
        min.w = Mathf.Min(min.w, max.w);
        layer.maskMapRemapMin = min;
        layer.maskMapRemapMax = max;
        EditorUtility.SetDirty(layer);
        return layer;
    }

    // 팩 셰이더의 나뭇잎 흔들림은 씬에 바람 프리팹이 있어야 움직임
    static void EnsureWind()
    {
        if (GameObject.Find(WindName) != null) return;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WindPrefabPath);
        if (prefab == null) return;
        var wind = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        wind.name = WindName;
    }

    static bool InGameOpen()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return false;
        if (EditorSceneManager.GetActiveScene().name == "InGame") return true;
        Debug.LogError("[ForestPack] InGame 씬을 열어 두고 실행하세요.");
        return false;
    }
}
