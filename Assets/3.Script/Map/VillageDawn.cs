using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 흑뢰 토벌 뒤의 마을: 밤 → 새벽. 마을 씬이 열릴 때 BossCleared 표시가 있으면 스스로 생김 (씬 수정 없음)
//  처음 돌아왔을 때(DawnSeen 표시 없음): 어둠의 장막이 위로 걷히며 하늘 · 해 · 안개 · 색보정이 새벽으로 바뀌는 연출 → 표시 + 저장
//  그 뒤로는 들어오자마자 새벽
// 새벽 값은 아래 필드 (Play 중 인스펙터에서 맞춘 값을 알려 주면 기본값으로 고정)
// 다른 진행 단계를 늘릴 땐 같은 방식으로 표시 하나 + 분위기 컴포넌트 하나를 추가
public class VillageDawn : MonoBehaviour
{
    public const string SeenFlag = "VillageDawnSeen";

    [Header("Sun")]
    public float sunPitch = 14f;                               // 해 높이 (도)
    public Color sunColor = new Color(1f, 0.78f, 0.55f);
    public float sunIntensity = 1.15f;
    [Header("Sky (Skybox/Procedural)")]
    public float skyExposure = 1.25f;
    public Color skyTint = new Color(0.62f, 0.6f, 0.7f);
    public Color groundColor = new Color(0.42f, 0.37f, 0.33f);
    public float atmosphere = 1.3f;
    [Header("Fog · Ambient")]
    public Color fogColor = new Color(0.66f, 0.6f, 0.58f);
    public float fogDensity = 0.011f;
    public float ambientIntensity = 1f;
    [Header("Grade (밤 색보정 위에 덮음)")]
    public float postExposure = 0.45f;
    public float contrast = 8f;
    public float saturation = 2f;
    public Color colorFilter = new Color(1f, 0.96f, 0.9f);
    public float temperature = 10f;
    [Header("Reveal")]
    public float revealDelay = 0.9f;  // 씬 밝아짐(페이드)이 끝난 뒤
    public float revealTime = 6f;
    public string revealTitle = "흑뢰가 걷혔다";
    public string revealSub = "산 너머로 새벽이 밝아 온다  ·  산문 포탈 너머로 시련의 터가 열렸다";

    static bool hooked;

