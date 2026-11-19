using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 옷자락 펄럭임: 허리천 · 허리끈에 실행 중 Unity Cloth를 붙임
// [변경] 겉옷(hakama 메시 = 소매와 몸판이 한 덩어리)은 빼고, 이름을 바꿔 프리팹에 저장된 예전 목록 대신 새 기본값을 씀
// 정점마다 "원래 자리에서 벗어날 수 있는 거리"를 정함: 골반 아래로 내려갈수록 크게, 팔 뼈에 붙은 정점(소매)은 0 → 걸을 때 팔이 옷을 뚫지 않음
// 순간이동 · 빠른 돌진 때는 옷이 늘어나지 않게 움직임 기록을 지움
public class PlayerClothFlutter : MonoBehaviour
{
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
    Vector3 lastPosition;

    // Cloth 정점 순서를 메시 정점과 추측해서 맞추던 방식 → 실제 위치로 짝지음 (순서가 어긋나 소매가 풀리던 문제)
    //  1) 모든 정점을 고정(0)한 채 Cloth를 붙이고 2프레임 기다림 → Cloth 정점 = 몸에 입혀진 위치
    //  2) 같은 순간 스킨 메시를 구워(BakeMesh) 가장 가까운 메시 정점을 찾아 그 정점의 허용 거리를 씀
    IEnumerator Start()
    {
        var pending = new List<(Cloth cloth, SkinnedMeshRenderer smr, float[] limits)>();
        foreach (SkinnedMeshRenderer smr in GetComponentsInChildren<SkinnedMeshRenderer>(false))
        {
            int index = System.Array.IndexOf(flutterMeshes, smr.name);
            if (index < 0 || !smr.enabled || smr.sharedMesh == null) continue;
            float[] limits = Limits(smr, index < flutterFree.Length ? flutterFree[index] : 0.2f);
            if (limits == null) continue;

            Cloth cloth = smr.GetComponent<Cloth>();
            if (cloth == null) cloth = smr.gameObject.AddComponent<Cloth>();
            ClothSkinningCoefficient[] c = cloth.coefficients;
            for (int i = 0; i < c.Length; i++) c[i].maxDistance = 0f;
            cloth.coefficients = c;
            Configure(cloth);
            pending.Add((cloth, smr, limits));
        }
        yield return null;
        yield return null;

        foreach (var p in pending)
        {
            if (p.cloth == null) continue;
            Match(p.cloth, p.smr, p.limits);
            cloths.Add(p.cloth);
        }
        lastPosition = transform.position;
    }

    // Cloth 정점마다 가장 가까운 (구운) 메시 정점의 허용 거리. 2cm 격자로 찾음
    void Match(Cloth cloth, SkinnedMeshRenderer smr, float[] limits)
    {
        // Cloth 정점이 어느 좌표계인지(로컬 · 크기 적용 · 월드) 확실하지 않아 셋 중 가장 잘 맞는 쪽을 씀
        Vector3[] clothVertices = cloth.vertices;
        var baked = new Mesh();
        smr.BakeMesh(baked, true);
        Vector3[] scaled = baked.vertices;
        smr.BakeMesh(baked, false);
        Vector3[] unscaled = baked.vertices;
        Destroy(baked);
        var world = new Vector3[unscaled.Length];
        for (int i = 0; i < world.Length; i++) world[i] = smr.transform.TransformPoint(unscaled[i]);
        Vector3[] skinned = scaled;
        float bestScore = Score(clothVertices, scaled);
        foreach (Vector3[] candidate in new[] { unscaled, world })
        {
            float score = Score(clothVertices, candidate);
            if (score < bestScore) { bestScore = score; skinned = candidate; }
        }

        const float cell = 0.02f;
        var grid = new Dictionary<Vector3Int, List<int>>();
        for (int i = 0; i < skinned.Length; i++)
        {
            Vector3Int key = Vector3Int.FloorToInt(skinned[i] / cell);
            if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
            list.Add(i);
        }

        ClothSkinningCoefficient[] c = cloth.coefficients;
        int matched = 0;
        for (int v = 0; v < c.Length && v < clothVertices.Length; v++)
        {
            Vector3 p = clothVertices[v];
            Vector3Int k = Vector3Int.FloorToInt(p / cell);
            int best = -1;
            float bestSqr = cell * cell * 4f;
            for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
            for (int z = -1; z <= 1; z++)
            {
                if (!grid.TryGetValue(k + new Vector3Int(x, y, z), out var list)) continue;
                foreach (int i in list)
                {
                    float d = (skinned[i] - p).sqrMagnitude;
                    if (d < bestSqr) { bestSqr = d; best = i; }
                }
            }
            c[v].maxDistance = best >= 0 && best < limits.Length ? limits[best] : 0f; // 못 찾으면 고정
            if (best >= 0) matched++;
        }
        cloth.coefficients = c;
        cloth.ClearTransformMotion();
        if (matched < c.Length * 0.9f)
            Debug.LogWarning($"[Cloth] {smr.name}: 정점 짝짓기 {matched}/{c.Length} — 못 찾은 정점은 고정");
    }

