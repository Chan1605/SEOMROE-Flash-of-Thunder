using UnityEngine;

// 소용돌이 포탈 면 (SG_WarpPortal → Resources/Fx/Portal_Warp 재질): 세운 원판 + 빛
// 열기 · 닫기는 크기가 튀어 오르듯 커지고 줄어듦. billboard면 카메라 쪽으로만 Y축 회전 (방향 정보가 없는 출구 · 뇌문용)
// 재질이 없으면 아무것도 안 그림 (기존 소용돌이 불티 · 빛만 남음)
public class WarpPortalFx : MonoBehaviour
{
    static readonly int PortalColorId = Shader.PropertyToID("_Portal_Color");
    static readonly int BgColorId = Shader.PropertyToID("_BG_Color");
    static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
    static Material source;
    static bool loaded;

    Transform face;
    Material material;
    Light glow;
    float radius, scale, target, speed = 3f, lightIntensity;
    bool billboard, destroyWhenClosed;

    public bool IsOpen => target > 0f;

    public static WarpPortalFx Create(Transform parent, Vector3 position, Quaternion rotation, float radius, Color portal, Color background, bool billboard, float light = 2.5f)
    {
        if (!loaded)
        {
            loaded = true;
            source = Resources.Load<Material>("Fx/Portal_Warp");
        }
        var go = new GameObject("WarpPortal");
        if (parent != null) go.transform.SetParent(parent, true);
        go.transform.SetPositionAndRotation(position, rotation);
        var fx = go.AddComponent<WarpPortalFx>();
        fx.radius = radius;
        fx.billboard = billboard;
        fx.lightIntensity = light;

        if (source != null)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(quad.GetComponent<Collider>());
            quad.name = "Face";
            quad.transform.SetParent(go.transform, false);
            var r = quad.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            fx.material = new Material(source);
            r.sharedMaterial = fx.material;
            fx.face = quad.transform;
        }

        if (light > 0f)
        {
            fx.glow = new GameObject("Glow").AddComponent<Light>();
            fx.glow.transform.SetParent(go.transform, false);
            fx.glow.transform.localPosition = Vector3.back * 0.6f;
            fx.glow.type = LightType.Point;
            fx.glow.range = radius * 4f;
            fx.glow.shadows = LightShadows.None;
        }
        fx.SetColors(portal, background);
        fx.Apply();
        return fx;
    }

    public void SetColors(Color portal, Color background)
    {
        if (material != null)
        {
            material.SetColor(PortalColorId, portal);
            material.SetColor(BgColorId, background);
        }
        if (glow != null) glow.color = portal.maxColorComponent > 1f ? portal / portal.maxColorComponent : portal;
    }

    public void SetBrightness(float value)
    {
        if (material != null) material.SetFloat(BrightnessId, value);
    }

    public void Open(float seconds = 0.6f)
    {
        target = 1f;
        speed = 1f / Mathf.Max(0.05f, seconds);
        destroyWhenClosed = false;
    }

    public void Close(float seconds = 0.5f, bool destroy = false)
    {
        target = 0f;
        speed = 1f / Mathf.Max(0.05f, seconds);
        destroyWhenClosed = destroy;
    }

    public void SetOpenInstant(bool open)
    {
        target = scale = open ? 1f : 0f;
        Apply();
    }

    void LateUpdate()
    {
        scale = Mathf.MoveTowards(scale, target, Time.deltaTime * speed);
        Apply();
        if (destroyWhenClosed && scale <= 0f) Destroy(gameObject);

        if (billboard && Camera.main != null)
        {
            Vector3 to = Camera.main.transform.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(-to.normalized, Vector3.up); // 쿼드 앞면(-Z)이 카메라를 봄
        }
    }

    // 열릴 때 살짝 넘쳤다 자리 잡는 크기 (EaseOutBack 비슷하게)
    void Apply()
    {
        float k = Mathf.Clamp01(scale);
        float eased = target > 0f ? 1f + 2.2f * Mathf.Pow(k - 1f, 3f) + 1.2f * Mathf.Pow(k - 1f, 2f) : k * k;
        float size = radius * 2f * Mathf.Max(0f, eased);
        if (face != null)
        {
            face.localScale = new Vector3(size, size, 1f);
            face.gameObject.SetActive(size > 0.001f);
        }
        if (glow != null) glow.intensity = lightIntensity * k * (0.85f + 0.15f * Mathf.Sin(Time.time * 2.4f));
    }

    void OnDestroy()
    {
        if (material != null) Destroy(material);
    }
}
