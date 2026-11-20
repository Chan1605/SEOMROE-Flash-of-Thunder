using UnityEngine;
using UnityEngine.SceneManagement;

// 배경음 진행기: 지금 상황에 맞는 곡 이름을 정하고 Resources/Audio/BGM/<이름>을 크로스페이드로 틂 (씬 수정 없이 자동 생성, 씬을 넘어도 유지)
// 곡 파일만 넣으면 바로 적용. 없는 곡은 대체 이름으로, 그것도 없으면 조용히 줄임
//   village · village_dawn(흑뢰 토벌 뒤, 없으면 village) · training(수련장 · 연습장) · field(산문 이동 중)
//   result(결과 화면, 없으면 ending) · battle(구역 전투, 없으면 field) · trial(시련의 터, 없으면 battle) · dungeon · boss · boss_phase2(없으면 boss) · ending(엔딩 크레딧)
// 타이틀은 TitleScreen이 직접 틀어서 여기서는 끔. 볼륨 = AudioOptions.BgmVolume
public class BgmDirector : MonoBehaviour
{
    const string Folder = "Audio/BGM/";
    const float FadeTime = 1.6f;
    const float CheckInterval = 0.5f;

    static BgmDirector instance;
    static readonly System.Collections.Generic.Dictionary<string, AudioClip> cache = new System.Collections.Generic.Dictionary<string, AudioClip>(); // 없는 곡도 null로 기억

    AudioSource a, b;      // 번갈아 쓰는 두 소스 (하나는 켜지고 하나는 꺼짐)
    AudioSource current;
    string currentKey;
    float checkTimer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (instance != null) return;
        var go = new GameObject("BgmDirector");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<BgmDirector>();
    }

    void Awake()
    {
        a = NewSource();
        b = NewSource();
        current = a;
    }

    AudioSource NewSource()
    {
        var s = gameObject.AddComponent<AudioSource>();
        s.loop = true;
        s.playOnAwake = false;
        s.spatialBlend = 0f;
        s.ignoreListenerPause = true; // 일시정지(AudioListener.pause) 중에도 음악은 흐름
        s.volume = 0f;
        return s;
    }

    void Update()
    {
        checkTimer -= Time.unscaledDeltaTime;
        if (checkTimer <= 0f)
        {
            checkTimer = CheckInterval;
            string key = Resolve(PickKey(), out AudioClip clip);
            if (key != currentKey) Switch(key, clip);
        }

        // 크로스페이드: 지금 소스는 목표 볼륨으로, 다른 소스는 0으로
        float target = AudioOptions.BgmVolume;
        float step = Time.unscaledDeltaTime / FadeTime;
        AudioSource other = current == a ? b : a;
        current.volume = Mathf.MoveTowards(current.volume, current.clip != null ? target : 0f, step * Mathf.Max(target, 0.01f));
        other.volume = Mathf.MoveTowards(other.volume, 0f, step);
        if (other.isPlaying && other.volume <= 0f) other.Stop();
    }

    void Switch(string key, AudioClip clip)
    {
        currentKey = key;
        if (current.clip == clip && current.isPlaying) return;
        current = current == a ? b : a;
        current.clip = clip;
        current.volume = 0f;
        if (clip != null) current.Play();
    }

    // 핵심: 상황 → 곡 이름 (위에서부터 우선)
    static string PickKey()
    {
        string scene = SceneManager.GetActiveScene().name;
        if (scene == TitleScreen.SceneName || scene == SceneTransition.LoadingScene) return null;
        if (EndingCredits.IsPlaying) return "ending";
        if (ResultScreen.IsShowing) return "result"; // 결과 화면 (없으면 ending)
        if (scene == VillagePortal.VillageScene) return RunState.HasFlag(BossReward.ClearedFlag) ? "village_dawn" : "village";
        if (scene == "Tutorial" || scene == "Training") return BossFight(out bool p2) ? (p2 ? "boss_phase2" : "boss") : "training";
        if (BossFight(out bool phase2)) return phase2 ? "boss_phase2" : "boss";
        if (scene == "Dungeon") return "dungeon";
        ZoneArena zone = ZoneArena.Fighting;
        if (zone != null) return zone.Endless ? "trial" : "battle";
        return "field";
    }

    // 보스 체력 막대가 뜨는 조건과 같음 (등장 연출 중엔 아직 아님)
    static bool BossFight(out bool phase2)
    {
        EnemySwordsman boss = EnemySwordsman.ActiveBoss;
        phase2 = boss != null && boss.IsPhase2;
        return boss != null && boss.IsAlive && boss.CanBeTargeted;
    }

    // 없는 곡은 대체 이름으로
    static string Resolve(string key, out AudioClip clip)
    {
        clip = null;
        while (!string.IsNullOrEmpty(key))
        {
            if (!cache.TryGetValue(key, out clip)) cache[key] = clip = Resources.Load<AudioClip>(Folder + key);
            if (clip != null) return key;
            key = Fallback(key);
        }
        return null;
    }

    static string Fallback(string key)
    {
        switch (key)
        {
            case "village_dawn": return "village";
            case "result": return "ending";
            case "boss_phase2": return "boss";
            case "trial": return "battle";
            case "battle": return "field";
            default: return null;
        }
    }
}
