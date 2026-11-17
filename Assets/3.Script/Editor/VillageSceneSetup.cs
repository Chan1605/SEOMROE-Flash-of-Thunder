using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Tools/Setup/Village Scene — 타이틀 START GAME으로 들어오는 마을(거점). 다시 실행해도 됨
//  1) 처음 한 번: InGame을 복사해 Village.unity로 (플레이어 · 카메라 · HUD · 메뉴 · 스킬트리 그대로)
//  2) 본편 구역 · 적을 지우고 닫힌 광장 하나 생성. 북 · 서쪽의 붉은 문 = 포탈 자리 (밤 + 등불)
//  3) 포탈 2 (서: 수련장, 북: 산문) + 목표 표식 · 화톳불 · 등불 기둥 · 탁자 · 울타리
//  4) 수련장 출구 → 마을 서쪽 포탈 앞으로 돌아오게 연결 + 빌드 설정
// 마을 겉모습은 Assets/11.Data/Zone/Village_Square.asset (처음 한 번 만들고, 이후엔 인스펙터 값 유지)
public static class VillageSceneSetup
{
    const string ScenePath = "Assets/1.Scene/Village.unity";
    const string TutorialScenePath = "Assets/1.Scene/Tutorial.unity";
    const string ThemePath = "Assets/11.Data/Zone/Village_Square.asset";
    const string ShrinePrefabPath = "Assets/2.Model/Prefabs/Shrine.prefab";
    const string ObjectiveSettingsPath = "Assets/11.Data/Navigation/Nav_Objective.asset";
    const string LanternMatPath = "Assets/6.Materials/Village_Lantern.mat";
    const string PackRoot = "Assets/ThirdParty/Toon Fantasy Nature/";
    const string PortalsName = "VillagePortals";
    const string PropsName = "VillageProps";
    const string ShrinesName = "Shrines";

    // 수련을 끝낸 기록(PlayerPrefs)을 지움 → 마을에서 산문이 다시 잠기고 수련장이 목표가 됨
    [MenuItem("Tools/Debug/Reset Tutorial Record")]
    static void ResetTutorialRecord()
    {
        TutorialDirector.ResetRecord();
        Debug.Log("[Village] 수련 기록을 지웠습니다. 다음 판부터 수련장이 먼저 안내됩니다.");
    }

    [MenuItem("Tools/Setup/Village Scene")]
    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        ZoneTheme theme = LoadOrCreateTheme();
        if (theme == null) return;

