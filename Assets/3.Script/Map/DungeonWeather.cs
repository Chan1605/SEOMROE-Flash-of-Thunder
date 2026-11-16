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
    [SerializeField] float rainRate = 3000f;   // [변경] 먼 비 층 기준 (가까운 층은 이 값의 0.35배)
    [SerializeField] float splashRate = 450f;  // [변경] 바닥 물방울 튐

    Transform cam;
    ParticleSystem rainNear, rainFar, splash; // [변경] 비를 가까운 층 · 먼 층으로 나누고 바닥 튐 추가
    Material dropMat, ringMat;
    float groundY, groundTimer;
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
        groundY = cam != null ? GroundBelow(cam.position, cam.position.y - 2f) : 0f; // [변경]
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
        if (dropMat != null) Destroy(dropMat.mainTexture);
        if (ringMat != null) Destroy(ringMat.mainTexture); // [변경] 실행 중 만든 텍스처 정리
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
        // [변경] 두 층의 비 · 바닥 튐이 카메라를 따라다님
        if (cam != null)
        {
            rainNear.transform.position = cam.position + Vector3.up * 7f;
            rainFar.transform.position = cam.position + Vector3.up * 14f;
            groundTimer -= Time.deltaTime;
            if (groundTimer <= 0f)
            {
                groundTimer = 0.2f;
                groundY = GroundBelow(cam.position, groundY);
            }
            splash.transform.position = new Vector3(cam.position.x, groundY + 0.03f, cam.position.z);
        }
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
    // [변경] 비 다시 만듦: 부드러운 점 텍스처로 끝이 가늘어지는 얇은 빗줄기, 가까운/먼 두 층으로 깊이감, 바닥에 물방울 고리
    void BuildRain()
    {
        dropMat = new Material(Shader.Find("Sprites/Default")) { mainTexture = SoftDot() };
        ringMat = new Material(Shader.Find("Sprites/Default")) { mainTexture = Ring() };

        // 가까운 층: 조금 굵고 밝게, 좁은 범위 · 먼 층: 아주 가늘고 흐리게, 넓은 범위
        rainNear = RainLayer("RainNear", 16f, rainRate * 0.35f, new Vector2(0.011f, 0.017f), 0.42f, new Vector2(-27f, -23f), 0.6f);
        rainFar = RainLayer("RainFar", 44f, rainRate, new Vector2(0.007f, 0.011f), 0.26f, new Vector2(-24f, -20f), 1.1f);
        splash = Splashes();
    }

    ParticleSystem RainLayer(string name, float box, float rate, Vector2 size, float alpha, Vector2 fall, float life)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.prewarm = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = life;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.62f, 0.7f, 0.85f, alpha * 0.55f), new Color(0.8f, 0.86f, 0.96f, alpha));
        main.maxParticles = Mathf.CeilToInt(rate * life * 1.2f);
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(box, 0.1f, box);
        ParticleSystem.VelocityOverLifetimeModule v = ps.velocityOverLifetime;
        v.enabled = true;
        v.space = ParticleSystemSimulationSpace.World;
        v.x = new ParticleSystem.MinMaxCurve(-2.2f, -1.6f); // 바람에 살짝 기움 (모두 비슷하게 → 빗줄기가 나란함)
        v.y = new ParticleSystem.MinMaxCurve(fall.x, fall.y);
        v.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
        ParticleSystem.EmissionModule e = ps.emission;
        e.rateOverTime = rate;
        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = dropMat;
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.022f; // 빗줄기 길이 ≈ 0.5m (한 프레임 잔상 정도)
        r.lengthScale = 2f;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        ps.Play();
        return ps;
    }

    // 바닥에 떨어진 자리에서 퍼지는 작은 고리 (카메라 아래 바닥 높이에 깔림)
    ParticleSystem Splashes()
    {
        var go = new GameObject("RainSplash");
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.4f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.38f);
        main.startColor = new Color(0.78f, 0.85f, 0.95f, 0.32f);
        main.maxParticles = 600;
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(22f, 0f, 22f);
        ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.25f, 1f, 1f));
        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        ParticleSystem.EmissionModule e = ps.emission;
        e.rateOverTime = splashRate;
        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = ringMat;
        r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        ps.Play();
        return ps;
    }

    static float GroundBelow(Vector3 from, float fallback)
    {
        float best = float.MaxValue;
        float y = fallback;
        foreach (RaycastHit hit in Physics.RaycastAll(from + Vector3.up * 2f, Vector3.down, 40f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.GetComponentInParent<CharacterController>() != null || hit.distance >= best) continue;
            best = hit.distance;
            y = hit.point.y;
        }
        return y;
    }

    // 가운데가 진하고 가장자리로 갈수록 사라지는 점 → 늘이면 양끝이 가는 빗줄기
    static Texture2D SoftDot()
    {
        const int N = 32;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = new Vector2((x + 0.5f) / N - 0.5f, (y + 0.5f) / N - 0.5f).magnitude * 2f;
                float a = Mathf.Clamp01(1f - d);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        tex.Apply();
        return tex;
    }

    static Texture2D Ring()
    {
        const int N = 64;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = new Vector2((x + 0.5f) / N - 0.5f, (y + 0.5f) / N - 0.5f).magnitude * 2f;
                float a = Mathf.Exp(-Mathf.Pow((d - 0.78f) / 0.09f, 2f));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        return tex;
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
