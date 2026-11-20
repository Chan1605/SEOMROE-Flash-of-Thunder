using System.Collections;
using System.Collections.Generic;
using SickscoreGames.HUDNavigationSystem;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// 던전(뇌운 협곡) 진행 — Tools/Setup/Dungeon Scene이 배치
//  가운데 허브에서 동 · 서 · 북 갈래 방(각자 ZoneArena)을 원하는 순서로 깸 → 갈래마다 스킬 포인트
//  셋 다 깨면 허브 구역(ZoneArena, 처음엔 꺼 둠)이 깨어나 수문장(중간보스) 전투 → 이기면 남쪽 출구(본편 보스 구역 입구로)
//  목표 안내: 오른쪽 위에 남은 목표 + 거리, 나침반 · 화면 밖 화살표에도 같은 목표 표식 (클리어한 갈래는 빠짐)
//  뇌문: 허브의 각 갈래 문 앞(아직 안 깬 곳) · 깬 갈래 방 가운데에 번개 문 → E로 바로 이동 (긴 통로를 오가지 않게)
//  날씨: 비 + 번개 · 천둥 (DungeonWeather)
public class DungeonDirector : MonoBehaviour
{
    public const string ClearedFlag = "DungeonCleared";

    [SerializeField] List<ZoneArena> branches = new List<ZoneArena>();
    [SerializeField] ZoneArena hub;
    [SerializeField] GameObject exitMarker;       // 남쪽 출구(SceneExit). 수문장을 이기면 켬
    [SerializeField] GameObject miniBossPrefab;   // 허브 웨이브의 정예 = 수문장
    [SerializeField] string miniBossName = "뇌운의 수문장";
    [SerializeField] Vector3 miniBossScale = new Vector3(5f, 1.25f, 6f); // 체력 · 공격력 · 경험치 배율
    [SerializeField] int pointsPerBranch = 1;
    [SerializeField] Font font;
    [SerializeField] HUDNavigationElementSettings markerSettings; // 본편 구역 목표와 같은 설정 (Nav_Objective)
    [SerializeField] float refreshInterval = 0.2f;
    [SerializeField] float roomRadius = 21f; // 이 안에 들어오면 그 방에 온 것으로 봄 (벽 반지름 19 + 여유)
    [SerializeField] float hubGateDistance = 11f;    // 허브 가운데에서 각 갈래 문 쪽으로 이만큼 → 뇌문 자리
    [SerializeField] float branchArrive = 17.5f;     // 갈래 방 가운데에서 입구 쪽으로 이만큼 → 도착 자리 (전투 시작 범위 바로 밖)
    [SerializeField] float hubArrive = 8f;           // 허브로 돌아올 때 그 갈래 문 쪽 이만큼
    [SerializeField] float gateRange = 2.4f;
    [SerializeField] string[] spawnAllRooms = { "Dungeon_North" }; // [변경] 웨이브 대신 한 번에 전부 나오는 갈래 (오브젝트 이름)

    static readonly Color Gold = new Color(0.95f, 0.8f, 0.45f);

    readonly HashSet<ZoneArena> done = new HashSet<ZoneArena>();
    readonly Dictionary<ZoneArena, GameObject> markers = new Dictionary<ZoneArena, GameObject>();
    GameObject exitNav;
    Transform player;
    float refreshTimer;
    Text objective, banner;
    CanvasGroup bannerGroup;
    float bannerTimer;

