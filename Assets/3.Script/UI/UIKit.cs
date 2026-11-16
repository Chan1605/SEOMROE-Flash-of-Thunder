using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// 코드로 UI를 만들 때 쓰는 공용 도우미 (1920x1080 기준, 좌상단 원점 · y는 아래로)
public static class UIKit
{
    public static Canvas CreateCanvas(Transform parent, string name, int sortingOrder)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Canvas canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    // x·centerY: 왼쪽 위 기준 픽셀, 세로는 가운데
    public static void Place(RectTransform rect, float x, float centerY, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = new Vector2(x, -centerY);
        rect.sizeDelta = new Vector2(width, height);
    }

    public static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    public static Text Text(Transform parent, string text, Font font, int size, Color color, TextAnchor anchor)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Text t = go.AddComponent<Text>();
        t.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = anchor;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    public static Image Image(Transform parent, Sprite sprite, Color color)
    {
        var go = new GameObject("Image", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Image image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        if (sprite != null && sprite.border != Vector4.zero) image.type = UnityEngine.UI.Image.Type.Sliced;
        return image;
    }

    // 글자 버튼: 평소 dim, 마우스를 올리면 하얗게, 누르면 accent
    public static Button TextButton(Text text, Color dim, Color accent, Action onClick)
    {
        text.raycastTarget = true;
        Button button = text.gameObject.AddComponent<Button>();
        button.targetGraphic = text;
        ColorBlock colors = button.colors;
        colors.normalColor = dim;
        colors.highlightedColor = Color.white;
        colors.selectedColor = dim;
        colors.pressedColor = accent;
        colors.disabledColor = new Color(dim.r, dim.g, dim.b, dim.a * 0.4f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        Navigation nav = button.navigation;
        nav.mode = Navigation.Mode.None;
        button.navigation = nav;
        button.onClick.AddListener(() => onClick());
        return button;
    }

    public static void EnsureEventSystem()
    {
        if (EventSystem.current != null || UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null) return;
        var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        UnityEngine.Object.DontDestroyOnLoad(go);
    }

    // ===== 코드로 만드는 스프라이트 =====
    // 둥근 사각형. thickness > 0이면 테두리만. 9-slice 테두리 = 반지름
    public static Sprite RoundRect(int width, int height, float radius, float thickness)
    {
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, radius, width - radius);
                float cy = Mathf.Clamp(y + 0.5f, radius, height - radius);
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                float a = Mathf.Clamp01(radius - d + 0.5f);
                if (thickness > 0f) a *= Mathf.Clamp01(d - (radius - thickness) + 0.5f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
            new Vector4(radius, radius, radius, radius));
    }

    // 왼쪽 진하고 오른쪽으로 사라지는 가로 그라데이션
    public static Sprite Gradient(int width)
    {
        var tex = new Texture2D(width, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int x = 0; x < width; x++) tex.SetPixel(x, 0, new Color(1f, 1f, 1f, 1f - Mathf.SmoothStep(0f, 1f, x / (float)(width - 1))));
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, width, 1), new Vector2(0f, 0.5f), 100f);
    }

    // 타이틀 · 메뉴 공용 붓질 한 획: 왼쪽에서 진하게 시작해 오른쪽으로 갈수록 가늘어지며 갈라짐
    public static Sprite BrushStroke(int w, int h)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            float v = (y + 0.5f) / h * 2f - 1f;
            float bristle = Mathf.PerlinNoise(y * 0.35f, 3.1f);              // 붓털마다 끝나는 자리가 다름
            float end = Mathf.Lerp(0.55f, 1f, bristle);
            float streak = Mathf.Lerp(0.55f, 1f, Mathf.PerlinNoise(y * 0.9f, 7.7f));
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w;
                float center = 0.08f * Mathf.Sin(u * 5f);
                float thick = Mathf.Lerp(0.95f, 0.45f, u);
                float body = Mathf.Clamp01((thick - Mathf.Abs(v - center)) * 6f);
                float head = Mathf.SmoothStep(0f, 0.06f, u);
                float tail = 1f - Mathf.SmoothStep(end - 0.25f, end, u);
                float a = body * head * tail * streak;
                pixels[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0f, 0.5f), 100f);
    }

    // ===== [변경] 공용 스타일 (타이틀 · 설정 · 스킬트리 · 결과 · 로딩이 같은 모양을 쓰도록) =====
    public static readonly Color Gold = new Color(0.95f, 0.8f, 0.45f);

    static Sprite gradient, brush;
    public static Sprite SharedGradient => gradient != null ? gradient : gradient = Gradient(256);
    public static Sprite SharedBrush => brush != null ? brush : brush = BrushStroke(512, 96);

    // 세리프(Cinzel) 글꼴: 씬의 설정 메뉴에 연결된 것을 빌려 씀. 없으면 fallback (Cinzel에는 한글이 없으니 영문에만)
    public static Font DisplayFont(Font fallback)
    {
        SettingsMenu settings = UnityEngine.Object.FindAnyObjectByType<SettingsMenu>(FindObjectsInactive.Include);
        Font font = settings != null ? settings.DisplayFont : null;
        return font != null ? font : fallback;
    }

    // 45도 돌린 작은 사각형. cx·cy는 중심 (좌상단 기준 픽셀)
    public static Image Diamond(Transform parent, float cx, float cy, float size, Color color)
    {
        Image gem = Image(parent, null, color);
        gem.raycastTarget = false;
        RectTransform r = gem.rectTransform;
        r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
        r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = new Vector2(cx, -cy);
        r.sizeDelta = new Vector2(size, size);
        r.localRotation = Quaternion.Euler(0f, 0f, 45f);
        return gem;
    }

    // ◆── 장식선: 마름모에서 시작해 오른쪽으로 사라지는 금빛 선. x는 마름모 왼쪽 끝
    public static void Ornament(Transform parent, float x, float y, float width, Color color)
    {
        Diamond(parent, x + 4.5f, y, 9f, color);
        Image line = Image(parent, SharedGradient, new Color(color.r, color.g, color.b, color.a * 0.75f));
        line.raycastTarget = false;
        Place(line.rectTransform, x + 16f, y, width, 2f);
    }

    // 고른 항목 뒤에 깔리는 붓질
    public static Image BrushBehind(Transform parent, float x, float y, float width, float height, Color color)
    {
        Image b = Image(parent, SharedBrush, color);
        b.raycastTarget = false;
        Place(b.rectTransform, x, y, width, height);
        return b;
    }
}
