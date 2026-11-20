using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 옷자락 펄럭임: 허리천 · 허리끈 + 겉옷 아랫자락에 실행 중 Unity Cloth를 붙임
// 겉옷(hakama 메시 = 소매와 몸판이 한 덩어리)은 시작할 때 메시를 둘로 나눔: 팔 뼈와 상관없는 골반 아래 자락만 떼어 새 렌더러로 → 그 자락에만 Cloth
//        소매 정점이 아예 없으니 팔이 옷을 뚫을 수 없음. 잘린 경계(허리 쪽)는 고정이라 틈이 생기지 않음
// 정점마다 "원래 자리에서 벗어날 수 있는 거리"를 정함: 골반 아래로 내려갈수록 크게 (인스펙터 값은 Play 시작 때 적용)
// 순간이동 · 빠른 돌진 때는 옷이 늘어나지 않게 움직임 기록을 지움
[DefaultExecutionOrder(-50)] // PlayerAfterimage.Awake보다 먼저 나눠야 잔상에도 자락이 들어감
public class PlayerClothFlutter : MonoBehaviour
{
    [SerializeField] string[] hemSources = { "hakama" };
    [SerializeField] float hemTop = 0.12f;
    [SerializeField] float hemFree = 0.2f;
    [SerializeField] bool hemActive = false; // [변경] 생성 직후 자락 오브젝트 활성 여부

    [SerializeField] string[] flutterMeshes = { "fabric", "belt1", "belt2" };
    [SerializeField] float[] flutterFree = { 0.26f, 0.24f, 0.24f }; // 아랫단 끝이 원래 자리에서 벗어날 수 있는 거리(m)
    [SerializeField] float fullFreeDepth = 0.6f; // 다리 길이의 이 비율만큼 골반 아래부터 최대
    [SerializeField] float stretching = 0.9f;
    [SerializeField] float bending = 0.4f;
    [SerializeField] float damping = 0.15f;
    [SerializeField] float velocityScale = 0.6f;
    [SerializeField] float accelerationScale = 0.9f;
    [SerializeField] Vector3 wind = new Vector3(0.6f, 0f, 0.4f); // 가만히 있어도 살짝 흔들림
    [SerializeField] float teleportDistance = 1.2f;

    // 팔 · 손 · 쇄골 뼈 이름에 이 글자가 있으면 소매로 보고 고정
    static readonly string[] ArmBones = { "clavicle", "upperarm", "lowerarm", "hand", "index", "middle", "ring", "pinky", "thumb" };

    readonly List<Cloth> cloths = new List<Cloth>();
    readonly List<SkinnedMeshRenderer> hems = new List<SkinnedMeshRenderer>();

    void Awake()
    {
        foreach (SkinnedMeshRenderer smr in GetComponentsInChildren<SkinnedMeshRenderer>(false))
        {
            if (System.Array.IndexOf(hemSources, smr.name) < 0 || !smr.enabled) continue;
            SkinnedMeshRenderer hem = SplitHem(smr);
            if (hem == null) continue;
            hems.Add(hem);
            hem.gameObject.SetActive(hemActive); // [변경] 만들고 바로 끔
        }
    }

    Vector3 lastPosition;

    // [변경] Play에서 확인: Cloth 정점 = 루트 뼈 기준 "지금 자세"의 위치 → 메시와 짝지을 필요 없이 그 높이로 바로 계산
    //  1) 모든 정점을 고정(0)한 채 Cloth를 붙이고 2프레임 기다림 (자세가 잡힌 뒤)
    //  2) 정점마다 월드 높이 → 골반 아래 깊이(다리 길이 비율)로 허용 거리. 자락은 hemTop(잘린 경계)에서 0
    IEnumerator Start()
    {
        var pending = new List<(Cloth cloth, SkinnedMeshRenderer smr, float free, float start)>();
        foreach (SkinnedMeshRenderer smr in GetComponentsInChildren<SkinnedMeshRenderer>(false))
        {
            int index = System.Array.IndexOf(flutterMeshes, smr.name);
            if (index < 0 || !smr.enabled || smr.sharedMesh == null) continue;
            pending.Add((Attach(smr), smr, index < flutterFree.Length ? flutterFree[index] : 0.2f, 0f));
        }
        // [변경] 꺼진 자락엔 Cloth를 붙이지 않음 (비활성 상태에선 정점 정보가 비어 높이 계산이 틀어짐)
        foreach (SkinnedMeshRenderer hem in hems)
            if (hem.gameObject.activeSelf) pending.Add((Attach(hem), hem, hemFree, hemTop));
        yield return null;
        yield return null;

        Animator animator = GetComponentInChildren<Animator>();
        Transform hips = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
        Transform foot = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.LeftFoot) : null;
        if (hips == null || foot == null) yield break;
        float hipY = hips.position.y;
        float leg = Mathf.Max(0.3f, hipY - foot.position.y);

