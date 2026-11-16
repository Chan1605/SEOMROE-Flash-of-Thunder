using System.Collections;
using UnityEngine;

// 던전(뇌운 협곡) 날씨: 비 + 가끔 먼 하늘에 번개 → 잠시 뒤 천둥. DungeonDirector가 실행 중에 붙임 (씬 연결 없음)
//  - 비: 카메라 주변에만 내림 (어디를 가도 같은 양). 빗소리는 계속 반복
//  - 번개: 7~15초마다. 햇빛 · 화면이 번쩍 + 먼 하늘에 번개 줄기, 거리만큼 늦게 천둥 (가까우면 찢어지는 소리, 멀면 우르르)
//  - 소리: Resources/Audio/Weather (rain_loop, thunder_01 = 가까운 천둥, 02~ = 먼 천둥)
public class DungeonWeather : MonoBehaviour
{
    static readonly Color Bolt = new Color(0.75f, 0.85f, 1f);

    [SerializeField] Vector2 strikeInterval = new Vector2(7f, 15f);
    [SerializeField] float rainVolume = 0.35f;
    [SerializeField] float thunderVolume = 0.8f;
    [SerializeField] float sunBoost = 2.5f;      // 번쩍일 때 햇빛에 더하는 밝기
    [SerializeField, Range(0f, 1f)] float screenFlash = 0.12f;

    Transform cam;
    ParticleSystem rain;
    AudioSource rainSource, thunderSource;
    AudioClip[] thunderClips;
    Light sun;
    float sunBase;
    LineRenderer bolt;
    UnityEngine.UI.Image flashImage;
    float timer, flash;

    void Start()
    {
        cam = Camera.main != null ? Camera.main.transform : null;
        sun = FindSun();
        BuildRain();
        BuildBolt();
        BuildFlash();

        var go = new GameObject("WeatherAudio");
        go.transform.SetParent(transform, false);
        rainSource = go.AddComponent<AudioSource>();
        rainSource.loop = true;
        rainSource.spatialBlend = 0f;
        rainSource.clip = Resources.Load<AudioClip>("Audio/Weather/rain_loop");
        if (rainSource.clip != null) rainSource.Play();
        thunderSource = go.AddComponent<AudioSource>();
        thunderSource.spatialBlend = 0f;
        thunderClips = new[]
        {
            Resources.Load<AudioClip>("Audio/Weather/thunder_01"),
            Resources.Load<AudioClip>("Audio/Weather/thunder_02"),
            Resources.Load<AudioClip>("Audio/Weather/thunder_03"),
        };
        timer = Random.Range(2f, 5f); // 들어오고 곧 한 번
    }

    void OnDestroy()
    {
        if (sun != null && flash > 0f) sun.intensity = sunBase;
    }

