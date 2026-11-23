using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 섬뢰일도 돌진 연출 (SkillPackFx가 실행 중 자동으로 붙임, 에셋 없이 LineRenderer로 그림)
//  A. 칼자국: 적 옆을 지나는 순간(TargetPassed) 적 몸을 가로지르는 대각선 빛줄기가 번쩍 그어짐 → 공중에 남아 있다가
//     한꺼번에 베이는 순간(TargetCut) 하나씩 차례로 크게 터지며 불티가 튐 (못 베면 slashTimeout 뒤 흐려짐)
//     [변경] 선 색(정점 색)은 1에서 잘려 빛나지 않음 → 밝기는 재질 색(MaterialPropertyBlock _Color)으로 1 넘게 줌
//  B. 경로 잔광: [변경] 휜 빛 줄기 3가닥(줄무늬 질감) → 꼬리부터 걷히며 사라짐 + 빛 알갱이 + 잔 번개
// 시간은 전부 실제 시간 (지나가는 순간 정지 · 화면 베기 중에도 그대로 보임)
public class DashSlashFx : MonoBehaviour
{
    [Header("A. Slash")] // [변경] 거의 안 보이던 문제: 굵게 · 발광(재질 색으로 블룸) · 그어질 때 섬광 · 마지막에 차례로 터짐
    public bool slashes = true;
    public float slashLength = 3.2f;
    public float slashHeight = 1.1f;
    public float slashTilt = 35f;           // 수평에서 기운 각도 (번갈아 ±)
    public float slashDraw = 0.05f;         // 그어지는 시간
    public float slashTimeout = 2.5f;
    public float slashCoreWidth = 0.16f;
    public float slashGlowWidth = 0.8f;
    public float slashBrightness = 3.5f;    // 심 밝기 (1 넘으면 블룸으로 번짐)
    public float glowBrightness = 2f;
    public float burstStagger = 0.05f;      // 마지막에 칼자국이 하나씩 터지는 간격
    public Color slashCore = new Color(1f, 1f, 1f, 1f);
    public Color slashGlow = new Color(0.45f, 0.72f, 1f, 0.6f);
    [Header("B. Path")]
    public bool path = true;
    public float pathHeight = 1f;
    public float pathWidth = 0.55f; // [변경] 질감 가장자리가 부드러워져 조금 넓게
    public float pathLife = 0.5f;
    public int arcCount = 2;
    public float dustPerMeter = 4f; // [변경] 길을 따라 흩날리는 빛 알갱이 수
    public Color pathColor = new Color(0.6f, 0.85f, 2.4f, 0.75f);

    class Slash
    {
        public LineRenderer core, glow;
        public Vector3 a, b;
        public float born;
        public bool bursting;
    }

    static readonly int ColorId = Shader.PropertyToID("_Color");
    MaterialPropertyBlock block;

    static Material material;
    PlayerSkill skill;
    readonly List<Slash> live = new List<Slash>();
    Vector3 dashDir = Vector3.forward;
    float tiltSign = 1f;
    int burstFrame = -1;

    void Awake()
    {
        skill = GetComponent<PlayerSkill>();
        if (material == null) material = new Material(Shader.Find("Sprites/Default")) { name = "DashSlashFx" };
    }

    void OnEnable()
    {
        if (skill == null) return;
        skill.Dashed += OnDashed;
        skill.TargetPassed += OnPassed;
        skill.TargetCut += OnCut;
    }

    void OnDisable()
    {
        if (skill == null) return;
        skill.Dashed -= OnDashed;
        skill.TargetPassed -= OnPassed;
        skill.TargetCut -= OnCut;
    }

    void Update()
    {
        // 못 베고 끝난 칼자국 정리
        for (int i = live.Count - 1; i >= 0; i--)
            if (!live[i].bursting && Time.unscaledTime - live[i].born > slashTimeout) StartCoroutine(Burst(live[i], false));
    }

