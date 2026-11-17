using SickscoreGames.HUDNavigationSystem;
using UnityEngine;

// 목표 지점 안내: 조건이 맞으면 나침반 · 화면 표식(HUD Navigation) + 하늘로 솟는 빛기둥을 켬
//  - showWhen: 언제 켤지 (수련 완료 · 구역 돌파 · 수련 단계)
//  - target: 표식을 세울 곳 (비우면 이 오브젝트). hideIfFlag가 서 있거나 arriveDistance 안으로 오면 끔
public class ObjectiveBeacon : MonoBehaviour
{
    public enum Condition { Always, TutorialFinished, ZoneCleared, TutorialStep, Manual } // [변경] Manual: 다른 스크립트가 ManualShow로 켬 (마을 포탈)

    [SerializeField] Condition showWhen = Condition.Always;
    [SerializeField] ZoneArena zone;                              // ZoneCleared용
    [SerializeField] TutorialGoal tutorialGoal = TutorialGoal.ShrineRest; // TutorialStep용
    [SerializeField] string hideIfFlag = "";
    [SerializeField] Transform target;
    [SerializeField] HUDNavigationElementSettings markerSettings;
    [SerializeField] Color color = new Color(0.95f, 0.8f, 0.45f);
    [SerializeField] float markerHeight = 2.5f;
    [SerializeField] float arriveDistance = 3f;
    [SerializeField] float beamHeight = 40f;
    [SerializeField] float beamWidth = 1.2f;

    GameObject marker;
    LineRenderer beam, core;
    Transform player;
    bool zoneCleared, arrived, shown;

    public bool ManualShow { get; set; } // [변경]

    Vector3 Point => target != null ? target.position : transform.position;

    void Awake()
    {
        marker = CreateMarker();
        beam = CreateBeam("Beam", beamWidth, 0.28f);
        core = CreateBeam("Core", beamWidth * 0.25f, 0.6f);
        Show(false);
    }

    void OnEnable()
    {
        if (zone != null) zone.Cleared += OnZoneCleared;
    }

    void OnDisable()
    {
        if (zone != null) zone.Cleared -= OnZoneCleared;
    }

    void Start()
    {
        PlayerHealth health = FindAnyObjectByType<PlayerHealth>();
        if (health != null) player = health.transform;
    }

    void OnZoneCleared() => zoneCleared = true;

    bool ConditionMet()
    {
        switch (showWhen)
        {
            case Condition.TutorialFinished: return TutorialDirector.Instance != null && TutorialDirector.Instance.Finished;
            case Condition.ZoneCleared: return zoneCleared;
            case Condition.TutorialStep: return TutorialDirector.Instance != null && TutorialDirector.Instance.IsOnGoal(tutorialGoal);
            case Condition.Manual: return ManualShow; // [변경]
            default: return true;
        }
    }

    void Update()
    {
        bool show = !arrived && ConditionMet() && (string.IsNullOrEmpty(hideIfFlag) || !RunState.HasFlag(hideIfFlag));
        if (show && player != null)
        {
            Vector3 d = player.position - Point;
            d.y = 0f;
            if (d.sqrMagnitude <= arriveDistance * arriveDistance)
            {
                arrived = showWhen != Condition.TutorialStep && showWhen != Condition.Manual; // [변경] 수련 단계 · 포탈 안내는 조건이 끝날 때까지 유지
                show = false;
            }
        }
        if (show != shown) Show(show);
        if (!shown) return;

        // 빛기둥이 숨 쉬듯 밝아졌다 어두워짐 (일시정지 중에도)
        float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 2.2f);
        Vector3 p = Point;
        SetBeam(beam, p, 0.28f * pulse);
        SetBeam(core, p, 0.6f * pulse);
        if (marker != null) marker.transform.position = p + Vector3.up * markerHeight;
    }

    void Show(bool on)
    {
        shown = on;
        if (marker != null) marker.SetActive(on); // 껐다 켜야 내비게이션 표식이 새로 만들어짐
        beam.enabled = core.enabled = on;
    }

    // ===== 만들기 =====
    GameObject CreateMarker()
    {
        if (markerSettings == null) return null;
        var go = new GameObject("Nav_Objective");
        go.SetActive(false); // 설정을 넣은 뒤 켜야 표식이 제대로 생김
        go.transform.SetParent(transform, false);
        go.AddComponent<HUDNavigationElement>().Settings = markerSettings;
        go.AddComponent<NavTarget>().Color = color;
        return go;
    }

    LineRenderer CreateBeam(string name, float width, float alpha)
    {
        var line = new GameObject(name).AddComponent<LineRenderer>();
        line.transform.SetParent(transform, false);
        line.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = SoftEdge() };
        line.textureMode = LineTextureMode.Stretch;
        line.alignment = LineAlignment.View; // 어느 방향에서 봐도 기둥으로 보임
        line.positionCount = 2;
        line.widthMultiplier = width;
        line.numCapVertices = 0;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.useWorldSpace = true;
        SetBeam(line, Point, alpha);
        return line;
    }

    // 바닥은 진하고 위로 갈수록 사라지는 기둥
    void SetBeam(LineRenderer line, Vector3 bottom, float alpha)
    {
        line.SetPosition(0, bottom);
        line.SetPosition(1, bottom + Vector3.up * beamHeight);
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                  new[] { new GradientAlphaKey(alpha, 0f), new GradientAlphaKey(alpha * 0.6f, 0.35f), new GradientAlphaKey(0f, 1f) });
        line.colorGradient = g;
    }

    // 가로로 가운데만 밝은 텍스처 → 기둥 가장자리가 부드러움
    static Texture2D softEdge;
    static Texture2D SoftEdge()
    {
        if (softEdge != null) return softEdge;
        const int W = 4, H = 32;
        softEdge = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < H; y++)
        {
            float v = 1f - Mathf.Abs((y + 0.5f) / H * 2f - 1f);
            for (int x = 0; x < W; x++) softEdge.SetPixel(x, y, new Color(1f, 1f, 1f, v * v));
        }
        softEdge.Apply();
        return softEdge;
    }
}
