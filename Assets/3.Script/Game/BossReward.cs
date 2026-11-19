using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// 보스 흑뢰 처치 보상 (연습장 1:1 제외): 첫 토벌 스킬 포인트 +3, 다시 잡으면 +1 → 표시 BossCleared + 저장
// 화면 위쪽에 "흑뢰 토벌" 배너. 결과 화면은 RunRecord가 그 뒤에 띄움
public class BossReward : MonoBehaviour
{
    public const string ClearedFlag = "BossCleared";
    const int FirstPoints = 3;
    const int RepeatPoints = 1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        EnemySwordsman.AnyKilled -= OnKilled;
        EnemySwordsman.AnyKilled += OnKilled;
    }

    static void OnKilled(EnemySwordsman e)
    {
        if (e == null || !e.IsBoss || e.IsTrainer || FindAnyObjectByType<TrainingMode>() != null) return;

        bool first = !RunState.HasFlag(ClearedFlag);
        int points = first ? FirstPoints : RepeatPoints;
        if (SkillTree.Active) SkillTree.AddPoints(points);
        RunState.SetFlag(ClearedFlag);

        var go = new GameObject("BossReward");
        go.AddComponent<BossReward>().StartCoroutine(go.GetComponent<BossReward>().Show(e.BossName, first, points));
    }

    IEnumerator Show(string bossName, bool first, int points)
    {
        Canvas canvas = UIKit.CreateCanvas(transform, "BossRewardCanvas", 60);
        var group = canvas.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        SettingsMenu settings = FindAnyObjectByType<SettingsMenu>();
        Font body = settings != null && settings.BodyFont != null ? settings.BodyFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        UIKit.BrushBehind(canvas.transform, 660f, 250f, 600f, 110f, new Color(0f, 0f, 0f, 0.55f));
        Text title = UIKit.Text(canvas.transform, $"{bossName} 토벌", body, 52, UIKit.Gold, TextAnchor.MiddleCenter);
        UIKit.Place(title.rectTransform, 560f, 236f, 800f, 70f);
        string sub = first ? $"첫 토벌  ·  스킬 포인트 +{points}" : $"스킬 포인트 +{points}";
        Text info = UIKit.Text(canvas.transform, sub + "   (화톳불에서 사용)", body, 24, Color.white, TextAnchor.MiddleCenter);
        UIKit.Place(info.rectTransform, 560f, 290f, 800f, 36f);

        for (float t = 0f; t < 0.4f; t += Time.unscaledDeltaTime) { group.alpha = t / 0.4f; yield return null; }
        group.alpha = 1f;
        for (float t = 0f; t < 2.6f; t += Time.unscaledDeltaTime) yield return null;
        SaveGame.Save(); // 보상 반영 뒤 저장 (화면 오른쪽 아래 "기록되었습니다")
        for (float t = 0f; t < 0.6f; t += Time.unscaledDeltaTime) { group.alpha = 1f - t / 0.6f; yield return null; }
        Destroy(gameObject);
    }
}
