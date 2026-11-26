using UnityEngine;

// 플레이어 효과음. 전투·스킬 이벤트를 받아 소리만 담당 (PlayerVfx와 같은 방식)
//  - 휘두르기: 매 공격마다 바람 소리. 판정이 켜지는 순간에 소리 정점이 오도록 지연 재생 (공속 반영)
//    적을 베면 바람 소리는 바로 줄여 끔 → 베는 소리만 들림 (예전엔 두 소리가 겹쳐 남)
//  - 베기: 칼이 적에게 맞았을 때 위에 겹침. 한 번에 여럿을 베도 한 번만
//  - 타격음 3겹: 베는 소리 + 살 맞는 둔탁한 소리 + 저음 쿵(강한 공격일수록 큼) → 가벼운 느낌 보완
//  - 발소리: PlayerFootsteps를 실행 중에 붙여 발이 땅에 닿을 때마다
//  - Q 차지: 누르는 순간부터 반복음 (일반 차지·풀차지·발도 자세 모두). 차지 단계·표식이 찰수록 커지고 높아짐
//  - 우클릭 차지 강타도 같은 반복음 (단계에 따라 커지고 높아짐)
//  - 발도 자세 진입: 진입음을 반복음 위에 겹쳐 한 번 (반복음은 끊기지 않음)
//  - 일섬 돌진: 표식한 적 옆을 지나가는 순간마다 베는 소리 // 돌진 시작 → 지나가는 순간(슬로와 같은 박자)
//  - 일섬 적중(칼을 거둔 뒤 일괄 베기): 낙뢰음. 여럿을 베면 첫 소리 뒤로 작게 몇 번 더 (따다닥)
//  - [변경] 사망: 쓰러지는 순간 사망 소리 (슬롯이 비면 Resources/Audio/Player/player_death), 부활하면 짧게 줄여 끔
//  - 처형: 숨통을 끊는 순간 처형 전용 소리 + 베는 소리를 낮게 겹침 / 완벽 회피: 성공 순간 맑은 "팅" + 시간이 늘어지는 울림
// 연결은 Tools/Setup/Player Sfx. 슬롯을 비우면 그 소리만 꺼짐
public class PlayerSfx : MonoBehaviour
{
    [SerializeField] PlayerCombat combat;
    [SerializeField] PlayerStats stats;  
    [SerializeField] PlayerSkill skill;
    [SerializeField] PlayerChargeAttack heavyCharge;
    [SerializeField] WeaponHitbox weapon; // 휘두르기 대신 적중으로 재생

    [Header("Air Swing")]
    [SerializeField] AudioClip[] airSwingClips;
    [SerializeField, Range(0f, 1f)] float airSwingVolume = 0.7f;
    [SerializeField] Vector2 airSwingPitch = new Vector2(0.97f, 1.03f);
    [SerializeField] float airSwingLead = 0.08f; // 판정 시작보다 이만큼 먼저 재생 (클립의 바람 소리 정점까지 걸리는 시간)

    [Header("Hit")]
    [SerializeField] AudioClip[] swingClips; // 이름은 그대로 (연결된 값 유지), 적중 소리로 사용
    [SerializeField, Range(0f, 1f)] float swingVolume = 0.8f;
    [SerializeField] Vector2 swingPitch = new Vector2(0.97f, 1.03f);
    [SerializeField] float heavyPitch = 0.9f;     // 강한 공격은 조금 낮고 묵직하게
    [SerializeField] float hitSoundInterval = 0.06f; // 이 시간 안에 여러 명을 베면 한 번만

    [Header("Hit Layers")] // 베는 소리 아래 깔리는 두 겹
    [SerializeField] AudioClip[] hitBodyClips;   // 살 맞는 둔탁한 소리
    [SerializeField, Range(0f, 1f)] float hitBodyVolume = 0.7f;
    [SerializeField] AudioClip[] hitThumpClips;  // 저음 쿵
    [SerializeField, Range(0f, 1f)] float hitThumpVolume = 0.85f;
    [SerializeField, Range(0f, 1f)] float lightThumpScale = 0.35f; // 약공격은 쿵을 작게

