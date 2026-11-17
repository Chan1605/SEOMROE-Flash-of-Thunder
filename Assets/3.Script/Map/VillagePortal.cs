using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// 마을 포탈: 붉은 문(결계) 앞에서 E → 다른 씬으로 (진행 상태는 이어받음). 배치는 Tools/Setup/Village Scene
//  - unlock: 언제 열리는지. 잠긴 동안엔 결계가 붉게 닫혀 있고 안내 문구만 보임
//  - guide: 목표 표식(같은 오브젝트의 ObjectiveBeacon)을 언제 이 포탈에 켤지
//  - 수련 완료 = 이번 판에 수련장을 나왔거나(RunState 표시) 예전에 한 번이라도 끝냈으면
//  - 잠긴 포탈에 다가가면: 안내 문구 + 카메라가 봉인된 문 → 지금 가야 할 포탈 순서로 비춤
public class VillagePortal : MonoBehaviour
{
    public const string VillageScene = "Village";
    public const string TutorialDoneFlag = "TutorialDone";
    public const string ReturnPoint = "VillagePortals/Portal_Sanmun/Arrive"; // 본편 · 던전에서 돌아올 때 서는 자리
    public const string TutorialReturnPoint = "VillagePortals/Portal_Tutorial/Arrive"; // [변경] 수련장 · 연습장에서 돌아올 때
    public const string FromVillageFlag = "FromVillage"; // [변경] 마을 포탈로 떠났다는 표시 (연습장에서 마을로 돌아가기 허용)

    public enum When { Always, BeforeTutorial, AfterTutorial, Never }

    [SerializeField] string sceneName = "InGame";
    [SerializeField] string spawnAt = "";
    [SerializeField] string displayName = "산문";
    [SerializeField] string lockedHint = "수련장을 먼저 마치세요";
    [SerializeField] When unlock = When.Always;
    [SerializeField] When guide = When.Never;
    [SerializeField] GameObject barrier;
    [SerializeField] ObjectiveBeacon beacon;
    [SerializeField] Font font;
    [SerializeField] float range = 3.2f;
    [SerializeField] float portalRadius = 2.2f;
    [SerializeField] Color color = new Color(0.55f, 0.78f, 1f);
    [SerializeField] Color lockedColor = new Color(0.85f, 0.22f, 0.18f);

    // [변경] 수련을 끝낸 뒤 이 포탈의 목적지 (비우면 그대로). 수련장 포탈 → 연습장
    [Header("After Tutorial")]
    [SerializeField] string altScene = "";
    [SerializeField] string altName = "";

    // 잠긴 문 안내 연출
    [Header("Locked Guide")]
    [SerializeField] string lockedTitle = "수련장을 먼저 마치고 오세요";
    [SerializeField] string lockedSub = "서쪽 문 너머 수련장에서 기본기를 익혀야 산문이 열립니다";
    [SerializeField] float sealedShot = 1.3f;  // 봉인된 문을 비추는 시간 (실제 시간)
    [SerializeField] float guideShot = 2.2f;   // 가야 할 포탈을 비추는 시간
    static readonly Color Gold = new Color(0.95f, 0.8f, 0.45f);
    static readonly List<VillagePortal> all = new List<VillagePortal>();

    public static bool TutorialDone => RunState.HasFlag(TutorialDoneFlag) || TutorialDirector.CompletedBefore;

    bool UseAlt => !string.IsNullOrEmpty(altScene) && TutorialDone; // [변경]
    string TargetScene => UseAlt ? altScene : sceneName;
    string TargetName => UseAlt && !string.IsNullOrEmpty(altName) ? altName : displayName;

    static bool Check(When w) =>
        w == When.Always || (w == When.BeforeTutorial && !TutorialDone) || (w == When.AfterTutorial && TutorialDone);

    Transform player;
    PlayerHealth health;
    Text prompt;
    ParticleSystem swirl;
    Light glow;
    Material barrierMat;
    bool open, wasOpen, near, wasNear, showing; // wasNear · showing
    CanvasGroup banner;
    Text bannerTitle, bannerSub;