    static Light FindSun()
    {
        if (RenderSettings.sun != null) return RenderSettings.sun;
        Light best = null;
        foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional && (best == null || l.intensity > best.intensity)) best = l;
        return best;
    }

    void LateUpdate()
    {
        if (cam == null && Camera.main != null) cam = Camera.main.transform;
        if (cam != null && rain != null) rain.transform.position = cam.position + Vector3.up * 12f; // 비구름이 카메라를 따라다님
        if (rainSource != null) rainSource.volume = rainVolume * AudioOptions.SfxVolume;

        if (!GamePause.IsPaused)
        {
            timer -= Time.deltaTime;
            if (timer <= 0f)
            {
                timer = Random.Range(strikeInterval.x, strikeInterval.y);
                StartCoroutine(Strike());
            }
        }
    }

    // 핵심: 번쩍(두 번 깜빡) → 거리에 비례한 시간 뒤 천둥. 가까운 번개는 찢어지는 천둥
    IEnumerator Strike()
    {
        if (cam == null) yield break;
        float distance = Random.Range(60f, 140f);
        float yaw = Random.Range(0f, 360f);
        Vector3 ground = cam.position + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * distance;
        DrawBolt(ground + Vector3.up * 90f, ground);

        if (sun != null) sunBase = sun.intensity;
        float[] pulses = { 1f, 0f, 0.7f, 0f };
        float[] times = { 0.07f, 0.06f, 0.1f, 0f };
        for (int i = 0; i < pulses.Length; i++)
        {
            SetFlash(pulses[i]);
            if (times[i] > 0f) yield return new WaitForSeconds(times[i]);
        }
        for (float t = 0f; t < 0.4f; t += Time.deltaTime)
        {
            SetFlash(0.4f * (1f - t / 0.4f));
            yield return null;
        }
        SetFlash(0f);

        float delay = distance / 120f; // 실제 소리 속도보다 빠르게 (기다림이 지루하지 않게)
        yield return new WaitForSeconds(Mathf.Max(0f, delay - 0.65f));
        bool near = distance < 90f;
        AudioClip clip = near ? thunderClips[0] : thunderClips[Random.Range(1, thunderClips.Length)];
        if (clip == null) clip = thunderClips[0];
        if (clip == null) yield break;
        thunderSource.pitch = Random.Range(0.9f, 1.08f);
        thunderSource.PlayOneShot(clip, thunderVolume * (near ? 1f : 0.75f) * AudioOptions.SfxVolume);
    }

    void SetFlash(float k)
    {
        flash = k;
        if (sun != null) sun.intensity = sunBase + sunBoost * k;
        if (flashImage != null) flashImage.color = new Color(Bolt.r, Bolt.g, Bolt.b, screenFlash * k);
        if (bolt != null) bolt.enabled = k > 0.5f;
    }

    // ===== 만들기 =====
    void BuildRain()
    {
        var go = new GameObject("Rain");
        go.transform.SetParent(transform, false);
        rain = go.AddComponent<ParticleSystem>();
        rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = rain.main;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = 1f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.028f);
        main.startColor = new Color(0.7f, 0.78f, 0.92f, 0.3f);
        main.maxParticles = 4000;
        ParticleSystem.ShapeModule shape = rain.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(36f, 0.1f, 36f);
        ParticleSystem.VelocityOverLifetimeModule v = rain.velocityOverLifetime;
        v.enabled = true;
        v.space = ParticleSystemSimulationSpace.World;
        v.x = new ParticleSystem.MinMaxCurve(-2.5f, -1.5f);
        v.y = new ParticleSystem.MinMaxCurve(-24f, -20f);
        v.z = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);
        ParticleSystem.EmissionModule e = rain.emission;
        e.rateOverTime = 2600f;
        var r = rain.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.035f;
        r.lengthScale = 1f;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        main.prewarm = true;
        rain.Play();
    }

    void BuildBolt()
    {
        bolt = new GameObject("SkyBolt").AddComponent<LineRenderer>();
        bolt.transform.SetParent(transform, false);
        bolt.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        bolt.startColor = bolt.endColor = new Color(0.9f, 0.95f, 1f);
        bolt.widthMultiplier = 0.9f;
        bolt.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.3f));
        bolt.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        bolt.enabled = false;
    }

    // 중점을 흔들어 나누는 번개 모양
    void DrawBolt(Vector3 top, Vector3 bottom)
    {
        var points = new System.Collections.Generic.List<Vector3> { top, bottom };
        float spread = 12f;
        for (int d = 0; d < 5; d++)
        {
            var next = new System.Collections.Generic.List<Vector3>();
            for (int i = 0; i < points.Count - 1; i++)
            {
                next.Add(points[i]);
                Vector3 mid = (points[i] + points[i + 1]) * 0.5f;
                mid += new Vector3(Random.Range(-spread, spread), 0f, Random.Range(-spread, spread));
                next.Add(mid);
            }
            next.Add(points[points.Count - 1]);
            points = next;
            spread *= 0.5f;
        }
        bolt.positionCount = points.Count;
        bolt.SetPositions(points.ToArray());
    }

    void BuildFlash()
    {
        Canvas canvas = UIKit.CreateCanvas(transform, "WeatherFlash", 5); // HUD 아래
        flashImage = UIKit.Image(canvas.transform, null, new Color(Bolt.r, Bolt.g, Bolt.b, 0f));
        flashImage.raycastTarget = false;
        UIKit.Stretch(flashImage.rectTransform);
    }
}