    [Header("Execution / Just Dodge")]
    [SerializeField] AudioClip[] executionClips;
    [SerializeField, Range(0f, 1f)] float executionVolume = 1f;
    [SerializeField, Range(0f, 1f)] float executionSliceVolume = 0.6f; // 겹치는 베는 소리 크기
    [SerializeField] AudioClip[] justDodgeClips;
    [SerializeField, Range(0f, 1f)] float justDodgeVolume = 0.8f;

    [Header("Death")] // [변경]
    [SerializeField] AudioClip deathClip;
    [SerializeField, Range(0f, 1f)] float deathVolume = 0.9f;
    [SerializeField] float deathFadeOnRevive = 0.8f;

    [Header("Footsteps")]
    [SerializeField] AudioClip[] footstepClips;
    [SerializeField, Range(0f, 1f)] float footstepVolume = 0.55f;
    [SerializeField] Vector2 footstepPitch = new Vector2(0.92f, 1.08f);
    [SerializeField] float footstepFullSpeed = 6f; // 이 빠르기(달리기)에서 가장 큼

    [Header("Charge / Draw Stance")]
    [SerializeField] AudioClip stanceStart;
    [SerializeField] AudioClip stanceLoop;
    [SerializeField, Range(0f, 1f)] float stanceStartVolume = 0.9f;
    [SerializeField] Vector2 stanceLoopVolume = new Vector2(0.55f, 1f); // 처음 → 가득 찼을 때
    [SerializeField] Vector2 stanceLoopPitch = new Vector2(1f, 1.12f);
    [SerializeField] float loopFadeIn = 0.08f;  // 반복음이 바로 시작하되 첫 순간 튀지 않게
    [SerializeField] float stanceFadeOut = 0.15f;
    [SerializeField] int maxChargeLevel = 2;    // 일반 차지 단계 → 채움 정도 (스킬 데이터 기본 최대 2단계)

    [Header("Skill Dash")]
    [SerializeField] AudioClip[] dashCutClips;
    [SerializeField, Range(0f, 1f)] float dashCutVolume = 0.9f;

    [Header("Skill Strike")]
    [SerializeField] AudioClip[] strikeClips;
    [SerializeField, Range(0f, 1f)] float strikeVolume = 1f;
    [SerializeField] int strikeExtra = 2;              // 2명 이상 베었을 때 덧붙는 소리 수 (최대)
    [SerializeField] float strikeExtraDelay = 0.07f;
    [SerializeField, Range(0f, 1f)] float strikeExtraVolume = 0.5f;

    AudioSource oneShot;
    AudioSource bodySource, thumpSource;
    int lastBody = -1, lastThump = -1;
    AudioSource executionSource, dodgeSource;
    int lastExecution = -1, lastDodge = -1;
    PlayerExecution execution;
    PlayerDodge dodge;
    AudioSource swingSource; // 종류마다 소스를 나눔 (한 소스의 피치를 바꾸면 이미 울리는 소리까지 바뀜)
    AudioSource dashSource;
    AudioSource[] strikeSources; // 덧붙는 낙뢰음은 예약 재생이 필요해 소스를 따로 둠
    int strikesThisFrame;
    AudioSource stanceStartSource;
    AudioSource stanceLoopSource;
    AudioSource deathSource; // [변경]
    PlayerHealth health;
    float deathFade = -1f;   // 0 이상이면 사망 소리를 줄이는 중

    int lastSwing = -1;
    float airFade = -1f; // 0 이상이면 바람 소리를 줄이는 중
    int lastAirSwing = -1;
    int lastDashCut = -1;
    AttackData pendingSwing;
    bool pendingHeavy;
    float pendingTimer = -1f;
    float lastHitTime = -1f;

