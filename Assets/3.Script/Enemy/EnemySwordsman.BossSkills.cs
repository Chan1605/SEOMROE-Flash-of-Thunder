using System.Collections.Generic;
using UnityEngine;

// 보스 흑뢰 2페이즈 보강 (수치는 EnemyData "Boss · Lightning Dash" · "Boss · Thunder Rain")
//  뇌광 연참: 바닥에 푸른 선 예고 → 번개처럼 일직선으로 꿰뚫고 지나감 × dashCount. 예고 마지막 30%는 방향 고정 → 옆으로 비키거나 회피
//             패링 불가, 마지막 한 번만 넘어뜨림. 다 끝나면 dashRecovery 동안 숨 고름(반격 기회)
//  뇌운 낙뢰: 칼을 치켜든 채 서서 플레이어 발밑에 낙뢰 원을 차례로 깖 → 원이 차면 낙뢰. 그동안 보스는 무방비라 파고들어도 됨
//  2페이즈 진입: 정면 컷 + 보스 주변 낙뢰 + 하늘 · 안개가 붉고 어둡게 + 칼에 번개 (BossPhaseFx)
//  처치: 슬로 + 낙뢰 + 분위기 복구. 소리는 BossSfx
public partial class EnemySwordsman
{
    [Header("Boss Skills Look")]
    [SerializeField] Color dashTellColor = new Color(0.6f, 1.2f, 2.6f);

    static readonly Color SkillLabelColor = new Color(0.62f, 0.82f, 1f);
    static readonly float[] RoarStrikeTimes = { 0.1f, 0.35f, 0.6f, 0.85f, 1.1f };

    float dashReady, rainReady;
    int dashLeft, rainLeft, roarStrikeIndex;
    float dashWindupTime, dashPlanned, dashTraveled, nextRainAt;
    Vector3 dashFrom, dashDir, dashPrev;
    bool dashHitDone, phaseCinematic, ignoringPlayer;
    BossLineTelegraph dashLine;
    CharacterController playerController;
    readonly List<BossRainStrike> rainStrikes = new List<BossRainStrike>();
    BossSfx sfx;
    BossPhaseFx phaseFx;

    bool IsBossDashing => state == State.BossDashWindup || state == State.BossDash;

    void InitBossSkills()
    {
        if (!IsBoss) return;
        if (target != null) playerController = target.GetComponent<CharacterController>();
        sfx = GetComponent<BossSfx>();
        if (sfx == null) sfx = gameObject.AddComponent<BossSfx>();
        phaseFx = GetComponent<BossPhaseFx>();
        if (phaseFx == null) phaseFx = gameObject.AddComponent<BossPhaseFx>();
        phaseFx.Init(weapon != null ? weapon.transform : transform);
    }

    void BossSound(BossSfx.Cue cue)
    {
        if (sfx != null) sfx.Play(cue);
    }

    void ResetBossSkills()
    {
        dashReady = rainReady = 0f;
        CancelBossSkills();
        ClearRain();
        EndPhase2Show();
        if (phaseFx != null) phaseFx.Exit(true);
    }

    void BossSkillsOnDisable()
    {
        EndPhase2Show();
        CancelBossSkills();
        ClearRain();
        if (phaseFx != null) phaseFx.Exit(true);
    }

    bool CanBossDash(float dist) =>
        phase2 && data.DashCount > 0 && localTime >= dashReady && dist >= data.DashMinDistance && dist <= data.DashMaxDistance;

    bool CanBossRain(float dist) =>
        phase2 && data.RainCount > 0 && localTime >= rainReady && dist >= data.RainMinDistance;

    // ===== 뇌광 연참 =====
    void StartBossDash()
    {
        dashReady = localTime + data.DashCooldown * CooldownScale;
        dashLeft = data.DashCount;
        DamageEvents.RaiseLabel(transform.position + Vector3.up * 2.6f * transform.lossyScale.y, "뇌광 연참!", SkillLabelColor);
        BeginDashWindup(data.DashWindup);
        Log("뇌광 연참");
    }

    void BeginDashWindup(float time)
    {
        moveSpeed = 0f;
        dashWindupTime = time;
        dashHitDone = false;
        if (animator.HasState(0, ChargeWindupHash)) animator.CrossFadeInFixedTime(ChargeWindupHash, 0.06f, 0, 0f);
        if (dashLine == null) dashLine = BossLineTelegraph.Create(telegraphMaterial);
        AimDash();
        SetState(State.BossDashWindup);
        BossSound(BossSfx.Cue.DashTell);
    }