    void Start()
    {
        if (hub != null) hub.enabled = false; // 봉인을 다 풀기 전엔 허브에서 전투가 시작되지 않음
        if (exitMarker != null) exitMarker.SetActive(false);
        foreach (ZoneArena b in branches)
            if (b != null) b.Cleared += () => OnBranchCleared(b);
        foreach (ZoneArena b in branches) // [변경] 북쪽 버티기 → 한 번에 전부
            if (b != null && System.Array.IndexOf(spawnAllRooms, b.name) >= 0) b.SpawnAllAtOnce();
        if (hub != null) hub.Cleared += OnHubCleared;
        ZoneArena.AnyEnemyCreated += OnEnemyCreated;

        PlayerHealth health = FindAnyObjectByType<PlayerHealth>();
        if (health != null) player = health.transform;
        foreach (ZoneArena b in branches)
            if (b != null) markers[b] = CreateMarker($"Nav_{b.DisplayName}", b.transform.position);
        if (hub != null) markers[hub] = CreateMarker("Nav_Hub", hub.transform.position);
        if (exitMarker != null) exitNav = CreateMarker("Nav_Exit", exitMarker.transform.position);

        BuildUi();
        BuildGates();
        gameObject.AddComponent<DungeonWeather>();
        UpdateObjective();
    }

    // 나침반 · 화면 밖 화살표용 표식. 꺼 둔 채 만들고 설정을 넣은 뒤 켜야 표식이 제대로 생김
    GameObject CreateMarker(string name, Vector3 position)
    {
        if (markerSettings == null) return null;
        var go = new GameObject(name);
        go.SetActive(false);
        go.transform.SetParent(transform, false);
        go.transform.position = position + Vector3.up * 2.5f;
        go.AddComponent<HUDNavigationElement>().Settings = markerSettings;
        go.AddComponent<NavTarget>().Color = Gold;
        return go;
    }

    void OnDestroy() => ZoneArena.AnyEnemyCreated -= OnEnemyCreated;

    void OnBranchCleared(ZoneArena branch)
    {
        if (!done.Add(branch)) return;
        if (SkillTree.Active && pointsPerBranch > 0) SkillTree.AddPoints(pointsPerBranch);
        UpdateObjective();

        if (done.Count < branches.Count)
        {
            ShowBanner($"구역 해제  {done.Count} / {branches.Count}   ·   스킬 포인트 +{pointsPerBranch}");
            return;
        }
        if (hub != null) hub.enabled = true; // 핵심: 마지막 봉인 → 허브가 전투 구역으로 깨어남
        ShowBanner("전지역 해제 — 협곡 중앙으로");
    }

    // 허브 웨이브에서 정예가 생기는 순간(Start 전) 수문장으로 만듦
    void OnEnemyCreated(ZoneArena arena, EnemySwordsman enemy)
    {
        if (arena != hub || enemy == null || miniBossPrefab == null) return;
        if (!enemy.name.StartsWith(miniBossPrefab.name)) return;
        enemy.ApplyScaling(miniBossScale.x, miniBossScale.y, miniBossScale.z);
        enemy.ShowAsBoss(miniBossName);
    }

    void OnHubCleared()
    {
        RunState.SetFlag(ClearedFlag);
        if (exitMarker != null) exitMarker.SetActive(true);
        UpdateObjective();
        ShowBanner($"{miniBossName} 격파 — 남쪽 길이 열렸다");
    }

    // 핵심: 지금 갈 수 있는 목표만 보여 줌 (깬 갈래는 빼고, 남은 곳은 거리와 함께)
    // 아직 안 깬 갈래 방에 들어와 있거나 어디서든 웨이브 중이면 그곳만 보여 주고 다른 갈림길 표식은 숨김
    void UpdateObjective()
    {
        if (objective == null) return;
        bool finished = hub != null && RunState.HasFlag(ClearedFlag);
        bool hubOpen = done.Count >= branches.Count && !finished;
        ZoneArena fighting = ZoneArena.Fighting;
        ZoneArena focus = fighting != null ? fighting : CurrentRoom();

        var lines = new System.Text.StringBuilder();
        if (finished)
        {
            lines.Append("남쪽 출구로 나아가라");
            if (exitMarker != null) lines.Append($"   {Distance(exitMarker.transform.position)}");
        }
        else if (hubOpen)
        {
            lines.Append($"협곡 중앙의 {miniBossName}을 쓰러뜨려라");
            if (fighting != hub) lines.Append($"   {Distance(hub.transform.position)}");
        }
        else if (focus != null) // 그 방의 목표만
        {
            lines.Append($"구역 해제  {done.Count} / {branches.Count}");
            lines.Append(focus == fighting ? $"\n{focus.DisplayName}   전투 중" : $"\n{focus.DisplayName}");
        }
        else
        {
            lines.Append($"구역 해제  {done.Count} / {branches.Count}");
            foreach (ZoneArena b in branches)
                if (b != null && !done.Contains(b))
                    lines.Append($"\n{b.DisplayName}   {Distance(b.transform.position)}");
        }
        objective.text = lines.ToString();

        foreach (KeyValuePair<ZoneArena, GameObject> pair in markers)
        {
            if (pair.Value == null) continue;
            bool show = pair.Key == hub ? hubOpen : !done.Contains(pair.Key) && !finished && !hubOpen;
            show &= focus == null; // 방 안 · 웨이브 중엔 나침반 · 화살표 표식 전부 숨김
            if (pair.Value.activeSelf != show) pair.Value.SetActive(show);
        }
        if (exitNav != null && exitNav.activeSelf != finished) exitNav.SetActive(finished);
    }