    bool charging;      // HoldStarted ~ HoldEnded (일반 차지·발도 자세·회피 직후 풀차지 포함)
    int chargeLevel;
    bool heavyCharging; // 우클릭 차지 강타
    int heavyLevel;
    bool wasStance;
    float loopFade;     // 0~1

    void Awake()
    {
        if (combat == null) combat = GetComponent<PlayerCombat>();
        if (stats == null) stats = GetComponent<PlayerStats>();
        if (skill == null) skill = GetComponent<PlayerSkill>();
        if (heavyCharge == null) heavyCharge = GetComponent<PlayerChargeAttack>();
        if (weapon == null) weapon = GetComponentInChildren<WeaponHitbox>(true);
        execution = GetComponent<PlayerExecution>();
        dodge = GetComponent<PlayerDodge>();
        health = GetComponent<PlayerHealth>(); // [변경]
        if (deathClip == null) deathClip = Resources.Load<AudioClip>("Audio/Player/player_death");
        deathSource = CreateSource("Sfx_Death", false);

        oneShot = CreateSource("Sfx_OneShot", false);
        bodySource = CreateSource("Sfx_HitBody", false);
        thumpSource = CreateSource("Sfx_HitThump", false);
        executionSource = CreateSource("Sfx_Execution", false);
        dodgeSource = CreateSource("Sfx_JustDodge", false);
        if (footstepClips != null && footstepClips.Length > 0 && GetComponent<PlayerFootsteps>() == null)
            gameObject.AddComponent<PlayerFootsteps>().Init(footstepClips, footstepVolume, footstepPitch, footstepFullSpeed);
        swingSource = CreateSource("Sfx_Swing", false);
        dashSource = CreateSource("Sfx_DashCut", false);
        stanceStartSource = CreateSource("Sfx_StanceStart", false);
        stanceLoopSource = CreateSource("Sfx_StanceLoop", true);
        strikeSources = new AudioSource[Mathf.Max(0, strikeExtra) + 1];
        for (int i = 0; i < strikeSources.Length; i++) strikeSources[i] = CreateSource("Sfx_Strike" + i, false);
    }

    void OnEnable()
    {
        if (weapon != null) weapon.OnHit += OnWeaponHit;
        if (combat != null) combat.AttackStarted += OnAttackStarted;
        if (execution != null) execution.Executed += OnExecuted;
        if (dodge != null) dodge.JustDodged += OnJustDodged;
        PlayerHealth.AnyDied += OnDied; // [변경]
        PlayerHealth.AnyRevived += OnRevived;
        if (skill != null)
        {
            skill.HoldStarted += OnHoldStarted;
            skill.ChargeLevelChanged += OnChargeLevelChanged;
            skill.HoldEnded += OnHoldEnded;
            skill.TargetCut += OnTargetCut;
            skill.TargetPassed += OnTargetPassed;
        }
        if (heavyCharge != null)
        {
            heavyCharge.ChargeStarted += OnHeavyChargeStarted;
            heavyCharge.ChargeLevelChanged += OnHeavyLevelChanged;
            heavyCharge.ChargeEnded += OnHeavyChargeEnded;
        }
    }

    void OnDisable()
    {
        if (weapon != null) weapon.OnHit -= OnWeaponHit;
        if (combat != null) combat.AttackStarted -= OnAttackStarted;
        if (execution != null) execution.Executed -= OnExecuted;
        if (dodge != null) dodge.JustDodged -= OnJustDodged;
        PlayerHealth.AnyDied -= OnDied; // [변경]
        PlayerHealth.AnyRevived -= OnRevived;
        pendingTimer = -1f;
        if (skill != null)
        {
            skill.HoldStarted -= OnHoldStarted;
            skill.ChargeLevelChanged -= OnChargeLevelChanged;
            skill.HoldEnded -= OnHoldEnded;
            skill.TargetCut -= OnTargetCut;
            skill.TargetPassed -= OnTargetPassed;
        }
        if (heavyCharge != null)
        {
            heavyCharge.ChargeStarted -= OnHeavyChargeStarted;
            heavyCharge.ChargeLevelChanged -= OnHeavyLevelChanged;
            heavyCharge.ChargeEnded -= OnHeavyChargeEnded;
        }
        charging = false;
        heavyCharging = false;
        StopCharge();
    }

