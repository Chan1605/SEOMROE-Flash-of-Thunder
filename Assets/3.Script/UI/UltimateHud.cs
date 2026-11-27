using UnityEngine;
using UnityEngine.UI;

// 궁극기 게이지 HUD (오른쪽 아래, Q 슬롯 왼쪽)
// [변경] 도형 마름모 3칸 → 궁극기 슬롯(아이콘 · 금 테두리 · V 키 캡) + 위에 보석 3개 (Resources/UI/Icons의 UltGem*)
//  - 보석은 아래부터 차오르고, 꽉 찬 보석은 금빛으로 은은하게 빛남. 칸이 새로 차면 번쩍, 게이지 없이 V를 누르면 흔들림
//  - 슬롯 아이콘: 지금 쓸 수 있는 가장 높은 단계 (게이지 없으면 1단계를 어둡게). V를 누르고 있으면 고른 단계 아이콘 + 왼쪽에 이름
public class UltimateHud : MonoBehaviour
{
    static readonly Color Fill = new Color(0.55f, 0.72f, 1f, 1f);
    static readonly Color Full = new Color(0.95f, 0.85f, 0.5f, 1f);
    static readonly Color SlotBack = new Color(0.05f, 0.06f, 0.08f, 0.9f);
    static readonly Color FrameDim = new Color(1f, 1f, 1f, 0.28f);
    static readonly string[] TierIcons = { "UltimateStorm", "UltimateHeavens", "UltimateDescent" };

    const float SlotSize = 84f;
    const float GemSize = 30f;
    const float GemStep = 26f;

    PlayerUltimate ultimate;
    RectTransform root;
    readonly Image[] fills = new Image[PlayerUltimate.MaxBars];
    readonly Image[] glows = new Image[PlayerUltimate.MaxBars];
    readonly Sprite[] tierSprites = new Sprite[3];
    Image icon, frame;
    Text key, selectName;
    float shake, punch;
    Vector2 basePos;

    public static UltimateHud Create(PlayerUltimate owner)
    {
        Canvas canvas = UIKit.CreateCanvas(null, "UltimateHud", 12);
        var hud = canvas.gameObject.AddComponent<UltimateHud>();
        hud.ultimate = owner;
        hud.Build(canvas.transform);
        return hud;
    }

