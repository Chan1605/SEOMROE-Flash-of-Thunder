using UnityEngine;
using UnityEngine.UI;

// 보스 체력 막대 (화면 아래 가운데) — [변경] 타이틀 · 설정 화면과 같은 금빛 장식 스타일로 새로 그림
//  ◆ 이름 · 금빛 테두리선 · 양 끝 마름모 / 체력(위가 밝은 붉은 막대 + 맞으면 하얗게 번쩍 + 금빛 잔상) / 체간(가운데서 양옆으로)
//  2페이즈는 1줄을 다 깎으면 부활하며 다시 차오름 → 차오르는 동안 빛줄기가 훑고, 2페이즈엔 짙은 진홍 + 막대 위를 번개빛이 흐름
//  GameHUD가 실행 중에 만들어 붙임. 보스(EnemySwordsman.ActiveBoss)가 등장하면 가운데서 양옆으로 펼쳐지며 나타나고 쓰러지면 사라짐
public class BossHealthBar : MonoBehaviour
{
    const float Width = 820f; // 왼쪽 아래 플레이어 HUD와 겹치지 않는 폭
    const float BarHeight = 14f;
    const float PostureWidth = 520f;
    static readonly Color HpColor = new Color(0.86f, 0.16f, 0.12f);
    static readonly Color HpPhase2 = new Color(0.62f, 0.06f, 0.16f);
    static readonly Color TrailColor = new Color(1f, 0.82f, 0.5f, 0.9f);
    static readonly Color PostureLow = new Color(1f, 0.78f, 0.3f);
    static readonly Color PostureHigh = new Color(1f, 0.32f, 0.1f);
    static readonly Color Line = new Color(UIKit.Gold.r, UIKit.Gold.g, UIKit.Gold.b, 0.85f);

    RectTransform root, frame, hpFill, hpTrail, postureFill, sheen;
    Image hpImage, postureImage, sheenImage;
    Text nameText, phaseText;
    CanvasGroup group;
    EnemySwordsman shown;
    float trail = 1f, trailHoldUntil, lastHp = 1f, flash, sheenPos = -1f, showT, hideAt = -1f, phasePunch;
    bool phaseShown;

    public static Canvas Current { get; private set; }
    Canvas canvas;

    void OnDestroy()
    {
        if (canvas != null) Destroy(canvas.gameObject);
        if (Current == canvas) Current = null;
    }

    public void Build(Font font)
    {
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        canvas = UIKit.CreateCanvas(null, "BossBarCanvas", 45); // 궁극기 연출(40) 위, 결과 화면(150) · 메뉴 아래
        Current = canvas;
        root = Rect("Root", canvas.transform, new Vector2(0.5f, 0f), new Vector2(Width, 90f));
        root.anchoredPosition = new Vector2(0f, 84f);
        group = root.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;

        // 이름 줄: ◆ 이름 ─────  (오른쪽 끝에 페이즈 표시)
        Diamond(root, -Width * 0.5f + 8f, 62f, 11f, UIKit.Gold);
        nameText = Label(root, "Name", font, 30, new Color(1f, 0.95f, 0.88f), TextAnchor.LowerLeft);
        nameText.rectTransform.anchoredPosition = new Vector2(-Width * 0.5f + 24f, 44f);
        phaseText = Label(root, "Phase", font, 24, new Color(1f, 0.32f, 0.28f), TextAnchor.LowerRight);
        phaseText.rectTransform.anchoredPosition = new Vector2(Width * 0.5f - 6f, 46f);
        phaseText.text = "";

        // 막대 틀: 어두운 바탕 + 위아래 금빛 머리카락 선(끝으로 갈수록 흐려짐) + 양 끝 마름모
        frame = Rect("Frame", root, new Vector2(0.5f, 0f), new Vector2(Width, BarHeight));
        frame.anchoredPosition = new Vector2(0f, 24f);
        Image back = frame.gameObject.AddComponent<Image>();
        back.color = new Color(0.02f, 0.02f, 0.03f, 0.78f);
        back.raycastTarget = false;
        HairLine(root, 24f + BarHeight + 3f);
        HairLine(root, 24f - 3f);
        Diamond(root, -Width * 0.5f - 12f, 24f + BarHeight * 0.5f, 12f, UIKit.Gold);
        Diamond(root, Width * 0.5f + 12f, 24f + BarHeight * 0.5f, 12f, UIKit.Gold);

        hpTrail = Fill("HpTrail", frame, TrailColor, null);
        Image fillImage;
        hpFill = Fill("HpFill", frame, HpColor, VerticalSheen(), out fillImage);
        hpImage = fillImage;
        hpFill.gameObject.AddComponent<RectMask2D>(); // 빛줄기를 채워진 부분 안에서만

        sheen = Rect("Sheen", hpFill, new Vector2(0f, 0.5f), new Vector2(160f, BarHeight * 1.6f));
        sheen.pivot = new Vector2(0.5f, 0.5f);
        sheenImage = sheen.gameObject.AddComponent<Image>();
        sheenImage.sprite = SoftBlob();
        sheenImage.color = new Color(1f, 0.95f, 0.85f, 0f);
        sheenImage.raycastTarget = false;

        // 체간: 가운데서 양옆으로 차오름 + 가운데 금빛 눈금
        RectTransform postureBack = Rect("PostureBack", root, new Vector2(0.5f, 0f), new Vector2(PostureWidth, 6f));
        postureBack.anchoredPosition = new Vector2(0f, 8f);
        Image pb = postureBack.gameObject.AddComponent<Image>();
        pb.color = new Color(0f, 0f, 0f, 0.55f);
        pb.raycastTarget = false;
        postureFill = Rect("PostureFill", postureBack, new Vector2(0.5f, 0.5f), new Vector2(0f, 6f));
        postureFill.pivot = new Vector2(0.5f, 0.5f);
        postureImage = postureFill.gameObject.AddComponent<Image>();
        postureImage.raycastTarget = false;
        postureImage.color = PostureLow;
        Diamond(root, 0f, 11f, 7f, UIKit.Gold);

        root.gameObject.SetActive(false);
    }

