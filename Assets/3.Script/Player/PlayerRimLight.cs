using UnityEngine;

// 캐릭터 보조광: 플레이어만 비추는 앞쪽 보조광(그늘에서도 얼굴 · 옷이 보이게) + 뒤쪽 테두리 빛(실루엣)
// URP 렌더링 레이어로 플레이어 렌더러에만 닿게 함 → 배경 밝기는 그대로. 위치는 카메라 기준으로 매 프레임 따라감
public class PlayerRimLight : MonoBehaviour
{
    [SerializeField, Range(1, 31)] int renderingLayer = 1;
    [SerializeField] float height = 1.3f;
    [SerializeField] Color fillColor = new Color(0.92f, 0.88f, 0.82f);
    [SerializeField] float fillIntensity = 0.9f;
    [SerializeField] float fillRange = 4f;
    [SerializeField] Color rimColor = new Color(0.55f, 0.72f, 1f);
    [SerializeField] float rimIntensity = 2.2f;
    [SerializeField] float rimRange = 4.5f;

    Light fill, rim;
    uint mask;

    void Start()
    {
        mask = 1u << renderingLayer;
        Tag();
        fill = MakeLight("PlayerFillLight", fillColor, fillIntensity, fillRange);
        rim = MakeLight("PlayerRimLight", rimColor, rimIntensity, rimRange);
    }

    // 플레이어 렌더러에 레이어 추가 (새로 생긴 칼 · 칼집 같은 것도 잡게 가끔 다시)
    void Tag()
    {
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
            r.renderingLayerMask |= mask;
        }
    }

    Light MakeLight(string name, Color color, float intensity, float range)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        Light light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.intensity = intensity;
        light.range = range;
        light.shadows = LightShadows.None;
        light.renderingLayerMask = (int)mask; // 플레이어 렌더러에만
        return light;
    }

    void LateUpdate()
    {
        if (fill == null) return;
        if (Time.frameCount % 120 == 0) Tag();
        Camera cam = Camera.main;
        Vector3 forward = cam != null ? cam.transform.forward : transform.forward;
        forward.y = 0f;
        forward = forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 chest = transform.position + Vector3.up * height;
        fill.transform.position = chest - forward * 1.6f + right * 0.6f + Vector3.up * 0.5f; // 카메라 쪽 · 살짝 옆 위
        rim.transform.position = chest + forward * 1.3f - right * 0.5f + Vector3.up * 0.9f;  // 캐릭터 뒤 위
    }
}
