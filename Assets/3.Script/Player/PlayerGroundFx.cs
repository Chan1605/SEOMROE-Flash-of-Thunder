using UnityEngine;

// 발밑 효과: 달릴 때 · 회피 · 착지 때 흙먼지, 연못 안이면 물방울 + 수면 고리
// 발 디딤은 PlayerFootsteps.Planted, 회피는 PlayerDodge.Dodged. 먼지 색은 씬 주변광에 맞춰 어둡게(밤에 빛나 보이지 않게)
public class PlayerGroundFx : MonoBehaviour
{
    [SerializeField] Material dustMaterial;
    [SerializeField] Material splashMaterial;
    [SerializeField] Color dustColor = new Color(0.55f, 0.5f, 0.42f, 0.4f);
    [SerializeField] Color splashColor = new Color(0.75f, 0.85f, 0.9f, 0.7f);
    [SerializeField] float stepDustMinPace = 0.55f; // 이 빠르기(0~1) 이상 걸음에만 먼지
    [SerializeField] int stepDust = 3;
    [SerializeField] int dodgeDust = 12;
    [SerializeField] int landDust = 10;
    [SerializeField] float landAirTime = 0.35f;

    ParticleSystem dust, drops, ripples;
    // [변경] 물속을 걸을 때 다리 둘레로 계속 퍼지는 물결 + 앞으로 차이는 물방울
    [SerializeField] float wakeInterval = 0.16f;
    float wakeTimer, moveSpeed;
    Vector3 lastPosition;
    PlayerFootsteps footsteps;
    PlayerLocomotion locomotion;
    float airTime;

