using UnityEngine;

// 보스 흑뢰 페이즈 흐름 · 등장
//  부활: 1페이즈 체력을 다 깎으면(처형 · 궁극기 포함) 쓰러진 척 → 낙뢰를 맞고 일어나며 체력이 phase2Ratio까지 차오름 → 포효(2페이즈 연출)
//        쓰러져 있는 동안 · 일어나는 동안은 무적. 처형으로 끝냈으면 짝 모션이 끝난 자리에서 바로 일어남 (BossGetUp)
//  팔방 검기: 2페이즈 도약 착지 순간 8갈래 → radialDelay 뒤 22.5° 돌린 8갈래. 첫 고리는 플레이어가 틈에 오도록, 두 번째는 플레이어 쪽을 겨눔
//  첫 등장: 레터박스 + 포탈이 열리고 보스가 걸어 나와 칼을 뽑음 (플레이어 조작 잠금 · 보스 무적). 재도전은 기존처럼 바로 싸움
public partial class EnemySwordsman
{
    [Header("Boss Intro")]
    [SerializeField] Color introPortalColor = new Color(1.6f, 0.35f, 0.45f);
    [SerializeField] Color introPortalBg = new Color(0.12f, 0.02f, 0.06f);

    // 등장 시간표 (초)
    const float IntroPortalOpen = 0.9f;  // 포탈이 열리고 보스가 나타나 걷기 시작
    const float IntroWalkEnd = 2.6f;     // 걷기 끝 · 포탈 닫힘
    const float IntroDraw = 2.8f;        // 칼 뽑기 + 이름

    bool reviving, reviveRose, introWalking, introShown, introClosed, introDrew, awaitingIntro;
    float reviveRoseAt, radialSecondAt = -1f, radialBaseYaw;
    Vector3 radialOrigin, introOrigin, introForward;
    WarpPortalFx introPortal;
    Renderer[] introRenderers;
    UltimateFx introFx;
    PlayerLocomotion lockedLocomotion;
    PlayerCombat lockedCombat;
    PlayerHealth lockedHealth;

    bool CanRevive => IsBoss && !phase2 && !reviving && data.Phase2Ratio > 0f;
    float BossMaxHealth => data.MaxHealth * healthScale;

    // ===== 부활 =====
    void BeginRevive()
    {
        reviving = true;
        reviveRose = false;
        health = 0f;
        if (swingActive) EndSwing();
        currentAttack = null;
        ClearPerilous();
        DestroyTelegraph();
        ClearRain();
        ReleaseGrabbedPlayer();
        radialSecondAt = -1f;
        postureBroken = false;
        posture = 0f;
        KillSlowMo.Play(0.3f, 0.8f); // 진짜 쓰러진 것처럼
        BossSound(BossSfx.Cue.LeapLand);
        Log("1페이즈 끝 · 부활 대기");
        if (state == State.Executed) return; // 처형 짝 모션이 끝나면 BossGetUp에서 이어서 일어남

        executableUntil = -1f;
        if (!controller.enabled) controller.enabled = true;
        moveSpeed = 0f;
        animator.CrossFadeInFixedTime(DeathHash, 0.1f, 0, 0f);
        SetState(State.BossRevive);
    }

    void UpdateBossRevive()
    {
        moveSpeed = 0f;
        if (!reviveRose)
        {
            if (stateTime < data.ReviveFallTime) return;
            ReviveRise();
        }

        // 핵심: 일어나는 동안 체력 막대가 0 → phase2Ratio로 부드럽게 차오름
        float k = Mathf.Clamp01((stateTime - reviveRoseAt) / Mathf.Max(0.1f, data.ReviveFillTime));
        health = BossMaxHealth * data.Phase2Ratio * k * k * (3f - 2f * k);
        if (k < 1f || stateTime - reviveRoseAt < data.ReviveFillTime + 0.2f) return;

        reviving = false;
        reviveRose = false;
        health = BossMaxHealth * data.Phase2Ratio;
        StartRoar(); // 포효 + 2페이즈 연출 (BeginPhase2Show)
    }

