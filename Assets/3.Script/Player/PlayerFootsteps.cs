using UnityEngine;

// 발소리: 발이 땅에 닿는 순간마다 재생 (PlayerSfx가 발소리 클립이 있으면 실행 중에 붙임)
//  - 애니메이션의 발 뼈 높이로 착지를 잡음 → 걷기 · 달리기 · 공격 중 디딤까지 박자가 자동으로 맞음
//  - 실제로 움직이는 빠르기에 따라 크기가 달라짐 (걷기는 작게, 달리기는 크게). 공중에서는 안 남
public class PlayerFootsteps : MonoBehaviour
{
    const float MinSpeed = 0.6f;      // 이보다 느리면 제자리 발 움직임으로 보고 무시
    const float LiftHeight = 0.08f;   // 가장 낮던 높이보다 이만큼 들려야 다음 착지를 받음
    const float PlantHeight = 0.03f;  // 가장 낮은 높이 근처로 내려오면 착지
    const float MinInterval = 0.12f;  // 두 발이 거의 동시에 닿아도 한 번만

    AudioClip[] clips;
    AudioClip[] waterClips; // 연못 안 발소리 (Resources/Audio/Footsteps/water_step_*)
    AudioClip[] liftClips;  // 물에서 발을 뺄 때 쓸리는 소리 (water_lift_*) → 디딤 · 뺌이 번갈아 들려 물을 헤치고 걷는 느낌
    int lastWaterClip = -1;
    AudioSource wadeSource;
    // [변경] 젖은 땅(비 오는 씬): 기본 발소리 위에 얇은 '찰박'을 이 크기로 얹음. 0이면 끔 (DungeonWeather가 켜고 끔)
    public static float WetAmount;
    AudioClip[] wetClips;
    const float WaterVolume = 1.15f;
    const float LiftVolume = 0.55f;
    float volume, runSpeed;
    Vector2 pitch;
    Animator animator;
    PlayerLocomotion locomotion;
    AudioSource source;
    readonly Transform[] feet = new Transform[2];
    readonly bool[] lifted = new bool[2];
    readonly float[] lowest = new float[2];
    Vector3 lastPosition;
    float speed, lastStepTime;
    int lastClip = -1;

    public void Init(AudioClip[] stepClips, float stepVolume, Vector2 stepPitch, float fullVolumeSpeed)
    {
        clips = stepClips;
        volume = stepVolume;
        pitch = stepPitch;
        runSpeed = Mathf.Max(0.1f, fullVolumeSpeed);
    }

    public event System.Action<Vector3, float> Planted; // 발 위치 · 빠르기(0~1) → 발밑 먼지 · 물 튐

    void Start()
    {
        animator = GetComponentInChildren<Animator>();
        locomotion = GetComponent<PlayerLocomotion>();
        if (animator != null && animator.isHuman)
        {
            feet[0] = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            feet[1] = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        }
        AudioClip[] all = Resources.LoadAll<AudioClip>("Audio/Footsteps");
        waterClips = System.Array.FindAll(all, c => c.name.StartsWith("water_step"));
        liftClips = System.Array.FindAll(all, c => c.name.StartsWith("water_lift"));
        wetClips = System.Array.FindAll(all, c => c.name.StartsWith("wet_splash"));
        var wade = new GameObject("Sfx_Wade");
        wade.transform.SetParent(transform, false);
        wadeSource = wade.AddComponent<AudioSource>();
        wadeSource.playOnAwake = false;
        wadeSource.spatialBlend = 0f;
        var go = new GameObject("Sfx_Footsteps");
        go.transform.SetParent(transform, false);
        source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        lastPosition = transform.position;
        for (int i = 0; i < 2; i++) lowest[i] = FootHeight(i);
    }

    float FootHeight(int i) =>
        feet[i] != null ? (feet[i].position.y - transform.position.y) / Mathf.Max(0.01f, transform.lossyScale.y) : 0f;

    // 핵심: 발마다 "가장 낮던 높이"를 기억 → 충분히 들렸다가 다시 그 높이로 내려오는 순간이 착지
    void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || clips == null || clips.Length == 0) return;

        Vector3 delta = transform.position - lastPosition;
        delta.y = 0f;
        lastPosition = transform.position;
        speed = Mathf.Lerp(speed, delta.magnitude / dt, Mathf.Clamp01(dt * 10f));
        bool moving = speed > MinSpeed && (locomotion == null || locomotion.IsGrounded);

        for (int i = 0; i < 2; i++)
        {
            if (feet[i] == null) continue;
            float h = FootHeight(i);
            lowest[i] = Mathf.Min(lowest[i] + dt * 0.05f, h); // 자세가 바뀌어도 천천히 따라가게
            if (h > lowest[i] + LiftHeight)
            {
                if (!lifted[i] && moving) Lift(feet[i].position);
                lifted[i] = true;
            }
            else if (lifted[i] && h < lowest[i] + PlantHeight)
            {
                lifted[i] = false;
                if (moving && Time.time - lastStepTime >= MinInterval)
                {
                    Play(feet[i].position);
                    Planted?.Invoke(feet[i].position, Mathf.Clamp01(speed / runSpeed));
                }
            }
        }
    }

    void Play(Vector3 foot)
    {
        lastStepTime = Time.time;
        bool water = waterClips != null && waterClips.Length > 0 && PlanarWaterReflection.FindWater(foot, out _);
        AudioClip[] set = water ? waterClips : clips;
        int last = water ? lastWaterClip : lastClip;
        int i = Random.Range(0, set.Length);
        if (set.Length > 1 && i == last) i = (i + 1) % set.Length;
        if (water) lastWaterClip = i; else lastClip = i;
        if (set[i] == null) return;
        float pace = Mathf.Lerp(0.45f, 1f, Mathf.Clamp01(speed / runSpeed));
        source.pitch = Random.Range(pitch.x, pitch.y);
        source.PlayOneShot(set[i], volume * pace * (water ? WaterVolume : 1f) * AudioOptions.SfxVolume);

        // 걷기는 거의 안 들리고 달릴수록 또렷하게 (pace²)
        if (!water && WetAmount > 0f && wetClips != null && wetClips.Length > 0)
        {
            wadeSource.pitch = Random.Range(0.95f, 1.1f);
            wadeSource.PlayOneShot(wetClips[Random.Range(0, wetClips.Length)], volume * WetAmount * pace * pace * AudioOptions.SfxVolume);
        }
    }

    void Lift(Vector3 foot)
    {
        if (liftClips == null || liftClips.Length == 0 || !PlanarWaterReflection.FindWater(foot, out _)) return;
        float pace = Mathf.Lerp(0.45f, 1f, Mathf.Clamp01(speed / runSpeed));
        wadeSource.pitch = Random.Range(0.92f, 1.12f);
        wadeSource.PlayOneShot(liftClips[Random.Range(0, liftClips.Length)], volume * LiftVolume * pace * AudioOptions.SfxVolume);
    }
}
