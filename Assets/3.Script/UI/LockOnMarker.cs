using UnityEngine;
using UnityEngine.UI;

// 락온 표시 — [변경] 타이틀 · 설정 · 보스 체력바와 같은 금빛 마름모 스타일
//  금빛 마름모 테두리(천천히 숨쉼) + 네 방향에서 대상을 겨누는 꺾쇠 + 가운데 붉은 작은 마름모
//  잡는 순간: 테두리가 크게 나타났다 조여 들고, 꺾쇠가 바깥에서 안으로 모여듦. 대상이 화면 뒤면 숨김
// HUD 캔버스 아래 전체 화면 RectTransform에 붙임 (Player Lock On 셋업이 만들어 줌)
public class LockOnMarker : MonoBehaviour
{
    [SerializeField] PlayerLockOn lockOn;
    [SerializeField] Color color = new Color(1f, 0.45f, 0.25f, 0.95f); // 가운데 마름모
    [SerializeField] float size = 16f;
    [SerializeField] float height = 1.25f;
    [SerializeField] Color frameColor = new Color(0.95f, 0.8f, 0.45f, 0.9f); // 금빛

    const float AcquireTime = 0.22f;

    RectTransform area, marker, frame, core;
    readonly RectTransform[] chevrons = new RectTransform[4];
    Image frameImage, coreImage;
    readonly Image[] chevronImages = new Image[4];
    Camera cam;
    Transform target;
    float acquiredAt;
    static Sprite diamondFrame, chevronSprite;

    void Awake()
    {
        area = (RectTransform)transform;
        if (lockOn == null) lockOn = FindAnyObjectByType<PlayerLockOn>();
        Build();
    }

    void OnEnable()
    {
        if (lockOn != null) lockOn.TargetChanged += OnTargetChanged;
    }

    void OnDisable()
    {
        if (lockOn != null) lockOn.TargetChanged -= OnTargetChanged;
    }

    void OnTargetChanged(Transform t)
    {
        target = t;
        marker.gameObject.SetActive(t != null);
        if (t == null) return;
        acquiredAt = Time.unscaledTime;
        Place();
    }

    void LateUpdate()
    {
        if (target == null)
        {
            if (marker.gameObject.activeSelf) marker.gameObject.SetActive(false);
            return;
        }
        Place();
        Animate();
    }

    // 핵심: 잡는 순간 0.22초 동안 바깥에서 조여 들고, 그 뒤엔 천천히 숨쉼
    void Animate()
    {
        float now = Time.unscaledTime;
        float k = Mathf.Clamp01((now - acquiredAt) / AcquireTime);
        float ease = 1f - Mathf.Pow(1f - k, 3f);
        float breathe = 0.5f + 0.5f * Mathf.Sin(now * 3.2f);

        float frameScale = Mathf.Lerp(2.1f, 1f, ease) * (1f + 0.06f * breathe * k);
        frame.localScale = Vector3.one * frameScale;
        frame.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-45f, 0f, ease));
        frameImage.color = new Color(frameColor.r, frameColor.g, frameColor.b, frameColor.a * ease);

        float reach = size * Mathf.Lerp(3.4f, 1.75f, ease) + size * 0.12f * breathe * k; // 꺾쇠가 대상 쪽으로 모임
        for (int i = 0; i < 4; i++)
        {
            float angle = i * 90f;
            Vector2 dir = new Vector2(Mathf.Sin(angle * Mathf.Deg2Rad), Mathf.Cos(angle * Mathf.Deg2Rad));
            chevrons[i].anchoredPosition = dir * reach;
            chevronImages[i].color = new Color(frameColor.r, frameColor.g, frameColor.b, frameColor.a * ease);
        }

        core.localScale = Vector3.one * (k < 1f ? Mathf.Lerp(0f, 1.3f, ease) : 0.9f + 0.25f * breathe);
        coreImage.color = color;
    }

    void Place()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null || target == null) return;

        Vector3 screen = cam.WorldToScreenPoint(target.position + Vector3.up * height);
        bool front = screen.z > 0f;
        frameImage.enabled = coreImage.enabled = front;
        foreach (Image c in chevronImages) c.enabled = front;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(area, screen, null, out Vector2 local);
        marker.anchoredPosition = local;
    }

    void Build()
    {
        if (diamondFrame == null) diamondFrame = MakeDiamondFrame();
        if (chevronSprite == null) chevronSprite = MakeChevron();

        marker = Rect("Marker", transform, size * 4f);
        frame = Rect("Frame", marker, size * 2.6f);
        frameImage = Img(frame, diamondFrame);

        for (int i = 0; i < 4; i++)
        {
            chevrons[i] = Rect("Chevron" + i, marker, size * 0.9f);
            chevrons[i].localRotation = Quaternion.Euler(0f, 0f, -i * 90f); // 위 · 오른쪽 · 아래 · 왼쪽에서 안쪽을 가리킴
            chevronImages[i] = Img(chevrons[i], chevronSprite);
        }

        core = Rect("Core", marker, size * 0.75f);
        core.localRotation = Quaternion.Euler(0f, 0f, 45f);
        coreImage = Img(core, null);
        var outline = core.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        marker.gameObject.SetActive(false);
    }

    static RectTransform Rect(string name, Transform parent, float s)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(s, s);
        return rt;
    }

    static Image Img(RectTransform rt, Sprite sprite)
    {
        Image img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        return img;
    }

    // 얇은 마름모 테두리 (안쪽은 비어 있음)
    static Sprite MakeDiamondFrame()
    {
        const int N = 96;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                float d = Mathf.Abs(u) + Mathf.Abs(v);
                float a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.9f) / 0.045f);
                // 꼭짓점 쪽만 진하고 변 가운데는 살짝 끊김 → 장식 테두리 느낌
                float gap = Mathf.Clamp01(Mathf.Abs(Mathf.Abs(u) - Mathf.Abs(v)) / 0.25f);
                px[y * N + x] = new Color32(255, 255, 255, (byte)(255f * a * Mathf.Lerp(0.15f, 1f, gap)));
            }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
    }

    // 아래(안쪽)를 가리키는 작은 꺾쇠 ∨
    static Sprite MakeChevron()
    {
        const int N = 48;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                float line = Mathf.Abs(v - (Mathf.Abs(u) * 0.9f - 0.35f)); // ∨ 모양
                float a = Mathf.Abs(u) < 0.8f ? Mathf.Clamp01(1f - line / 0.12f) : 0f;
                px[y * N + x] = new Color32(255, 255, 255, (byte)(255f * a));
            }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
    }
}