    AudioSource CreateSource(string objectName, bool loop)
    {
        var go = new GameObject(objectName);
        go.transform.SetParent(transform, false);
        var source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 0f; // 플레이어 소리는 항상 화면 중심이라 2D
        return source;
    }

    void Update()
    {
        UpdateAirFade();
        UpdateAirSwing();
        UpdateCharge();
        UpdateDeathFade(); // [변경]
    }

    // ===== 휘두르기 =====
    void OnAttackStarted(AttackData attack, AttackInput input)
    {
        if (airSwingClips == null || airSwingClips.Length == 0 || attack == null) return;

        // 핵심: 판정 시작(실제 시간) - 정점까지 시간 → 칼이 지나가는 순간에 소리가 가장 큼. 다음 공격이 오면 예약을 덮어씀
        float speed = stats != null ? Mathf.Max(0.1f, stats.AttackSpeed) : 1f;
        float hitAt = attack.HitStart * attack.Duration / speed;
        pendingSwing = attack;
        pendingHeavy = input == AttackInput.Heavy;
        pendingTimer = Mathf.Max(0f, hitAt - airSwingLead);
        if (pendingTimer <= 0f) PlayAirSwing();
    }

    void UpdateAirSwing()
    {
        if (pendingTimer < 0f) return;
        pendingTimer -= Time.deltaTime; // 공격 시간과 같은 시계 (히트스톱 중엔 같이 늦어짐)
        if (pendingTimer <= 0f) PlayAirSwing();
    }

    void PlayAirSwing()
    {
        pendingTimer = -1f;
        if (pendingSwing == null) return;
        airFade = -1f;
        swingSource.volume = 1f;
        AudioClip clip = Pick(airSwingClips, ref lastAirSwing);
        if (clip == null) return;
        swingSource.pitch = Random.Range(airSwingPitch.x, airSwingPitch.y) * (pendingHeavy ? heavyPitch : 1f);
        swingSource.PlayOneShot(clip, airSwingVolume * AudioOptions.SfxVolume);
    }

    // 적중 순간 바람 소리를 0.04초 만에 줄여 끔 (뚝 끊기는 소리가 나지 않게) · 아직 예약 중이면 취소
    void SilenceAirSwing()
    {
        pendingTimer = -1f;
        if (swingSource.isPlaying) airFade = 0f;
    }

    void UpdateAirFade()
    {
        if (airFade < 0f) return;
        airFade += Time.unscaledDeltaTime / 0.04f;
        swingSource.volume = Mathf.Clamp01(1f - airFade);
        if (airFade < 1f) return;
        swingSource.Stop();
        swingSource.volume = 1f;
        airFade = -1f;
    }

    // ===== 일섬 돌진 =====
    void OnTargetPassed(Vector3 position, bool last)
    {
        AudioClip clip = Pick(dashCutClips, ref lastDashCut);
        if (clip == null) return;
        dashSource.pitch = Random.Range(0.97f, 1.03f);
        dashSource.PlayOneShot(clip, dashCutVolume * AudioOptions.SfxVolume);
    }

    // 직전과 다른 변형을 무작위로
    static AudioClip Pick(AudioClip[] clips, ref int last)
    {
        if (clips == null || clips.Length == 0) return null;
        int i = Random.Range(0, clips.Length);
        if (clips.Length > 1 && i == last) i = (i + 1) % clips.Length;
        last = i;
        return clips[i];
    }