    // 핵심: 메시 정점마다 허용 거리. 바인드 포즈 기준이라 지금 자세와 상관없음
    //  깊이 = 골반 → 발 방향으로 골반에서 얼마나 내려갔나 (다리 길이 비율) / 팔 뼈 가중치가 있으면 그만큼 고정
    float[] Limits(SkinnedMeshRenderer smr, float free)
    {
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
        if (pelvis < 0 || foot < 0 || pelvis >= bind.Length || foot >= bind.Length) return null;

        Vector3 hip = bind[pelvis].inverse.MultiplyPoint(Vector3.zero);
        Vector3 down = bind[foot].inverse.MultiplyPoint(Vector3.zero) - hip;
        float leg = down.magnitude;
        if (leg < 1e-5f) return null;
        down /= leg;
        float unit = 1f / Mathf.Max(0.0001f, smr.transform.lossyScale.y); // Cloth 거리는 메시 로컬 단위

        Vector3[] vertices = mesh.vertices;
        BoneWeight[] weights = mesh.boneWeights;
        var limits = new float[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            float depth = Vector3.Dot(vertices[i] - hip, down) / leg;
            float k = Mathf.Clamp01(depth / fullFreeDepth);
            if (i < weights.Length)
            {
                BoneWeight w = weights[i];
                float armWeight = Arm(arm, w.boneIndex0, w.weight0) + Arm(arm, w.boneIndex1, w.weight1)
                                + Arm(arm, w.boneIndex2, w.weight2) + Arm(arm, w.boneIndex3, w.weight3);
                k *= Mathf.Clamp01(1f - armWeight * 5f); // 팔 가중치 20% 이상이면 완전히 고정
            }
            limits[i] = k * k * free * unit;
        }
        return limits;
    }

    static float Arm(bool[] arm, int bone, float weight) => bone >= 0 && bone < arm.Length && arm[bone] ? weight : 0f;

    // 앞쪽 정점 몇 개로 대강 비교: 각 Cloth 정점에서 가장 가까운 후보 정점까지 거리 평균 (작을수록 같은 좌표계)
    static float Score(Vector3[] clothVertices, Vector3[] candidate)
    {
        float sum = 0f;
        int n = Mathf.Min(40, clothVertices.Length);
        for (int v = 0; v < n; v++)
        {
            Vector3 p = clothVertices[v * clothVertices.Length / n];
            float best = float.MaxValue;
            for (int i = 0; i < candidate.Length; i += 3) best = Mathf.Min(best, (candidate[i] - p).sqrMagnitude);
            sum += best;
        }
        return sum / Mathf.Max(1, n);
    }

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
