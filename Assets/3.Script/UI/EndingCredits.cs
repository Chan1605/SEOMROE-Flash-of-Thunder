using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// 엔딩 크레딧: 결과 화면 CONTINUE(첫 토벌)에서 재생 → 끝나면 onFinished (마을로)
// 내용은 Resources/Credits/credits.txt (없으면 코드 기본값). 한 줄 규칙:
//   "! 글"  큰 제목 (세리프)          "~ 글"  제목 아래 부제 (금빛)
//   "# 글"  분류 머리글 (금빛 + 장식선)  "- 글"  작은 설명 (흐리게)
//   "@ 글"  마지막 문구: 화면 가운데에 멈춰 잠시 머무름
//   그 외   이름 줄 · 빈 줄은 간격
// [변경] 시작할 때 엔딩 스팅어(Resources/Audio/Ending/ending_sting)를 한 번 틀고, 그동안 배경음(ending)은 줄임
// Space를 누르고 있으면 빠르게, ESC는 건너뛰기. 게임은 멈춘 상태라 전부 실제 시간
public class EndingCredits : MonoBehaviour
{
    public const string SeenFlag = "CreditsSeen";
    public static bool Seen => RunState.HasFlag(SeenFlag);
    public static bool IsPlaying { get; private set; }

    const float Speed = 70f;       // 초당 위로 흐르는 픽셀 (1080 기준)
    const float FastScale = 5f;
    const float FinalHold = 3f;
    const string StingPath = "Audio/Ending/ending_sting"; // [변경]
    static readonly Color Dim = new Color(1f, 1f, 1f, 0.55f);

    Action onFinished;
    Font titleFont, bodyFont, displayFont;
    Image backdrop;
    RectTransform scroll, finalLine;
    CanvasGroup group, hint;
    AudioSource sting; // [변경]
    float contentHeight;
    bool skip;

    public static void Play(Font title, Font body, Action onFinished)
    {
        if (IsPlaying) return;
        var go = new GameObject("EndingCredits");
        var c = go.AddComponent<EndingCredits>();
        c.titleFont = title;
        c.bodyFont = body != null ? body : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        c.displayFont = UIKit.DisplayFont(title);
        c.onFinished = onFinished;
        IsPlaying = true;
        c.Build();
        c.StartCoroutine(c.Run());
    }

    void OnDestroy() => IsPlaying = false;