    void Build(Transform canvas)
    {
        var go = new GameObject("Ultimate", typeof(RectTransform));
        go.transform.SetParent(canvas, false);
        root = (RectTransform)go.transform;
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(1f, 0f);
        basePos = new Vector2(-180f, 56f); // Q 슬롯(108px, 오른쪽 아래 여백 56) 왼쪽
        root.anchoredPosition = basePos;
        root.sizeDelta = new Vector2(SlotSize, 150f);

        for (int i = 0; i < tierSprites.Length; i++) tierSprites[i] = UiIcons.Get(TierIcons[i]);

        // 슬롯: Q 슬롯과 같은 둥근 바탕 · 아이콘 · 테두리 (가운데 높이를 Q 슬롯과 맞춤)
        float slotY = 54f;
        Place(UIKit.Image(root, UIKit.RoundRect(84, 84, 18f, 0f), SlotBack), 0f, slotY, SlotSize);
        icon = UIKit.Image(root, tierSprites[0], Color.white);
        Place(icon, 0f, slotY, SlotSize - 10f);
        frame = UIKit.Image(root, UIKit.RoundRect(88, 88, 20f, 2.5f), FrameDim);
        Place(frame, 0f, slotY, SlotSize + 4f);

        // 보석 3개: 슬롯 위 가로 한 줄
        Sprite socket = UiIcons.Get("UltGemSocket");
        Sprite gem = UiIcons.Get("UltGemFill");
        Sprite glow = UiIcons.Get("UltGemGlow");
        Sprite square = UIKit.RoundRect(32, 32, 5f, 0f);
        bool fallback = socket == null || gem == null; // 그림이 없으면 예전 마름모 도형
        float gemY = slotY + SlotSize * 0.5f + 22f;
        for (int i = 0; i < PlayerUltimate.MaxBars; i++)
        {
            float x = (i - 1) * GemStep;
            glows[i] = Gem(glow != null ? glow : square, x, gemY, GemSize * 1.7f, new Color(Full.r, Full.g, Full.b, 0f), fallback);
            Gem(fallback ? square : socket, x, gemY, GemSize * (fallback ? 0.75f : 1f), fallback ? new Color(0.05f, 0.06f, 0.09f, 0.75f) : Color.white, fallback);
            Image fill = Gem(fallback ? square : gem, x, gemY, GemSize * (fallback ? 0.75f : 1f), Fill, fallback);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Vertical;
            fill.fillOrigin = (int)Image.OriginVertical.Bottom;
            fills[i] = fill;
        }

        // V 키 캡: 슬롯 오른쪽 아래 모서리
        SettingsMenu menu = FindAnyObjectByType<SettingsMenu>(FindObjectsInactive.Include);
        Font font = menu != null ? menu.TitleFont : null;
        Vector2 corner = new Vector2(SlotSize * 0.44f, slotY - SlotSize * 0.44f);
        Image cap = UIKit.Image(root, UIKit.RoundRect(48, 48, 24f, 0f), new Color(0.05f, 0.06f, 0.08f, 0.95f));
        Place(cap, corner.x, corner.y, 24f);
        Image capFrame = UIKit.Image(root, UIKit.RoundRect(48, 48, 24f, 2f), UIKit.Gold);
        Place(capFrame, corner.x, corner.y, 24f);
        key = UIKit.Text(root, "V", font, 16, new Color(1f, 1f, 1f, 0.7f), TextAnchor.MiddleCenter);
        Place(key, corner.x, corner.y, 24f);

        selectName = UIKit.Text(root, "", font, 26, Color.white, TextAnchor.MiddleRight);
        RectTransform n = selectName.rectTransform;
        n.anchorMin = n.anchorMax = new Vector2(0f, 0f);
        n.pivot = new Vector2(1f, 0.5f);
        n.anchoredPosition = new Vector2(-14f, slotY);
        n.sizeDelta = new Vector2(320f, 36f);
        selectName.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2f, -2f);
    }

    // 가운데 아래 기준 (x, y) 중심에 size 정사각형
    static void Place(Graphic g, float x, float y, float size)
    {
        g.raycastTarget = false;
        RectTransform r = g.rectTransform;
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0f);
        r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = new Vector2(x, y);
        r.sizeDelta = new Vector2(size, size);
        if (g is Image img) img.preserveAspect = true;
    }

    Image Gem(Sprite sprite, float x, float y, float size, Color color, bool rotate)
    {
        Image img = UIKit.Image(root, sprite, color);
        img.type = Image.Type.Simple;
        Place(img, x, y, size);
        if (rotate) img.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        return img;
    }

    public void Ready(int bars) => punch = 1f;
    public void Deny() => shake = 0.35f;

    void Update()
    {
        if (ultimate == null)
        {
            Destroy(gameObject);
            return;
        }
        float dt = Time.unscaledDeltaTime;
        float gauge = ultimate.Gauge;
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f);
        for (int i = 0; i < fills.Length; i++)
        {
            float amount = Mathf.Clamp01(gauge - i);
            bool full = amount >= 1f;
            bool picked = i < ultimate.Selecting; // V 홀드로 고른 단계까지
            fills[i].fillAmount = amount;
            fills[i].color = picked ? Color.white : full ? Full : Fill;
            float g = picked ? 0.9f : full ? 0.25f + 0.35f * pulse + 0.6f * punch : 0f;
            Color gc = picked ? Color.white : Full;
            glows[i].color = new Color(gc.r, gc.g, gc.b, ultimate.Active ? 0f : g);
        }

        // [변경] 슬롯 아이콘 · 테두리
        int bars = ultimate.Bars;
        int tier = ultimate.Selecting > 0 ? ultimate.Selecting : Mathf.Clamp(Mathf.Min(bars, SkillTree.UltimateTier), 1, 3);
        Sprite s = tierSprites[tier - 1];
        if (s != null && icon.sprite != s) icon.sprite = s;
        icon.enabled = icon.sprite != null;
        bool ready = bars > 0 && !ultimate.Active;
        icon.color = ultimate.Selecting > 0 || ready ? Color.white : new Color(0.55f, 0.55f, 0.6f, 0.45f);
        frame.color = ultimate.Selecting > 0 ? Color.white : ready ? Color.Lerp(UIKit.Gold, Color.white, 0.35f * punch) : FrameDim;

        selectName.text = ultimate.SelectingName;
        key.color = new Color(1f, 1f, 1f, bars > 0 ? 0.95f : 0.45f);

        punch = Mathf.MoveTowards(punch, 0f, dt * 2.5f);
        root.localScale = Vector3.one * (1f + 0.15f * punch * punch);
        shake = Mathf.MoveTowards(shake, 0f, dt);
        root.anchoredPosition = basePos + new Vector2(Mathf.Sin(Time.unscaledTime * 70f) * 8f * shake, 0f);
    }
}
