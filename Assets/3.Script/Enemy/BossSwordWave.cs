using UnityEngine;

// 보스 검기: 일직선으로 날아가며 닿은 플레이어에게 한 번 피해 (회피 무적이면 통과)
// [변경] 검기 패링: 닿기 직전 우클릭(PlayerParry가 IAttackThreat로 찾음) → 패링 연출 · 소리와 함께 검기를 되받아쳐 보스에게 날림
//  되받은 검기는 금빛 · 푸른색으로 바뀌고 보스를 살짝 따라가 맞으면 피해 + 체간. reflect를 끄면 그 자리에서 사라지기만 함. 땅 검기(팔방)는 패링 불가
// 적 시간(CombatTime)을 따라 느려짐 → 저스트 회피 슬로 중에도 맞춰 느려짐
// ground: 땅에 붙어 기어가는 검기 (2페이즈 팔방) — 땅에서 솟은 세로 날 · 지형 높이를 따라감 · 바닥에 붉은 균열 자국 · 흙먼지와 불티
// 흑뢰 색: 검기 입자를 진홍 ~ 검붉은 색으로 다시 칠하고, 뒤로 검은 연기 꼬리를 남김
public class BossSwordWave : MonoBehaviour, IAttackThreat
{
    public static readonly System.Collections.Generic.List<BossSwordWave> Active = new System.Collections.Generic.List<BossSwordWave>(); // [변경] PlayerParry가 검색
    const float ReflectSpeedScale = 1.35f;
    const float ReflectTurnSpeed = 2.5f; // 라디안/초 — 보스가 옆으로 움직여도 살짝 따라감
    static readonly Color ReflectA = new Color(2.6f, 2f, 0.6f);   // 금빛
    static readonly Color ReflectB = new Color(0.45f, 0.85f, 2.4f); // 푸른빛

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    const float GroundHalfWidth = 0.55f;  // 세로 날 판정 반폭
    const float GroundBladeScale = 0.85f;
    const float GroundBladeLift = 0.9f;   // 세로 날 가운데 높이 (아래 끝이 바닥)

    readonly Collider[] buffer = new Collider[16];
    Transform owner;
    DamageInfo info;
    float speed, range, halfWidth, traveled;
    bool touched, done, ground;
    ParticleSystem trail;
    static Material trailMaterial;
    GameObject hitVfx;
    Material ownMaterial;
    GameObject visual;
    // [변경] 패링 · 되받아치기
    bool parryable, reflect, reflected;
    float reflectDamage, reflectPosture;
    GameObject reflectedBy;
    Vector3 lastQuery;

    public static BossSwordWave Fire(GameObject owner, Vector3 position, Vector3 direction, float speed, float range, float width,
        DamageInfo info, GameObject visualPrefab, GameObject hitVfx, Material fallbackMaterial) =>
        Fire(owner, position, direction, speed, range, width, info, visualPrefab, hitVfx, fallbackMaterial, false);

