using UnityEngine;
using UnityEngine.InputSystem;

// 화톳불(체크포인트): 가까이에서 E → 체력 회복 + 스킬트리 열기
// 불빛은 실제 시간으로 일렁임 (일시정지 중에도 살아 있게). 생성·배치는 Tools/Setup/Skill Tree + Shrines
public class Shrine : MonoBehaviour
{
    [SerializeField] float interactRange = 2.6f;
    [SerializeField] Light fireLight;
    [SerializeField] float flickerAmount = 0.25f;
    [SerializeField] float flickerSpeed = 7f;
    [SerializeField] bool logRest;

    // 한눈에 휴식처로 보이게: 위로 오르는 불티 · 바닥의 따뜻한 빛 고리 (실행 중에 만듦) // 머리 위 이름 표시 제거
    static readonly Color Warm = new Color(1f, 0.62f, 0.28f);

    Transform player;
    PlayerHealth health;
    float baseIntensity;
    bool inRange;
    float seed;

    void Awake()
    {
        if (fireLight != null) baseIntensity = fireLight.intensity;
        seed = Random.value * 100f;
        BuildLook();
    }

    void Start()
    {
        health = FindAnyObjectByType<PlayerHealth>();
        if (health != null) player = health.transform;
    }

    void OnDisable()
    {
        if (SkillTreeMenu.Instance != null) SkillTreeMenu.Instance.SetPrompt(this, false);
    }

    void Update()
    {
        if (fireLight != null)
        {
            float t = Time.unscaledTime * flickerSpeed + seed;
            float n = Mathf.PerlinNoise(t, seed) * 2f - 1f;
            fireLight.intensity = baseIntensity * (1f + n * flickerAmount);
        }

        SkillTreeMenu menu = SkillTreeMenu.Instance;
        if (player == null || menu == null) return;

        Vector3 d = player.position - transform.position;
        d.y = 0f;
        bool close = d.sqrMagnitude <= interactRange * interactRange && (health == null || !health.IsDead);
        bool fighting = ZoneArena.Fighting != null; // [변경] 전투 중(시련 포함)엔 쉴 수 없음
        bool near = close && !fighting;
        if (near != inRange)
        {
            inRange = near;
            menu.SetPrompt(this, near);
        }

        if (!close || GamePause.IsPaused || menu.IsOpen) return;
        Keyboard kb = Keyboard.current;
        if (kb == null || !kb.eKey.wasPressedThisFrame) return;
        if (fighting) // [변경] 눌러도 회복 · 스킬트리 없이 안내만
        {
            DamageEvents.RaiseLabel(transform.position + Vector3.up * 2.2f, "전투 중에는 쉴 수 없다", new Color(1f, 0.6f, 0.4f));
            return;
        }

        // 핵심: 휴식 = 체력 회복 → 스킬트리
        if (health != null) health.HealFull();
        menu.Open();
        if (logRest) Debug.Log($"[Shrine] {name}에서 휴식");
    }

    // ===== 휴식처 표시 =====
    void BuildLook()
    {
        var mat = new Material(Shader.Find("Sprites/Default"));

        // 위로 오르는 불티
        var ps = new GameObject("Embers").AddComponent<ParticleSystem>();
        ps.transform.SetParent(transform, false);
        ps.transform.localPosition = new Vector3(0f, 1.5f, 0f);
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.45f, 0.15f), new Color(1f, 0.8f, 0.4f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.useUnscaledTime = true;
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 12f;
        shape.radius = 0.15f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        ParticleSystem.NoiseModule noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.4f;
        noise.frequency = 0.8f;
        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        ParticleSystem.EmissionModule em = ps.emission;
        em.rateOverTime = 14f;
        var pr = ps.GetComponent<ParticleSystemRenderer>();
        pr.sharedMaterial = mat;
        pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();

        // 바닥의 빛 고리 (휴식 범위)
        var ring = new GameObject("RestRing").AddComponent<LineRenderer>();
        ring.transform.SetParent(transform, false);
        ring.sharedMaterial = mat;
        ring.useWorldSpace = false;
        ring.loop = true;
        ring.widthMultiplier = 0.06f;
        ring.startColor = ring.endColor = new Color(Warm.r, Warm.g, Warm.b, 0.55f);
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        const int Segments = 48;
        ring.positionCount = Segments;
        for (int i = 0; i < Segments; i++)
        {
            float a = i * Mathf.PI * 2f / Segments;
            ring.SetPosition(i, new Vector3(Mathf.Cos(a) * interactRange, 0.06f, Mathf.Sin(a) * interactRange));
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.75f, 0.3f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, interactRange);
    }
}