        foreach (var p in pending)
        {
            if (p.cloth == null) continue;
            FromCloth(p.cloth, p.smr, p.free, p.start, hipY, leg);
            cloths.Add(p.cloth);
        }
        lastPosition = transform.position;
    }

    void FromCloth(Cloth cloth, SkinnedMeshRenderer smr, float free, float start, float hipY, float leg)
    {
        Vector3[] v = cloth.vertices;
        ClothSkinningCoefficient[] c = cloth.coefficients;
        Transform space = smr.rootBone != null ? smr.rootBone : smr.transform;
        // 루트 뼈 기준이 맞는지 확인 (옷 높이가 발~골반 위 범위에 들어와야 함), 아니면 렌더러 기준
        if (!Plausible(v, space, hipY, leg)) space = smr.transform;
        float unit = 1f / Mathf.Max(0.0001f, space.lossyScale.y);
        var y = new float[v.Length];
        float top = float.MinValue, bottom = float.MaxValue;
        for (int i = 0; i < v.Length; i++) { y[i] = space.TransformPoint(v[i]).y; top = Mathf.Max(top, y[i]); bottom = Mathf.Min(bottom, y[i]); }
        // [변경] 허리끈처럼 골반 위에만 걸친 메시는 골반 기준으로는 전부 0 → 자기 위끝(묶인 곳)~아래끝 비율로
        bool own = (hipY - bottom) / leg < start + 0.1f;
        for (int i = 0; i < c.Length && i < v.Length; i++)
        {
            float k = own
                ? Mathf.Clamp01((top - y[i]) / Mathf.Max(0.01f, top - bottom))
                : Mathf.Clamp01(((hipY - y[i]) / leg - start) / Mathf.Max(0.01f, fullFreeDepth - start));
            c[i].maxDistance = k * k * free * unit;
        }
        cloth.coefficients = c;
        cloth.ClearTransformMotion();
    }

    static bool Plausible(Vector3[] v, Transform space, float hipY, float leg)
    {
        if (v.Length == 0) return false;
        float min = float.MaxValue, max = float.MinValue;
        foreach (Vector3 p in v) { float y = space.TransformPoint(p).y; min = Mathf.Min(min, y); max = Mathf.Max(max, y); }
        return min > hipY - leg - 0.4f && max < hipY + 0.8f;
    }

    // 모든 정점 고정(0)으로 Cloth를 붙임 → 2프레임 뒤 FromCloth에서 실제 값
    Cloth Attach(SkinnedMeshRenderer smr)
    {
        Cloth cloth = smr.GetComponent<Cloth>();
        if (cloth == null) cloth = smr.gameObject.AddComponent<Cloth>();
        ClothSkinningCoefficient[] c = cloth.coefficients;
        for (int i = 0; i < c.Length; i++) c[i].maxDistance = 0f;
        cloth.coefficients = c;
        Configure(cloth);
        return cloth;
    }

    // 겉옷 메시를 "팔 가중치 없음 + 골반 아래 hemTop 이상"인 삼각형(자락)과 나머지(몸판)로 나눔
    //  몸판은 원래 렌더러에 그대로, 자락은 같은 뼈 · 재질을 쓰는 새 렌더러(이름_Hem)로
    SkinnedMeshRenderer SplitHem(SkinnedMeshRenderer smr)
    {
        Mesh src = smr.sharedMesh;
        if (src != null && !src.isReadable) Debug.LogWarning($"[Cloth] {smr.name}: 메시 Read/Write가 꺼져 있어 자락을 나누지 못함 (Tools/Setup/Player Visuals)");
        if (src == null || !src.isReadable || !Analyze(smr, out float[] depth, out float[] arm)) return null;
        var ok = new bool[depth.Length];
        for (int i = 0; i < ok.Length; i++) ok[i] = arm[i] <= 0f && depth[i] >= hemTop;

        int subCount = src.subMeshCount;
        var upper = new List<int>[subCount];
        var hem = new List<int>[subCount];
        int hemTriangles = 0;
        for (int s = 0; s < subCount; s++)
        {
            upper[s] = new List<int>();
            hem[s] = new List<int>();
            int[] tris = src.GetTriangles(s);
            for (int t = 0; t < tris.Length; t += 3)
            {
                bool inHem = ok[tris[t]] && ok[tris[t + 1]] && ok[tris[t + 2]];
                (inHem ? hem[s] : upper[s]).AddRange(new[] { tris[t], tris[t + 1], tris[t + 2] });
                if (inHem) hemTriangles++;
            }
        }
        if (hemTriangles == 0) return null;

        // 자락: 쓰는 정점만 모아 새 메시
        var map = new Dictionary<int, int>();
        var used = new List<int>();
        for (int s = 0; s < subCount; s++)
            foreach (int v in hem[s])
                if (!map.ContainsKey(v)) { map[v] = used.Count; used.Add(v); }
        Vector3[] pos = src.vertices, nor = src.normals;
        Vector4[] tan = src.tangents;
        Vector2[] uv = src.uv;
        BoneWeight[] bw = src.boneWeights;
        var hemMesh = new Mesh { name = src.name + "_Hem" };
        hemMesh.SetVertices(used.ConvertAll(i => pos[i]));
        if (nor.Length == pos.Length) hemMesh.SetNormals(used.ConvertAll(i => nor[i]));
        if (tan.Length == pos.Length) hemMesh.SetTangents(used.ConvertAll(i => tan[i]));
        if (uv.Length == pos.Length) hemMesh.SetUVs(0, used.ConvertAll(i => uv[i]));
        hemMesh.boneWeights = used.ConvertAll(i => bw[i]).ToArray();
        hemMesh.bindposes = src.bindposes;
        hemMesh.subMeshCount = subCount;
        for (int s = 0; s < subCount; s++) hemMesh.SetTriangles(hem[s].ConvertAll(v => map[v]), s);
        hemMesh.RecalculateBounds();

        // 몸판: 원래 메시 복사본에서 자락 삼각형만 뺌
        Mesh upperMesh = Instantiate(src);
        upperMesh.name = src.name + "_Upper";
        for (int s = 0; s < subCount; s++) upperMesh.SetTriangles(upper[s], s);

        var go = new GameObject(smr.name + "_Hem");
        go.transform.SetParent(smr.transform.parent, false);
        go.transform.localPosition = smr.transform.localPosition;
        go.transform.localRotation = smr.transform.localRotation;
        go.transform.localScale = smr.transform.localScale;
        var r = go.AddComponent<SkinnedMeshRenderer>();
        r.bones = smr.bones;
        r.rootBone = smr.rootBone;
        r.sharedMesh = hemMesh;
        r.sharedMaterials = smr.sharedMaterials;
        r.localBounds = smr.localBounds;
        r.updateWhenOffscreen = smr.updateWhenOffscreen;
        r.quality = smr.quality;
        r.shadowCastingMode = smr.shadowCastingMode;
        r.receiveShadows = smr.receiveShadows;
        r.renderingLayerMask = smr.renderingLayerMask;
        smr.sharedMesh = upperMesh;
        return r;
    }

    // 정점마다 골반 아래 깊이(다리 길이 비율)와 팔 뼈 가중치. 바인드 포즈 기준이라 지금 자세와 상관없음
    bool Analyze(SkinnedMeshRenderer smr, out float[] depth, out float[] armWeight)
    {
        depth = armWeight = null;
        Mesh mesh = smr.sharedMesh;
        Transform[] bones = smr.bones;
        Matrix4x4[] bind = mesh.bindposes;
        int pelvis = -1, foot = -1;
        var arm = new bool[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] == null) continue;
            string n = bones[i].name.ToLowerInvariant();
            if (n == "pelvis") pelvis = i;
            else if (foot < 0 && (n.StartsWith("foot") || n.StartsWith("calf"))) foot = i;
            foreach (string a in ArmBones) if (n.StartsWith(a)) arm[i] = true;
        }
        if (pelvis < 0 || foot < 0 || pelvis >= bind.Length || foot >= bind.Length) return false;

        Vector3 hip = bind[pelvis].inverse.MultiplyPoint(Vector3.zero);
        Vector3 down = bind[foot].inverse.MultiplyPoint(Vector3.zero) - hip;
        float leg = down.magnitude;
        if (leg < 1e-5f) return false;
        down /= leg;

        Vector3[] vertices = mesh.vertices;
        BoneWeight[] weights = mesh.boneWeights;
        depth = new float[vertices.Length];
        armWeight = new float[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            depth[i] = Vector3.Dot(vertices[i] - hip, down) / leg;
            if (i >= weights.Length) continue;
            BoneWeight w = weights[i];
            armWeight[i] = Arm(arm, w.boneIndex0, w.weight0) + Arm(arm, w.boneIndex1, w.weight1)
                         + Arm(arm, w.boneIndex2, w.weight2) + Arm(arm, w.boneIndex3, w.weight3);
        }
        return true;
    }

    static float Arm(bool[] arm, int bone, float weight) => bone >= 0 && bone < arm.Length && arm[bone] ? weight : 0f;

    void Configure(Cloth cloth)
    {
        cloth.stretchingStiffness = stretching;
        cloth.bendingStiffness = bending;
        cloth.damping = damping;
        cloth.worldVelocityScale = velocityScale;
        cloth.worldAccelerationScale = accelerationScale;
        cloth.useGravity = true;
        cloth.friction = 0.5f;
        cloth.randomAcceleration = wind;
        cloth.ClearTransformMotion();
    }

    void LateUpdate()
    {
        if ((transform.position - lastPosition).sqrMagnitude > teleportDistance * teleportDistance)
            foreach (Cloth cloth in cloths)
                if (cloth != null) cloth.ClearTransformMotion();
        lastPosition = transform.position;
    }
}