    void LateUpdate()
    {
        if (root == null) return;
        float dt = Time.unscaledDeltaTime;
        EnemySwordsman boss = EnemySwordsman.ActiveBoss;
        if (boss != null && boss != shown && boss.CanBeTargeted) Show(boss);
        if (shown == null)
        {
            if (root.gameObject.activeSelf && hideAt < 0f) hideAt = Time.unscaledTime;
            UpdateFade(dt);
            return;
        }

        float hp = shown.Health01;
        if (hp < lastHp - 0.0001f)
        {
            trailHoldUntil = Time.unscaledTime + 0.45f;
            flash = 1f; // 맞은 순간 막대가 하얗게
        }
        else if (hp > lastHp + 0.0001f) sheenPos = Mathf.Max(sheenPos, 0f); // 부활로 차오르는 중 → 빛줄기
        lastHp = hp;
        SetFill(hpFill, hp);

        // 잔상: 맞은 뒤 잠깐 멈췄다가 따라 내려옴. 차오를 때는 같이 올라감
        if (hp < trail && Time.unscaledTime >= trailHoldUntil) trail = Mathf.MoveTowards(trail, hp, dt * 0.5f);
        else if (hp > trail) trail = hp;
        SetFill(hpTrail, trail);

        flash = Mathf.MoveTowards(flash, 0f, dt * 6f);
        Color baseColor = shown.IsPhase2 ? HpPhase2 : HpColor;
        hpImage.color = Color.Lerp(baseColor, Color.white, flash * 0.75f);

        UpdateSheen(dt);

        float posture = shown.IsPostureBroken ? 1f : shown.Posture01;
        postureFill.sizeDelta = new Vector2(PostureWidth * posture, postureFill.sizeDelta.y);
        float blink = shown.IsPostureBroken ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 16f) : 0f;
        postureImage.color = Color.Lerp(Color.Lerp(PostureLow, PostureHigh, posture * posture), Color.white, blink);

        if (shown.IsPhase2 && !phaseShown)
        {
            phaseShown = true;
            phaseText.text = "분노";
            phasePunch = 1f;
        }
        phasePunch = Mathf.MoveTowards(phasePunch, 0f, dt * 2.5f);
        phaseText.transform.localScale = Vector3.one * (1f + 0.35f * phasePunch * phasePunch);