        LinkTutorialExit(); // 수련장 씬을 먼저 열어 출구만 고치고 저장

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null && !AssetDatabase.CopyAsset(TutorialSceneSetup.SourceScenePath, ScenePath))
        {
            Debug.LogError("[Village] InGame 씬을 복사하지 못했습니다.");
            return;
        }
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        TutorialSceneSetup.RemoveMainContent(theme.zoneName);
        foreach (string name in new[] { "DungeonEntrance", PortalsName, PropsName, ShrinesName, "TrainingMode" })
            DestroyRoot(name);

        ZoneGenerator.Generate(theme, true, true, true, new int[0]);
        GameObject root = GameObject.Find(theme.zoneName);
        TutorialSceneSetup.StripArena(root);
        Terrain terrain = root.GetComponentInChildren<Terrain>();
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == "Barrier") t.gameObject.SetActive(true); // 문은 모두 닫아 둠 (포탈 막)

        Vector3 c = theme.origin;
        float R = theme.combatRadius; // 배치는 광장 크기에 비례
        var start = new GameObject("VillageStart").transform;
        start.position = Ground(terrain, c + new Vector3(0f, 0f, -0.6f * R));
        TutorialSceneSetup.MovePlayer(start);
        Object.DestroyImmediate(start.gameObject);

        Font font = AssetDatabase.LoadAssetAtPath<Font>(TutorialSceneSetup.BodyFontPath);
        ScriptableObject nav = AssetDatabase.LoadAssetAtPath<ScriptableObject>(ObjectiveSettingsPath);
        var portals = new GameObject(PortalsName).transform;
        // 서쪽: 수련장 (처음부터 열림, 수련 전까지 목표) / 북쪽: 산문 (수련 뒤 열림, 수련 뒤 목표)
        Portal(portals, root, terrain, theme, "Gate_West", Vector3.left, "Portal_Tutorial", "Tutorial", "수련장",
            VillagePortal.When.Always, VillagePortal.When.BeforeTutorial, font, nav, "Training", "연습장"); // [변경] 수련 뒤엔 연습장으로
        Portal(portals, root, terrain, theme, "Gate_Exit", Vector3.forward, "Portal_Sanmun", SceneTransition.MainScene, "산문",
            VillagePortal.When.AfterTutorial, VillagePortal.When.AfterTutorial, font, nav);

        bool shrine = PlaceShrine(terrain, c + new Vector3(0.4f * R, 0f, -0.3f * R), c);
        int props = PlaceProps(terrain, theme);
        TutorialSceneSetup.EnsureRecordingMode();
        AddToBuildSettings();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[Village] 완료 · 포탈 2 · 화톳불 {(shrine ? "배치" : "없음")} · 소품 {props} → {ScenePath}");
    }

    // ===== 테마: 수련장 테마를 복사해 밤 마을로 =====
    static ZoneTheme LoadOrCreateTheme()
    {
        var theme = AssetDatabase.LoadAssetAtPath<ZoneTheme>(ThemePath);
        if (theme != null) return theme;
        ZoneTheme source = TutorialSceneSetup.LoadOrCreateTheme();
        if (source == null) return null;

        theme = Object.Instantiate(source);
        theme.zoneName = "Village_Square";
        theme.seed = source.seed + 211;
        theme.origin = Vector3.zero;
        theme.displayNumber = "";
        theme.displayName = "산문 아랫마을";
        theme.gateSouth = false;
        theme.gateNorth = true;  // 산문 포탈
        theme.gateWest = true;   // 수련장 포탈
        theme.gateEast = false;
        // 광장을 본편 구역의 약 2배로 (반지름 15 → 30)
        theme.combatRadius = 30f;
        theme.wallRadius = 34f;
        theme.terrainSize = 190f;
        theme.hillWidth = 22f;
        theme.innerRockCount = 6;
        theme.treeMaxCount = 220;
        theme.arenaCoverCount = 45;
        theme.outerCoverCount = 90;
        theme.groundParticleCount = 12;

        // 밤 하늘 · 푸른 달빛 · 짙은 남색 안개 → 등불 · 화톳불 · 포탈 빛이 돋보이게
        theme.skybox = AssetDatabase.LoadAssetAtPath<Material>(PackRoot + "Skyboxes/TFF_Skybox_Night_01A.mat");
        theme.sunColor = new Color(0.55f, 0.64f, 0.9f);
        theme.sunEuler = new Vector3(38f, 140f, 0f);
        theme.fog = true;
        theme.fogColor = new Color(0.16f, 0.2f, 0.3f);
        theme.fogDensity = 0.012f;
        theme.overheadParticles = new GameObject[0]; // 낙엽 · 꽃잎은 빼고 반딧불만

        AssetDatabase.CreateAsset(theme, ThemePath);
        AssetDatabase.SaveAssets();
        return theme;
    }

    // ===== 포탈 =====
    // 문 바로 안쪽에 포탈(E 범위), 그보다 5m 안쪽에 도착 자리(Arrive: 다른 씬에서 돌아올 때 섬)
    static void Portal(Transform parent, GameObject zone, Terrain terrain, ZoneTheme theme, string gateName, Vector3 dir,
        string name, string scene, string label, VillagePortal.When unlock, VillagePortal.When guide, Font font, ScriptableObject nav,
        string altScene = "", string altName = "")
    {
        Transform gate = zone.transform.Find("Generated/" + gateName);
        Vector3 gatePos = gate != null ? gate.position : theme.origin + dir * theme.wallRadius;

        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(Ground(terrain, gatePos - dir * 1.2f), Quaternion.LookRotation(dir, Vector3.up));

        var arrive = new GameObject("Arrive").transform;
        arrive.SetParent(go.transform, false);
        arrive.SetPositionAndRotation(Ground(terrain, gatePos - dir * 5f) + Vector3.up * 0.1f, Quaternion.LookRotation(-dir, Vector3.up));

        ObjectiveBeacon beacon = go.AddComponent<ObjectiveBeacon>();
        var b = new SerializedObject(beacon);
        b.FindProperty("showWhen").enumValueIndex = (int)ObjectiveBeacon.Condition.Manual;
        b.FindProperty("markerSettings").objectReferenceValue = nav;
        b.FindProperty("markerHeight").floatValue = 3f;
        b.FindProperty("arriveDistance").floatValue = 3.2f;
        b.ApplyModifiedPropertiesWithoutUndo();

        VillagePortal portal = go.AddComponent<VillagePortal>();
        var p = new SerializedObject(portal);
        p.FindProperty("sceneName").stringValue = scene;
        p.FindProperty("displayName").stringValue = label;
        p.FindProperty("unlock").enumValueIndex = (int)unlock;
        p.FindProperty("guide").enumValueIndex = (int)guide;
        Transform barrier = gate != null ? gate.Find("Barrier") : null;
        p.FindProperty("barrier").objectReferenceValue = barrier != null ? barrier.gameObject : null;
        p.FindProperty("beacon").objectReferenceValue = beacon;
        p.FindProperty("font").objectReferenceValue = font;
        p.FindProperty("altScene").stringValue = altScene; // [변경]
        p.FindProperty("altName").stringValue = altName;
        p.ApplyModifiedPropertiesWithoutUndo();
    }

    // 수련장 출구: 본편 대신 마을 서쪽 포탈 앞으로 + 수련 완료 표시
    static void LinkTutorialExit()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TutorialScenePath) == null) return;
        Scene tutorial = EditorSceneManager.OpenScene(TutorialScenePath, OpenSceneMode.Single);
        SceneExit exit = Object.FindAnyObjectByType<SceneExit>(FindObjectsInactive.Include);
        if (exit == null)
        {
            Debug.LogWarning("[Village] 수련장 씬에 SceneExit가 없습니다. Tools/Setup/Tutorial Exit을 먼저 실행하세요.");
            return;
        }
        var so = new SerializedObject(exit);
        so.FindProperty("sceneName").stringValue = VillagePortal.VillageScene;
        so.FindProperty("spawnAt").stringValue = $"{PortalsName}/Portal_Tutorial/Arrive";
        so.FindProperty("setFlag").stringValue = VillagePortal.TutorialDoneFlag;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(tutorial);
        EditorSceneManager.SaveScene(tutorial);
    }

    // ===== 화톳불 · 소품 =====
    static bool PlaceShrine(Terrain terrain, Vector3 pos, Vector3 lookAt)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShrinePrefabPath);
        if (prefab == null) return false;
        var group = new GameObject(ShrinesName);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.transform);
        go.name = "Shrine (Village)";
        pos = Ground(terrain, pos);
        Vector3 face = lookAt - pos;
        face.y = 0f;
        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(face, Vector3.up));
        if (Object.FindAnyObjectByType<SkillTreeMenu>(FindObjectsInactive.Include) == null)
            Debug.LogWarning("[Village] 씬에 SkillTreeMenu가 없습니다. Tools/Setup/Skill Tree + Shrines를 이 씬에서 한 번 실행하세요.");
        return true;
    }

    // 광장 가장자리에 등불 기둥 8개 (문 방향은 비움) + 남동쪽 탁자 · 의자 + 남쪽 울타리
    static int PlaceProps(Terrain terrain, ZoneTheme theme)
    {
        var group = new GameObject(PropsName).transform;
        Vector3 c = theme.origin;
        int count = 0;

        GameObject pole = Pack("Structures/TFF_Wooden_Pole_01A.prefab");
        Material lantern = LanternMaterial();
        float R = theme.combatRadius;
        float r = R - 2f;
        const int Posts = 12; // 넓어진 둘레에 맞춰 8 → 12
        for (int i = 0; i < Posts; i++)
        {
            float a = (i + 0.5f) * Mathf.PI * 2f / Posts;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            if (Vector3.Dot(dir, Vector3.forward) > 0.9f || Vector3.Dot(dir, Vector3.left) > 0.9f) continue; // 포탈 앞은 비움
            Vector3 pos = Ground(terrain, c + dir * r);
            Transform post = pole != null ? Place(pole, group, pos, -dir, 3.2f) : null;
            if (post == null)
            {
                post = GameObject.CreatePrimitive(PrimitiveType.Cylinder).transform;
                post.SetParent(group, false);
                post.position = pos + Vector3.up * 1.6f;
                post.localScale = new Vector3(0.15f, 1.6f, 0.15f);
            }
            post.name = $"LanternPost_{i}";
            Lantern(group, pos + Vector3.up * 3.3f, lantern);
            count++;
        }

        GameObject table = Pack("Structures/TFF_Wooden_Table_01A.prefab");
        GameObject chair = Pack("Structures/TFF_Wooden_Chair_01A.prefab");
        Vector3 tablePos = c + new Vector3(-0.5f * R, 0f, -0.45f * R);
        if (table != null) { Place(table, group, Ground(terrain, tablePos), Vector3.forward, 1.4f); count++; }
        if (chair != null)
        {
            Place(chair, group, Ground(terrain, tablePos + new Vector3(-1.4f, 0f, 0f)), Vector3.right, 1.1f);
            Place(chair, group, Ground(terrain, tablePos + new Vector3(1.4f, 0f, 0f)), Vector3.left, 1.1f);
            count += 2;
        }

        GameObject fence = Pack("Structures/TFF_Wooden_Fence_01A.prefab");
        if (fence != null)
        {
            for (int i = -4; i <= 4; i++) // 둘레가 길어져 울타리를 촘촘히 · 길게
            {
                float a = -Mathf.PI * 0.5f + i * 2.2f / R; // 남쪽 호
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 tangent = new Vector3(-dir.z, 0f, dir.x);
                Place(fence, group, Ground(terrain, c + dir * (theme.combatRadius - 0.5f)), tangent, 1.2f);
                count++;
            }
        }

        // 넓어진 광장을 채우는 정자 2채 (북동 · 북서, 포탈 길은 비움)
        GameObject pavilion = Pack("Structures/TFF_Wooden_Pavilion_01A.prefab");
        if (pavilion != null)
        {
            foreach (Vector3 offset in new[] { new Vector3(0.5f, 0f, 0.5f), new Vector3(-0.5f, 0f, 0.5f) })
            {
                Vector3 pos = c + offset * R;
                Place(pavilion, group, Ground(terrain, pos), c - pos, 6f);
                count++;
            }
        }
        return count;
    }

    // 프리팹을 세우고 높이(가장 긴 변이 아니라 세로)를 height에 맞춤
    static Transform Place(GameObject prefab, Transform parent, Vector3 pos, Vector3 forward, float height)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        forward.y = 0f;
        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(forward.sqrMagnitude > 0.01f ? forward : Vector3.forward, Vector3.up));
        Renderer[] rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length > 0)
        {
            Bounds bounds = rs[0].bounds;
            foreach (Renderer x in rs) bounds.Encapsulate(x.bounds);
            if (bounds.size.y > 0.01f) go.transform.localScale *= height / bounds.size.y;
        }
        return go.transform;
    }

    // 기둥 위 등불: 빛나는 작은 상자 + 따뜻한 점광원 (그림자 없음)
    static void Lantern(Transform parent, Vector3 pos, Material mat)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(box.GetComponent<Collider>());
        box.name = "Lantern";
        box.transform.SetParent(parent, false);
        box.transform.position = pos;
        box.transform.localScale = new Vector3(0.35f, 0.45f, 0.35f);
        if (mat != null) box.GetComponent<Renderer>().sharedMaterial = mat;
        box.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var light = new GameObject("Light").AddComponent<Light>();
        light.transform.SetParent(box.transform, false);
        light.type = LightType.Point;
        light.color = new Color(1f, 0.68f, 0.35f);
        light.range = 8f;
        light.intensity = 2.2f;
        light.shadows = LightShadows.None;
    }

    static Material LanternMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(LanternMatPath);
        if (mat != null) return mat;
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) return null;
        mat = new Material(shader) { name = "Village_Lantern" };
        mat.SetColor("_BaseColor", new Color(1f, 0.72f, 0.38f));
        AssetDatabase.CreateAsset(mat, LanternMatPath);
        return mat;
    }

    // ===== 도우미 =====
    static GameObject Pack(string relative)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PackRoot + "Prefabs/" + relative);
        if (prefab == null) Debug.LogWarning($"[Village] {PackRoot}Prefabs/{relative}를 찾지 못했습니다.");
        return prefab;
    }

    static Vector3 Ground(Terrain terrain, Vector3 pos)
    {
        pos.y = TutorialSceneSetup.GroundY(terrain, pos);
        return pos;
    }

    static void DestroyRoot(string name)
    {
        GameObject go = SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == name);
        if (go != null) Object.DestroyImmediate(go);
    }

    // 타이틀 바로 다음에 둠 (타이틀 START GAME → 마을)
    static void AddToBuildSettings()
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        if (scenes.Any(s => s.path == ScenePath)) return;
        int title = scenes.FindIndex(s => s.path.EndsWith("/Title.unity"));
        scenes.Insert(title >= 0 ? title + 1 : scenes.Count, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
