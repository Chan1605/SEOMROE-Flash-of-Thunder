using UnityEngine;

// 보스 검기: 일직선으로 날아가며 닿은 플레이어에게 한 번 피해 (패링 불가, 회피 무적이면 통과)
// 적 시간(CombatTime)을 따라 느려짐 → 저스트 회피 슬로 중에도 맞춰 느려짐
// ground: 땅에 붙어 기어가는 검기 (2페이즈 팔방) — 납작하게 눌린 모양 · 지형 높이를 따라감 · 뒤로 흙먼지와 불티가 튐
// [변경] 흑뢰 색: 검기 입자를 진홍 ~ 검붉은 색으로 다시 칠하고, 뒤로 검은 연기 꼬리를 남김
public class BossSwordWave : MonoBehaviour
{
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    readonly Collider[] buffer = new Collider[16];
    Transform owner;
    DamageInfo info;
    float speed, range, halfWidth, traveled;
    bool touched, done, ground;
    ParticleSystem trail;
    static Material trailMaterial;
    GameObject hitVfx;
    Material ownMaterial;

    public static void Fire(GameObject owner, Vector3 position, Vector3 direction, float speed, float range, float width,
        DamageInfo info, GameObject visualPrefab, GameObject hitVfx, Material fallbackMaterial) =>
        Fire(owner, position, direction, speed, range, width, info, visualPrefab, hitVfx, fallbackMaterial, false);

    public static void Fire(GameObject owner, Vector3 position, Vector3 direction, float speed, float range, float width,
        DamageInfo info, GameObject visualPrefab, GameObject hitVfx, Material fallbackMaterial, bool ground)
    {
        var go = new GameObject("BossSwordWave");
        go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction, Vector3.up));
        var wave = go.AddComponent<BossSwordWave>();
        wave.owner = owner != null ? owner.transform : null;
        wave.info = info;
        wave.speed = speed;
        wave.range = range;
        wave.halfWidth = width * 0.5f;
        wave.hitVfx = hitVfx;
        wave.ground = ground;
        if (ground)
        {
            wave.SnapToGround();
            wave.trail = GroundTrail(go.transform, width);
        }

        if (visualPrefab != null)
        {
            GameObject visual = Instantiate(visualPrefab, go.transform, false);
            Recolor(visual); // [변경]
            DarkTrail(go.transform, width);
            if (ground) // 납작하게 눌러 바닥에 붙임
            {
                visual.transform.localPosition = Vector3.up * 0.25f;
                visual.transform.localScale = Vector3.Scale(visual.transform.localScale, new Vector3(1.15f, 0.35f, 1f));
            }
            return;
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
    }

    void Update()
    {
        if (done) return;

        float step = speed * Time.deltaTime * CombatTime.EnemyScale;
        if (!touched) CheckHit(step);

        transform.position += transform.forward * step;
        if (ground) SnapToGround();
        traveled += step;
        if (traveled >= range) Finish();
    }

    // 이번 프레임에 지나갈 구간 전체를 상자로 검사 (빠른 속도에도 건너뛰지 않게)
    void CheckHit(float step)
    {
        Vector3 center = transform.position + transform.forward * (step * 0.5f);
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

    // [변경] 입자 색을 진홍 ~ 짙은 검붉은 색 사이로 (원래 알파는 유지). 더하기 합성이라 검정은 안 보이므로 검정은 연기 꼬리가 맡음
    static readonly Color Crimson = new Color(2.2f, 0.16f, 0.08f);
    static readonly Color DeepRed = new Color(0.55f, 0.02f, 0.03f);

    static void Recolor(GameObject visual)
    {
        foreach (ParticleSystem ps in visual.GetComponentsInChildren<ParticleSystem>(true))
        {
            ParticleSystem.MainModule main = ps.main;
            float a = main.startColor.color.a;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(Crimson.r, Crimson.g, Crimson.b, a), new Color(DeepRed.r, DeepRed.g, DeepRed.b, a));
            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            if (col.enabled) col.color = new ParticleSystem.MinMaxGradient(Fade(col.color.gradient));
        }
        foreach (TrailRenderer t in visual.GetComponentsInChildren<TrailRenderer>(true))
        {
            t.startColor = new Color(Crimson.r, Crimson.g, Crimson.b, t.startColor.a);
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

    static Material smokeMaterial;

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
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(2f, 0.15f, 0.08f), new Color(0.12f, 0.02f, 0.02f, 0.85f)); // [변경] 붉은 불티 + 검은 흙
        main.gravityModifier = 1.6f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 160;
        ParticleSystem.EmissionModule em = ps.emission;
        em.rateOverTime = 0f;
        em.rateOverDistance = 9f;
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(width, 0.05f, 0.2f);
        shape.rotation = new Vector3(-60f, 0f, 0f); // 위 · 뒤로 튐
        var r = go.GetComponent<ParticleSystemRenderer>();
        if (trailMaterial == null) trailMaterial = new Material(Shader.Find("Sprites/Default"));
        r.sharedMaterial = trailMaterial;
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
            if (!(r is ParticleSystemRenderer)) r.enabled = false;
        Destroy(gameObject, 1.2f);
    }

    void OnDestroy()
    {
        if (ownMaterial != null) Destroy(ownMaterial);
    }
}