    void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame) skip = true;
    }

    bool Fast
    {
        get
        {
            Keyboard kb = Keyboard.current;
            Mouse m = Mouse.current;
            return (kb != null && (kb.spaceKey.isPressed || kb.enterKey.isPressed)) || (m != null && m.leftButton.isPressed);
        }
    }

    void Build()
    {
        Canvas canvas = UIKit.CreateCanvas(transform, "CreditsCanvas", 170); // 결과 화면(150) 위
        group = canvas.gameObject.AddComponent<CanvasGroup>();
        backdrop = UIKit.Image(canvas.transform, null, new Color(0.005f, 0.006f, 0.012f, 1f));
        UIKit.Stretch(backdrop.rectTransform);
        group.alpha = 0f;

        var go = new GameObject("Scroll", typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);
        scroll = (RectTransform)go.transform;
        UIKit.Stretch(scroll);

        float y = 1080f + 80f; // 화면 아래에서 시작
        foreach (string raw in Lines())
            y = AddLine(raw.TrimEnd(), y);
        contentHeight = y;

        var hintGo = new GameObject("Hint", typeof(RectTransform));
        hintGo.transform.SetParent(canvas.transform, false);
        UIKit.Stretch((RectTransform)hintGo.transform);
        hint = hintGo.AddComponent<CanvasGroup>();
        Text h = UIKit.Text(hintGo.transform, "Space 빠르게 · ESC 건너뛰기", bodyFont, 18, new Color(1f, 1f, 1f, 0.35f), TextAnchor.MiddleRight);
        UIKit.Place(h.rectTransform, 1920f - 560f, 1040f, 500f, 28f);
    }

    // 핵심: 줄 종류마다 글꼴 · 크기 · 간격을 정하고 위에서부터 쌓음 (좌표는 1920x1080 왼쪽 위 기준)
    float AddLine(string line, float y)
    {
        if (string.IsNullOrEmpty(line)) return y + 40f;
        char tag = line[0];
        string text = line.Length > 1 && "!~#-@".IndexOf(tag) >= 0 ? line.Substring(1).Trim() : line.Trim();
        switch (tag)
        {
            case '!':
                Center(UIKit.Text(scroll, text, displayFont, 72, Color.white, TextAnchor.MiddleCenter), y + 50f, 110f)
                    .gameObject.AddComponent<Shadow>().effectDistance = new Vector2(0f, -4f);
                return y + 110f;
            case '~':
                Center(UIKit.Text(scroll, text, bodyFont, 34, UIKit.Gold, TextAnchor.MiddleCenter), y + 24f, 50f);
                return y + 60f;
            case '#':
                y += 50f;
                Center(UIKit.Text(scroll, text, bodyFont, 28, UIKit.Gold, TextAnchor.MiddleCenter), y, 44f);
                UIKit.Diamond(scroll, 960f, y + 34f, 9f, new Color(UIKit.Gold.r, UIKit.Gold.g, UIKit.Gold.b, 0.8f));
                return y + 64f;
            case '-':
                Center(UIKit.Text(scroll, text, bodyFont, 22, Dim, TextAnchor.MiddleCenter), y + 16f, 34f);
                return y + 36f;
            case '@':
                y += 260f;
                finalLine = Center(UIKit.Text(scroll, text, displayFont, 44, Color.white, TextAnchor.MiddleCenter), y, 70f).rectTransform;
                return y + 60f;
            default:
                Center(UIKit.Text(scroll, text, bodyFont, 30, Color.white, TextAnchor.MiddleCenter), y + 22f, 46f);
                return y + 46f;
        }
    }

    static Text Center(Text t, float centerY, float height)
    {
        UIKit.Place(t.rectTransform, 0f, centerY, 1920f, height);
        return t;
    }

    IEnumerator Run()
    {
        PlaySting(); // [변경]
        yield return Fade(0.8f, k => group.alpha = k);

        // 마지막 문구(@)가 있으면 화면 가운데(540)에 오면 멈춤, 없으면 전부 화면 위로 지나갈 때까지
        float finalY = finalLine != null ? -finalLine.anchoredPosition.y : contentHeight;
        float stopAt = finalLine != null ? finalY - 540f : contentHeight;
        float offset = 0f;
        while (offset < stopAt && !skip)
        {
            offset += Speed * (Fast ? FastScale : 1f) * Time.unscaledDeltaTime;
            scroll.anchoredPosition = new Vector2(0f, Mathf.Min(offset, stopAt));
            yield return null;
        }

        if (!skip)
        {
            hint.alpha = 0f;
            for (float t = 0f; t < FinalHold && !skip; t += Time.unscaledDeltaTime) yield return null;
        }

        RunState.SetFlag(SeenFlag);
        float stingStart = sting != null ? sting.volume : 0f; // [변경] 건너뛰어도 뚝 끊기지 않게 함께 줄임
        yield return Fade(0.8f, k =>
        {
            scroll.localScale = Vector3.one * (1f + 0.03f * k); // 살짝 다가오며
            if (sting != null) sting.volume = stingStart * (1f - k);
        });
        onFinished?.Invoke(); // 마을로 (SceneTransition이 검게 덮고 씬을 바꾸면 이 오브젝트도 사라짐)
    }

    // [변경] 스팅어는 음악이라 배경음 볼륨을 따름
    void PlaySting()
    {
        AudioClip clip = Resources.Load<AudioClip>(StingPath);
        if (clip == null) return;
        sting = gameObject.AddComponent<AudioSource>();
        sting.playOnAwake = false;
        sting.spatialBlend = 0f;
        sting.ignoreListenerPause = true;
        sting.clip = clip;
        sting.volume = AudioOptions.BgmVolume;
        sting.Play();
        BgmDirector.Duck(clip.length);
    }

    static IEnumerator Fade(float duration, Action<float> step)
    {
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            step(Mathf.SmoothStep(0f, 1f, t / duration));
            yield return null;
        }
        step(1f);
    }

    static IEnumerable<string> Lines()
    {
        TextAsset asset = Resources.Load<TextAsset>("Credits/credits");
        string text = asset != null ? asset.text : DefaultText;
        return text.Replace("\r", "").Split('\n');
    }

    const string DefaultText =
        "! SEOMROE: Flash of Thunder\n~ 섬  뢰\n\n# 개발\n개발자\n\n@ Thank you for playing";
}