        if (!shown.CanBeTargeted && hideAt < 0f) hideAt = Time.unscaledTime + 1.5f;
        UpdateFade(dt);
    }

    // 빛줄기: 부활로 차오를 때 한 번 훑고, 2페이즈엔 계속 천천히 흐름
    void UpdateSheen(float dt)
    {
        bool loop = shown.IsPhase2;
        if (sheenPos < 0f && loop) sheenPos = 0f;
        if (sheenPos < 0f)
        {
            sheenImage.color = new Color(1f, 0.95f, 0.85f, 0f);
            return;
        }
        sheenPos += dt * (loop ? 0.45f : 1.2f);
        float fillWidth = Width * Mathf.Max(0.001f, hpFill.anchorMax.x);
        sheen.anchoredPosition = new Vector2(Mathf.Lerp(-80f, fillWidth + 80f, sheenPos), 0f);
        float a = Mathf.Sin(Mathf.Clamp01(sheenPos) * Mathf.PI) * (loop ? 0.35f : 0.8f);
        sheenImage.color = loop ? new Color(1f, 0.5f, 0.6f, a) : new Color(1f, 0.95f, 0.85f, a);
        if (sheenPos >= 1f) sheenPos = loop ? 0f : -1f;
    }

    // 나타남: 가운데서 양옆으로 펼쳐짐 / 사라짐: 흐려짐
    void UpdateFade(float dt)
    {
        if (hideAt >= 0f && Time.unscaledTime >= hideAt)
        {
            group.alpha = Mathf.MoveTowards(group.alpha, 0f, dt * 1.4f);
            if (group.alpha <= 0f)
            {
                root.gameObject.SetActive(false);
                shown = null;
                hideAt = -1f;
            }
            return;
        }
        if (shown == null) return;
        showT = Mathf.MoveTowards(showT, 1f, dt * 1.6f);
        float e = 1f - Mathf.Pow(1f - showT, 3f);
        group.alpha = Mathf.Clamp01(showT * 2f);
        root.localScale = new Vector3(Mathf.Lerp(0.2f, 1f, e), 1f, 1f);
    }

    void Show(EnemySwordsman boss)
    {
        shown = boss;
        hideAt = -1f;
        showT = 0f;
        lastHp = trail = boss.Health01;
        flash = 0f;
        sheenPos = -1f;
        phaseShown = boss.IsPhase2;
        phaseText.text = phaseShown ? "분노" : "";
        nameText.text = boss.BossName;
        root.gameObject.SetActive(true);
        group.alpha = 0f;
    }

    static void SetFill(RectTransform fill, float value) => fill.anchorMax = new Vector2(Mathf.Clamp01(value), 1f);

    // ===== 만들기 =====
    RectTransform Fill(string name, RectTransform parent, Color color, Sprite sprite)
    {
        Image unused;
        return Fill(name, parent, color, sprite, out unused);
    }

    RectTransform Fill(string name, RectTransform parent, Color color, Sprite sprite, out Image image)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0f, 0.5f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        return rt;
    }

    // 막대 위아래 금빛 선: 가운데 진하고 양 끝으로 흐려짐 (그라데이션 두 장을 마주 붙임)
    void HairLine(RectTransform parent, float y)
    {
        for (int side = -1; side <= 1; side += 2)
        {
            RectTransform rt = Rect("Line", parent, new Vector2(0.5f, 0f), new Vector2(Width * 0.5f + 24f, 1.5f));
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, y);
            rt.localScale = new Vector3(side, 1f, 1f);
            Image img = rt.gameObject.AddComponent<Image>();
            img.sprite = UIKit.SharedGradient;
            img.color = Line;
            img.raycastTarget = false;
        }
    }

    static void Diamond(RectTransform parent, float x, float y, float size, Color color)
    {
        RectTransform rt = Rect("Diamond", parent, new Vector2(0.5f, 0f), new Vector2(size, size));
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.localRotation = Quaternion.Euler(0f, 0f, 45f);
        Image img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
    }

    static Text Label(RectTransform parent, string name, Font font, int size, Color color, TextAnchor anchor)
    {
        RectTransform rt = Rect(name, parent, new Vector2(0.5f, 0f), new Vector2(Width * 0.6f, size * 1.4f));
        rt.pivot = anchor == TextAnchor.LowerLeft ? new Vector2(0f, 0f) : new Vector2(1f, 0f);
        var text = rt.gameObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.fontStyle = FontStyle.Bold;
        text.alignment = anchor;
        text.color = color;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        var shadow = rt.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.9f);
        shadow.effectDistance = new Vector2(2f, -2f);
        return text;
    }

    static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = size;
        return rt;
    }

    // 위쪽이 밝고 아래로 어두워지는 세로 결 (막대에 입체감)
    static Sprite VerticalSheen()
    {
        const int H = 16;
        var tex = new Texture2D(1, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < H; y++)
        {
            float t = y / (H - 1f); // 0 아래 ~ 1 위
            float v = Mathf.Lerp(0.62f, 1f, t * t) + (t > 0.78f ? 0.18f : 0f);
            tex.SetPixel(0, y, new Color(Mathf.Min(1f, v), Mathf.Min(1f, v), Mathf.Min(1f, v), 1f));
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, H), new Vector2(0.5f, 0.5f), 100f);
    }

    // 가운데 밝고 가장자리로 사라지는 가로로 긴 빛 덩어리 (빛줄기)
    static Sprite SoftBlob()
    {
        const int W = 64, H = 16;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W * 2f - 1f, v = (y + 0.5f) / H * 2f - 1f;
                float a = Mathf.Clamp01(1f - u * u) * Mathf.Clamp01(1f - v * v * 0.6f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f);
    }
}