    void Start()
    {
        locomotion = GetComponent<PlayerLocomotion>();
        PlayerDodge dodge = GetComponent<PlayerDodge>();
        if (dodge != null) dodge.Dodged += OnDodged;
        dust = MakeSystem("Fx_Dust", dustMaterial, ParticleSystemRenderMode.Billboard);
        drops = MakeSystem("Fx_WaterDrops", splashMaterial, ParticleSystemRenderMode.Stretch);
        ripples = MakeSystem("Fx_Ripples", splashMaterial != null ? new Material(splashMaterial) { mainTexture = RingTexture() } : null, ParticleSystemRenderMode.HorizontalBillboard);

        var dm = dust.main;
        dm.gravityModifier = -0.02f;
        var size = dust.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.8f)));
        FadeOut(dust);

        var drm = drops.main;
        drm.gravityModifier = 1.6f;
        var dr = drops.GetComponent<ParticleSystemRenderer>();
        dr.velocityScale = 0.05f;
        dr.lengthScale = 1.2f;
        FadeOut(drops);

        var rs = ripples.sizeOverLifetime;
        rs.enabled = true;
        rs.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.2f), new Keyframe(1f, 1f)));
        FadeOut(ripples);
    }

    void OnDestroy()
    {
        PlayerDodge dodge = GetComponent<PlayerDodge>();
        if (dodge != null) dodge.Dodged -= OnDodged;
        if (footsteps != null) footsteps.Planted -= OnPlanted;
    }

    void Update()
    {
        if (footsteps == null) // PlayerSfx가 실행 중에 붙임
        {
            footsteps = GetComponent<PlayerFootsteps>();
            if (footsteps != null) footsteps.Planted += OnPlanted;
        }

        Vector3 delta = transform.position - lastPosition;
        delta.y = 0f;
        lastPosition = transform.position;
        if (Time.deltaTime > 0f) moveSpeed = Mathf.Lerp(moveSpeed, delta.magnitude / Time.deltaTime, Mathf.Clamp01(Time.deltaTime * 10f));
        Wake(delta);

        if (locomotion == null) return;
        if (!locomotion.IsGrounded) airTime += Time.deltaTime;
        else
        {
            if (airTime > landAirTime) Burst(transform.position, landDust, 1.6f, Vector3.zero);
            airTime = 0f;
        }
    }

    void Wake(Vector3 delta)
    {
        if (moveSpeed < 0.6f || !PlanarWaterReflection.FindWater(transform.position, out float waterY)) { wakeTimer = 0f; return; }
        wakeTimer -= Time.deltaTime;
        if (wakeTimer > 0f) return;
        float k = Mathf.Clamp01(moveSpeed / 6f);
        wakeTimer = wakeInterval * Mathf.Lerp(1.4f, 0.8f, k);
        Vector3 p = new Vector3(transform.position.x, waterY + 0.02f, transform.position.z);
        Color c = splashColor;
        c.a *= Mathf.Lerp(0.45f, 0.75f, k);
        Emit(ripples, p + Random.insideUnitSphere * 0.15f, Vector3.zero, Mathf.Lerp(0.9f, 1.5f, k), 1.1f, c);
        Vector3 forward = delta.sqrMagnitude > 1e-6f ? delta.normalized : transform.forward;
        for (int i = 0; i < Mathf.RoundToInt(Mathf.Lerp(1f, 4f, k)); i++)
        {
            Vector3 v = forward * Random.Range(0.8f, 2f) * k + Random.insideUnitSphere * 0.5f;
            v.y = Random.Range(0.8f, 1.8f) * (0.5f + k);
            Emit(drops, p + forward * 0.3f, v, Random.Range(0.025f, 0.05f), Random.Range(0.3f, 0.5f), splashColor);
        }
    }

    void OnPlanted(Vector3 foot, float pace)
    {
        if (pace < stepDustMinPace && !PlanarWaterReflection.FindWater(foot, out _)) return;
        Burst(foot, Mathf.Max(1, Mathf.RoundToInt(stepDust * pace)), 0.6f, Vector3.zero);
    }

    void OnDodged(Vector3 direction) => Burst(transform.position, dodgeDust, 2.2f, -direction);

    // 같은 자리에서 물 위면 물방울 + 고리, 아니면 흙먼지
    void Burst(Vector3 at, int count, float strength, Vector3 push)
    {
        if (PlanarWaterReflection.FindWater(at, out float waterY))
        {
            Vector3 p = new Vector3(at.x, waterY + 0.02f, at.z);
            for (int i = 0; i < count * 2; i++)
            {
                Vector3 v = Random.insideUnitSphere * 1.2f * strength;
                v.y = Random.Range(1.5f, 3f) * Mathf.Sqrt(strength);
                Emit(drops, p, v + push * 1.5f, Random.Range(0.03f, 0.06f), Random.Range(0.35f, 0.6f), splashColor);
            }
            Emit(ripples, p, Vector3.zero, Random.Range(0.9f, 1.3f) * Mathf.Sqrt(strength), 0.9f, splashColor);
            return;
        }

        Color c = dustColor * AmbientLight();
        c.a = dustColor.a;
        for (int i = 0; i < count; i++)
        {
            Vector3 v = Random.insideUnitSphere * 0.6f * strength;
            v.y = Mathf.Abs(v.y) * 0.4f + 0.15f;
            Vector3 p = at + new Vector3(Random.Range(-0.2f, 0.2f), 0.08f, Random.Range(-0.2f, 0.2f));
            Emit(dust, p, v + push * strength * 0.8f, Random.Range(0.25f, 0.5f) * Mathf.Sqrt(strength), Random.Range(0.7f, 1.2f), c);
        }
    }

    static float AmbientLight()
    {
        Color a = RenderSettings.ambientSkyColor * RenderSettings.ambientIntensity;
        return Mathf.Clamp(a.grayscale * 1.8f, 0.25f, 1f);
    }

    static void Emit(ParticleSystem ps, Vector3 position, Vector3 velocity, float size, float lifetime, Color color)
    {
        if (ps == null) return;
        var e = new ParticleSystem.EmitParams
        {
            position = position,
            velocity = velocity,
            startSize = size,
            startLifetime = lifetime,
            startColor = color,
            rotation = Random.Range(0f, 360f),
            applyShapeToPosition = false,
        };
        ps.Emit(e, 1);
    }

    ParticleSystem MakeSystem(string name, Material material, ParticleSystemRenderMode mode)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 200;
        var emission = ps.emission;
        emission.enabled = false;
        var shape = ps.shape;
        shape.enabled = false;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = mode;
        r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        ps.Play();
        return ps;
    }

    static void FadeOut(ParticleSystem ps)
    {
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
    }

    // 수면 고리 (가운데 비고 테두리만 밝은 원)
    static Texture2D ring;

    static Texture2D RingTexture()
    {
        if (ring != null) return ring;
        const int N = 64;
        ring = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Ripple" };
        var pixels = new Color32[N * N];
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            float d = new Vector2(x - (N - 1) * 0.5f, y - (N - 1) * 0.5f).magnitude / (N * 0.5f);
            float a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.85f) / 0.1f);
            pixels[y * N + x] = new Color32(255, 255, 255, (byte)(a * a * 255f));
        }
        ring.SetPixels32(pixels);
        ring.Apply();
        return ring;
    }
}