    // ===== B. 경로 잔광 ===== [변경] 평평한 띠 → 빛 줄기 다발 + 꼬리부터 걷힘 + 불티로 흩어짐
    //  ① 가운데 굵은 줄기 + 양옆 가는 줄기 2개가 살짝 휘어 겹침 (줄무늬 · 부드러운 가장자리 질감)
    //  ② 꼬리(출발 쪽)부터 도착 쪽으로 걷히며 가늘어짐 → 혜성 꼬리처럼 따라 사라짐
    //  ③ 길을 따라 작은 빛 알갱이가 흩날림, 잔 번개는 앞쪽 시간 동안만 튐
    void OnDashed(Vector3 start, Vector3 end)
    {
        Vector3 dir = end - start;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f) dashDir = dir.normalized;
        if (path) StartCoroutine(Path(start + Vector3.up * pathHeight, end + Vector3.up * pathHeight));
    }

    IEnumerator Path(Vector3 a, Vector3 b)
    {
        Vector3 side = Vector3.Cross(Vector3.up, (b - a).normalized);
        float length = Vector3.Distance(a, b);
        // 줄기마다: 옆 · 위로 휜 정도, 굵기 비율, 밝기
        var wisps = new (LineRenderer line, Vector3 bend, float width, float bright)[]
        {
            (NewStreak(), Vector3.up * 0.06f, 1f, 1f),
            (NewStreak(), side * 0.16f * length * 0.08f + Vector3.up * 0.12f, 0.45f, 0.7f),
            (NewStreak(), -side * 0.16f * length * 0.08f - Vector3.up * 0.05f, 0.35f, 0.6f),
        };
        var arcs = new LineRenderer[arcCount];
        for (int i = 0; i < arcCount; i++)
        {
            arcs[i] = NewLine("DashArc", 12);
            arcs[i].widthMultiplier = 0.025f;
        }
        EmitDust(a, b);

        float jitterAt = 0f;
        for (float t = 0f; t < pathLife; t += Time.unscaledDeltaTime)
        {
            float k = t / pathLife;
            float tail = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((k - 0.1f) / 0.9f)); // 처음 10%는 그대로, 그 뒤 꼬리가 앞으로 걷힘
            float alpha = 1f - k * k * k;
            foreach (var w in wisps)
            {
                Curve(w.line, a, b, w.bend, tail);
                w.line.widthMultiplier = pathWidth * w.width * Mathf.Lerp(1f, 0.5f, k);
                Color c = new Color(pathColor.r * w.bright, pathColor.g * w.bright, pathColor.b * w.bright, pathColor.a * alpha);
                SetColor(w.line, new Color(c.r, c.g, c.b, 0f), new Color(c.r * 1.4f, c.g * 1.3f, c.b * 1.2f, c.a)); // 꼬리는 투명 → 머리가 가장 밝음
            }
            if (t >= jitterAt && k < 0.45f)
            {
                jitterAt = t + 0.04f;
                foreach (LineRenderer arc in arcs) Zigzag(arc, Vector3.Lerp(a, b, tail), b, 0.22f);
            }
            float arcAlpha = k < 0.45f ? 0.9f : 0f;
            foreach (LineRenderer arc in arcs) SetColor(arc, new Color(0.7f, 0.9f, 3f, 0f), new Color(0.8f, 0.95f, 3f, arcAlpha));
            yield return null;
        }
        foreach (var w in wisps) Destroy(w.line.gameObject);
        foreach (LineRenderer arc in arcs) Destroy(arc.gameObject);
    }

    // a→b 사이를 bend만큼 휜 곡선으로, from(0~1)부터 끝까지만 그림
    static void Curve(LineRenderer line, Vector3 a, Vector3 b, Vector3 bend, float from)
    {
        int n = line.positionCount;
        for (int i = 0; i < n; i++)
        {
            float u = Mathf.Lerp(from, 1f, i / (float)(n - 1));
            line.SetPosition(i, Vector3.Lerp(a, b, u) + bend * (4f * u * (1f - u)));
        }
    }

    LineRenderer NewStreak()
    {
        LineRenderer line = NewLine("DashStreak", 10);
        line.sharedMaterial = StreakMaterial();
        line.textureMode = LineTextureMode.Stretch;
        line.widthCurve = new AnimationCurve(new Keyframe(0f, 0.15f), new Keyframe(0.8f, 1f), new Keyframe(1f, 0.35f));
        return line;
    }

    // 길을 따라 빛 알갱이 (실제 시간으로 움직이는 공용 파티클 하나에 뿌림)
    void EmitDust(Vector3 a, Vector3 b)
    {
        ParticleSystem ps = Dust();
        var p = new ParticleSystem.EmitParams();
        int count = Mathf.RoundToInt(Vector3.Distance(a, b) * dustPerMeter);
        for (int i = 0; i < count; i++)
        {
            p.position = Vector3.Lerp(a, b, Random.value) + Random.insideUnitSphere * 0.25f;
            p.velocity = (b - a).normalized * Random.Range(0.5f, 2.5f) + Random.insideUnitSphere * 0.8f + Vector3.up * Random.Range(0f, 0.6f);
            p.startLifetime = Random.Range(0.35f, 0.8f);
            p.startSize = Random.Range(0.025f, 0.06f);
            p.startColor = Color.Lerp(new Color(0.75f, 0.9f, 1f), Color.white, Random.value);
            ps.Emit(p, 1);
        }
    }

    static ParticleSystem dust;
    static ParticleSystem Dust()
    {
        if (dust != null) return dust;
        var go = new GameObject("DashDust");
        dust = go.AddComponent<ParticleSystem>();
        dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = dust.main;
        main.useUnscaledTime = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = false;
        main.maxParticles = 600;
        main.gravityModifier = 0.15f;
        ParticleSystem.EmissionModule emission = dust.emission;
        emission.enabled = false;
        ParticleSystem.ShapeModule shape = dust.shape;
        shape.enabled = false;
        ParticleSystem.ColorOverLifetimeModule col = dust.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.4f, 0.7f, 1f), 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = DotTexture() };
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.05f;
        r.lengthScale = 1.5f;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        dust.Play();
        return dust;
    }

    static void Zigzag(LineRenderer line, Vector3 a, Vector3 b, float amount)
    {
        int n = line.positionCount;
        Vector3 side = Vector3.Cross(Vector3.up, (b - a).normalized);
        for (int i = 0; i < n; i++)
        {
            float u = i / (float)(n - 1);
            float edge = Mathf.Sin(u * Mathf.PI); // 양 끝은 띠에 붙고 가운데가 많이 튐
            Vector3 offset = (side * Random.Range(-1f, 1f) + Vector3.up * Random.Range(-0.6f, 0.6f)) * amount * edge;
            line.SetPosition(i, Vector3.Lerp(a, b, u) + offset);
        }
    }

    // 줄기 질감: 가로(길이)로 흐르는 줄무늬 × 세로(폭)로 부드럽게 사라지는 가장자리
    static Material streakMaterial;
    static Material StreakMaterial()
    {
        if (streakMaterial != null) return streakMaterial;
        const int W = 256, H = 32;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        float seed = Random.value * 100f;
        for (int y = 0; y < H; y++)
        {
            float v = (y + 0.5f) / H * 2f - 1f;
            float edge = Mathf.Exp(-v * v * 4.5f);
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W;
                float streak = 0.45f + 0.55f * Mathf.PerlinNoise(seed + u * 6f, y * 0.45f);
                float a = edge * Mathf.Lerp(streak, 1f, Mathf.Exp(-v * v * 40f)); // 가운데 심은 줄무늬 없이 또렷하게
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();
        streakMaterial = new Material(Shader.Find("Sprites/Default")) { name = "DashStreak", mainTexture = tex };
        return streakMaterial;
    }

    static Texture2D dotTexture;
    static Texture2D DotTexture()
    {
        if (dotTexture != null) return dotTexture;
        const int N = 32;
        dotTexture = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                dotTexture.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        dotTexture.Apply();
        return dotTexture;
    }

    // ===== A. 칼자국 =====
    void OnPassed(Vector3 target, bool last)
    {
        if (!slashes) return;
        tiltSign = -tiltSign;
        // 핵심: 돌진 방향을 축으로 기울인 직선 → 적 몸을 비스듬히 가로지름 (번갈아 기울여 X자처럼 겹침)
        Vector3 axis = Quaternion.AngleAxis(slashTilt * tiltSign, dashDir) * Vector3.Cross(Vector3.up, dashDir);
        axis = Vector3.Lerp(axis, dashDir, 0.35f).normalized;
        Vector3 center = target + Vector3.up * slashHeight;

        var s = new Slash { born = Time.unscaledTime, a = center - axis * slashLength * 0.5f, b = center + axis * slashLength * 0.5f };
        s.glow = NewLine("SlashGlow", 2);
        s.core = NewLine("SlashCore", 2);
        s.glow.sharedMaterial = s.core.sharedMaterial = SoftMaterial();
        var taper = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0f));
        s.glow.widthCurve = s.core.widthCurve = taper;
        SetColor(s.glow, slashGlow, slashGlow);
        SetColor(s.core, slashCore, slashCore);
        Style(s, 1f, 1f);
        live.Add(s);
        StartCoroutine(Draw(s));
    }

    // 그어짐(0.05초) → 굵고 밝게 번쩍 → 0.12초에 기본 굵기로 가라앉음 + 가운데서 불티
    IEnumerator Draw(Slash s)
    {
        for (float t = 0f; t < slashDraw && s.core != null && !s.bursting; t += Time.unscaledDeltaTime)
        {
            SetEnds(s, s.a, Vector3.Lerp(s.a, s.b, t / slashDraw));
            yield return null;
        }
        if (s.core == null || s.bursting) yield break;
        SetEnds(s, s.a, s.b);
        Sparks(s, 8, 3f);
        const float settle = 0.12f;
        for (float t = 0f; t < settle && s.core != null && !s.bursting; t += Time.unscaledDeltaTime)
        {
            float k = 1f - t / settle;
            Style(s, 1f + 0.8f * k, 1f + 0.6f * k);
            yield return null;
        }
        if (s.core != null && !s.bursting) Style(s, 1f, 1f);
    }

    // 굵기 배율 · 밝기 배율을 한 번에
    void Style(Slash s, float width, float bright, float alpha = 1f)
    {
        s.core.widthMultiplier = slashCoreWidth * width;
        s.glow.widthMultiplier = slashGlowWidth * width;
        block ??= new MaterialPropertyBlock();
        block.SetColor(ColorId, new Color(slashBrightness * bright, slashBrightness * bright, slashBrightness * bright, alpha));
        s.core.SetPropertyBlock(block);
        block.SetColor(ColorId, new Color(glowBrightness * bright, glowBrightness * bright, glowBrightness * bright, alpha));
        s.glow.SetPropertyBlock(block);
    }

    static void SetEnds(Slash s, Vector3 a, Vector3 b)
    {
        s.core.SetPosition(0, a);
        s.core.SetPosition(1, b);
        s.glow.SetPosition(0, a);
        s.glow.SetPosition(1, b);
    }

    // 한꺼번에 베이는 순간: 먼저 그은 칼자국부터 burstStagger 간격으로 하나씩 터짐 (같은 프레임 여러 TargetCut은 한 번만)
    void OnCut(Vector3 position)
    {
        if (Time.frameCount == burstFrame) return;
        burstFrame = Time.frameCount;
        int order = 0;
        foreach (Slash s in live.ToArray())
            if (!s.bursting) StartCoroutine(Burst(s, true, order++ * burstStagger));
    }

    IEnumerator Burst(Slash s, bool flash, float delay = 0f)
    {
        s.bursting = true;
        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
        if (s.core == null) yield break;
        SetEnds(s, s.a, s.b);
        const float pop = 0.07f, shrink = 0.22f;
        if (flash)
        {
            Sparks(s, 16, 6f);
            for (float t = 0f; t < pop && s.core != null; t += Time.unscaledDeltaTime)
            {
                float k = t / pop;
                Style(s, Mathf.Lerp(1f, 2.6f, k), Mathf.Lerp(1f, 2.2f, k));
                yield return null;
            }
        }
        float width0 = flash ? 2.6f : 1f, bright0 = flash ? 2.2f : 1f;
        for (float t = 0f; t < shrink && s.core != null; t += Time.unscaledDeltaTime)
        {
            float k = t / shrink;
            Style(s, width0 * (1f - k * k), bright0, 1f - k);
            yield return null;
        }
        live.Remove(s);
        if (s.core != null) Destroy(s.core.gameObject);
        if (s.glow != null) Destroy(s.glow.gameObject);
    }

    // 칼자국을 따라 양쪽으로 튀는 불티 (경로 잔광과 같은 공용 파티클)
    void Sparks(Slash s, int count, float speed)
    {
        ParticleSystem ps = Dust();
        Vector3 axis = (s.b - s.a).normalized;
        var p = new ParticleSystem.EmitParams();
        for (int i = 0; i < count; i++)
        {
            float u = Random.value;
            p.position = Vector3.Lerp(s.a, s.b, 0.2f + 0.6f * u);
            p.velocity = axis * (u - 0.5f) * 2f * speed + Random.insideUnitSphere * speed * 0.3f;
            p.startLifetime = Random.Range(0.25f, 0.55f);
            p.startSize = Random.Range(0.03f, 0.07f);
            p.startColor = Color.Lerp(slashGlow, Color.white, 0.6f);
            ps.Emit(p, 1);
        }
    }

    // 폭 방향으로 부드럽게 사라지는 질감 (줄무늬 없음)
    static Material softMaterial;
    static Material SoftMaterial()
    {
        if (softMaterial != null) return softMaterial;
        const int W = 4, H = 32;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < H; y++)
        {
            float v = (y + 0.5f) / H * 2f - 1f;
            float a = Mathf.Exp(-v * v * 5f);
            for (int x = 0; x < W; x++) tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        softMaterial = new Material(Shader.Find("Sprites/Default")) { name = "DashSlashSoft", mainTexture = tex };
        return softMaterial;
    }

    // ===== 도우미 =====
    static LineRenderer NewLine(string name, int points)
    {
        var line = new GameObject(name).AddComponent<LineRenderer>();
        line.sharedMaterial = material;
        line.positionCount = points;
        line.useWorldSpace = true;
        line.alignment = LineAlignment.View;
        line.numCapVertices = 2;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        return line;
    }

    static void SetColor(LineRenderer line, Color start, Color end)
    {
        line.startColor = start;
        line.endColor = end;
    }

    void OnDestroy()
    {
        foreach (Slash s in live)
        {
            if (s.core != null) Destroy(s.core.gameObject);
            if (s.glow != null) Destroy(s.glow.gameObject);
        }
        live.Clear();
    }
}
