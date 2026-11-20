using UnityEngine;
using UnityEngine.UI;

// 개발자 옵션 (ESC > DEVELOPER, 시연용). 값은 실행 중에만 유지 (저장 안 함, 씬을 넘어도 유지)
//  - 무적: 피해 · 잡기를 받지 않음. 패링 · 역뢰 · 저스트 회피 판정은 그대로 (PlayerHealth가 그 뒤에서 확인)
//  - 레벨업: 경험치 없이 한 단계 (레벨업 연출 · 스킬 포인트 그대로)
//  - 게이지 채우기: 뇌기 · 궁극기 가득
// 무적이 켜져 있으면 화면 왼쪽 위에 작게 "DEV · 무적" 표시 (시연 중 켜 둔 걸 잊지 않게)
public static class DevCheats
{
    static bool godMode;
    static Text badge;

    public static bool GodMode
    {
        get => godMode;
        set
        {
            godMode = value;
            UpdateBadge();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay()
    {
        godMode = false;
        badge = null;
    }

    public static PlayerStats Stats => Object.FindAnyObjectByType<PlayerStats>();

    public static void LevelUp()
    {
        PlayerStats stats = Stats;
        if (stats != null) stats.LevelUp();
    }

    public static void FillGauges()
    {
        PlayerThunder thunder = Object.FindAnyObjectByType<PlayerThunder>();
        if (thunder != null) thunder.Add(thunder.MaxGauge);
        PlayerUltimate ultimate = Object.FindAnyObjectByType<PlayerUltimate>();
        if (ultimate != null) ultimate.SetGauge(PlayerUltimate.MaxBars);
    }

    // 씬을 넘어도 보이도록 DontDestroyOnLoad 캔버스 하나
    static void UpdateBadge()
    {
        if (badge == null)
        {
            if (!godMode) return;
            var go = new GameObject("DevBadge");
            Object.DontDestroyOnLoad(go);
            Canvas canvas = UIKit.CreateCanvas(go.transform, "DevBadgeCanvas", 190);
            canvas.GetComponent<GraphicRaycaster>().enabled = false;
            SettingsMenu menu = Object.FindAnyObjectByType<SettingsMenu>(FindObjectsInactive.Include);
            Font font = menu != null && menu.BodyFont != null ? menu.BodyFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            badge = UIKit.Text(canvas.transform, "DEV · 무적", font, 18,
                new Color(1f, 0.85f, 0.4f, 0.75f), TextAnchor.MiddleLeft);
            UIKit.Place(badge.rectTransform, 24f, 24f, 300f, 28f);
        }
        badge.gameObject.SetActive(godMode);
    }
}
