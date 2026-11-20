using UnityEngine;

// 뇌신참 검기의 바닥 연출 (EnchantWave가 만듦, 다 되면 스스로 사라짐)
//  Ring: 발사 · 끝 자리에서 빠르게 퍼지며 얇아지는 충격파 고리
//  FadeOut: 지나간 바닥의 빛 자국을 잠시 유지했다가 흐리며 지움
public class WaveFx : MonoBehaviour
{
    const int RingSegments = 48;
    const float RingLife = 0.35f;
    static Material material;

    LineRenderer line;
    bool ring;
    float radius, hold, fade, t;
    Color baseColor, ringColor = new Color(0.6f, 0.9f, 2.2f, 1f); // [변경] 고리 색 (뇌신참 흑뢰 배색이면 붉게)

    public static void Ring(Vector3 center, float radius) => Ring(center, radius, new Color(0.6f, 0.9f, 2.2f, 1f));

    public static void Ring(Vector3 center, float radius, Color color) // [변경]
    {
        var go = new GameObject("WaveShock");
        go.transform.position = center;
        var fx = go.AddComponent<WaveFx>();
        fx.ring = true;
        fx.radius = radius;
        fx.ringColor = color;
        fx.line = go.AddComponent<LineRenderer>();
        fx.line.sharedMaterial = Material();
        fx.line.useWorldSpace = false;
        fx.line.loop = true;
        fx.line.positionCount = RingSegments;
        fx.line.alignment = LineAlignment.TransformZ; // 바닥에 눕힘
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        fx.line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        fx.SetRing(0f);
    }

    public static void FadeOut(LineRenderer target, float holdSeconds, float fadeSeconds)
    {
        var fx = target.gameObject.AddComponent<WaveFx>();
        fx.line = target;
        fx.hold = holdSeconds;
        fx.fade = Mathf.Max(0.01f, fadeSeconds);
        fx.baseColor = target.startColor;
    }

    void Update()
    {
        t += Time.deltaTime;
        if (ring)
        {
            if (t >= RingLife) { Destroy(gameObject); return; }
            SetRing(t / RingLife);
            return;
        }
        if (t >= hold + fade) { Destroy(gameObject); return; }
        float a = t < hold ? 1f : 1f - (t - hold) / fade;
        line.startColor = line.endColor = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * a);
    }

    // 로컬 XY 평면에 원 (오브젝트를 X축 90도로 눕혀 바닥에 깔림)
    void SetRing(float k)
    {
        float r = radius * (1f - (1f - k) * (1f - k)); // 빠르게 퍼지다 느려짐
        for (int i = 0; i < RingSegments; i++)
        {
            float a = i * Mathf.PI * 2f / RingSegments;
            line.SetPosition(i, new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f));
        }
        line.widthMultiplier = Mathf.Lerp(0.6f, 0.05f, k);
        line.startColor = line.endColor = new Color(ringColor.r, ringColor.g, ringColor.b, 1f - k);
    }

    static Material Material()
    {
        if (material == null) material = new Material(Shader.Find("Sprites/Default")) { name = "WaveFx" };
        return material;
    }
}