    // 아직 안 깬 갈래 방 안에 서 있으면 그 방
    ZoneArena CurrentRoom()
    {
        if (player == null) return null;
        foreach (ZoneArena b in branches)
        {
            if (b == null || done.Contains(b)) continue;
            Vector3 d = player.position - b.transform.position;
            d.y = 0f;
            if (d.sqrMagnitude <= roomRadius * roomRadius) return b;
        }
        return null;
    }

    string Distance(Vector3 target)
    {
        if (player == null) return "";
        Vector3 d = target - player.position;
        d.y = 0f;
        return $"{Mathf.RoundToInt(d.magnitude)}m";
    }

    void ShowBanner(string text)
    {
        banner.text = text;
        bannerTimer = 3f;
    }

    void Update()
    {
        UpdateGates();
        refreshTimer -= Time.unscaledDeltaTime;
        if (refreshTimer <= 0f) // 거리 · 전투 중 표시 갱신
        {
            refreshTimer = refreshInterval;
            UpdateObjective();
        }
        if (bannerTimer > 0f) bannerTimer -= Time.unscaledDeltaTime;
        float t = bannerTimer;
        bannerGroup.alpha = t <= 0f ? 0f : Mathf.Clamp01(Mathf.Min(t / 0.6f, (3f - t) / 0.25f));
    }

    void BuildUi()
    {
        Canvas canvas = UIKit.CreateCanvas(transform, "DungeonUi", 25);
        objective = UIKit.Text(canvas.transform, "", font, 26, Gold, TextAnchor.UpperRight);
        objective.lineSpacing = 1.25f; // 여러 줄 (남은 갈래 목록)
        RectTransform o = objective.rectTransform;
        o.anchorMin = o.anchorMax = new Vector2(1f, 1f);
        o.pivot = new Vector2(1f, 1f);
        o.anchoredPosition = new Vector2(-48f, -160f);
        o.sizeDelta = new Vector2(900f, 160f);
        objective.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2f, -2f);

