using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// 맵 꾸미기: 관상용 물 재질 · 직접 만든 구조물(석등 · 집 · 상자 · 통 · 장작 · 제단) · 마을 / 보스 구역 배치 메뉴
// 배치는 씬의 "Dressing" 묶음에만 하므로 다시 실행하면 통째로 새로 만듦 (지형 · 웨이브 · 포탈은 그대로)
public static class MapDressing
{
    const string StructureFolder = "Assets/11.Data/Zone/Forest Look/Structures";
    const string WaterFolder = "Assets/6.Materials/Water";
    const string PackRoot = "Assets/NatureManufacture Assets/Forest Environment Dynamic Nature/";
    const string VillageScene = "Assets/1.Scene/Village.unity";
    const string DressName = "Dressing";

    // ===== 물 =====
    // 연못 재질 (SEOMROE/Water). 깊이로 물가를 투명하게 하므로 URP Depth Texture를 켬
    public static Material WaterMaterial()
    {
        string normalPath = $"{WaterFolder}/Water_Normal.png";
        var importer = AssetImporter.GetAtPath(normalPath) as TextureImporter;
        if (importer != null && importer.textureType != TextureImporterType.NormalMap)
        {
            importer.textureType = TextureImporterType.NormalMap;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.SaveAndReimport();
        }

        Shader shader = Shader.Find("SEOMROE/Water");
        if (shader == null)
        {
            Debug.LogWarning("[Dressing] SEOMROE/Water 셰이더를 찾지 못했습니다 (6.Materials/Water/SeomroeWater.shader).");
            return null;
        }
        string path = $"{WaterFolder}/Water_Pond.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader) { name = "Water_Pond" };
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = shader;
        mat.SetTexture("_NormalMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
        // 잔잔한 거울 수면: 물결 약하게 · 잘게 · 느리게, 반사 강하게 (재질에 저장된 예전 값을 덮음)
        mat.SetColor("_ShallowColor", new Color(0.14f, 0.2f, 0.19f, 0.3f));
        mat.SetColor("_DeepColor", new Color(0.02f, 0.045f, 0.055f, 0.95f));
        mat.SetFloat("_DepthDistance", 0.7f);
        mat.SetFloat("_NormalStrength", 0.2f);
        mat.SetFloat("_Tiling", 0.38f);
        mat.SetFloat("_FlowSpeed", 0.018f);
        mat.SetFloat("_Reflection", 1f);
        mat.SetFloat("_Roughness", 0.02f);
        mat.SetFloat("_F0", 0.06f);
        mat.SetFloat("_PlanarDistort", 0.03f);
        mat.SetFloat("_SpecPower", 700f);
        mat.SetFloat("_SpecStrength", 3f);
        EditorUtility.SetDirty(mat);
        EnsureDepthTexture();
        return mat;
    }

    static void EnsureDepthTexture()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset"))
        {
            Object rp = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));
            var so = new SerializedObject(rp);
            SerializedProperty depth = so.FindProperty("m_RequireDepthTexture");
            if (depth == null || depth.boolValue) continue;
            depth.boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"[Dressing] {rp.name}: Depth Texture 켬 (물가 투명 처리용)");
        }
    }

    // ===== 재질 =====
    static Material Load(string name) => AssetDatabase.LoadAssetAtPath<Material>($"{StructureFolder}/{name}.mat");

    static Material Lit(string name, Color color, float smoothness)
    {
        string path = $"{StructureFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // 창호지 · 등불처럼 스스로 빛나는 면 (블룸에 살짝 번짐)
    static Material Glow(string name, Color color, float intensity)
    {
        Material mat = Lit(name, color, 0.1f);
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", color * intensity);
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    class Mats
    {
        public Material stone, wood, dark, red, roof, plaster, paper, straw, rune, charred;
    }

    static Mats LoadMats()
    {
        var m = new Mats
        {
            stone = Load("Torii_Stone"),
            wood = Load("Wood_Natural"),
            dark = Load("Torii_Black"),
            red = Load("Torii_Lacquer"),
            roof = Load("Shrine_Roof"),
            plaster = Lit("Plaster", new Color(0.74f, 0.7f, 0.62f), 0.08f),
            paper = Glow("Paper_Glow", new Color(1f, 0.78f, 0.5f), 1.6f),
            straw = Lit("Straw_Rope", new Color(0.62f, 0.52f, 0.32f), 0.05f),
            rune = Glow("Thunder_Rune", new Color(0.55f, 0.62f, 1f), 3f),
            charred = Lit("Charred", new Color(0.07f, 0.065f, 0.06f), 0.15f),
        };
        if (m.stone == null || m.wood == null || m.dark == null)
            Debug.LogWarning("[Dressing] Structures 재질(Torii_Stone · Wood_Natural · Torii_Black)이 없습니다. Forest Look 메뉴를 한 번 실행하세요.");
        return m;
    }

    // ===== 구조물 프리팹 =====
    // 석등 (가스가 등롱 모양, 약 2.2m): 받침 · 기둥 · 받침대 · 불집(창호지 빛) · 갓 · 보주 + 따뜻한 점광원
    static GameObject BuildStoneLantern(Mats m)
    {
        var root = new GameObject("Forest_StoneLantern");
        Box(root.transform, "Base", new Vector3(0f, 0.12f, 0f), new Vector3(0.9f, 0.24f, 0.9f), m.stone, true);
        Box(root.transform, "Base2", new Vector3(0f, 0.3f, 0f), new Vector3(0.62f, 0.14f, 0.62f), m.stone, false);
        Cylinder(root.transform, "Pillar", new Vector3(0f, 0.8f, 0f), new Vector3(0.3f, 0.45f, 0.3f), m.stone, true);
        Box(root.transform, "Platform", new Vector3(0f, 1.32f, 0f), new Vector3(0.72f, 0.16f, 0.72f), m.stone, false);
        Box(root.transform, "Firebox", new Vector3(0f, 1.62f, 0f), new Vector3(0.48f, 0.44f, 0.48f), m.stone, false);
        Box(root.transform, "Window_Z", new Vector3(0f, 1.62f, 0f), new Vector3(0.28f, 0.26f, 0.5f), m.paper, false);
        Box(root.transform, "Window_X", new Vector3(0f, 1.62f, 0f), new Vector3(0.5f, 0.26f, 0.28f), m.paper, false);
        Box(root.transform, "CapBase", new Vector3(0f, 1.88f, 0f), new Vector3(0.98f, 0.07f, 0.98f), m.stone, false);
        var cap = new GameObject("Cap");
        cap.transform.SetParent(root.transform, false);
        cap.transform.localPosition = new Vector3(0f, 1.915f, 0f);
        cap.AddComponent<MeshFilter>().sharedMesh = PyramidMesh("Lantern_Cap", 0.5f, 0.3f);
        cap.AddComponent<MeshRenderer>().sharedMaterial = m.stone;
        Primitive(PrimitiveType.Sphere, root.transform, "Finial", new Vector3(0f, 2.28f, 0f), Vector3.one * 0.17f, m.stone, false);

        var light = new GameObject("Light").AddComponent<Light>();
        light.transform.SetParent(root.transform, false);
        light.transform.localPosition = new Vector3(0f, 1.62f, 0f);
        light.type = LightType.Point;
        light.color = new Color(1f, 0.66f, 0.36f);
        light.range = 6f;
        light.intensity = 1.6f;
        light.shadows = LightShadows.None;
        return SavePrefab(root);
    }

    // 작은 민가 (7 x 5m): 돌 기초 · 마루 · 기둥 · 회벽 + 아래 판자 · 창호지 문과 창(밤에 빛남) · 맞배지붕. 앞(+Z)이 문
    static GameObject BuildHouse(Mats m)
    {
        const float L = 7f, D = 5f, H = 2.6f, floorY = 0.48f;
        var root = new GameObject("Forest_House");
        Transform t = root.transform;
        Box(t, "Foundation", new Vector3(0f, 0f, 0f), new Vector3(L + 0.4f, 0.96f, D + 0.4f), m.stone, true);
        Box(t, "Floor", new Vector3(0f, floorY, 0f), new Vector3(L, 0.1f, D), m.dark, false);
        Box(t, "Step", new Vector3(0f, 0.2f, D * 0.5f + 0.55f), new Vector3(1.8f, 0.4f, 0.7f), m.stone, true);

        float top = floorY + H;
        foreach (float x in new[] { -L * 0.5f, 0f, L * 0.5f })
        foreach (float z in new[] { -D * 0.5f, D * 0.5f })
            Box(t, "Post", new Vector3(x, floorY + H * 0.5f, z), new Vector3(0.22f, H, 0.22f), m.dark, false);
        foreach (float z in new[] { -D * 0.5f, D * 0.5f })
            Box(t, "Beam", new Vector3(0f, top, z), new Vector3(L + 0.3f, 0.24f, 0.26f), m.dark, false);
        foreach (float x in new[] { -L * 0.5f, L * 0.5f })
            Box(t, "Beam", new Vector3(x, top, 0f), new Vector3(0.26f, 0.24f, D + 0.3f), m.dark, false);

        float wallY = floorY + H * 0.5f;
        Wall(t, m, "Wall_Back", new Vector3(0f, wallY, -D * 0.5f), new Vector3(L, H, 0.14f), true);
        Wall(t, m, "Wall_L", new Vector3(-L * 0.5f, wallY, 0f), new Vector3(0.14f, H, D), false);
        Wall(t, m, "Wall_R", new Vector3(L * 0.5f, wallY, 0f), new Vector3(0.14f, H, D), false);
        float side = (L - 2f) * 0.5f;
        Wall(t, m, "Wall_FrontL", new Vector3(-(1f + side * 0.5f), wallY, D * 0.5f), new Vector3(side, H, 0.14f), true);
        Wall(t, m, "Wall_FrontR", new Vector3(1f + side * 0.5f, wallY, D * 0.5f), new Vector3(side, H, 0.14f), true);

        // 문 (창호지 미닫이 2짝) + 문틀
        Box(t, "Door", new Vector3(0f, floorY + 1.05f, D * 0.5f), new Vector3(2f, 2.1f, 0.06f), m.paper, false);
        Box(t, "DoorFrame_Top", new Vector3(0f, floorY + 2.15f, D * 0.5f + 0.04f), new Vector3(2.1f, 0.1f, 0.1f), m.dark, false);
        Box(t, "DoorFrame_Mid", new Vector3(0f, floorY + 1.05f, D * 0.5f + 0.05f), new Vector3(0.06f, 2.1f, 0.06f), m.dark, false);
        for (int i = 1; i <= 4; i++)
            Box(t, "DoorBar", new Vector3(0f, floorY + i * 0.42f, D * 0.5f + 0.05f), new Vector3(2f, 0.03f, 0.03f), m.dark, false);

        // 창: 옆벽 · 앞벽에 격자 창
        float winY = floorY + 1.55f;
        Window(t, m, new Vector3(-(1f + side * 0.5f), winY, D * 0.5f), true);
        Window(t, m, new Vector3(1f + side * 0.5f, winY, D * 0.5f), true);
        Window(t, m, new Vector3(-L * 0.5f, winY, 0f), false);
        Window(t, m, new Vector3(L * 0.5f, winY, 0f), false);

        // 맞배지붕 + 박공 회벽 + 용마루
        var roof = new GameObject("Roof");
        roof.transform.SetParent(t, false);
        roof.AddComponent<MeshFilter>().sharedMesh = GableRoofMesh(L * 0.5f + 0.8f, D * 0.5f + 0.9f, top + 0.1f, top + 1.7f);
        roof.AddComponent<MeshRenderer>().sharedMaterial = m.roof;
        var gable = new GameObject("Gable");
        gable.transform.SetParent(t, false);
        float edgeY = top + 0.1f + (1f - (D * 0.5f) / (D * 0.5f + 0.9f)) * 1.6f; // 벽 위치에서 지붕 밑면 높이
        gable.AddComponent<MeshFilter>().sharedMesh = GableEndMesh(L * 0.5f, D * 0.5f, top, edgeY, top + 1.66f);
        gable.AddComponent<MeshRenderer>().sharedMaterial = m.plaster;
        Box(t, "Ridge", new Vector3(0f, top + 1.75f, 0f), new Vector3(L + 1.8f, 0.2f, 0.32f), m.dark, false);

        // 문 앞을 비추는 등 (그림자 없음)
        var light = new GameObject("Light").AddComponent<Light>();
        light.transform.SetParent(t, false);
        light.transform.localPosition = new Vector3(0f, floorY + 1.8f, D * 0.5f + 1.2f);
        light.type = LightType.Point;
        light.color = new Color(1f, 0.7f, 0.42f);
        light.range = 7f;
        light.intensity = 1.3f;
        light.shadows = LightShadows.None;
        return SavePrefab(root);
    }

    // 벽: 위 회벽 · 아래 판자 (0.9m)
    static void Wall(Transform t, Mats m, string name, Vector3 center, Vector3 size, bool alongX)
    {
        Box(t, name, center, size, m.plaster, true);
        Vector3 low = new Vector3(alongX ? size.x : size.x + 0.06f, 0.9f, alongX ? size.z + 0.06f : size.z);
        Box(t, name + "_Board", new Vector3(center.x, center.y - size.y * 0.5f + 0.45f, center.z), low, m.wood, false);
    }

    static void Window(Transform t, Mats m, Vector3 center, bool alongX)
    {
        Vector3 size = alongX ? new Vector3(1.1f, 0.65f, 0.16f) : new Vector3(0.16f, 0.65f, 1.1f);
        Box(t, "Window", center, size, m.paper, false);
        for (int i = -1; i <= 1; i++)
        {
            Vector3 offset = alongX ? new Vector3(i * 0.32f, 0f, 0.06f * Mathf.Sign(center.z)) : new Vector3(0.06f * Mathf.Sign(center.x), 0f, i * 0.32f);
            Vector3 bar = alongX ? new Vector3(0.04f, 0.68f, 0.06f) : new Vector3(0.06f, 0.68f, 0.04f);
            Box(t, "WindowBar", center + offset, bar, m.dark, false);
        }
    }

    static GameObject BuildCrate(Mats m)
    {
        var root = new GameObject("Forest_Crate");
        Box(root.transform, "Body", new Vector3(0f, 0.4f, 0f), new Vector3(0.8f, 0.8f, 0.8f), m.wood, true);
        foreach (float y in new[] { 0.06f, 0.74f })
            Box(root.transform, "Band", new Vector3(0f, y, 0f), new Vector3(0.84f, 0.1f, 0.84f), m.dark, false);
        return SavePrefab(root);
    }

    static GameObject BuildBarrel(Mats m)
    {
        var root = new GameObject("Forest_Barrel");
        Cylinder(root.transform, "Body", new Vector3(0f, 0.45f, 0f), new Vector3(0.7f, 0.45f, 0.7f), m.wood, true);
        foreach (float y in new[] { 0.15f, 0.75f })
            Cylinder(root.transform, "Hoop", new Vector3(0f, y, 0f), new Vector3(0.74f, 0.03f, 0.74f), m.dark, false);
        Cylinder(root.transform, "Lid", new Vector3(0f, 0.905f, 0f), new Vector3(0.66f, 0.01f, 0.66f), m.dark, false);
        return SavePrefab(root);
    }

    // [변경] 허수아비 (짚 샌드백, 약 2.2m): 박힌 말뚝 · 가로대 팔 · 새끼줄로 묶은 짚 몸통과 팔 · 짚 머리 · 삿갓 · 아래로 삐져나온 짚
    //  보이는 부분은 전부 Pivot(밑동) 아래 → StrawDummy가 맞은 방향으로 기울였다 되돌림
    static GameObject BuildScarecrow(Mats m)
    {
        Material straw = m.straw;
        Material hat = Lit("Straw_Hat", new Color(0.5f, 0.42f, 0.26f), 0.05f);
        Material rope = Lit("Rope_Dark", new Color(0.24f, 0.19f, 0.12f), 0.05f);
        var root = new GameObject("Forest_Scarecrow");
        var pivot = new GameObject("Pivot").transform;
        pivot.SetParent(root.transform, false);

        Cylinder(pivot, "Post", new Vector3(0f, 1.05f, 0f), new Vector3(0.12f, 1.1f, 0.12f), m.wood, false);
        var bar = Cylinder(pivot, "Crossbar", new Vector3(0f, 1.5f, 0f), new Vector3(0.09f, 0.75f, 0.09f), m.wood, false);
        bar.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

        // 몸통: 위가 조금 넓은 짚 다발 두 겹 + 새끼줄 세 줄
        Cylinder(pivot, "Body", new Vector3(0f, 1.2f, 0f), new Vector3(0.5f, 0.38f, 0.42f), straw, false);
        Cylinder(pivot, "Chest", new Vector3(0f, 1.42f, 0f), new Vector3(0.56f, 0.12f, 0.46f), straw, false);
        foreach (float y in new[] { 0.95f, 1.18f, 1.4f })
            Cylinder(pivot, "Rope", new Vector3(0f, y, 0f), new Vector3(0.53f, 0.02f, 0.45f), rope, false);
        // 아래로 삐져나온 짚 가닥
        for (int i = 0; i < 10; i++)
        {
            float a = i * 36f;
            var tuft = Box(pivot, "Straw", Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0.74f, 0.17f), new Vector3(0.05f, 0.28f, 0.015f), straw, false);
            tuft.transform.localRotation = Quaternion.Euler(0f, a, 0f) * Quaternion.Euler(-14f, 0f, 0f);
        }

        // 팔: 가로대 끝을 감싼 짚 다발 + 손목 끈 + 삐져나온 짚
        foreach (float side in new[] { -1f, 1f })
        {
            var arm = Cylinder(pivot, "Arm", new Vector3(side * 0.52f, 1.5f, 0f), new Vector3(0.17f, 0.2f, 0.17f), straw, false);
            arm.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            var cuff = Cylinder(pivot, "Rope", new Vector3(side * 0.66f, 1.5f, 0f), new Vector3(0.18f, 0.015f, 0.18f), rope, false);
            cuff.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            for (int i = 0; i < 4; i++)
            {
                var tuft = Box(pivot, "Straw", new Vector3(side * 0.78f, 1.5f + (i - 1.5f) * 0.035f, (i % 2 - 0.5f) * 0.05f), new Vector3(0.14f, 0.012f, 0.03f), straw, false);
                tuft.transform.localRotation = Quaternion.Euler(0f, 0f, side * (i - 1.5f) * 10f);
            }
        }

        // 머리 + 목 끈 + 삿갓
        Primitive(PrimitiveType.Sphere, pivot, "Head", new Vector3(0f, 1.84f, 0f), new Vector3(0.36f, 0.4f, 0.34f), straw, false);
        Cylinder(pivot, "Rope", new Vector3(0f, 1.66f, 0f), new Vector3(0.2f, 0.02f, 0.2f), rope, false);
        var cap = new GameObject("Hat");
        cap.transform.SetParent(pivot, false);
        cap.transform.localPosition = new Vector3(0f, 1.95f, 0f);
        cap.transform.localRotation = Quaternion.Euler(-6f, 0f, 4f);
        cap.AddComponent<MeshFilter>().sharedMesh = ConeMesh("Scarecrow_Hat", 0.46f, 0.24f);
        cap.AddComponent<MeshRenderer>().sharedMaterial = hat;

        var body = root.AddComponent<CapsuleCollider>();
        body.center = new Vector3(0f, 1.1f, 0f);
        body.radius = 0.32f;
        body.height = 2.2f;
        var dummy = root.AddComponent<StrawDummy>();
        var so = new SerializedObject(dummy);
        so.FindProperty("pivot").objectReferenceValue = pivot;
        so.ApplyModifiedPropertiesWithoutUndo();
        return SavePrefab(root);
    }

    // 원뿔 (삿갓): 밑면 반지름 radius · 높이 height, 아래에서도 보이게 양면
    static Mesh ConeMesh(string name, float radius, float height)
    {
        const int seg = 24;
        var faces = new List<Vector3[]>();
        Vector3 top = new Vector3(0f, height, 0f);
        for (int i = 0; i < seg; i++)
        {
            float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
            Vector3 p0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius;
            Vector3 p1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius;
            faces.Add(new[] { p1, top, p0 });
        }
        return SaveMesh(name, faces, true);
    }

    // 장작더미: 통나무 3 · 2 · 1단 + 양옆 말뚝
    static GameObject BuildWoodPile(Mats m)
    {
        var root = new GameObject("Forest_WoodPile");
        const float r = 0.13f;
        for (int row = 0; row < 3; row++)
        for (int i = 0; i < 3 - row; i++)
        {
            float x = (i - (2 - row) * 0.5f) * r * 2.05f;
            var log = Cylinder(root.transform, "Log", new Vector3(x, r + row * r * 1.75f, 0f), new Vector3(r * 2f, 0.7f, r * 2f), m.wood, false);
            log.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }
        foreach (float x in new[] { -0.55f, 0.55f })
            Box(root.transform, "Stake", new Vector3(x, 0.35f, 0f), new Vector3(0.07f, 0.7f, 0.07f), m.dark, false);
        root.AddComponent<BoxCollider>().size = new Vector3(1.2f, 0.75f, 1.4f);
        root.GetComponent<BoxCollider>().center = new Vector3(0f, 0.375f, 0f);
        return SavePrefab(root);
    }

    // 흑뢰의 제단: 3단 돌 기단 · 검은 비석(푸른 번개 문양이 빛남) · 금줄(새끼줄 · 종이 술) · 양옆 석등 자리
    static GameObject BuildAltar(Mats m)
    {
        var root = new GameObject("Boss_Altar");
        Transform t = root.transform;
        Box(t, "Tier1", new Vector3(0f, 0f, 0f), new Vector3(7f, 1f, 5f), m.stone, true);
        Box(t, "Tier2", new Vector3(0f, 0.65f, -0.3f), new Vector3(5f, 0.5f, 3.6f), m.stone, true);
        Box(t, "Tier3", new Vector3(0f, 1.05f, -0.5f), new Vector3(3.2f, 0.4f, 2.4f), m.stone, true);
        Box(t, "Stairs1", new Vector3(0f, 0.65f, 1.85f), new Vector3(1.8f, 0.3f, 0.6f), m.stone, false);
        var stele = Box(t, "Stele", new Vector3(0f, 3.05f, -0.6f), new Vector3(1.3f, 3.6f, 0.55f), m.charred, true);
        stele.transform.localRotation = Quaternion.Euler(0f, 0f, 2.5f);
        // 비석 앞면 번개 문양 (지그재그 빛 줄)
        Vector3[] bolt = { new Vector3(0.1f, 4.4f), new Vector3(-0.25f, 3.7f), new Vector3(0.15f, 3.25f), new Vector3(-0.2f, 2.5f), new Vector3(0.12f, 1.9f) };
        for (int i = 0; i < bolt.Length - 1; i++)
        {
            Vector3 a = bolt[i], b = bolt[i + 1];
            Vector3 mid = (a + b) * 0.5f;
            var seg = Box(t, "Rune", new Vector3(mid.x, mid.y, -0.31f), new Vector3(0.07f, Vector3.Distance(a, b) + 0.05f, 0.02f), m.rune, false);
            seg.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg - 90f);
        }
        // 금줄: 비석 허리에 두르는 새끼줄 + 흰 종이 술 (지그재그)
        var rope = Cylinder(t, "Shimenawa", new Vector3(0f, 2.55f, -0.6f), new Vector3(0.16f, 0.78f, 0.16f), m.straw, false);
        rope.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        Material white = Lit("Shide_Paper", new Color(0.92f, 0.9f, 0.85f), 0.1f);
        foreach (float x in new[] { -0.45f, 0f, 0.45f })
        for (int k = 0; k < 3; k++)
        {
            var paper = Box(t, "Shide", new Vector3(x + (k % 2 == 0 ? 0f : 0.05f), 2.38f - k * 0.16f, -0.9f), new Vector3(0.1f, 0.15f, 0.01f), white, false);
            paper.transform.localRotation = Quaternion.Euler(0f, 0f, k % 2 == 0 ? 12f : -12f);
        }
        // 제단 앞 향로 · 그릇
        Cylinder(t, "Censer", new Vector3(0f, 1.42f, 0.3f), new Vector3(0.6f, 0.17f, 0.6f), m.dark, false);

        var light = new GameObject("RuneLight").AddComponent<Light>();
        light.transform.SetParent(t, false);
        light.transform.localPosition = new Vector3(0f, 3.2f, 0.6f);
        light.type = LightType.Point;
        light.color = new Color(0.55f, 0.62f, 1f);
        light.range = 8f;
        light.intensity = 2.5f;
        light.shadows = LightShadows.None;
        return SavePrefab(root);
    }

    // 무너진 도리이: 기둥 하나는 서 있고 하나는 부러져 누움, 갓(가사기)은 땅에 비스듬히
    static GameObject BuildBrokenTorii(Mats m)
    {
        var root = new GameObject("Broken_Torii");
        Transform t = root.transform;
        Cylinder(t, "Pillar_Standing", new Vector3(-2.2f, 2.6f, 0f), new Vector3(0.5f, 2.6f, 0.5f), m.red, true);
        Box(t, "Base_L", new Vector3(-2.2f, 0.2f, 0f), new Vector3(0.9f, 0.5f, 0.9f), m.stone, true);
        Box(t, "Base_R", new Vector3(2.2f, 0.2f, 0f), new Vector3(0.9f, 0.5f, 0.9f), m.stone, true);
        Cylinder(t, "Pillar_Stump", new Vector3(2.2f, 0.85f, 0f), new Vector3(0.5f, 0.8f, 0.5f), m.red, true);
        var fallen = Cylinder(t, "Pillar_Fallen", new Vector3(3.6f, 0.3f, 1.6f), new Vector3(0.48f, 1.6f, 0.48f), m.red, true);
        fallen.transform.localRotation = Quaternion.Euler(84f, 35f, 0f);
        var kasagi = Box(t, "Kasagi_Fallen", new Vector3(0.6f, 0.45f, 2.4f), new Vector3(7f, 0.45f, 0.6f), m.dark, true);
        kasagi.transform.localRotation = Quaternion.Euler(4f, -12f, 9f);
        var nuki = Box(t, "Nuki_Hanging", new Vector3(-1.1f, 3.2f, 0.1f), new Vector3(3f, 0.32f, 0.26f), m.red, false);
        nuki.transform.localRotation = Quaternion.Euler(0f, 0f, -28f);
        return SavePrefab(root);
    }

    static GameObject BuildEmbers()
    {
        var root = new GameObject("Boss_Embers");
        var ps = root.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.prewarm = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.42f, 0.12f), new Color(1f, 0.75f, 0.35f));
        main.gravityModifier = -0.02f;
        main.maxParticles = 400;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        ParticleSystem.EmissionModule em = ps.emission;
        em.rateOverTime = 28f;
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(48f, 2f, 48f);
        ParticleSystem.NoiseModule noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.6f;
        noise.frequency = 0.25f;
        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.5f, 0.3f), 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var pr = root.GetComponent<ParticleSystemRenderer>();
        pr.sharedMaterial = EmberMaterial();
        pr.shadowCastingMode = ShadowCastingMode.Off;
        return SavePrefab(root);
    }

    static Material EmberMaterial()
    {
        string path = $"{StructureFolder}/Ember.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;
        mat = new Material(Shader.Find("Sprites/Default")) { name = "Ember" };
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    // ===== 마을 =====
    // 광장(반지름 30) 기준: 북쪽 · 서쪽 포탈 길과 화톳불 · 탁자 · 정자 2채 · 연못(동쪽)은 비우고 바깥 띠에 민가 7채
    [MenuItem("Tools/Map/Dressing/Village")]
    static void DressVillage()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        string returnTo = EditorSceneManager.GetActiveScene().path;
        var scene = EditorSceneManager.OpenScene(VillageScene, OpenSceneMode.Single);

        var theme = AssetDatabase.LoadAssetAtPath<ZoneTheme>("Assets/11.Data/Zone/Village_Square.asset");
        if (theme == null) { Debug.LogWarning("[Dressing] Village_Square 테마가 없습니다."); return; }
        Mats m = LoadMats();
        GameObject lantern = BuildStoneLantern(m), house = BuildHouse(m), crate = BuildCrate(m), barrel = BuildBarrel(m), pile = BuildWoodPile(m);
        GameObject bench = AssetDatabase.LoadAssetAtPath<GameObject>($"{StructureFolder}/Forest_Bench.prefab");
        GameObject[] trees = Pack("Beech Trees/Prefabs/prefab_beech_tree_{0}.prefab", "06", "07", "08");
        GameObject[] roads = Pack("Stumps Roots and Branches/Prefabs Moss Cover/prefab_stone_road_0{0}_Moss.prefab", "1", "2", "3", "4", "5", "6");
        GameObject[] grass = Pack("Foliage and Grass/Prefabs/prefab_{0}.prefab", "grass_01_1", "grass_02_1", "plant_01_1", "fern_01_2", "shaggy_soldier_01_1");

        Transform root = NewDressing(null);
        var rng = new System.Random(4711);
        Vector3 c = theme.origin;
        int count = 0;

        // 민가: 바깥 띠(반지름 24~25)에 광장을 향해. 옆에 상자 · 통 · 장작 · 긴 의자
        var houses = new (float angle, float r)[] { (240f, 24f), (205f, 24.5f), (150f, 25f), (115f, 24f), (65f, 24f), (300f, 24f), (330f, 24f) };
        GameObject[] clutter = { crate, barrel, pile, crate, barrel };
        foreach (var spot in houses)
        {
            Vector3 dir = Dir(spot.angle);
            Vector3 pos = c + dir * spot.r;
            Place(house, root, pos, -dir, 0f, 0.05f);
            Vector3 right = Vector3.Cross(Vector3.up, -dir);
            // 집 옆 (문 쪽에서 보아 왼쪽 또는 오른쪽) 소품 2~3개
            float s = rng.NextDouble() < 0.5 ? -1f : 1f;
            int n = 2 + rng.Next(2);
            for (int i = 0; i < n; i++)
            {
                Vector3 p = pos + right * s * (4.4f + i * 0.9f) + (-dir) * (1.2f - i * 1.1f);
                Place(clutter[rng.Next(clutter.Length)], root, p, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f) * -dir, 0f);
                count++;
            }
            if (bench != null && rng.NextDouble() < 0.6)
                Place(bench, root, pos + (-dir) * 3.6f + right * -s * 2.4f, right, 0f);
            count++;
        }

        // 석등: 북쪽 · 서쪽 포탈 길 양옆 + 연못 옆
        foreach (float z in new[] { 10f, 21f })
            foreach (float x in new[] { -4.2f, 4.2f })
                { Place(lantern, root, c + new Vector3(x, 0f, z), Vector3.left * Mathf.Sign(x), 0f); count++; }
        foreach (float x in new[] { -11f, -22f })
            foreach (float z in new[] { -4.2f, 4.2f })
                { Place(lantern, root, c + new Vector3(x, 0f, z), Vector3.back * Mathf.Sign(z), 0f); count++; }
        if (theme.ponds != null)
            foreach (ZoneTheme.Pond p in theme.ponds)
            {
                Vector3 pc = c + new Vector3(p.position.x, 0f, p.position.y);
                Vector3 toCenter = (c - pc).normalized;
                foreach (float a in new[] { -35f, 35f })
                {
                    Vector3 side = Quaternion.Euler(0f, a, 0f) * toCenter;
                    Place(lantern, root, pc + side * (p.radius * 1.25f + 1.2f), -side, 0f);
                    count++;
                }
            }

        // 돌길: 광장 가운데에서 북쪽 · 서쪽 포탈, 남쪽 시작 자리, 화톳불 쪽
        if (roads.Length > 0)
        {
            Road(theme, roads, root, rng, c + new Vector3(0f, 0f, 3f), c + new Vector3(0f, 0f, 29f));
            Road(theme, roads, root, rng, c + new Vector3(-3f, 0f, 0f), c + new Vector3(-29f, 0f, 0f));
            Road(theme, roads, root, rng, c + new Vector3(0f, 0f, -3f), c + new Vector3(0f, 0f, -16f));
            Road(theme, roads, root, rng, c + new Vector3(2.5f, 0f, -2.5f), c + new Vector3(9.5f, 0f, -7.5f));
        }

        // [변경] 허수아비(짚 샌드백) 3개: 서쪽 길과 북서 정자 사이 빈터, 광장 쪽을 봄 + 발밑 짚 더미 · 풀
        GameObject scarecrow = BuildScarecrow(m);
        foreach (Vector3 spot in new[] { new Vector3(-11.5f, 0f, 8f), new Vector3(-14.5f, 0f, 7.4f), new Vector3(-17.5f, 0f, 8.1f) })
        {
            Vector3 face = (c - (c + spot)).normalized;
            Place(scarecrow, root, c + spot, Quaternion.Euler(0f, (float)rng.NextDouble() * 30f - 15f, 0f) * face, 0f, 0.05f, true);
            for (int i = 0; i < 4 && grass.Length > 0; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                Vector3 p = c + spot + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (0.7f + (float)rng.NextDouble() * 1.2f);
                Place(grass[rng.Next(grass.Length)], root, p, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f) * Vector3.forward, 0f, 0f, false);
            }
            count++;
        }

        // 광장 나무 3그루 + 둘레 풀 무리
        var treeSpots = new[] { new Vector3(7f, 0f, 9f), new Vector3(-8f, 0f, -7f), new Vector3(9f, 0f, -15f) };
        foreach (Vector3 spot in treeSpots)
        {
            if (trees.Length == 0) break;
            Place(trees[rng.Next(trees.Length)], root, c + spot, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f) * Vector3.forward, 15f, 0.2f, true, byHeight: true);
            for (int i = 0; i < 6 && grass.Length > 0; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                Vector3 p = c + spot + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (1.2f + (float)rng.NextDouble() * 2.2f);
                Place(grass[rng.Next(grass.Length)], root, p, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f) * Vector3.forward, 0f, 0f, false);
            }
            count++;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        if (!string.IsNullOrEmpty(returnTo) && returnTo != scene.path) EditorSceneManager.OpenScene(returnTo, OpenSceneMode.Single);
        Debug.Log($"[Dressing] 마을 꾸미기 · 민가 {houses.Length} · 소품 {count} → 저장 완료");
    }

    // 돌길 조각을 from → to 사이에 2.4m 간격으로 (연못 자리는 건너뜀)
    static void Road(ZoneTheme theme, GameObject[] roads, Transform root, System.Random rng, Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        float len = d.magnitude;
        Vector3 n = d / len, side = Vector3.Cross(Vector3.up, n);
        for (float k = 0f; k < len; k += 2.4f)
        {
            Vector3 p = from + n * k + side * ((float)rng.NextDouble() - 0.5f) * 0.8f;
            if (InPond(theme, p - theme.origin, 0.5f)) continue;
            Place(roads[rng.Next(roads.Length)], root, p, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f) * Vector3.forward, 2.6f, 0.08f, true); // 통과하지 않게 충돌 유지
        }
    }

    // ===== 보스 구역 (흑뢰의 제단) =====
    // 공터(반지름 20)는 비워 둠. 가장자리 석등 고리(몇 개는 쓰러짐) · 벽 너머 동쪽 제단 · 서쪽 무너진 도리이 · 큰 바위 · 불티
    [MenuItem("Tools/Map/Dressing/Boss Zone (InGame)")]
    static void DressBoss()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!EditorSceneManager.GetActiveScene().path.EndsWith("InGame.unity"))
        {
            Debug.LogWarning("[Dressing] InGame 씬을 열고 실행하세요.");
            return;
        }
        var theme = AssetDatabase.LoadAssetAtPath<ZoneTheme>("Assets/11.Data/Zone/Zone4_Boss.asset");
        GameObject zone = theme != null ? GameObject.Find(theme.zoneName) : null;
        if (zone == null) { Debug.LogWarning("[Dressing] 보스 구역(Zone_4_Boss)을 찾지 못했습니다."); return; }

        Mats m = LoadMats();
        GameObject lantern = BuildStoneLantern(m), altar = BuildAltar(m), torii = BuildBrokenTorii(m), embers = BuildEmbers();
        GameObject[] rocks = theme.wallRocks != null ? theme.wallRocks.Where(r => r != null).ToArray() : new GameObject[0];
        GameObject[] logs = Pack("Stumps Roots and Branches/Prefabs Moss Cover/prefab_{0}.prefab", "dead_log_01_Moss", "dead_log_03_Moss", "beech_forest_stump_01_1_Moss");

        Transform root = NewDressing(zone.transform);
        var rng = new System.Random(335);
        Vector3 c = theme.origin;
        int count = 0;

        // 석등 고리: 24°마다, 남북 문 앞은 비움. 4개 중 하나는 쓰러져 불이 꺼짐
        float ringR = theme.combatRadius + 1.4f;
        int index = 0;
        for (float a = 0f; a < 360f; a += 24f)
        {
            if (Mathf.Abs(Mathf.DeltaAngle(a, 90f)) < 24f || Mathf.Abs(Mathf.DeltaAngle(a, 270f)) < 24f) continue;
            Vector3 dir = Dir(a);
            Transform l = Place(lantern, root, c + dir * ringR, -dir, 0f);
            if (index++ % 4 == 2) // 쓰러진 석등
            {
                l.rotation = Quaternion.LookRotation(-dir, Vector3.up) * Quaternion.Euler(-78f, (float)rng.NextDouble() * 40f - 20f, 0f); // 바깥으로 쓰러짐
                l.position += Vector3.up * 0.25f + dir * 0.6f;
                foreach (Light light in l.GetComponentsInChildren<Light>()) light.enabled = false;
                foreach (Renderer r in l.GetComponentsInChildren<Renderer>())
                    if (r.sharedMaterial == m.paper) r.sharedMaterial = m.stone;
            }
            count++;
        }

        // 벽 너머 동쪽 제단(공터를 봄) · 서쪽 무너진 도리이
        Place(altar, root, c + Dir(0f) * (theme.wallRadius + 6f), -Dir(0f), 0f, 0.4f, true);
        Place(torii, root, c + Dir(180f) * (theme.wallRadius + 5f), -Dir(180f), 0f, 0.1f, true);
        count += 2;

        // 큰 바위: 벽 너머 사이사이, 높이 들쭉날쭉
        foreach (float a in new[] { 25f, 70f, 115f, 160f, 205f, 250f, 290f, 335f })
        {
            if (rocks.Length == 0) break;
            float r = theme.wallRadius + 7f + (float)rng.NextDouble() * 5f;
            Transform rock = Place(rocks[rng.Next(rocks.Length)], root, c + Dir(a + (float)rng.NextDouble() * 10f - 5f) * r,
                Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f) * Vector3.forward, 7f + (float)rng.NextDouble() * 5f, 1f, true);
            rock.rotation *= Quaternion.Euler((float)rng.NextDouble() * 16f - 8f, 0f, (float)rng.NextDouble() * 16f - 8f);
            count++;
        }

        // 공터 가장자리 쓰러진 통나무 · 그루터기 (전투 공간 밖)
        for (int i = 0; i < 6 && logs.Length > 0; i++)
        {
            float a = (float)rng.NextDouble() * 360f;
            if (Mathf.Abs(Mathf.DeltaAngle(a, 90f)) < 20f || Mathf.Abs(Mathf.DeltaAngle(a, 270f)) < 20f) continue;
            Vector3 p = c + Dir(a) * (theme.combatRadius + 2.2f + (float)rng.NextDouble() * 0.6f);
            Place(logs[rng.Next(logs.Length)], root, p, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f) * Vector3.forward, 2.6f, 0.15f, false);
            count++;
        }

        var ember = (GameObject)PrefabUtility.InstantiatePrefab(embers, root);
        ember.transform.position = c + Vector3.up * 0.5f;

        EditorSceneManager.MarkSceneDirty(zone.scene);
        Debug.Log($"[Dressing] 보스 구역 꾸미기 · 석등 · 제단 · 도리이 · 바위 등 {count}개 + 불티 → 씬을 저장하세요");
    }

    // ===== 화톳불 (篝火): 돌 받침 + 쇠 삼발이 · 쇠 바구니 + 장작 · 숯불 · 불꽃 =====
    // 기본 도형 돌 등롱 → 숲 구조물과 같은 재질의 화톳불. 프리팹을 제자리에서 고쳐 씬마다 옮긴 위치 · 이름은 그대로
    const string ShrinePrefab = "Assets/2.Model/Prefabs/Shrine.prefab";
    const string ShrineMatFolder = "Assets/6.Materials/Shrine";

    [MenuItem("Tools/Map/Dressing/Shrine Look (Bonfire)")]
    public static void RebuildShrineLook()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ShrinePrefab) == null) { Debug.LogWarning($"[Dressing] {ShrinePrefab} 없음"); return; }
        Mats m = LoadMats();
        Material iron = Lit("Iron_Dark", new Color(0.07f, 0.065f, 0.06f), 0.35f);
        iron.SetFloat("_Metallic", 0.75f);
        Material ember = Glow("Fire_Embers", new Color(1f, 0.38f, 0.1f), 4f);

        GameObject root = PrefabUtility.LoadPrefabContents(ShrinePrefab);
        foreach (string old in new[] { "Base", "Pillar", "Fire", "Roof", "FireLight", "Visual" })
        {
            Transform t = root.transform.Find(old);
            if (t != null) Object.DestroyImmediate(t.gameObject);
        }

        var visual = new GameObject("Visual").transform;
        visual.SetParent(root.transform, false);

        // 돌 받침 + 둘레 작은 바위
        Cylinder(visual, "Base", new Vector3(0f, 0.06f, 0f), new Vector3(1.5f, 0.06f, 1.5f), m.stone, true);
        GameObject[] stones = Pack("Rocks/Prefabs/prefab_beech_forest_stones_01_{0}.prefab", "2", "4", "6", "8");
        var rng = new System.Random(19);
        for (int i = 0; i < 8 && stones.Length > 0; i++)
        {
            float a = (i + (float)rng.NextDouble() * 0.4f) / 8f * Mathf.PI * 2f;
            var rock = (GameObject)PrefabUtility.InstantiatePrefab(stones[rng.Next(stones.Length)], visual);
            Bounds b = LocalBounds(rock);
            float size = 0.32f + (float)rng.NextDouble() * 0.18f;
            rock.transform.localScale = Vector3.one * (size / Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.y, b.size.z)));
            rock.transform.localPosition = new Vector3(Mathf.Cos(a) * 0.78f, 0.02f, Mathf.Sin(a) * 0.78f);
            rock.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            foreach (Collider c in rock.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        }

        // 쇠 삼발이: 바닥 반지름 0.5 → 위 0.22에서 바구니를 받침
        for (int i = 0; i < 3; i++)
        {
            float a = i * Mathf.PI * 2f / 3f + 0.5f;
            Vector3 bottom = new Vector3(Mathf.Cos(a) * 0.52f, 0.1f, Mathf.Sin(a) * 0.52f);
            Vector3 top = new Vector3(Mathf.Cos(a + Mathf.PI) * 0.12f, 1.22f, Mathf.Sin(a + Mathf.PI) * 0.12f); // 서로 엇갈려 교차
            Rod(visual, "Leg", bottom, top, 0.045f, iron);
        }

        // 쇠 바구니: 아래 · 위 테 + 살 10개 (위로 벌어짐)
        Ring(visual, "Basket_Low", 1.2f, 0.26f, 0.02f, iron);
        Ring(visual, "Basket_High", 1.6f, 0.4f, 0.025f, iron);
        for (int i = 0; i < 10; i++)
        {
            float a = i * Mathf.PI * 2f / 10f;
            Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Rod(visual, "Bar", d * 0.26f + Vector3.up * 1.2f, d * 0.4f + Vector3.up * 1.6f, 0.018f, iron);
        }
        Cylinder(visual, "Basket_Floor", new Vector3(0f, 1.21f, 0f), new Vector3(0.5f, 0.01f, 0.5f), iron, false);

        // 숯불 + 엇갈려 쌓인 장작 (끝은 탄 색)
        Primitive(PrimitiveType.Sphere, visual, "Embers", new Vector3(0f, 1.27f, 0f), new Vector3(0.5f, 0.12f, 0.5f), ember, false);
        for (int i = 0; i < 6; i++)
        {
            float a = i * 60f + (float)rng.NextDouble() * 20f;
            Vector3 c = new Vector3(0f, 1.32f + (i % 3) * 0.06f, 0f);
            Vector3 dir = Quaternion.Euler(0f, a, 0f) * new Vector3(1f, 0.25f, 0f);
            Rod(visual, "Log", c - dir * 0.28f, c + dir * 0.28f, 0.05f, i % 2 == 0 ? m.wood : m.charred);
        }

        // 불꽃 (부드러운 원 질감 · 더하기 합성 → 블룸에 번짐)
        var flame = new GameObject("Flame").AddComponent<ParticleSystem>();
        flame.transform.SetParent(visual, false);
        flame.transform.localPosition = new Vector3(0f, 1.35f, 0f);
        flame.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        ParticleSystem.MainModule main = flame.main;
        main.loop = true;
        main.prewarm = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.85f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.7f, 1.3f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new Color(1f, 0.55f, 0.2f);
        main.useUnscaledTime = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 80;
        ParticleSystem.EmissionModule em = flame.emission;
        em.rateOverTime = 34f;
        ParticleSystem.ShapeModule shape = flame.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 8f;
        shape.radius = 0.2f;
        ParticleSystem.SizeOverLifetimeModule sol = flame.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.8f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.15f)));
        ParticleSystem.ColorOverLifetimeModule col = flame.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.85f, 0.45f), 0f), new GradientColorKey(new Color(1f, 0.45f, 0.12f), 0.45f), new GradientColorKey(new Color(0.6f, 0.12f, 0.04f), 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.12f), new GradientAlphaKey(0.5f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        ParticleSystem.NoiseModule noise = flame.noise;
        noise.enabled = true;
        noise.strength = 0.35f;
        noise.frequency = 1.4f;
        var fr = flame.GetComponent<ParticleSystemRenderer>();
        fr.sharedMaterial = FlameMaterial();
        fr.shadowCastingMode = ShadowCastingMode.Off;

        var lightGo = new GameObject("FireLight");
        lightGo.transform.SetParent(visual, false);
        lightGo.transform.localPosition = new Vector3(0f, 1.8f, 0f);
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.6f, 0.3f);
        light.range = 8f;
        light.intensity = 3.2f;
        light.shadows = LightShadows.None;

        // 통과 막기 (삼발이 · 바구니 둘레)
        var body = new GameObject("Collider").AddComponent<CapsuleCollider>();
        body.transform.SetParent(visual, false);
        body.center = new Vector3(0f, 0.85f, 0f);
        body.radius = 0.5f;
        body.height = 1.7f;

        Component shrine = root.GetComponent("Shrine");
        if (shrine != null)
        {
            var so = new SerializedObject(shrine);
            SerializedProperty fire = so.FindProperty("fireLight");
            if (fire != null) fire.objectReferenceValue = light;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        PrefabUtility.SaveAsPrefabAsset(root, ShrinePrefab);
        PrefabUtility.UnloadPrefabContents(root);
        AssetDatabase.SaveAssets();
        Debug.Log("[Dressing] 화톳불 프리팹을 새 모양으로 교체 (모든 씬의 화톳불에 적용, 위치 · 이름은 그대로)");
    }

    static Material FlameMaterial()
    {
        if (!AssetDatabase.IsValidFolder(ShrineMatFolder)) AssetDatabase.CreateFolder("Assets/6.Materials", "Shrine");
        string path = $"{ShrineMatFolder}/Fire_Flame.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = "Fire_Flame" };
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{ShrineMatFolder}/Fire_Soft.png"));
        mat.SetColor("_BaseColor", new Color(2.4f, 1.5f, 0.9f, 1f)); // HDR → 블룸
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 2f); // 더하기
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.One);
        mat.SetFloat("_ZWrite", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // from → to 사이 원기둥 (반지름 radius)
    static void Rod(Transform parent, string name, Vector3 from, Vector3 to, float radius, Material mat)
    {
        Vector3 d = to - from;
        GameObject go = Cylinder(parent, name, (from + to) * 0.5f, new Vector3(radius * 2f, d.magnitude * 0.5f, radius * 2f), mat, false);
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
    }

    static void Ring(Transform parent, string name, float y, float radius, float tube, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, y, 0f);
        go.AddComponent<MeshFilter>().sharedMesh = TorusMesh($"Torus_{name}", radius, tube);
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
    }

    static Mesh TorusMesh(string name, float radius, float tube)
    {
        string path = $"{StructureFolder}/{name}.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh == null)
        {
            mesh = new Mesh { name = name };
            AssetDatabase.CreateAsset(mesh, path);
        }
        const int seg = 32, side = 8;
        var verts = new List<Vector3>();
        var tris = new List<int>();
        for (int i = 0; i <= seg; i++)
        {
            float a = i * Mathf.PI * 2f / seg;
            Vector3 c = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            for (int j = 0; j <= side; j++)
            {
                float b = j * Mathf.PI * 2f / side;
                verts.Add(c * (radius + Mathf.Cos(b) * tube) + Vector3.up * Mathf.Sin(b) * tube);
            }
        }
        for (int i = 0; i < seg; i++)
        for (int j = 0; j < side; j++)
        {
            int a0 = i * (side + 1) + j, a1 = a0 + 1, b0 = a0 + side + 1, b1 = b0 + 1;
            tris.AddRange(new[] { a0, a1, b1, a0, b1, b0 });
        }
        mesh.Clear();
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    // 회전 · 크기 없는 상태의 렌더러 범위 (프리팹 안에서 크기 맞출 때)
    static Bounds LocalBounds(GameObject go)
    {
        Renderer[] rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(Vector3.zero, Vector3.one);
        Bounds b = rs[0].localBounds;
        foreach (Renderer r in rs) b.Encapsulate(r.localBounds);
        return b;
    }

    // ===== 도우미 =====
    static Vector3 Dir(float degrees) => new Vector3(Mathf.Cos(degrees * Mathf.Deg2Rad), 0f, Mathf.Sin(degrees * Mathf.Deg2Rad));

    static bool InPond(ZoneTheme t, Vector3 local, float margin) =>
        t.ponds != null && t.ponds.Any(p => p.radius > 0f && new Vector2(local.x - p.position.x, local.z - p.position.y).magnitude < p.radius * 1.2f + margin);

    // 같은 이름 묶음을 지우고 새로 만듦 (parent가 없으면 씬 최상위)
    static Transform NewDressing(Transform parent)
    {
        Transform old = parent != null ? parent.Find(DressName) : GameObject.Find(DressName)?.transform;
        if (old != null && (parent != null || old.parent == null)) Undo.DestroyObjectImmediate(old.gameObject);
        var go = new GameObject(DressName);
        Undo.RegisterCreatedObjectUndo(go, "Dressing");
        if (parent != null) go.transform.SetParent(parent, false);
        return go.transform;
    }

    // size > 0이면 가장 긴 변(byHeight면 높이)을 size로. 바닥 높이에 맞추고 sink만큼 묻음
    static Transform Place(GameObject prefab, Transform parent, Vector3 pos, Vector3 forward, float size, float sink = 0.05f, bool colliders = true, bool byHeight = false)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        forward.y = 0f;
        go.transform.rotation = Quaternion.LookRotation(forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward, Vector3.up);
        if (size > 0f)
        {
            Renderer[] rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                go.transform.position = Vector3.zero;
                Bounds b = rs[0].bounds;
                foreach (Renderer r in rs) b.Encapsulate(r.bounds);
                float source = byHeight ? b.size.y : Mathf.Max(b.size.x, b.size.y, b.size.z);
                if (source > 0.001f) go.transform.localScale = Vector3.one * (size / source);
            }
        }
        pos.y = GroundY(pos) - sink;
        go.transform.position = pos;
        if (!colliders)
            foreach (Collider c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        return go.transform;
    }

    static float GroundY(Vector3 pos)
    {
        foreach (Terrain t in Terrain.activeTerrains)
        {
            Vector3 tp = t.transform.position, size = t.terrainData.size;
            if (pos.x >= tp.x && pos.x <= tp.x + size.x && pos.z >= tp.z && pos.z <= tp.z + size.z)
                return t.SampleHeight(pos) + tp.y;
        }
        return pos.y;
    }

    static GameObject[] Pack(string pattern, params string[] ids) =>
        ids.Select(id => AssetDatabase.LoadAssetAtPath<GameObject>(PackRoot + string.Format(pattern, id))).Where(p => p != null).ToArray();

    static GameObject SavePrefab(GameObject root)
    {
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{StructureFolder}/{root.name}.prefab");
        Object.DestroyImmediate(root);
        return prefab;
    }

    // 사각뿔 (석등 갓): 밑변 한 변의 절반 half, 높이 height. 아래에서도 보이게 밑면 포함
    static Mesh PyramidMesh(string name, float half, float height)
    {
        Vector3 a = new Vector3(-half, 0f, -half), b = new Vector3(half, 0f, -half), c = new Vector3(half, 0f, half), d = new Vector3(-half, 0f, half);
        Vector3 top = new Vector3(0f, height, 0f);
        return SaveMesh(name, new List<Vector3[]> { new[] { a, top, b }, new[] { b, top, c }, new[] { c, top, d }, new[] { d, top, a }, new[] { a, b, c, d } }, false);
    }

    // 맞배지붕: X축 방향 용마루, 처마는 앞뒤(±Z). 양면
    static Mesh GableRoofMesh(float halfX, float halfZ, float eaveY, float ridgeY)
    {
        Vector3 fl = new Vector3(-halfX, eaveY, halfZ), fr = new Vector3(halfX, eaveY, halfZ);
        Vector3 bl = new Vector3(-halfX, eaveY, -halfZ), br = new Vector3(halfX, eaveY, -halfZ);
        Vector3 rl = new Vector3(-halfX, ridgeY, 0f), rr = new Vector3(halfX, ridgeY, 0f);
        return SaveMesh("House_Roof", new List<Vector3[]> { new[] { fl, rl, rr, fr }, new[] { br, rr, rl, bl } }, true);
    }

    // 박공 (벽 위 ~ 지붕 밑면까지 오각형 벽, 양 끝)
    static Mesh GableEndMesh(float halfX, float halfZ, float baseY, float edgeY, float peakY)
    {
        var faces = new List<Vector3[]>();
        foreach (float x in new[] { -halfX, halfX })
            faces.Add(new[] { new Vector3(x, baseY, -halfZ), new Vector3(x, edgeY, -halfZ), new Vector3(x, peakY, 0f), new Vector3(x, edgeY, halfZ), new Vector3(x, baseY, halfZ) });
        return SaveMesh("House_Gable", faces, true);
    }

    static Mesh SaveMesh(string name, List<Vector3[]> faces, bool twoSided)
    {
        string path = $"{StructureFolder}/{name}.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh == null)
        {
            mesh = new Mesh { name = name };
            AssetDatabase.CreateAsset(mesh, path);
        }
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        foreach (bool back in twoSided ? new[] { false, true } : new[] { false })
        foreach (Vector3[] f in faces)
        {
            int start = verts.Count;
            Vector3 n = Vector3.Cross(f[1] - f[0], f[2] - f[0]).normalized;
            bool flat = Mathf.Abs(n.y) > 0.7f; // 위/아래를 보는 면은 xz, 옆면은 (x+z, y)로 질감
            foreach (Vector3 v in f)
            {
                verts.Add(v);
                uvs.Add(flat ? new Vector2(v.x, v.z) * 0.5f : new Vector2(v.x + v.z, v.y) * 0.5f);
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
}
