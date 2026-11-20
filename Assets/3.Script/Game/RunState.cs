using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// 씬을 넘어갈 때 이어받는 진행 상태 (한 판 동안만, 저장 파일 없음)
//  - 스킬트리: 남은 포인트 · 해금 목록 → SkillTreeMenu.Awake가 초기화 대신 복원, 수련 패시브는 Start에서 다시 적용
//  - 레벨 · 경험치 · 추가 스탯 → 새 씬이 로드되면 PlayerStats에 바로 넣음 (레벨업 이벤트 없이)
//  - 표시(flag): 한 판 동안 남길 표시 (예: 던전 클리어 → 본편의 던전 입구 끔)
//  - SpawnAt: 도착 씬에서 플레이어를 옮길 오브젝트 경로 (던전 → 본편 보스 구역 입구)
// SceneTransition.Load가 떠나기 직전에 Capture. 에디터에서 씬을 바로 플레이하면 비어 있어 예전과 같음
public static class RunState
{
    static bool hasSkills, hasStats;
    static int points;
    static readonly List<SkillId> unlocked = new List<SkillId>();
    static int level, xp;
    static Vector4 bonus;
    static readonly HashSet<string> flags = new HashSet<string>();

    public static string SpawnAt { get; set; }
    public static float UltimateGauge { get; private set; } // 궁극기 게이지 (PlayerUltimate가 Start에서 읽음)
    public static float ThunderGauge { get; private set; }  // [변경] 뇌기 (PlayerThunder가 Start 다음 프레임에 읽음 — 수련 패시브로 칸이 늘어난 뒤)
    public static bool HasFlag(string flag) => flags.Contains(flag);
    public static void SetFlag(string flag) => flags.Add(flag);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay()
    {
        Clear();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    // 타이틀의 "새 게임" 등 처음부터 시작할 때
    public static void Clear()
    {
        hasSkills = hasStats = false;
        unlocked.Clear();
        UltimateGauge = 0f;
        ThunderGauge = 0f; // [변경]
        flags.Clear();
    }

    public static void Capture()
    {
        hasSkills = SkillTree.Active;
        if (hasSkills)
        {
            points = SkillTree.Points;
            unlocked.Clear();
            unlocked.AddRange(SkillTree.Unlocked);
        }

        PlayerUltimate ultimate = Object.FindAnyObjectByType<PlayerUltimate>();
        UltimateGauge = ultimate != null ? ultimate.Gauge : 0f;
        PlayerThunder thunder = Object.FindAnyObjectByType<PlayerThunder>();
        ThunderGauge = thunder != null ? thunder.Gauge : 0f; // [변경] 던전 → 흑뢰 등 씬을 넘어도 뇌기 유지

        PlayerStats stats = Object.FindAnyObjectByType<PlayerStats>();
        hasStats = stats != null;
        if (hasStats)
        {
            level = stats.Level;
            xp = stats.Xp;
            bonus = stats.Bonus;
        }
    }

    // 저장 파일(SaveGame)용: 지금 상태를 JSON으로 / JSON에서 되살림
    [System.Serializable]
    class Saved
    {
        public int version = 1;
        public bool skills;
        public int points;
        public int[] unlocked;
        public bool stats;
        public int level, xp;
        public Vector4 bonus;
        public string[] flags;
        public float ultimate;
    }

    public static string ToJson()
    {
        var s = new Saved
        {
            skills = hasSkills,
            points = points,
            unlocked = unlocked.ConvertAll(id => (int)id).ToArray(),
            stats = hasStats,
            level = level,
            xp = xp,
            bonus = bonus,
            flags = new List<string>(flags).ToArray(),
            ultimate = UltimateGauge,
        };
        return JsonUtility.ToJson(s);
    }

    public static bool FromJson(string json)
    {
        Saved s = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<Saved>(json);
        if (s == null) return false;
        Clear();
        hasSkills = s.skills;
        points = s.points;
        if (s.unlocked != null) foreach (int id in s.unlocked) unlocked.Add((SkillId)id);
        hasStats = s.stats;
        level = s.level;
        xp = s.xp;
        bonus = s.bonus;
        if (s.flags != null) foreach (string f in s.flags) flags.Add(f);
        UltimateGauge = s.ultimate;
        return true;
    }

    public static int SavedLevel => hasStats ? level : 1;

    // SkillTreeMenu.Awake에서 호출. 복원했으면 true (그땐 Activate로 초기화하지 않음)
    public static bool TryRestoreSkills()
    {
        if (!hasSkills) return false;
        SkillTree.Restore(points, unlocked);
        return true;
    }

    // 씬 오브젝트의 Awake·OnEnable 뒤, Start 전에 불림
    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == SceneTransition.LoadingScene) return; // 로딩 씬은 건너뜀 (옮길 자리 · 플레이어가 없음)
        MoveToSpawn();
        if (!hasStats) return;
        PlayerStats stats = Object.FindAnyObjectByType<PlayerStats>();
        if (stats != null) stats.Restore(level, xp, bonus);
    }

    // 카메라 Start · 첫 LateUpdate 전이라 카메라도 새 자리에서 바로 시작함
    static void MoveToSpawn()
    {
        string path = SpawnAt;
        SpawnAt = null;
        if (string.IsNullOrEmpty(path)) return;
        GameObject point = GameObject.Find(path);
        PlayerHealth player = Object.FindAnyObjectByType<PlayerHealth>();
        if (point == null || player == null)
        {
            Debug.LogWarning($"[RunState] 도착 위치 {path}를 찾지 못했습니다.");
            return;
        }
        var cc = player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        player.transform.SetPositionAndRotation(point.transform.position, point.transform.rotation);
        if (cc != null) cc.enabled = true;
    }
}