    // 같은 프레임에 베인 수를 모아 한 번에 처리 (일괄 베기는 한 프레임에 전부 들어옴)
    void LateUpdate()
    {
        if (strikesThisFrame > 0) PlayStrikes(strikesThisFrame);
        strikesThisFrame = 0;
    }

    void OnTargetCut(Vector3 position) => strikesThisFrame++;

    // 핵심: 첫 낙뢰는 바로, 덧붙는 낙뢰는 dsp 시계로 예약 → 프레임과 무관하게 일정한 간격
    void PlayStrikes(int count)
    {
        if (strikeClips == null || strikeClips.Length == 0) return;
        double now = AudioSettings.dspTime + 0.005;
        int total = Mathf.Min(count, strikeSources.Length);
        int first = Random.Range(0, strikeClips.Length);
        for (int i = 0; i < total; i++)
        {
            AudioClip clip = strikeClips[(first + i) % strikeClips.Length];
            if (clip == null) continue;
            AudioSource source = strikeSources[i];
            source.Stop();
            source.clip = clip;
            source.pitch = Random.Range(0.97f, 1.03f);
            source.volume = (i == 0 ? strikeVolume : strikeVolume * strikeExtraVolume) * AudioOptions.SfxVolume;
            source.PlayScheduled(now + i * strikeExtraDelay);
        }
    }

    // ===== 적중 =====
    void OnWeaponHit(IDamageable target, DamageInfo info, HitResult result)
    {
        if (result != HitResult.Hit) return;
        SilenceAirSwing(); // 베었으면 허공 소리는 끔
        if (swingClips == null || swingClips.Length == 0) return;
        if (Time.unscaledTime - lastHitTime < hitSoundInterval) return;
        lastHitTime = Time.unscaledTime;

        int i = Random.Range(0, swingClips.Length);
        if (swingClips.Length > 1 && i == lastSwing) i = (i + 1) % swingClips.Length;
        lastSwing = i;
        if (swingClips[i] == null) return;

        bool heavy = info.Weight != AttackWeight.Light;
        oneShot.pitch = Random.Range(swingPitch.x, swingPitch.y) * (heavy ? heavyPitch : 1f);
        oneShot.PlayOneShot(swingClips[i], swingVolume * AudioOptions.SfxVolume);

        // 아래 두 겹: 살 맞는 소리는 항상, 저음 쿵은 강한 공격에서 크게
        PlayLayer(bodySource, hitBodyClips, ref lastBody, hitBodyVolume, heavy ? 0.85f : 1f);
        PlayLayer(thumpSource, hitThumpClips, ref lastThump, hitThumpVolume * (heavy ? 1f : lightThumpScale), heavy ? 0.9f : 1.05f);
    }

    void PlayLayer(AudioSource source, AudioClip[] clips, ref int last, float volume, float basePitch)
    {
        AudioClip clip = Pick(clips, ref last);
        if (clip == null || volume <= 0f) return;
        source.pitch = basePitch * Random.Range(0.95f, 1.05f);
        source.PlayOneShot(clip, volume * AudioOptions.SfxVolume);
    }

    // ===== 처형 · 완벽 회피 =====
    // 처형 짝 모션에서 숨통을 끊는 순간(Executed): 처형 소리 + 베는 소리를 낮게 겹쳐 묵직하게
    void OnExecuted(EnemySwordsman victim)
    {
        PlayLayer(executionSource, executionClips, ref lastExecution, executionVolume, 1f);
        PlayLayer(oneShot, swingClips, ref lastSwing, swingVolume * executionSliceVolume, 0.85f);
    }

    void OnJustDodged() => PlayLayer(dodgeSource, justDodgeClips, ref lastDodge, justDodgeVolume, 1f);

