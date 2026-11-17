using System.Collections.Generic;
using UnityEngine;

// 뇌신 강림(R) 검기: 휘두른 방향으로 날아가는 번개 초승달. PlayerEnchant가 Fire로 만들고, 다 날아가면 스스로 사라짐
// 퀄리티 개편 — 선 하나였던 검기를 여러 겹으로
//  - 날: 가로 초승달 + 앞날을 따라 세운 세로 띠를 한 메시로 (위에서 봐도 옆에서 봐도 두께가 보임)
//        앞날은 흰 심(HDR → 블룸), 뒤로 갈수록 푸르게 사라짐, 양 끝은 가늘게
//  - 잔상 1겹 · 날을 따라 튀는 불꽃 · 날 위를 타는 짧은 번개 · 따라가는 푸른 빛
//  - 커지며 날아가다 마지막에 사라짐, 적을 벨 때 그 자리에 불꽃이 터짐
public class EnchantWave : MonoBehaviour
{
    const float BaseSpeed = 26f;
    const int Segments = 28;
    const float ArcAngle = 150f;     // 초승달이 펼쳐진 각도
    const float BladeDepth = 0.25f;  // 가로 날의 두께 (가운데 기준)
    const float BandHeight = 0.9f;   // 세로 띠 높이 (가운데 기준)
    const float BoltInterval = 0.06f;

    static readonly Color Core = new Color(2.6f, 2.9f, 3.6f, 1f);     // HDR 흰 심
    static readonly Color Body = new Color(0.45f, 0.85f, 2.2f, 0.75f);
    static readonly Color Edge = new Color(0.3f, 0.6f, 1.6f, 0f);
    static Material bladeMat, sparkMat;
    static Texture2D dot;

    GameObject owner;
    Vector3 direction;
    float range, width, damage, posture, traveled, boltTimer;
    float scale = 1f, speed = BaseSpeed; // 크기 배율 (뇌신참은 크고 느리게)
    bool heavy;
    MeshRenderer blade, echo;
    MaterialPropertyBlock block;
    ParticleSystem sparks;
    Light glow;
    Vector3[] frontLine; // 앞날 위 점들 (로컬) → 불꽃 · 번개 위치
    readonly HashSet<IDamageable> hit = new HashSet<IDamageable>();
    readonly Collider[] buffer = new Collider[16];

