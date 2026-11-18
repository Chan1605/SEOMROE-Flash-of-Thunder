using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 실사 숲(NatureManufacture Forest Environment) 적용: 구역 테마의 나무 · 바위 · 풀 · 지형 질감을 새 팩으로 바꾸고 그 구역을 다시 생성
// Tools/Map/Forest Pack/...  (InGame 씬을 열어 두고 실행)
//  - 처음 바꿀 때 원래 테마를 11.Data/Zone/Toon Backup에 복사해 둠 → Restore Toon Themes로 언제든 되돌림
//  - 구역 이름 · 크기 · 문 · 웨이브 · 정자 같은 랜드마크 · 하늘 · 안개는 그대로 (나무 · 바위 · 풀 · 바닥만)
public static class ForestPackSetup
{
    const string Root = "Assets/NatureManufacture Assets/Forest Environment Dynamic Nature/";
    const string WindPrefabPath = "Assets/NatureManufacture Assets/NatureManufacture Wind/Prefab_Wind.prefab";
    const string ZoneFolder = "Assets/11.Data/Zone/";
    const string BackupFolder = ZoneFolder + "Toon Backup";
    const string LayerFolder = ZoneFolder + "Forest Layers"; // 광택을 줄인 지형 레이어 사본
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

    // [변경] 본편 외 씬: 씬마다 열어서 테마 교체 → 소품 · 지형 질감만 다시 생성 (결계 · 포탈 · 진행기 연결 유지) → 저장
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
        if (!AssetDatabase.IsValidFolder(BackupFolder)) AssetDatabase.CreateFolder(ZoneFolder.TrimEnd('/'), "Toon Backup");

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

    // 원본 백업(처음 한 번) → 배치 목록 교체 → 저장
    static ZoneTheme PrepareTheme(string themeName, PackSet set)
    {
        string path = $"{ZoneFolder}{themeName}.asset";
        var theme = AssetDatabase.LoadAssetAtPath<ZoneTheme>(path);
        if (theme == null) { Debug.LogWarning($"[ForestPack] {path} 없음 · 건너뜀"); return null; }
        string backup = $"{BackupFolder}/{themeName}.asset";
        if (!File.Exists(backup)) AssetDatabase.CopyAsset(path, backup);
        ApplyTo(theme, set);
        EditorUtility.SetDirty(theme);
        AssetDatabase.SaveAssets();
        return theme;
    }

    [MenuItem("Tools/Map/Forest Pack/Restore Toon Themes")]
    static void Restore()
    {
        if (!InGameOpen()) return;
        int restored = 0;
        foreach (var zone in Zones)
        {
            string themeName = zone.theme, menu = zone.menu;
            var backup = AssetDatabase.LoadAssetAtPath<ZoneTheme>($"{BackupFolder}/{themeName}.asset");
            var theme = AssetDatabase.LoadAssetAtPath<ZoneTheme>($"{ZoneFolder}{themeName}.asset");
            if (backup == null || theme == null) continue;
            EditorUtility.CopySerialized(backup, theme);
            EditorUtility.SetDirty(theme);
            AssetDatabase.SaveAssets();
            EditorApplication.ExecuteMenuItem(menu);
            restored++;
        }
        Debug.Log($"[ForestPack] 카툰 테마로 되돌림 · {restored}개 구역 다시 생성 → 씬을 저장하세요");
    }

    static void Apply(int from, int to)
    {
        if (!InGameOpen()) return;
        if (!AssetDatabase.IsValidFolder("Assets/NatureManufacture Assets"))
        {
            Debug.LogError("[ForestPack] NatureManufacture Assets 폴더가 없습니다. 팩과 URP 17.1 지원팩을 먼저 Import하세요.");
            return;
        }
        if (!AssetDatabase.IsValidFolder(BackupFolder)) AssetDatabase.CreateFolder(ZoneFolder.TrimEnd('/'), "Toon Backup");

        var set = Load();
        for (int i = from; i < to; i++)
        {
            if (PrepareTheme(Zones[i].theme, set) == null) continue; // [변경] 백업 · 교체를 다른 씬 메뉴와 공용으로
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
        if (set.pathStones.Length > 0) // 카툰 돌판 → 작은 실사 돌을 듬성듬성
        {
            theme.pathStones = set.pathStones;
            theme.pathStoneSize = 1.1f;
            theme.pathStoneSpacing = 3.2f;
        }
        theme.innerRocks = set.innerRocks;
        theme.arenaCover = set.arenaCover; // 전투 공간 안: 낮은 풀 · 낙엽만 (시야 가림 없음)
        theme.outerCover = set.outerCover; // 벽 바깥: 고사리 · 덤불 · 어린 나무 · 버섯 · 가지
        if (set.leaves != null) theme.overheadParticles = new[] { set.leaves };
        if (set.groundLayer != null) theme.groundLayer = set.groundLayer;
        if (set.arenaLayer != null) theme.arenaLayer = set.arenaLayer;
        if (set.roadLayer != null) theme.roadLayer = set.roadLayer;
    }

    class PackSet
    {
        public GameObject[] trees, wallRocks, innerRocks, arenaCover, outerCover, pathStones;
        public GameObject leaves;
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
            pathStones = Prefabs("Rocks/Prefabs/prefab_beech_forest_stones_01_{0}.prefab", "11", "12", "13", "14"),
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
                Prefabs("Stumps Roots and Branches/Prefabs/prefab_beech_forest_branch_01_{0}.prefab", "1", "3")),
            leaves = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Particles/prefab_Leafs Forest.prefab"),
            groundLayer = Layer("Terrain_Layer5_Leaves", 0.3f), // 사본 · 광택 상한
            arenaLayer = Layer("Terrain_Layer2_Soil", 0.25f),
            roadLayer = Layer("Terrain_Layer3_Soil_Wet", 0.45f),
        };
        return set;
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