    void OnEnable() => all.Add(this);  
    void OnDisable() => all.Remove(this);

    void Start()
    {
        health = FindAnyObjectByType<PlayerHealth>();
        if (health != null) player = health.transform;
        if (barrier != null && barrier.TryGetComponent(out Renderer r)) barrierMat = r.material; // 이 문만 색을 바꾸도록 복사본
        BuildSwirl();
        BuildPrompt();
        BuildBanner();
        wasOpen = !Check(unlock);
    }

    void OnDestroy()
    {
        if (barrierMat != null) Destroy(barrierMat);
    }

    void Update()
    {
        open = Check(unlock);
        if (open != wasOpen) ApplyLook();
        wasOpen = open;
        if (beacon != null) beacon.ManualShow = open && Check(guide);

        // 열린 포탈은 천천히 숨 쉬듯 밝아졌다 어두워짐
        if (glow != null) glow.intensity = (open ? 3f : 1.2f) * (0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 2.4f));

        near = false;
        if (player != null && (health == null || !health.IsDead))
        {
            Vector3 d = player.position - transform.position;
            d.y = 0f;
            near = d.sqrMagnitude <= range * range;
        }
        if (prompt != null)
            prompt.text = !near || showing ? "" : open ? $"E   {TargetName}" : $"{displayName}  —  {lockedHint}"; // [변경] TargetName

