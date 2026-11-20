using UnityEngine;

// 패링 예고 이펙트 (적마다 하나, BossGrabTell과 같은 방식) — [변경] 순간 번쩍임 → 판정까지 켜져 있는 예고로
//  칼날을 따라가는 파란 십자 별빛 + 둘레 빛 고리 + 파란 점광. owner.ParryTell01(0~1)을 읽어 커지고 빨리 맥박침
//  Begin: 예고 시작 "칭" 소리 · Burst: 판정 순간 크게 번쩍 후 사라짐. 소리는 Resources/Audio/Enemy/parry_tell
public class ParryGlint : MonoBehaviour
{
    static readonly Color StarColor = new Color(0.55f, 0.9f, 3.2f, 1f); // HDR → 블룸으로 번짐
    static readonly Color RingColor = new Color(0.35f, 0.7f, 2.4f, 0.8f);
    const float BurstTime = 0.22f;

    static Material material;
    static Texture2D starTexture, ringTexture;
    static AudioClip clip;
    static bool loaded;

    EnemySwordsman owner;
    Transform follow, star, ring;
    MaterialPropertyBlock starBlock, ringBlock;
    Renderer starRenderer, ringRenderer;
    Light glow;
    float burst = -1f, scale;

    public static ParryGlint Create(EnemySwordsman owner, Transform follow)
    {
        if (!loaded)
        {
            loaded = true;
            clip = Resources.Load<AudioClip>("Audio/Enemy/parry_tell");
            material = new Material(Shader.Find("Sprites/Default")) { name = "ParryGlint" };
            starTexture = StarTexture();
            ringTexture = RingTexture();
        }
        var go = new GameObject("ParryGlint");
        var g = go.AddComponent<ParryGlint>();
        g.owner = owner;
        g.follow = follow;
        g.scale = Mathf.Max(0.6f, owner.transform.lossyScale.x);
        g.star = g.Quad("Star", starTexture, out g.starRenderer, out g.starBlock);
        g.ring = g.Quad("Ring", ringTexture, out g.ringRenderer, out g.ringBlock);
        g.glow = go.AddComponent<Light>();
        g.glow.type = LightType.Point;
        g.glow.color = new Color(0.45f, 0.7f, 1f);
        g.glow.shadows = LightShadows.None;
        g.SetVisible(false);
        return g;
    }

    public void Begin()
    {
        burst = -1f;
        if (clip != null && follow != null) AudioSource.PlayClipAtPoint(clip, follow.position, 0.9f * AudioOptions.SfxVolume);
    }

    public void Burst() => burst = 0f;

    void LateUpdate()
    {
        if (owner == null || follow == null)
        {
            Destroy(gameObject);
            return;
        }

        float k = owner.ParryTell01;
        float size, intensity, ringSize;
        if (burst >= 0f) // 판정 순간: 크게 번쩍였다 빠르게 사라짐
        {
            burst += Time.deltaTime;
            float b = burst / BurstTime;
            if (b >= 1f)
            {
                burst = -1f;
                SetVisible(false);
                return;
            }
            size = Mathf.Lerp(1.9f, 0f, b * b);
            ringSize = Mathf.Lerp(1.6f, 3f, b);
            intensity = Mathf.Lerp(9f, 0f, b);
        }
        else if (k > 0f) // 예고: 처음부터 잘 보이게 0.75배에서 시작, 다가올수록 커지고 맥박이 빨라짐
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.Lerp(9f, 26f, k));
            size = Mathf.Lerp(0.75f, 1.35f, k) * (0.82f + 0.36f * pulse);
            ringSize = Mathf.Lerp(2.2f, 1.1f, k); // 고리는 바깥에서 칼날로 조여 들어옴
            intensity = Mathf.Lerp(2.5f, 6f, k) * (0.75f + 0.5f * pulse);
        }
        else
        {
            SetVisible(false);
            return;
        }

        SetVisible(true);
        transform.position = follow.position;
        Camera cam = Camera.main;
        if (cam != null)
        {
            Quaternion face = Quaternion.LookRotation(transform.position - cam.transform.position, cam.transform.up);
            star.rotation = face * Quaternion.Euler(0f, 0f, 20f + Time.time * 40f);
            ring.rotation = face;
        }
        star.localScale = Vector3.one * size * scale;
        ring.localScale = Vector3.one * ringSize * scale;
        float ringAlpha = burst >= 0f ? 1f - burst / BurstTime : Mathf.Lerp(0.35f, 1f, k);
        ringBlock.SetColor("_Color", new Color(RingColor.r, RingColor.g, RingColor.b, RingColor.a * ringAlpha));
        ringRenderer.SetPropertyBlock(ringBlock);
        glow.range = 4f * scale;
        glow.intensity = intensity;
    }

    Transform Quad(string name, Texture2D texture, out Renderer renderer, out MaterialPropertyBlock block)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(quad.GetComponent<Collider>());
        quad.name = name;
        quad.transform.SetParent(transform, false);
        renderer = quad.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        block = new MaterialPropertyBlock();
        block.SetTexture("_MainTex", texture);
        block.SetColor("_Color", name == "Star" ? StarColor : RingColor);
        renderer.SetPropertyBlock(block);
        return quad.transform;
    }

    void SetVisible(bool on)
    {
        if (star.gameObject.activeSelf != on) star.gameObject.SetActive(on);
        if (ring.gameObject.activeSelf != on) ring.gameObject.SetActive(on);
        glow.enabled = on;
    }

    // 가운데가 밝고 네 갈래로 뻗는 별빛
    static Texture2D StarTexture()
    {
        const int N = 64;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float core = Mathf.Exp(-r * r * 22f);
                float rays = Mathf.Exp(-Mathf.Abs(u) * 22f) * (1f - Mathf.Abs(v)) + Mathf.Exp(-Mathf.Abs(v) * 22f) * (1f - Mathf.Abs(u));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(core + rays * 0.9f)));
            }
        tex.Apply();
        return tex;
    }

    // 얇은 빛 고리
    static Texture2D RingTexture()
    {
        const int N = 64;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                float d = Mathf.Abs(Mathf.Sqrt(u * u + v * v) - 0.82f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(1f - d / 0.09f)));
            }
        tex.Apply();
        return tex;
    }
}
