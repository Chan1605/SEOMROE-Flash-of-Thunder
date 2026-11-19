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
        bool fighting = ZoneArena.Fighting != null; // 전투 중(시련 포함)엔 쉴 수 없음
        bool near = close && !fighting;
        if (near != inRange)
        {
            inRange = near;
            menu.SetPrompt(this, near);
        }

        if (!close || GamePause.IsPaused || menu.IsOpen) return;
        Keyboard kb = Keyboard.current;
        if (kb == null || !kb.eKey.wasPressedThisFrame) return;
        if (fighting) // 눌러도 회복 · 스킬트리 없이 안내만
        {
            DamageEvents.RaiseLabel(transform.position + Vector3.up * 2.2f, "전투 중에는 쉴 수 없다", new Color(1f, 0.6f, 0.4f));
            return;
        }

        // 핵심: 휴식 = 체력 회복 → 스킬트리
        if (health != null) health.HealFull();
        menu.Open();
        StartCoroutine(SaveWhenClosed()); // [변경] 스킬트리를 닫으면 자동 저장
        if (logRest) Debug.Log($"[Shrine] {name}에서 휴식");
    }

    // 수련장 · 연습장 화톳불은 저장하지 않음 (불러오기는 늘 수련을 마친 마을부터)
    System.Collections.IEnumerator SaveWhenClosed()
    {
        while (SkillTreeMenu.Instance != null && SkillTreeMenu.Instance.IsOpen) yield return null;
        if (FindAnyObjectByType<TutorialDirector>() == null && FindAnyObjectByType<TrainingMode>() == null) SaveGame.Save();
    }

    // ===== 휴식처 표시 =====
    void BuildLook()
    {
        var mat = new Material(Shader.Find("Sprites/Default"));
        var sparkMat = new Material(mat) { mainTexture = SparkTexture() }; // 네모 → 둥근 빛 점

        // 위로 오르는 불티: 움직이는 방향으로 살짝 늘어난 둥근 점 (불똥 꼬리)
        var ps = new GameObject("Embers").AddComponent<ParticleSystem>();
        ps.transform.SetParent(transform, false);
        ps.transform.localPosition = new Vector3(0f, 1.55f, 0f);
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.055f);
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
        pr.sharedMaterial = sparkMat;
        pr.renderMode = ParticleSystemRenderMode.Stretch;
        pr.velocityScale = 0.06f;
        pr.lengthScale = 1.5f;
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

    // 가운데가 밝고 가장자리로 부드럽게 사라지는 원 (모든 화톳불이 하나를 같이 씀)
    static Texture2D spark;

    static Texture2D SparkTexture()
    {
        if (spark != null) return spark;
        const int N = 32;
        spark = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Spark" };
        var pixels = new Color32[N * N];
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            float d = new Vector2(x - (N - 1) * 0.5f, y - (N - 1) * 0.5f).magnitude / (N * 0.5f);
            float a = Mathf.Pow(Mathf.Clamp01(1f - d), 2f);
            pixels[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
        }
        spark.SetPixels32(pixels);
        spark.Apply();
        return spark;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.75f, 0.3f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, interactRange);
    }
}
