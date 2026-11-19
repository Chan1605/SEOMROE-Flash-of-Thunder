using UnityEngine;

// 마을 허수아비 (짚 샌드백): 맞으면 맞은 방향으로 기울었다가 출렁이며 돌아오고 짚 부스러기가 튐. 쓰러지거나 부서지지 않음
// 기둥 밑동(pivot)을 축으로 기울어짐 → 땅에 박힌 말뚝처럼 보임
public class StrawDummy : MonoBehaviour, IDamageable
{
    [SerializeField] Transform pivot;
    [SerializeField] float lightPush = 55f;   // 맞을 때 기우는 속도 (도/초)
    [SerializeField] float heavyPush = 110f;
    [SerializeField] float maxTilt = 22f;
    [SerializeField] float stiffness = 70f;   // 되돌아오는 힘
    [SerializeField] float damping = 5f;      // 출렁임이 잦아드는 빠르기
    [SerializeField] Color strawColor = new Color(0.78f, 0.66f, 0.38f);

    Vector3 tilt, velocity; // 수평 방향 벡터 × 기운 각도
    ParticleSystem straw;

    public bool CanBeTargeted => true;

    void Start() => straw = MakeStraw();

    public HitResult ReceiveDamage(DamageInfo info)
    {
        Vector3 dir = info.Direction;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f && info.Attacker != null) dir = transform.position - info.Attacker.transform.position;
        dir.y = 0f;
        dir = dir.sqrMagnitude > 0.001f ? dir.normalized : transform.forward;

        bool heavy = info.Weight != AttackWeight.Light;
        velocity += dir * (heavy ? heavyPush : lightPush);
        Vector3 at = info.HitPoint != Vector3.zero ? info.HitPoint : transform.position + Vector3.up * 1.2f;
        if (straw != null)
        {
            straw.transform.position = at;
            straw.transform.rotation = Quaternion.LookRotation(dir + Vector3.up * 0.4f);
            straw.Emit(heavy ? 26 : 14);
        }
        return HitResult.Hit;
    }

    // 핵심: 기울기를 용수철처럼 (기울기 쪽으로 당겨 돌아오고 감쇠) → 맞은 뒤 두세 번 흔들리다 멈춤
    void Update()
    {
        if (pivot == null) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        velocity += (-stiffness * tilt - damping * velocity) * dt;
        tilt += velocity * dt;
        if (tilt.magnitude > maxTilt) { tilt = tilt.normalized * maxTilt; velocity *= 0.5f; }

        float angle = tilt.magnitude;
        pivot.rotation = angle > 0.01f
            ? Quaternion.AngleAxis(angle, Vector3.Cross(Vector3.up, tilt / angle)) * transform.rotation
            : transform.rotation;
    }

    ParticleSystem MakeStraw()
    {
        var go = new GameObject("StrawBits");
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
        main.startSize3D = true;
        main.startSizeX = new ParticleSystem.MinMaxCurve(0.012f, 0.02f);
        main.startSizeY = new ParticleSystem.MinMaxCurve(0.08f, 0.16f); // 가늘고 긴 짚 조각
        main.startSizeZ = 1f;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(strawColor, strawColor * 0.75f);
        main.gravityModifier = 0.8f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 120;
        var emission = ps.emission;
        emission.enabled = false;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 40f;
        shape.radius = 0.1f;
        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();
        return ps;
    }
}