    void AimDash()
    {
        Vector3 to = Flat(target.position - transform.position);
        dashDir = to.sqrMagnitude > 0.01f ? to.normalized : transform.forward;
        dashPlanned = Mathf.Min(to.magnitude + data.DashOvershoot, data.DashMaxDistance);
        if (dashLine != null) dashLine.Set(transform.position, dashDir, dashPlanned, data.DashWidth * 2f);
    }

    void UpdateBossDashWindup(float dt)
    {
        moveSpeed = 0f;
        float k = stateTime / Mathf.Max(0.05f, dashWindupTime);
        if (k < 0.7f) // 마지막 30%는 방향 고정
        {
            FaceTarget(dt, 2.5f);
            AimDash();
        }
        if (dashLine != null) dashLine.SetProgress(k);
        if (stateTime < dashWindupTime) return;

        dashFrom = dashPrev = transform.position;
        dashTraveled = 0f;
        transform.rotation = Quaternion.LookRotation(dashDir, Vector3.up);
        IgnorePlayer(true); // 플레이어 몸을 꿰뚫고 지나감
        if (animator.HasState(0, ChargeRunHash)) animator.CrossFadeInFixedTime(ChargeRunHash, 0.04f, 0, 0f);
        BossSound(BossSfx.Cue.Dash);
        SetState(State.BossDash);
    }

    // 지난 프레임 위치 → 지금 위치 선분으로 판정 (빨라서 프레임 사이를 건너뛰지 않게)
    void UpdateBossDash(float dt)
    {
        moveSpeed = data.DashSpeed;
        Vector3 now = transform.position;
        if (!dashHitDone && SegmentDistance(target.position, dashPrev, now) <= data.DashWidth && Mathf.Abs(target.position.y - now.y) < 1.8f) DashHit();
        dashTraveled += Flat(now - dashPrev).magnitude;
        dashPrev = now;

        Vector3 v = controller.velocity;
        bool stuck = stateTime > 0.15f && new Vector2(v.x, v.z).magnitude < data.DashSpeed * CombatTime.EnemyScale * 0.3f;
        if (dashTraveled >= dashPlanned || stuck || stateTime > 1.2f) EndDashLeg();
    }

    void DashHit()
    {
        dashHitDone = true;
        var damageable = target.GetComponentInParent<IDamageable>();
        damageable?.ReceiveDamage(new DamageInfo
        {
            Attacker = gameObject,
            Damage = data.DashDamage * DamageMul,
            Weight = AttackWeight.Heavy,
            KnockDown = dashLeft <= 1, // 마지막 한 번만 넘어뜨림
            HitPoint = target.position + Vector3.up * 1.1f,
            Direction = dashDir
        });
    }

    void EndDashLeg()
    {
        moveSpeed = 0f;
        IgnorePlayer(false);
        ThunderBolt3D.Strike(dashFrom + Vector3.up * 1.2f, transform.position + Vector3.up * 1.2f, 0.9f, 3); // 지나간 길에 번개
        if (bossCamera != null) bossCamera.Shake(0.12f, 0.15f);
        dashLeft--;
        if (dashLeft > 0)
        {
            BeginDashWindup(data.DashNextWindup);
            return;
        }

        DestroyDashLine();
        stunHeavy = false;
        stunDuration = data.DashRecovery;
        attackReadyTime = localTime + data.DashRecovery + 0.3f;
        PlayMoveState(0.2f);
        SetState(State.Stun); // 숨 고름 = 반격 기회
    }

    bool DashThreatens(Vector3 position, float margin)
    {
        Vector3 from = state == State.BossDash ? dashPrev : transform.position;
        Vector3 end = (state == State.BossDash ? dashFrom : transform.position) + dashDir * dashPlanned;
        return SegmentDistance(position, from, end) <= data.DashWidth + margin;
    }

    static float SegmentDistance(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = Flat(b - a), ap = Flat(p - a);
        float len = ab.sqrMagnitude;
        float t = len > 0.0001f ? Mathf.Clamp01(Vector3.Dot(ap, ab) / len) : 0f;
        return (ap - ab * t).magnitude;
    }

    void IgnorePlayer(bool ignore)
    {
        if (ignoringPlayer == ignore || playerController == null || controller == null) return;
        Physics.IgnoreCollision(controller, playerController, ignore);
        ignoringPlayer = ignore;
    }

    void DestroyDashLine()
    {
        if (dashLine != null) Destroy(dashLine.gameObject);
        dashLine = null;
    }

