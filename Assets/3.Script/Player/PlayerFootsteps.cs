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

    void Start()
    {
        animator = GetComponentInChildren<Animator>();
        locomotion = GetComponent<PlayerLocomotion>();
        if (animator != null && animator.isHuman)
        {
            feet[0] = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            feet[1] = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        }
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
            if (h > lowest[i] + LiftHeight) lifted[i] = true;
            else if (lifted[i] && h < lowest[i] + PlantHeight)
            {
                lifted[i] = false;
                if (moving && Time.time - lastStepTime >= MinInterval) Play();
            }
        }
    }

    void Play()
    {
        lastStepTime = Time.time;
        int i = Random.Range(0, clips.Length);
        if (clips.Length > 1 && i == lastClip) i = (i + 1) % clips.Length;
        lastClip = i;
        if (clips[i] == null) return;
        float pace = Mathf.Lerp(0.45f, 1f, Mathf.Clamp01(speed / runSpeed));
        source.pitch = Random.Range(pitch.x, pitch.y);
        source.PlayOneShot(clips[i], volume * pace * AudioOptions.SfxVolume);
    }
}
