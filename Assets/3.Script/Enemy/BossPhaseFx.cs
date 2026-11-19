using UnityEngine;

// 보스 2페이즈 분위기: 안개 · 주변광 · 해가 2초에 걸쳐 붉고 어둡게, 보스 가슴께 붉은 빛 일렁임, 칼에 잔 번개
// Exit하면 2초에 걸쳐 원래 값으로 (즉시 = 연습장 재시작 · 비활성화). 원래 값은 Enter 때 저장
public class BossPhaseFx : MonoBehaviour
{
    static readonly Color FogTint = new Color(0.28f, 0.07f, 0.08f);
    static readonly Color AuraColor = new Color(1f, 0.3f, 0.35f);

    Transform blade;
    Light aura, sun;
    bool active, saved;
    float weight, nextArc;
    Color fog0, ambient0;
    float ambientIntensity0, sun0;

    public void Init(Transform bladeTransform) => blade = bladeTransform;

    public void Enter()
    {
        if (!saved)
        {
            fog0 = RenderSettings.fogColor;
            ambient0 = RenderSettings.ambientLight;
            ambientIntensity0 = RenderSettings.ambientIntensity;
            sun = RenderSettings.sun;
            if (sun == null)
                foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional) { sun = l; break; }
            sun0 = sun != null ? sun.intensity : 0f;
            saved = true;
        }
        active = true;
        if (aura == null)
        {
            var go = new GameObject("Phase2Aura");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * 1.4f;
            aura = go.AddComponent<Light>();
            aura.type = LightType.Point;
            aura.color = AuraColor;
            aura.range = 5f;
            aura.shadows = LightShadows.None;
        }
    }

    public void Exit(bool instant)
    {
        active = false;
        if (!instant) return;
        weight = 0f;
        Restore();
    }

    void Update()
    {
        if (!saved) return;
        weight = Mathf.MoveTowards(weight, active ? 1f : 0f, Time.deltaTime / 2f);
        Apply();
        if (aura != null) aura.intensity = weight * (2.2f + Mathf.PerlinNoise(Time.time * 7f, 0.3f) * 1.6f);
        if (!active && weight <= 0f)
        {
            Restore();
            return;
        }

        if (active && blade != null && Time.time >= nextArc) // 칼에 잔 번개
        {
            nextArc = Time.time + Random.Range(0.5f, 1.1f);
            Vector3 p = blade.position;
            ThunderBolt3D.Strike(p, p + Random.onUnitSphere * 0.9f, 0.25f, 0);
        }
    }

    void Apply()
    {
        float k = weight * weight * (3f - 2f * weight);
        RenderSettings.fogColor = Color.Lerp(fog0, fog0 * 0.4f + FogTint, k);
        RenderSettings.ambientLight = Color.Lerp(ambient0, ambient0 * 0.6f, k);
        RenderSettings.ambientIntensity = Mathf.Lerp(ambientIntensity0, ambientIntensity0 * 0.6f, k);
        if (sun != null) sun.intensity = Mathf.Lerp(sun0, sun0 * 0.55f, k);
    }

    void Restore()
    {
        if (!saved) return;
        RenderSettings.fogColor = fog0;
        RenderSettings.ambientLight = ambient0;
        RenderSettings.ambientIntensity = ambientIntensity0;
        if (sun != null) sun.intensity = sun0;
        saved = false;
        if (aura != null) aura.intensity = 0f;
    }

    void OnDestroy() => Restore();
}
