using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// "기록되었습니다" 알림 (오른쪽 아래, 1.6초)
public class SaveToast : MonoBehaviour
{
    static SaveToast current;
    CanvasGroup group;

    public static void Show()
    {
        if (current != null) Destroy(current.gameObject);
        var go = new GameObject("SaveToast");
        current = go.AddComponent<SaveToast>();
        current.Build();
    }

    void Build()
    {
        Canvas canvas = UIKit.CreateCanvas(transform, "SaveToastCanvas", 90);
        group = canvas.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        SettingsMenu settings = FindAnyObjectByType<SettingsMenu>();
        Font font = settings != null && settings.BodyFont != null ? settings.BodyFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Text text = UIKit.Text(canvas.transform, "◆  기록되었습니다", font, 22, UIKit.Gold, TextAnchor.MiddleRight);
        RectTransform rt = text.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.sizeDelta = new Vector2(420f, 40f);
        rt.anchoredPosition = new Vector2(-48f, 130f);
        StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        for (float t = 0f; t < 0.25f; t += Time.unscaledDeltaTime) { group.alpha = t / 0.25f; yield return null; }
        group.alpha = 1f;
        for (float t = 0f; t < 1.2f; t += Time.unscaledDeltaTime) yield return null;
        for (float t = 0f; t < 0.4f; t += Time.unscaledDeltaTime) { group.alpha = 1f - t / 0.4f; yield return null; }
        if (current == this) current = null;
        Destroy(gameObject);
    }
}