    void ReviveRise()
    {
        reviveRose = true;
        reviveRoseAt = stateTime;
        Vector3 c = transform.position;
        ThunderBolt3D.StrikeDark(c + new Vector3(-1.5f, 28f, 1f), c, 1.8f, 4); // [변경] 검붉은 번개
        BossSound(BossSfx.Cue.Thunder);
        if (bossCamera != null) bossCamera.Shake(0.25f, 0.4f);
        if (phaseFx != null) phaseFx.Enter(); // 하늘이 먼저 어두워지기 시작
        if (animator.HasState(0, BossGetUpHash)) animator.CrossFadeInFixedTime(BossGetUpHash, 0.2f, 0, 0f);
        DamageEvents.RaiseLabel(c + Vector3.up * 2.6f * transform.lossyScale.y, "다시 일어선다", new Color(1f, 0.3f, 0.25f));
        Log("부활");
    }

    // ===== 2페이즈 팔방 검기 =====
    void FireRadialWaves(bool first)
    {
        int count = Mathf.Max(1, data.RadialCount);
        float step = 360f / count;
        if (first)
        {
            radialOrigin = transform.position;
            Vector3 to = Flat(target.position - radialOrigin);
            float toYaw = to.sqrMagnitude > 0.01f ? Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg : transform.eulerAngles.y;
            radialBaseYaw = toYaw + step * 0.5f; // 첫 고리: 플레이어가 갈래 사이 틈
            radialSecondAt = localTime + data.RadialDelay;
        }
        float offset = first ? 0f : step * 0.5f; // 두 번째: 반 칸 돌려 플레이어 쪽을 겨눔 → 움직이거나 회피
        float s = transform.lossyScale.x;
        Vector3 origin = radialOrigin + Vector3.up * 0.2f; // 땅에 붙어 기어가는 검기
        for (int i = 0; i < count; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, radialBaseYaw + offset + i * step, 0f) * Vector3.forward;
            BossSwordWave.Fire(gameObject, origin + dir * 0.8f * s, dir, data.WaveSpeed * data.RadialSpeedScale, data.WaveRange, data.RadialWidth, new DamageInfo
            {
                Attacker = gameObject,
                Damage = data.WaveDamage * DamageMul,
                Weight = AttackWeight.Heavy
            }, waveVisual, waveHitVfx, telegraphMaterial, true);
        }
        BossSound(BossSfx.Cue.Wave);
    }

    void UpdateRadialWaves()
    {
        if (radialSecondAt < 0f || localTime < radialSecondAt) return;
        radialSecondAt = -1f;
        if (state == State.Dead || reviving) return;
        FireRadialWaves(false);
    }

    // ===== 첫 등장 =====
    // 등장 연출을 볼 보스는 생성 순간부터 숨김 → 플레이어가 다가와 연출이 시작될 때 포탈에서 처음 나타남
    void HideUntilIntro()
    {
        if (!IsBoss || data == null || !data.IntroPortal || introSeen.Contains(data)) return;
        awaitingIntro = true;
        SetBodyVisible(false);
    }

    void StartBossIntro()
    {
        awaitingIntro = false;
        ActiveBoss = null; // 등장이 끝난 뒤 체력 막대가 나타나도록
        moveSpeed = 0f;
        float s = transform.lossyScale.x;
        introOrigin = transform.position;
        Vector3 to = Flat(target.position - introOrigin);
        introForward = to.sqrMagnitude > 0.01f ? to.normalized : transform.forward;
        transform.rotation = Quaternion.LookRotation(introForward, Vector3.up);

        // 포탈은 원래 자리, 보스는 그 뒤 2m에 숨어 있다가 걸어 나옴
        introPortal = WarpPortalFx.Create(null, introOrigin + Vector3.up * 1.9f * s, Quaternion.LookRotation(-introForward, Vector3.up), 1.6f * s, introPortalColor, introPortalBg, false, 3f);
        introPortal.Open(0.8f);
        controller.enabled = false;
        transform.position = introOrigin - introForward * 2f * s;
        controller.enabled = true;
        SetBodyVisible(false);
        introWalking = introShown = introClosed = introDrew = false;

        LockPlayerForIntro(true);
        introFx = FindAnyObjectByType<UltimateFx>();
        if (introFx != null) introFx.Letterbox(true);
        if (bossCamera != null)
            bossCamera.BeginCinematic(introOrigin + Vector3.up * 1.6f * s, Mathf.Atan2(introForward.x, introForward.z) * Mathf.Rad2Deg + 200f, 5f, 7.5f * s);
        BossSound(BossSfx.Cue.DashTell);
        SetState(State.BossIntro);
        Log("등장 연출");
    }

    void UpdateBossIntro(float dt)
    {
        moveSpeed = 0f;
        float s = transform.lossyScale.x;
        if (!introShown && stateTime >= IntroPortalOpen)
        {
            introShown = introWalking = true;
            SetBodyVisible(true);
            PlayMoveState(0.1f);
        }
        if (introWalking)
        {
            transform.rotation = Quaternion.LookRotation(introForward, Vector3.up);
            if (Vector3.Dot(transform.position - introOrigin, introForward) < 1f * s && stateTime < IntroWalkEnd) moveSpeed = data.WalkSpeed;
            else introWalking = false;
        }
        if (!introClosed && stateTime >= IntroWalkEnd)
        {
            introClosed = true;
            if (introPortal != null) introPortal.Close(0.5f, true);
            introPortal = null;
        }
        if (!introDrew && stateTime >= IntroDraw)
        {
            introDrew = true;
            if (animator.HasState(0, BossIntroHash)) animator.CrossFadeInFixedTime(BossIntroHash, 0.15f, 0, 0f);
            if (introFx != null) introFx.ShowTitle("黑雷", BossName, data.IntroTime + 0.6f);
            Vector3 p = transform.position + transform.right * 3f * s;
            ThunderBolt3D.StrikeDark(p + Vector3.up * 26f, p, 1.4f, 3); // [변경] 검붉은 번개
            BossSound(BossSfx.Cue.Thunder);
            if (bossCamera != null) bossCamera.Shake(0.15f, 0.3f);
        }
        if (stateTime < IntroDraw + data.IntroTime) return;

        EndBossIntro();
        attackReadyTime = localTime + 0.5f;
        PlayMoveState(0.25f);
        SetState(State.Chase);
    }

    // 끝났거나 끊김(비활성화 · 재시작): 숨긴 몸 · 포탈 · 레터박스 · 카메라 · 플레이어 잠금을 모두 되돌림
    void EndBossIntro()
    {
        SetBodyVisible(true);
        if (introPortal != null) introPortal.Close(0.3f, true);
        introPortal = null;
        if (introFx != null) introFx.Letterbox(false);
        introFx = null;
        if (bossCamera != null && state == State.BossIntro) bossCamera.EndCinematic();
        LockPlayerForIntro(false);
        if (state == State.BossIntro) ActiveBoss = this;
    }

    void LockPlayerForIntro(bool on)
    {
        if (on)
        {
            if (target == null) return;
            lockedLocomotion = target.GetComponent<PlayerLocomotion>();
            lockedCombat = target.GetComponent<PlayerCombat>();
            lockedHealth = target.GetComponent<PlayerHealth>();
        }
        if (lockedCombat != null)
        {
            if (on) lockedCombat.CancelAttack();
            lockedCombat.InputLocked = on;
        }
        if (lockedLocomotion != null) lockedLocomotion.MovementLocked = on;
        if (lockedHealth != null)
        {
            lockedHealth.ActionLocked = on;
            lockedHealth.SetInvulnerable(on ? 10f : 0.5f);
        }
        if (!on) lockedLocomotion = null;
        if (!on) lockedCombat = null;
        if (!on) lockedHealth = null;
    }

    void SetBodyVisible(bool visible)
    {
        if (introRenderers == null) introRenderers = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in introRenderers)
            if (r != null && !(r is ParticleSystemRenderer)) r.forceRenderingOff = !visible;
    }

    // 재시작 · 비활성화 때 부활 · 검기 · 등장 상태 정리 (ResetBossSkills · BossSkillsOnDisable에서 부름)
    void ResetBossPhase()
    {
        reviving = reviveRose = false;
        if (awaitingIntro && introSeen.Contains(data)) // 재도전으로 등장 연출을 건너뛰면 바로 보이게
        {
            awaitingIntro = false;
            SetBodyVisible(true);
        }
        radialSecondAt = -1f;
        if (state == State.BossIntro || introPortal != null || lockedHealth != null) EndBossIntro();
    }
}