    // ===== [변경] 사망 =====
    void OnDied(PlayerHealth who)
    {
        if (who != health || deathClip == null) return;
        StopCharge(); // 차지 반복음이 남지 않게
        deathFade = -1f;
        deathSource.Stop();
        deathSource.clip = deathClip;
        deathSource.volume = deathVolume * AudioOptions.SfxVolume;
        deathSource.Play();
    }

    // 부활 시 남은 울림을 짧게 줄여 끔 (부활 대기가 소리보다 짧음)
    void OnRevived(PlayerHealth who)
    {
        if (who == health && deathSource.isPlaying) deathFade = 0f;
    }

    void UpdateDeathFade()
    {
        if (deathFade < 0f) return;
        deathFade += Time.unscaledDeltaTime / Mathf.Max(0.01f, deathFadeOnRevive);
        deathSource.volume = deathVolume * AudioOptions.SfxVolume * (1f - Mathf.Clamp01(deathFade));
        if (deathFade >= 1f)
        {
            deathSource.Stop();
            deathFade = -1f;
        }
    }

    // ===== Q 차지 · 발도 자세 =====
    void OnHoldStarted()
    {
        charging = true;
        chargeLevel = 0;
        StartLoop();
    }

    // 우클릭 차지 강타
    void OnHeavyChargeStarted()
    {
        heavyCharging = true;
        heavyLevel = 0;
        StartLoop();
    }

    void OnHeavyLevelChanged(int level) => heavyLevel = level;

    void OnHeavyChargeEnded() => heavyCharging = false;

    void StartLoop()
    {
        if (stanceLoop == null) return;
        if (!stanceLoopSource.isPlaying)
        {
            stanceLoopSource.clip = stanceLoop;
            stanceLoopSource.volume = 0f;
            stanceLoopSource.Play();
            loopFade = 0f;
        }
    }

    void OnChargeLevelChanged(int level) => chargeLevel = level;

    void OnHoldEnded() => charging = false;

    void UpdateCharge()
    {
        bool stance = skill != null && skill.InDrawStance && !skill.StanceAutoDraw;
        // 핵심: 발도 자세에 들어가는 순간 진입음을 반복음 위에 겹침 → 진입음이 끝나길 기다리지 않음
        if (stance && !wasStance && stanceStart != null)
        {
            stanceStartSource.pitch = 1f;
            stanceStartSource.PlayOneShot(stanceStart, stanceStartVolume * AudioOptions.SfxVolume);
        }
        wasStance = stance;

        if (!stanceLoopSource.isPlaying) return;

        bool on = charging || heavyCharging;
        float speed = on ? 1f / Mathf.Max(0.01f, loopFadeIn) : 1f / Mathf.Max(0.01f, stanceFadeOut);
        loopFade = Mathf.MoveTowards(loopFade, on ? 1f : 0f, Time.unscaledDeltaTime * speed);

        float fill = charging ? Mathf.Clamp01(chargeLevel / (float)Mathf.Max(1, maxChargeLevel)) : 0f;
        if (heavyCharging) fill = Mathf.Max(fill, Mathf.Clamp01((heavyLevel - 1) / (float)Mathf.Max(1, heavyCharge.MaxLevel - 1))); // 1단계부터 시작
        if (stance)
        {
            float marks = skill.StanceMarkCount / (float)skill.StanceMarkScale; // 각성 발도는 표식 제한이 없어 기준 수로 나눔
            fill = Mathf.Max(fill, Mathf.Clamp01((1f - skill.StanceRemaining01) * 0.6f + marks));
        }

        stanceLoopSource.volume = Mathf.Lerp(stanceLoopVolume.x, stanceLoopVolume.y, fill) * loopFade * AudioOptions.SfxVolume;
        stanceLoopSource.pitch = Mathf.Lerp(stanceLoopPitch.x, stanceLoopPitch.y, fill);

        if (!on && loopFade <= 0f) StopCharge();
    }

    void StopCharge()
    {
        if (stanceLoopSource != null) stanceLoopSource.Stop();
        loopFade = 0f;
    }
}