    // scale: 날 두께 · 높이 · 빛 · 불꽃을 키움 / heavy: 강경직 · 아머 무시 (뇌신참)
    // [변경] speed: 초당 이동 거리 (0 이하면 예전처럼 BaseSpeed ÷ √scale)
    public static void Fire(GameObject owner, Vector3 origin, Vector3 direction, float range, float width, float damage, float posture, Color color,
        float scale = 1f, bool heavy = false, float speed = 0f)
    {
        var go = new GameObject("EnchantWave");
        float roll = Random.Range(-18f, 18f); // 매번 조금씩 기울여 단조롭지 않게
        go.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(direction, Vector3.up) * Quaternion.Euler(0f, 0f, roll));
        var wave = go.AddComponent<EnchantWave>();
        wave.owner = owner;
        wave.direction = direction;
        wave.range = range;
        wave.width = width;
        wave.damage = damage;
        wave.posture = posture;
        wave.scale = Mathf.Max(0.1f, scale);
        wave.speed = speed > 0f ? speed : BaseSpeed / Mathf.Sqrt(wave.scale); // [변경]
        wave.heavy = heavy;
        wave.Build();
    }

    void Build()
    {
        EnsureMaterials();
        block = new MaterialPropertyBlock();
        Mesh mesh = BuildMesh(width * 0.5f, BladeDepth * scale, BandHeight * scale, out frontLine);
        blade = AddRenderer("Blade", mesh, Vector3.zero, 1f);
        echo = AddRenderer("Echo", mesh, Vector3.back * 0.8f, 0.9f); // 뒤따르는 잔상

        sparks = new GameObject("Sparks").AddComponent<ParticleSystem>();
        sparks.transform.SetParent(transform, false);
        sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = sparks.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.45f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
        main.gravityModifier = 0.4f;
        main.maxParticles = 300;
        ParticleSystem.EmissionModule em = sparks.emission;
        em.enabled = false; // 직접 Emit
        ParticleSystem.ColorOverLifetimeModule col = sparks.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.45f, 0.75f, 1f), 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var pr = sparks.GetComponent<ParticleSystemRenderer>();
        pr.sharedMaterial = sparkMat;
        pr.renderMode = ParticleSystemRenderMode.Stretch;
        pr.velocityScale = 0.05f;
        pr.lengthScale = 1.5f;
        pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        sparks.Play();

        glow = new GameObject("Glow").AddComponent<Light>();
        glow.transform.SetParent(transform, false);
        glow.type = LightType.Point;
        glow.color = new Color(0.55f, 0.8f, 1f);
        glow.range = 6f * scale;
        glow.intensity = 4f * Mathf.Sqrt(scale);
        glow.shadows = LightShadows.None;

        SetAlpha(0f);
    }

    MeshRenderer AddRenderer(string name, Mesh mesh, Vector3 offset, float scale)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = offset;
        go.transform.localScale = Vector3.one * scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = bladeMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return r;
    }

    // 핵심: 한 프레임에 나아간 만큼 앞쪽 상자 범위를 검사 → 빠르게 날아도 사이 적을 놓치지 않음
    void Update()
    {
        float step = speed * Time.deltaTime;
        traveled += step;
        transform.position += direction * step;
        float k = Mathf.Clamp01(traveled / range);
        transform.localScale = Vector3.one * Mathf.Lerp(0.85f, 1.3f, k); // 날아가며 커짐

        HitAlongPath(step);

        // 처음 순간 켜지고 · 후반 45%에서 사라짐
        float alpha = Mathf.Clamp01(k / 0.08f) * (1f - Mathf.Clamp01((k - 0.55f) / 0.45f));
        SetAlpha(alpha);
        glow.intensity = 4f * Mathf.Sqrt(scale) * alpha;

        EmitTrail(alpha);
        boltTimer -= Time.deltaTime;
        if (boltTimer <= 0f && alpha > 0.3f)
        {
            boltTimer = BoltInterval / scale;
            int a = Random.Range(2, frontLine.Length / 2);
            int b = Mathf.Min(frontLine.Length - 3, a + Random.Range(4, 9));
            ThunderBolt3D.Strike(transform.TransformPoint(frontLine[a]), transform.TransformPoint(frontLine[b]), 0.18f * scale, 0);
        }

        if (traveled >= range) Finish();
    }

    void HitAlongPath(float step)
    {
        Vector3 half = new Vector3(width * 0.5f * transform.localScale.x, 1.1f * scale, step * 0.5f + 0.5f);
        int count = Physics.OverlapBoxNonAlloc(transform.position - direction * step * 0.5f, half, buffer, transform.rotation, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider c = buffer[i];
            if (owner != null && c.transform.IsChildOf(owner.transform)) continue;
            IDamageable target = c.GetComponentInParent<IDamageable>();
            if (target == null || !target.CanBeTargeted || !hit.Add(target)) continue;
            Vector3 point = c.ClosestPoint(transform.position);
            target.ReceiveDamage(new DamageInfo
            {
                Attacker = owner,
                Damage = damage,
                PostureDamage = posture,
                Parryable = false,
                Weight = heavy ? AttackWeight.Heavy : AttackWeight.Light,
                BreakArmor = heavy,
                HitPoint = point,
                Direction = direction
            });
            Burst(point);
        }
    }

    // 날을 따라 불꽃이 뒤로 흩날림
    void EmitTrail(float alpha)
    {
        if (alpha < 0.2f) return;
        var p = new ParticleSystem.EmitParams();
        int count = Mathf.RoundToInt(5 * scale);
        for (int i = 0; i < count; i++)
        {
            Vector3 local = frontLine[Random.Range(1, frontLine.Length - 1)];
            p.position = transform.TransformPoint(local);
            p.velocity = -direction * Random.Range(2f, 6f) + Random.insideUnitSphere * 2f + Vector3.up * Random.Range(0f, 1.5f);
            sparks.Emit(p, 1);
        }
    }

    // 적을 벤 자리: 사방으로 튀는 불꽃 + 짧은 번개
    void Burst(Vector3 point)
    {
        var p = new ParticleSystem.EmitParams();
        for (int i = 0; i < 16; i++)
        {
            p.position = point;
            p.velocity = Random.onUnitSphere * Random.Range(3f, 8f) + direction * 2f;
            sparks.Emit(p, 1);
        }
        ThunderBolt3D.Strike(point + Vector3.up * 1.4f + Random.insideUnitSphere * 0.4f, point, 0.3f, 1);
    }

    void SetAlpha(float a)
    {
        block.SetColor("_Color", new Color(1f, 1f, 1f, a));
        blade.SetPropertyBlock(block);
        block.SetColor("_Color", new Color(1f, 1f, 1f, a * 0.35f));
        echo.SetPropertyBlock(block);
    }

    // 불꽃은 남은 수명만큼 더 보이게 떼어 놓고 사라짐
    void Finish()
    {
        sparks.transform.SetParent(null, true);
        Destroy(sparks.gameObject, 0.6f);
        Destroy(blade.GetComponent<MeshFilter>().sharedMesh);
        Destroy(gameObject);
    }

    // ===== 메시: 가로 초승달 + 세로 띠 (정점 색으로 심 · 몸 · 가장자리 그라데이션) =====
    // 원점(로컬)이 앞날 가운데, +Z가 날아가는 쪽. 날 끝으로 갈수록 두께 · 높이 · 밝기가 줄어듦
    static Mesh BuildMesh(float halfWidth, float bladeDepth, float bandHeight, out Vector3[] front)
    {
        var verts = new List<Vector3>();
        var colors = new List<Color>();
        var tris = new List<int>();
        front = new Vector3[Segments + 1];

        float half = ArcAngle * 0.5f * Mathf.Deg2Rad;
        float radius = halfWidth / Mathf.Sin(half); // 양 끝이 halfWidth에 오도록
        Vector3 center = new Vector3(0f, 0f, -radius);

        // 가로 날: 뒤(0) → 몸 → 심 → 앞 가장자리(0) 네 줄
        float[] depth = { 1f, 0.55f, 0.12f, -0.06f };
        Color[] shade = { Edge, Body, Core, new Color(Core.r, Core.g, Core.b, 0f) };
        int rows = depth.Length;
        for (int s = 0; s <= Segments; s++)
        {
            float t = s / (float)Segments * 2f - 1f;           // -1 ~ 1
            float taper = Mathf.Pow(1f - t * t, 0.7f);
            float tip = 1f - Mathf.Pow(Mathf.Abs(t), 6f);
            float angle = t * half;
            Vector3 dir = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            for (int r = 0; r < rows; r++)
            {
                verts.Add(center + dir * (radius - depth[r] * bladeDepth * taper));
                colors.Add(Fade(shade[r], tip));
            }
            front[s] = center + dir * (radius - 0.12f * bladeDepth * taper);
        }
        Strip(tris, 0, Segments, rows);

        // 세로 띠: 심 위치를 따라 아래(0) → 몸 → 심 → 몸 → 위(0) 다섯 줄
        int baseIndex = verts.Count;
        float[] height = { -1f, -0.3f, 0f, 0.3f, 1f };
        Color[] bandShade = { Edge, Body, Core, Body, Edge };
        for (int s = 0; s <= Segments; s++)
        {
            float t = s / (float)Segments * 2f - 1f;
            float taper = Mathf.Pow(1f - t * t, 0.9f);
            float tip = 1f - Mathf.Pow(Mathf.Abs(t), 6f);
            Vector3 p = front[s];
            for (int r = 0; r < height.Length; r++)
            {
                verts.Add(p + Vector3.up * height[r] * bandHeight * 0.5f * taper);
                colors.Add(Fade(bandShade[r], tip * 0.85f));
            }
        }
        Strip(tris, baseIndex, Segments, height.Length);

        var mesh = new Mesh { name = "EnchantWave" };
        mesh.SetVertices(verts);
        mesh.SetColors(colors);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    static Color Fade(Color c, float k) => new Color(c.r, c.g, c.b, c.a * k);

    // 한 단면에 rows개 정점이 있는 띠를 삼각형으로 (양면은 재질이 그림)
    static void Strip(List<int> tris, int start, int segments, int rows)
    {
        for (int s = 0; s < segments; s++)
            for (int r = 0; r < rows - 1; r++)
            {
                int a = start + s * rows + r, b = a + rows;
                tris.Add(a); tris.Add(b); tris.Add(a + 1);
                tris.Add(a + 1); tris.Add(b); tris.Add(b + 1);
            }
    }

    static void EnsureMaterials()
    {
        if (bladeMat != null) return;
        Shader shader = Shader.Find("Sprites/Default"); // 기본 포함 셰이더 · 양면 · 정점 색 (HDR 색이면 블룸으로 번짐)
        bladeMat = new Material(shader) { name = "EnchantWave_Blade" };
        sparkMat = new Material(shader) { name = "EnchantWave_Spark", mainTexture = Dot() };
    }

    static Texture2D Dot()
    {
        if (dot != null) return dot;
        const int N = 32;
        dot = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = new Vector2((x + 0.5f) / N - 0.5f, (y + 0.5f) / N - 0.5f).magnitude * 2f;
                float a = Mathf.Clamp01(1f - d);
                dot.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        dot.Apply();
        return dot;
    }
}