        banner = UIKit.Text(canvas.transform, "", font, 44, Color.white, TextAnchor.MiddleCenter);
        RectTransform b = banner.rectTransform;
        b.anchorMin = b.anchorMax = new Vector2(0.5f, 0.66f);
        b.sizeDelta = new Vector2(1500f, 70f);
        banner.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2f, -2f);
        bannerGroup = banner.gameObject.AddComponent<CanvasGroup>();
        bannerGroup.alpha = 0f;

        // 뇌문 안내 (화면 아래 가운데) · 이동할 때 번쩍이는 막
        prompt = UIKit.Text(canvas.transform, "", font, 28, Color.white, TextAnchor.MiddleCenter);
        RectTransform p = prompt.rectTransform;
        p.anchorMin = p.anchorMax = new Vector2(0.5f, 0f);
        p.anchoredPosition = new Vector2(0f, 200f);
        p.sizeDelta = new Vector2(1000f, 44f);
        prompt.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2f, -2f);
        warpFlash = UIKit.Image(canvas.transform, null, new Color(0.8f, 0.9f, 1f, 0f));
        warpFlash.raycastTarget = false;
        UIKit.Stretch(warpFlash.rectTransform);
    }

    // ===== 뇌문 (갈래 방 ↔ 허브 바로 이동) =====
    class Gate
    {
        public ZoneArena branch;
        public bool toBranch;      // true = 허브 → 갈래, false = 깬 갈래 → 허브
        public Vector3 position, destination;
        public float destinationYaw;
        public GameObject visual;
    }

    readonly List<Gate> gates = new List<Gate>();
    Text prompt;
    Image warpFlash;
    Gate nearGate;
    bool warping;
    AudioSource warpSource;

    void BuildGates()
    {
        if (hub == null) return;
        Vector3 c = hub.transform.position;
        foreach (ZoneArena b in branches)
        {
            if (b == null) continue;
            Vector3 dir = b.transform.position - c;
            dir.y = 0f;
            dir.Normalize();
            float yawIn = Quaternion.LookRotation(dir).eulerAngles.y;
            gates.Add(NewGate(b, true, c + dir * hubGateDistance, b.transform.position - dir * branchArrive, yawIn));
            gates.Add(NewGate(b, false, b.transform.position, c + dir * hubArrive, yawIn + 180f));
        }
        warpSource = gameObject.AddComponent<AudioSource>();
        warpSource.spatialBlend = 0f;
        warpSource.clip = Resources.Load<AudioClip>("Audio/Weather/warp");
    }

    Gate NewGate(ZoneArena branch, bool toBranch, Vector3 position, Vector3 destination, float yaw)
    {
        position = Ground(position);
        var gate = new Gate { branch = branch, toBranch = toBranch, position = position, destination = Ground(destination), destinationYaw = yaw };
        gate.visual = GateVisual(position);
        gate.visual.SetActive(false);
        return gate;
    }

    // 지금 쓸 수 있는 문: 전투 중 · 던전을 다 깬 뒤엔 전부 닫힘. 허브 → 아직 안 깬 갈래만, 갈래 → 허브는 그 갈래를 깬 뒤에만
    bool GateOpen(Gate g)
    {
        if (ZoneArena.Fighting != null || RunState.HasFlag(ClearedFlag)) return false;
        bool cleared = done.Contains(g.branch);
        return g.toBranch ? !cleared : cleared;
    }

    void UpdateGates()
    {
        nearGate = null;
        float best = gateRange * gateRange;
        foreach (Gate g in gates)
        {
            bool open = GateOpen(g);
            if (g.visual.activeSelf != open) g.visual.SetActive(open);
            if (!open || player == null) continue;
            Vector3 d = player.position - g.position;
            d.y = 0f;
            if (d.sqrMagnitude <= best)
            {
                best = d.sqrMagnitude;
                nearGate = g;
            }
        }
        if (prompt != null)
            prompt.text = nearGate == null || warping ? "" : nearGate.toBranch ? $"E   뇌문 — {nearGate.branch.DisplayName}" : "E   뇌문 — 협곡 중앙으로";

        Keyboard kb = Keyboard.current;
        if (nearGate != null && !warping && !GamePause.IsPaused && kb != null && kb.eKey.wasPressedThisFrame)
            StartCoroutine(Warp(nearGate));
    }

    // 핵심: 번쩍 → 화면이 하얀 동안 위치 · 카메라를 옮김 → 도착 자리에 낙뢰와 함께 나타남
    IEnumerator Warp(Gate g)
    {
        warping = true;
        if (warpSource != null && warpSource.clip != null) warpSource.PlayOneShot(warpSource.clip, AudioOptions.SfxVolume);
        ZoneArena.NotifyEnemySpawned(g.position); // 적 등장 낙뢰 연출 재사용
        for (float t = 0f; t < 0.12f; t += Time.unscaledDeltaTime)
        {
            warpFlash.color = new Color(0.8f, 0.9f, 1f, t / 0.12f);
            yield return null;
        }
        warpFlash.color = new Color(0.8f, 0.9f, 1f, 1f);

        var cc = player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        player.SetPositionAndRotation(g.destination, Quaternion.Euler(0f, g.destinationYaw, 0f));
        if (cc != null) cc.enabled = true;
        ThirdPersonCamera cam = FindAnyObjectByType<ThirdPersonCamera>();
        if (cam != null) cam.Snap(g.destinationYaw);
        yield return null;
        ZoneArena.NotifyEnemySpawned(g.destination);

        for (float t = 0f; t < 0.35f; t += Time.unscaledDeltaTime)
        {
            warpFlash.color = new Color(0.8f, 0.9f, 1f, 1f - t / 0.35f);
            yield return null;
        }
        warpFlash.color = new Color(0.8f, 0.9f, 1f, 0f);
        warping = false;
    }

    static Vector3 Ground(Vector3 p)
    {
        float best = float.MaxValue;
        foreach (RaycastHit hit in Physics.RaycastAll(p + Vector3.up * 30f, Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.GetComponentInParent<CharacterController>() != null || hit.distance >= best) continue;
            best = hit.distance;
            p.y = hit.point.y;
        }
        return p;
    }

    // 바닥의 번개 고리 + 위로 오르는 불티 + 푸른 빛
    GameObject GateVisual(Vector3 position)
    {
        var root = new GameObject("ThunderGate");
        root.transform.SetParent(transform, false);
        root.transform.position = position;
        var mat = new Material(Shader.Find("Sprites/Default"));

        /* 포탈 면이 생겨 바닥 고리는 끔
        var ring = new GameObject("Ring").AddComponent<LineRenderer>();
        ring.transform.SetParent(root.transform, false);
        ring.sharedMaterial = mat;
        ring.useWorldSpace = false;
        ring.loop = true;
        ring.widthMultiplier = 0.08f;
        ring.startColor = ring.endColor = new Color(0.6f, 0.82f, 1f, 0.9f);
        const int Segments = 48;
        ring.positionCount = Segments;
        for (int i = 0; i < Segments; i++)
        {
            float a = i * Mathf.PI * 2f / Segments;
            ring.SetPosition(i, new Vector3(Mathf.Cos(a) * 1.4f, 0.08f, Mathf.Sin(a) * 1.4f));
        }
        */

        //var ps = new GameObject("Sparks").AddComponent<ParticleSystem>();
        //ps.transform.SetParent(root.transform, false);
        //ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        //ParticleSystem.MainModule main = ps.main;
        //main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
        //main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3f);
        //main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
        //main.startColor = new Color(0.7f, 0.88f, 1f);
        //main.simulationSpace = ParticleSystemSimulationSpace.World;
        //ParticleSystem.ShapeModule shape = ps.shape;
        //shape.shapeType = ParticleSystemShapeType.Circle;
        //shape.radius = 1.3f;
        //shape.rotation = new Vector3(-90f, 0f, 0f); // 위로 뿜음
        //ParticleSystem.EmissionModule em = ps.emission;
        //em.rateOverTime = 30f;
        //var pr = ps.GetComponent<ParticleSystemRenderer>();
        //pr.sharedMaterial = mat;
        //pr.renderMode = ParticleSystemRenderMode.Stretch;
        //pr.velocityScale = 0.06f;
        //ps.Play();

        var glow = new GameObject("Glow").AddComponent<Light>();
        glow.transform.SetParent(root.transform, false);
        glow.transform.localPosition = Vector3.up * 1f;
        glow.type = LightType.Point;
        glow.color = new Color(0.55f, 0.75f, 1f);
        glow.range = 6f;
        glow.intensity = 2.5f;

        // 고리 위에 서 있는 소용돌이 포탈 면 (카메라 쪽을 봄)
        WarpPortalFx portal = WarpPortalFx.Create(root.transform, position + Vector3.up * 1.6f, Quaternion.identity, 1.25f,
            new Color(0.5f, 0.8f, 1.8f), new Color(0.03f, 0.05f, 0.2f), true, 0f);
        portal.SetOpenInstant(true);
        return root;
    }
}
