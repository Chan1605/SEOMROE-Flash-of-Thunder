using UnityEngine;

// 저장 (한 칸): RunState(레벨 · 경험치 · 스킬 · 표시)를 PlayerPrefs에 JSON으로
//  - 자동 저장: 마을에 들어올 때(VillagePortal) · 화톳불에서 쉬고 스킬트리를 닫을 때(Shrine)
//  - 불러오기: 타이틀 LOAD GAME → 저장본을 RunState에 넣고 마을로 (keepSnapshot)
//  - 저장하면 오른쪽 아래에 "기록되었습니다"가 잠깐 뜸
public static class SaveGame
{
    const string Key = "Save.Slot1";
    const string TimeKey = "Save.Slot1.Time";
    const string LevelKey = "Save.Slot1.Level";

    public static bool Exists => PlayerPrefs.HasKey(Key);

    // 타이틀 이어하기 옆에 보이는 요약 (예: "Lv 7 · 11/19 15:30")
    public static string Summary => Exists ? $"Lv {PlayerPrefs.GetInt(LevelKey, 1)} · {PlayerPrefs.GetString(TimeKey, "")}" : "";

    public static void Save()
    {
        RunState.Capture();
        PlayerPrefs.SetString(Key, RunState.ToJson());
        PlayerPrefs.SetString(TimeKey, System.DateTime.Now.ToString("MM/dd HH:mm"));
        PlayerPrefs.SetInt(LevelKey, RunState.SavedLevel);
        PlayerPrefs.Save();
        SaveToast.Show();
    }

    // 저장은 수련을 마친 뒤(마을 · 본편)에만 생기므로 불러오면 수련 완료로 봄
    public static bool Load()
    {
        if (!Exists || !RunState.FromJson(PlayerPrefs.GetString(Key))) return false;
        RunState.SetFlag(VillagePortal.TutorialDoneFlag);
        return true;
    }

    public static void Delete()
    {
        PlayerPrefs.DeleteKey(Key);
        PlayerPrefs.DeleteKey(TimeKey);
        PlayerPrefs.DeleteKey(LevelKey);
        PlayerPrefs.Save();
    }
}