    public static BossSwordWave Fire(GameObject owner, Vector3 position, Vector3 direction, float speed, float range, float width,
        DamageInfo info, GameObject visualPrefab, GameObject hitVfx, Material fallbackMaterial, bool ground) // [변경] 만든 검기를 돌려줌 (패링 설정용)
    {
        var go = new GameObject("BossSwordWave");
        go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction, Vector3.up));
        var wave = go.AddComponent<BossSwordWave>();
        wave.owner = owner != null ? owner.transform : null;
        wave.info = info;
        wave.speed = speed;
        wave.range = range;
        wave.halfWidth = ground ? Mathf.Min(width * 0.5f, GroundHalfWidth) : width * 0.5f; // 세로 날은 판정 폭도 좁게
        wave.hitVfx = hitVfx;
        wave.ground = ground;
        if (ground)
        {
            wave.SnapToGround();
            wave.trail = GroundTrail(go.transform, width);
            GroundScar(go.transform);
        }

        if (visualPrefab != null)
        {
            GameObject visual = Instantiate(visualPrefab, go.transform, false);
            wave.visual = visual;
            Recolor(visual);
            DarkTrail(go.transform, width);
            if (ground) // 가로 초승달을 90도 세워 바닥에서 솟은 세로 날로 (아래 끝이 땅에 닿게)
            {
                visual.transform.localRotation = Quaternion.Euler(0f, 0f, 90f) * visual.transform.localRotation;
                visual.transform.localScale *= GroundBladeScale;
                visual.transform.localPosition = Vector3.up * GroundBladeLift;
            }
            return wave;
        }

        // 기본 모양: 가로로 넓은 얇은 붉은 판
        GameObject blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(blade.GetComponent<Collider>());
        blade.transform.SetParent(go.transform, false);
        blade.transform.localScale = new Vector3(width, 0.08f, 0.35f);
        var r = blade.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        wave.ownMaterial = new Material(fallbackMaterial != null ? fallbackMaterial : BossTelegraph.RuntimeMaterial());
        wave.ownMaterial.SetColor(BaseColorId, new Color(1f, 0.2f, 0.15f, 0.8f));
        r.sharedMaterial = wave.ownMaterial;
        return wave;
    }

    // [변경] 패링 설정 (보스가 Fire 직후 호출). 땅 검기는 무시
    public BossSwordWave Parry(bool canParry, bool reflectBack, float damage, float posture)
    {
        parryable = canParry && !ground;
        reflect = reflectBack;
        reflectDamage = damage;
        reflectPosture = posture;
        return this;
    }

    void OnEnable() => Active.Add(this);
    void OnDisable() => Active.Remove(this);

    // ===== [변경] IAttackThreat: 플레이어 쪽으로 오는 검기가 닿기까지 남은 실제 시간 =====
    public bool IsParryable => parryable && !reflected && !done;

    float SecondsUntil(Vector3 p)
    {
        Vector3 to = p - transform.position;
        to.y = 0f;
        Vector3 fwd = transform.forward;
        fwd.y = 0f;
        fwd.Normalize();
        float along = Vector3.Dot(to, fwd);
        float lateral = (to - fwd * along).magnitude;
        if (lateral > halfWidth + 0.8f || along < -0.8f) return -1f; // 옆으로 비껴가거나 이미 지나감
        return Mathf.Max(0f, along - 0.6f) / Mathf.Max(0.01f, speed * CombatTime.EnemyScale);
    }

    public bool IsAboutToHit(Vector3 targetPosition, float earlyWindow, float lateWindow)
    {
        lastQuery = targetPosition;
        float t = SecondsUntil(targetPosition);
        return t >= 0f && t <= Mathf.Max(earlyWindow, 0.25f); // 검기는 칼보다 빨라 창을 조금 넉넉히
    }

    public float SecondsUntilHit() => SecondsUntil(lastQuery);

    public HitResult ReceiveParry(GameObject parrier)
    {
        if (!IsParryable) return HitResult.Ignored;
        if (reflect && owner != null) Reflect(parrier);
        else Finish();
        return HitResult.Countered;
    }

    // 핵심: 방향을 보스 쪽으로 뒤집고, 피해 대상을 보스로 바꿈 (플레이어는 더 이상 안 맞음)
    void Reflect(GameObject parrier)
    {
        reflected = true;
        reflectedBy = parrier;
        touched = false;
        Vector3 to = owner.position + Vector3.up * 1.1f - transform.position;
        to.y = 0f;
        transform.rotation = Quaternion.LookRotation(to.sqrMagnitude > 0.01f ? to.normalized : -transform.forward, Vector3.up);
        traveled = 0f;
        range = to.magnitude + 8f;
        speed *= ReflectSpeedScale;
        if (visual != null) Recolor(visual, ReflectA, ReflectB);
        Transform smoke = transform.Find("DarkTrail"); // 검은 연기 꼬리는 보스 검기 표시라 끔
        if (smoke != null) smoke.GetComponent<ParticleSystem>().Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    void Update()
    {
        if (done) return;

        float step = speed * Time.deltaTime * (reflected ? 1f : CombatTime.EnemyScale); // [변경] 되받은 검기는 플레이어 것 → 적 슬로와 무관
        if (reflected) Steer();
        if (!touched) { if (reflected) CheckReflectHit(step); else CheckHit(step); }

        transform.position += transform.forward * step;
        if (ground) SnapToGround();
        traveled += step;
        if (traveled >= range) Finish();
    }

    // [변경] 되받은 검기: 보스 쪽으로 살짝 꺾으며 날아감
    void Steer()
    {
        if (owner == null) return;
        Vector3 to = owner.position - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.01f) return;
        Vector3 dir = Vector3.RotateTowards(transform.forward, to.normalized, ReflectTurnSpeed * Time.deltaTime, 0f);
        transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
    }

    void CheckReflectHit(float step)
    {
        Vector3 center = transform.position + transform.forward * (step * 0.5f);
        Vector3 half = new Vector3(halfWidth, 1.2f, step * 0.5f + 0.5f);
        int count = Physics.OverlapBoxNonAlloc(center, half, buffer, transform.rotation, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider col = buffer[i];
            if (owner == null || !col.transform.IsChildOf(owner)) continue;
            var target = col.GetComponentInParent<IDamageable>();
            if (target == null || !target.CanBeTargeted) continue;

            touched = true;
            Vector3 point = col.ClosestPoint(center);
            target.ReceiveDamage(new DamageInfo
            {
                Attacker = reflectedBy,
                Damage = reflectDamage,
                PostureDamage = reflectPosture,
                Parryable = false,
                Weight = AttackWeight.Heavy,
                HitPoint = point,
                Direction = transform.forward
            });
            ThunderBolt3D.StrikeGold(point + Vector3.up * 12f, point, 0.9f, 2);
            if (hitVfx != null) Destroy(Instantiate(hitVfx, point, transform.rotation), 2f);
            Finish();
            return;
        }
    }

    // 이번 프레임에 지나갈 구간 전체를 상자로 검사 (빠른 속도에도 건너뛰지 않게)
    void CheckHit(float step)
    {
        Vector3 center = transform.position + transform.forward * (step * 0.5f) + (ground ? Vector3.up * GroundBladeLift : Vector3.zero); // 세로 날 높이에 맞춤
        Vector3 half = new Vector3(halfWidth, 1f, step * 0.5f + 0.4f);
        int count = Physics.OverlapBoxNonAlloc(center, half, buffer, transform.rotation, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider col = buffer[i];
            if (owner != null && col.transform.IsChildOf(owner)) continue;
            var player = col.GetComponentInParent<PlayerHealth>();
            if (player == null) continue;

            touched = true; // 피했든 맞았든 한 번만
            DamageInfo hit = info;
            hit.HitPoint = col.ClosestPoint(center);
            hit.Direction = transform.forward;
            if (player.ReceiveDamage(hit) == HitResult.Hit)
            {
                if (hitVfx != null) Destroy(Instantiate(hitVfx, hit.HitPoint, transform.rotation), 2f);
                Finish();
            }
            return;
        }
    }

    // 입자 색을 진홍 ~ 짙은 검붉은 색 사이로 (원래 알파는 유지). 더하기 합성이라 검정은 안 보이므로 검정은 연기 꼬리가 맡음
    static readonly Color Crimson = new Color(2.2f, 0.16f, 0.08f);
    static readonly Color DeepRed = new Color(0.55f, 0.02f, 0.03f);

    static void Recolor(GameObject visual) => Recolor(visual, Crimson, DeepRed);

    // [변경] 색 두 개를 받아 다시 칠함 (되받은 검기는 금빛 · 푸른빛)
    static void Recolor(GameObject visual, Color from, Color to)
    {
        foreach (ParticleSystem ps in visual.GetComponentsInChildren<ParticleSystem>(true))
        {
            ParticleSystem.MainModule main = ps.main;
            float a = main.startColor.color.a;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(from.r, from.g, from.b, a), new Color(to.r, to.g, to.b, a));
            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            if (col.enabled) col.color = new ParticleSystem.MinMaxGradient(Fade(col.color.gradient));
        }
        foreach (TrailRenderer t in visual.GetComponentsInChildren<TrailRenderer>(true))
        {
            t.startColor = new Color(from.r, from.g, from.b, t.startColor.a);
            t.endColor = new Color(0.05f, 0f, 0f, t.endColor.a);
        }
    }

    // 원래 그라데이션의 알파는 그대로, 색만 흰색으로 (시작 색 × 흰색 = 위에서 정한 붉은 색)
    static Gradient Fade(Gradient source)
    {
        var g = new Gradient();
        if (source == null) return g;
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, source.alphaKeys);
        return g;
    }

    // 검기 뒤로 남는 검은 연기 (반투명 합성 → 검정이 보임)
    static void DarkTrail(Transform parent, float width)
    {
        var go = new GameObject("DarkTrail");
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.02f, 0f, 0.01f, 0.75f), new Color(0.18f, 0.01f, 0.02f, 0.6f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 120;
        ParticleSystem.EmissionModule em = ps.emission;
        em.rateOverTime = 0f;
        em.rateOverDistance = 5f;
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(width * 0.8f, 0.4f, 0.2f);
        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.4f));
        var r = go.GetComponent<ParticleSystemRenderer>();
        if (smokeMaterial == null) smokeMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = SoftDot() };
        r.sharedMaterial = smokeMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();
    }

    static Material smokeMaterial, scarMaterial;

    // 세로 날이 지나간 바닥에 남는 붉은 균열 (눕힌 TrailRenderer)
    static void GroundScar(Transform parent)
    {
        var go = new GameObject("GroundScar");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.up * 0.03f;
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        var t = go.AddComponent<TrailRenderer>();
        t.alignment = LineAlignment.TransformZ;
        t.time = 0.7f;
        t.minVertexDistance = 0.25f;
        t.widthCurve = new AnimationCurve(new Keyframe(0f, 0.32f), new Keyframe(1f, 0.05f));
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(new Color(2.4f, 0.2f, 0.1f), 0f), new GradientColorKey(new Color(0.08f, 0f, 0.01f), 1f) },
                  new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0f, 1f) });
        t.colorGradient = g;
        if (scarMaterial == null) scarMaterial = new Material(Shader.Find("Sprites/Default"));
        t.sharedMaterial = scarMaterial;
        t.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    static Texture2D SoftDot()
    {
        const int N = 32;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        tex.Apply();
        return tex;
    }

    // 지형(플레이어 · 적이 아닌 것) 바로 위로
    void SnapToGround()
    {
        Vector3 p = transform.position;
        foreach (RaycastHit hit in Physics.RaycastAll(p + Vector3.up * 2f, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.GetComponentInParent<IDamageable>() != null) continue;
            transform.position = new Vector3(p.x, hit.point.y + 0.12f, p.z);
            return;
        }
    }

    // 지나간 자리에서 튀는 흙먼지 · 붉은 불티 (월드 공간, 이동 거리마다 방출)
    static ParticleSystem GroundTrail(Transform parent, float width)
    {
        var go = new GameObject("GroundTrail");
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.16f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(2f, 0.15f, 0.08f), new Color(0.12f, 0.02f, 0.02f, 0.85f)); // 붉은 불티 + 검은 흙
        main.gravityModifier = 1.6f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 160;
        ParticleSystem.EmissionModule em = ps.emission;
        em.rateOverTime = 0f;
        em.rateOverDistance = 9f;
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(Mathf.Min(width, 0.6f), 0.05f, 0.2f); // 세로 날 밑동에서만
        shape.rotation = new Vector3(-60f, 0f, 0f); // 위 · 뒤로 튐
        var r = go.GetComponent<ParticleSystemRenderer>();
        // 질감 없는 재질이라 입자가 네모로 보였음 → 둥근 점 질감 + 움직이는 방향으로 늘인 불똥
        if (trailMaterial == null) trailMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = SoftDot() };
        r.sharedMaterial = trailMaterial;
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.04f;
        r.lengthScale = 1.2f;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();
        return ps;
    }

    // 파티클은 방출만 멈추고 남은 입자가 사라진 뒤 삭제
    void Finish()
    {
        done = true;
        foreach (ParticleSystem ps in GetComponentsInChildren<ParticleSystem>())
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
            if (r is TrailRenderer t) t.emitting = false; // 바닥 균열은 남은 만큼 흐려지며 사라짐
            else if (!(r is ParticleSystemRenderer)) r.enabled = false;
        Destroy(gameObject, 1.2f);
    }

    void OnDestroy()
    {
        if (ownMaterial != null) Destroy(ownMaterial);
    }
}