    // 끊김(체간 붕괴 · 사망 · 재시작): 선 예고 제거, 충돌 복구, 낙뢰 원 배치 중단 (이미 깔린 원은 떨어짐)
    void CancelBossSkills()
    {
        DestroyDashLine();
        IgnorePlayer(false);
        dashLeft = 0;
        rainLeft = 0;
    }

    // ===== 뇌운 낙뢰 =====
    void StartBossRain()
    {
        rainReady = localTime + data.RainCooldown * CooldownScale;
        rainLeft = data.RainCount;
        nextRainAt = data.RainCastTime * 0.45f;
        moveSpeed = 0f;
        if (animator.HasState(0, BossRoarHash)) animator.CrossFadeInFixedTime(BossRoarHash, 0.15f, 0, 0f);
        DamageEvents.RaiseLabel(transform.position + Vector3.up * 2.6f * transform.lossyScale.y, "뇌운!", SkillLabelColor);
        BossSound(BossSfx.Cue.RainCast);
        SetState(State.BossRain);
        Log("뇌운 낙뢰");
    }

    void UpdateBossRain(float dt)
    {
        moveSpeed = 0f;
        FaceTarget(dt, 1f);
        while (rainLeft > 0 && stateTime >= nextRainAt)
        {
            SpawnRain(rainLeft == data.RainCount);
            rainLeft--;
            nextRainAt += data.RainInterval;
        }
        if (rainLeft > 0 || stateTime < data.RainCastTime) return;

        attackReadyTime = localTime + 0.3f;
        PlayMoveState(0.25f);
        SetState(State.Chase);
    }

    // 첫 원은 플레이어 발밑, 다음부터는 주변에 흩뿌리고 움직이는 쪽을 살짝 앞질러 깖
    void SpawnRain(bool first)
    {
        Vector3 p = target.position;
        if (!first)
        {
            Vector2 r = Random.insideUnitCircle * data.RainScatter;
            p += new Vector3(r.x, 0f, r.y);
            if (playerController != null) p += Flat(playerController.velocity) * 0.25f;
        }
        rainStrikes.RemoveAll(s => s == null);
        rainStrikes.Add(BossRainStrike.Create(telegraphMaterial, p, data.RainRadius, data.RainDelay, target, sfx, new DamageInfo
        {
            Attacker = gameObject,
            Damage = data.RainDamage * DamageMul,
            Weight = AttackWeight.Heavy
        }));
    }

    void ClearRain()
    {
        foreach (BossRainStrike s in rainStrikes)
            if (s != null) Destroy(s.gameObject);
        rainStrikes.Clear();
    }

    // ===== 2페이즈 진입 연출 (포효 동안) =====
    void BeginPhase2Show()
    {
        dashReady = localTime + 2.5f; // 포효 직후 새 패턴이 곧 나옴
        rainReady = localTime + 5f;
        roarStrikeIndex = 0;
        BossSound(BossSfx.Cue.Roar);
        if (phaseFx != null) phaseFx.Enter();
        if (bossCamera == null) return;
        float s = transform.lossyScale.x;
        phaseCinematic = true;
        bossCamera.BeginCinematic(transform.position + Vector3.up * 1.7f * s, transform.eulerAngles.y + 205f, 6f, 5.5f * s); // 보스 정면 비스듬히
    }

    void UpdatePhase2Show()
    {
        while (roarStrikeIndex < RoarStrikeTimes.Length && stateTime >= RoarStrikeTimes[roarStrikeIndex])
        {
            Vector3 c = transform.position;
            Vector3 p = roarStrikeIndex == 0 ? c
                : c + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(4f, 8f);
            Vector2 j = Random.insideUnitCircle * 4f;
            ThunderBolt3D.Strike(p + new Vector3(j.x, 28f, j.y), p, roarStrikeIndex == 0 ? 1.6f : 1.1f, 3);
            BossSound(BossSfx.Cue.Thunder);
            if (bossCamera != null) bossCamera.Shake(0.15f, 0.2f);
            roarStrikeIndex++;
        }
    }

    void EndPhase2Show()
    {
        if (phaseCinematic && bossCamera != null) bossCamera.EndCinematic();
        phaseCinematic = false;
    }

    // ===== 처치 =====
    void BossDeathShow()
    {
        EndPhase2Show();
        ClearRain();
        Vector3 c = transform.position;
        ThunderBolt3D.Strike(c + new Vector3(2f, 30f, -1f), c, 1.8f, 4);
        BossSound(BossSfx.Cue.Death);
        if (bossCamera != null) bossCamera.Shake(0.3f, 0.5f);
        if (phaseFx != null) phaseFx.Exit(false);
        KillSlowMo.Play(0.18f, 1.2f);
    }
}