    Light sun;
    Material sky;
    Volume dawnVolume;
    ColorAdjustments grade;
    // 밤 값 (연출 시작점)
    Quaternion nightSunRot;
    Color nightSunColor, nightFog;
    float nightSunIntensity, nightFogDensity, nightAmbient;
    float nightExposure = 1f, nightAtmosphere = 1f;
    Color nightTint = Color.gray, nightGround = Color.gray;
    float lastEnvUpdate;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        if (!hooked)
        {
            hooked = true;
            SceneManager.sceneLoaded += (scene, mode) => TryInstall(scene);
        }
        TryInstall(SceneManager.GetActiveScene()); // 에디터에서 마을 씬을 바로 Play한 경우
    }

    static void TryInstall(Scene scene)
    {
        if (scene.name != VillagePortal.VillageScene || !RunState.HasFlag(BossReward.ClearedFlag)) return;
        if (FindAnyObjectByType<VillageDawn>() != null) return;
        new GameObject("VillageDawn").AddComponent<VillageDawn>();
    }

    void Start()
    {
        Capture();
        BuildVolume();
        if (RunState.HasFlag(SeenFlag)) Apply(1f, true);
        else StartCoroutine(Reveal());
    }

    void OnDestroy()
    {
        if (sky != null) Destroy(sky);
        if (dawnVolume != null && dawnVolume.sharedProfile != null) Destroy(dawnVolume.sharedProfile);
    }

    void Capture()
    {
        sun = RenderSettings.sun;
        if (sun == null)
            foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { sun = l; break; }
        if (sun != null)
        {
            nightSunRot = sun.transform.rotation;
            nightSunColor = sun.color;
            nightSunIntensity = sun.intensity;
        }

        if (RenderSettings.skybox != null)
        {
            sky = new Material(RenderSettings.skybox) { name = "VillageDawnSky" }; // 원본 에셋을 바꾸지 않게 복사본
            RenderSettings.skybox = sky;
            if (sky.HasProperty("_Exposure")) nightExposure = sky.GetFloat("_Exposure");
            if (sky.HasProperty("_AtmosphereThickness")) nightAtmosphere = sky.GetFloat("_AtmosphereThickness");
            if (sky.HasProperty("_SkyTint")) nightTint = sky.GetColor("_SkyTint");
            if (sky.HasProperty("_GroundColor")) nightGround = sky.GetColor("_GroundColor");
        }
        nightFog = RenderSettings.fogColor;
        nightFogDensity = RenderSettings.fogDensity;
        nightAmbient = RenderSettings.ambientIntensity;
    }

    // 밤 구역 볼륨(ForestLook)보다 우선순위가 높은 전역 볼륨 → weight만큼 밤 색보정을 새벽 값으로 덮음
    void BuildVolume()
    {
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        grade = profile.Add<ColorAdjustments>(true);
        grade.postExposure.Override(postExposure);
        grade.contrast.Override(contrast);
        grade.saturation.Override(saturation);
        grade.colorFilter.Override(colorFilter);
        WhiteBalance wb = profile.Add<WhiteBalance>(true);
        wb.temperature.Override(temperature);
        LiftGammaGain lgg = profile.Add<LiftGammaGain>(true); // 밤 프로필의 푸른 그늘 · 감마를 중립으로
        lgg.lift.Override(new Vector4(1f, 1f, 1f, 0f));
        lgg.gamma.Override(new Vector4(1f, 1f, 1f, 0f));
        lgg.gain.Override(new Vector4(1f, 1f, 1f, 0f));

        var go = new GameObject("DawnVolume");
        go.transform.SetParent(transform, false);
        dawnVolume = go.AddComponent<Volume>();
        dawnVolume.isGlobal = true;
        dawnVolume.priority = 50f;
        dawnVolume.sharedProfile = profile;
        dawnVolume.weight = 0f;
    }

    // 핵심: k(0 밤 → 1 새벽)로 해 · 하늘 · 안개 · 주변광 · 색보정을 한꺼번에 섞음. 주변광은 하늘에서 오므로 가끔 다시 계산
    void Apply(float k, bool force = false)
    {
        if (sun != null)
        {
            Vector3 e = nightSunRot.eulerAngles;
            Quaternion dawnRot = Quaternion.Euler(sunPitch, e.y, 0f);
            sun.transform.rotation = Quaternion.Slerp(nightSunRot, dawnRot, k);
            sun.color = Color.Lerp(nightSunColor, sunColor, k);
            sun.intensity = Mathf.Lerp(nightSunIntensity, sunIntensity, k);
        }
        if (sky != null)
        {
            if (sky.HasProperty("_Exposure")) sky.SetFloat("_Exposure", Mathf.Lerp(nightExposure, skyExposure, k));
            if (sky.HasProperty("_AtmosphereThickness")) sky.SetFloat("_AtmosphereThickness", Mathf.Lerp(nightAtmosphere, atmosphere, k));
            if (sky.HasProperty("_SkyTint")) sky.SetColor("_SkyTint", Color.Lerp(nightTint, skyTint, k));
            if (sky.HasProperty("_GroundColor")) sky.SetColor("_GroundColor", Color.Lerp(nightGround, groundColor, k));
        }
        RenderSettings.fogColor = Color.Lerp(nightFog, fogColor, k);
        RenderSettings.fogDensity = Mathf.Lerp(nightFogDensity, fogDensity, k);
        RenderSettings.ambientIntensity = Mathf.Lerp(nightAmbient, ambientIntensity, k);
        if (dawnVolume != null) dawnVolume.weight = k;

        if (force || Time.unscaledTime - lastEnvUpdate > 0.3f)
        {
            lastEnvUpdate = Time.unscaledTime;
            DynamicGI.UpdateEnvironment();
        }
    }

    // ===== 첫 귀환 연출 =====
    // 어둠의 장막(화면 위쪽부터 짙은 검은 막)이 위로 걷히고, 그 사이 하늘이 밝아짐 → 제목 → 장막이 다 걷히면 끝
    IEnumerator Reveal()
    {
        Apply(0f, true);
        PlayerLocomotion locomotion = FindAnyObjectByType<PlayerLocomotion>();
        PlayerCombat combat = FindAnyObjectByType<PlayerCombat>();
        if (locomotion != null) locomotion.MovementLocked = true;
        if (combat != null) combat.InputLocked = true;

        Font font = null;
        SettingsMenu settings = FindAnyObjectByType<SettingsMenu>(FindObjectsInactive.Include);
        if (settings != null) font = settings.BodyFont;
        Canvas canvas = UIKit.CreateCanvas(transform, "DawnCanvas", 45);
        Image curtain = UIKit.Image(canvas.transform, null, new Color(0f, 0f, 0f, 0.92f));
        UIKit.Stretch(curtain.rectTransform);
        Image edge = UIKit.Image(curtain.transform, FadeDown(), new Color(0f, 0f, 0f, 0.92f)); // 장막 아래 끝을 부드럽게
        edge.rectTransform.anchorMin = Vector2.zero;
        edge.rectTransform.anchorMax = new Vector2(1f, 0f);
        edge.rectTransform.pivot = new Vector2(0.5f, 1f);
        edge.rectTransform.sizeDelta = new Vector2(0f, 420f);
        Image glow = UIKit.Image(canvas.transform, null, new Color(1f, 0.85f, 0.6f, 0f)); // 장막이 걷히는 순간의 새벽빛
        UIKit.Stretch(glow.rectTransform);
        glow.raycastTarget = curtain.raycastTarget = edge.raycastTarget = false;

        var titleGroup = new GameObject("Title", typeof(RectTransform)).AddComponent<CanvasGroup>();
        titleGroup.transform.SetParent(canvas.transform, false);
        UIKit.Stretch((RectTransform)titleGroup.transform);
        titleGroup.alpha = 0f;
        UIKit.BrushBehind(titleGroup.transform, 460f, 300f, 1000f, 130f, new Color(0f, 0f, 0f, 0.5f));
        Text title = UIKit.Text(titleGroup.transform, revealTitle, font, 54, UIKit.Gold, TextAnchor.MiddleCenter);
        UIKit.Place(title.rectTransform, 0f, 285f, 1920f, 74f);
        title.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.7f);
        Text sub = UIKit.Text(titleGroup.transform, revealSub, font, 24, new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleCenter);
        UIKit.Place(sub.rectTransform, 0f, 340f, 1920f, 36f);

        for (float t = 0f; t < revealDelay; t += Time.unscaledDeltaTime) yield return null;

        float lift = 1080f + 420f;
        for (float t = 0f; t < revealTime; t += Time.unscaledDeltaTime)
        {
            float k = t / revealTime;
            float s = Mathf.SmoothStep(0f, 1f, k);
            Apply(s);
            curtain.rectTransform.anchoredPosition = new Vector2(0f, lift * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k * 1.25f))); // 장막은 조금 먼저 다 걷힘
            glow.color = new Color(1f, 0.85f, 0.6f, 0.22f * Mathf.Sin(Mathf.Clamp01(k * 1.3f) * Mathf.PI));
            titleGroup.alpha = Mathf.Clamp01((k - 0.25f) / 0.15f) * (1f - Mathf.Clamp01((k - 0.85f) / 0.15f));
            yield return null;
        }
        Apply(1f, true);

        if (locomotion != null) locomotion.MovementLocked = false;
        if (combat != null) combat.InputLocked = false;
        RunState.SetFlag(SeenFlag);
        SaveGame.Save();
        Destroy(canvas.gameObject);
    }

    // 위는 진하고 아래로 갈수록 투명한 세로 그라데이션
    static Sprite FadeDown()
    {
        var tex = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 64; y++) tex.SetPixel(0, y, new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, y / 63f)));
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, 64), new Vector2(0.5f, 0.5f));
    }
}