        // 잠긴 문: 범위에 들어오는 순간(또는 E) 안내 연출
        bool entered = near && !wasNear;
        wasNear = near;
        if (!near || showing || GamePause.IsPaused || SceneTransition.Busy) return;
        if (SkillTreeMenu.Instance != null && SkillTreeMenu.Instance.IsOpen) return;
        Keyboard kb = Keyboard.current;
        bool pressed = kb != null && kb.eKey.wasPressedThisFrame;
        if (!open)
        {
            if (entered || pressed) StartCoroutine(ShowLocked());
            return;
        }
        if (!pressed) return;
        RunState.SetFlag(FromVillageFlag); // [변경]
        SceneTransition.Load(TargetScene, true, spawnAt);
    }

    // 열림: 푸른 결계 + 소용돌이 / 잠김: 붉고 흐린 결계, 소용돌이 없음
    void ApplyLook()
    {
        Color c = open ? new Color(color.r, color.g, color.b, 0.4f) : new Color(lockedColor.r, lockedColor.g, lockedColor.b, 0.3f);
        if (barrierMat != null)
        {
            if (barrierMat.HasProperty("_BaseColor")) barrierMat.SetColor("_BaseColor", c);
            else barrierMat.color = c;
        }
        if (glow != null) glow.color = open ? color : lockedColor;
        if (swirl == null) return;
        if (open && !swirl.isPlaying) swirl.Play();
        else if (!open) swirl.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    // ===== 잠긴 문 안내 =====
    // 핵심: 조작을 잠시 막고 → 봉인된 이 문을 비춤 → 지금 목표인 포탈(수련장)을 비춤 → 원래 카메라로
    IEnumerator ShowLocked()
    {
        showing = true;
        var locomotion = player != null ? player.GetComponent<PlayerLocomotion>() : null;
        var combat = player != null ? player.GetComponent<PlayerCombat>() : null;
        if (locomotion != null) locomotion.MovementLocked = true;
        if (combat != null) combat.InputLocked = true;
        ThirdPersonCamera cam = FindAnyObjectByType<ThirdPersonCamera>();

        bannerTitle.text = lockedTitle;
        bannerSub.text = lockedSub;
        if (cam != null) cam.BeginCinematic(transform.position + Vector3.up * 3f, transform.eulerAngles.y, 8f, 9f);
        for (float t = 0f; t < sealedShot; t += Time.unscaledDeltaTime)
        {
            banner.alpha = Mathf.Clamp01(t / 0.35f);
            yield return null;
        }

        VillagePortal guide = all.Find(p => p != this && p.open && Check(p.guide));
        if (cam != null && guide != null)
        {
            cam.BeginCinematic(guide.transform.position + Vector3.up * 3f, guide.transform.eulerAngles.y, 10f, 11f);
            yield return new WaitForSecondsRealtime(guideShot);
        }

        if (cam != null) cam.EndCinematic();
        if (locomotion != null) locomotion.MovementLocked = false;
        if (combat != null) combat.InputLocked = false;
        for (float t = 0f; t < 0.5f; t += Time.unscaledDeltaTime)
        {
            banner.alpha = 1f - t / 0.5f;
            yield return null;
        }
        banner.alpha = 0f;
        showing = false;
    }

    void BuildBanner()
    {
        Canvas canvas = UIKit.CreateCanvas(transform, "LockedBanner", 21);
        banner = canvas.gameObject.AddComponent<CanvasGroup>();
        banner.alpha = 0f;
        banner.blocksRaycasts = false;
        Image back = UIKit.Image(canvas.transform, UIKit.SharedBrush, new Color(0f, 0f, 0f, 0.55f));
        RectTransform b = back.rectTransform;
        b.anchorMin = b.anchorMax = new Vector2(0.5f, 0.72f);
        b.sizeDelta = new Vector2(1100f, 150f);
        bannerTitle = UIKit.Text(canvas.transform, "", font, 40, Gold, TextAnchor.MiddleCenter);
        Center(bannerTitle.rectTransform, 22f);
        bannerTitle.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.7f);
        bannerSub = UIKit.Text(canvas.transform, "", font, 22, new Color(1f, 1f, 1f, 0.8f), TextAnchor.MiddleCenter);
        Center(bannerSub.rectTransform, -30f);
    }

    static void Center(RectTransform r, float y)
    {
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.72f);
        r.anchoredPosition = new Vector2(0f, y);
        r.sizeDelta = new Vector2(1200f, 50f);
    }

    // ===== 만들기 =====
    // 핵심: 문 안쪽 면(로컬 XY)에 원을 그리며 안으로 빨려 드는 불티 → 포탈처럼 보임
    void BuildSwirl()
    {
        var root = new GameObject("Swirl");
        root.transform.SetParent(transform, false);
        root.transform.localPosition = Vector3.up * 2.5f;

        swirl = root.AddComponent<ParticleSystem>();
        swirl.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = swirl.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
        main.startColor = new ParticleSystem.MinMaxGradient(color, Color.white);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 400;
        ParticleSystem.ShapeModule shape = swirl.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = portalRadius;
        shape.radiusThickness = 0.15f;
        ParticleSystem.VelocityOverLifetimeModule v = swirl.velocityOverLifetime;
        v.enabled = true;
        v.space = ParticleSystemSimulationSpace.Local;
        v.orbitalZ = new ParticleSystem.MinMaxCurve(1.6f);
        v.radial = new ParticleSystem.MinMaxCurve(-0.9f);
        ParticleSystem.ColorOverLifetimeModule col = swirl.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        ParticleSystem.EmissionModule em = swirl.emission;
        em.rateOverTime = 90f;
        var pr = swirl.GetComponent<ParticleSystemRenderer>();
        pr.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        glow = new GameObject("Glow").AddComponent<Light>();
        glow.transform.SetParent(root.transform, false);
        glow.transform.localPosition = Vector3.back * 1f; // 마을 쪽을 비춤
        glow.type = LightType.Point;
        glow.range = 8f;
        glow.shadows = LightShadows.None;
    }

    void BuildPrompt()
    {
        Canvas canvas = UIKit.CreateCanvas(transform, "PortalPrompt", 20);
        prompt = UIKit.Text(canvas.transform, "", font, 28, Color.white, TextAnchor.MiddleCenter);
        RectTransform p = prompt.rectTransform;
        p.anchorMin = p.anchorMax = new Vector2(0.5f, 0f);
        p.anchoredPosition = new Vector2(0f, 200f);
        p.sizeDelta = new Vector2(1000f, 44f);
        prompt.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2f, -2f);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.55f, 0.78f, 1f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, range);
    }
}
