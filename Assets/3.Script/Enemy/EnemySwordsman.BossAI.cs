using UnityEngine;

// 보스 흑뢰 개편: 플레이어 기술에 대응하는 행동 (수치는 EnemyData "Boss · AI")
//  - 일섬 간파: 섬뢰일도(즉사 판정 공격)가 들어오면 쿨타임마다 한 번 튕겨내고 곧바로 반격 베기. 체간 붕괴 · 경직 중엔 못 함
//  - 패링 적응: 짧은 시간에 연달아 패링 · 역뢰를 당하면 다음 연타 첫 타를 위험 공격으로
//  - 위험 공격: "위험!" 표시 + 몸이 붉게 깜빡이고 예고가 김. 패링 · 역뢰 불가(회피만), 피해 ×1.5 + 넘어뜨림. 페이즈별 확률로도 나옴
//  - 2페이즈: 연타를 덜 끊고, 연타가 끝나도 확률로 곧바로 다시 몰아침
//  - 체간: 회복이 시작되면 점점 빨라짐 → 쉬지 않고 압박해야 붕괴
public partial class EnemySwordsman
{
    [Header("Boss AI Look")]
    [SerializeField] Color perilousColor = new Color(2.4f, 0.25f, 0.05f);

    static readonly Color PerilousLabelColor = new Color(1f, 0.32f, 0.12f);
    static readonly Color DeflectLabelColor = new Color(0.62f, 0.82f, 1f);
    const float CounterWindup = 0.15f; // 튕겨낸 뒤 반격까지 남기는 예고

    float deflectReadyTime;
    float lastParriedTime = -99f;
    int parryStreak;
    bool perilousNext;   // 다음 연타 첫 타를 위험 공격으로 (패링 적응)
    bool perilousCombo;  // 이번 연타 첫 타가 위험 공격
    bool restartedCombo; // 2페이즈 재돌입은 연속 한 번만

    void InitBossAI()
    {
        deflectReadyTime = 0f;
        lastParriedTime = -99f;
        parryStreak = 0;
        perilousNext = perilousCombo = restartedCombo = false;
    }

    // 예고 시간: 위험 공격이면 더 길게 (판정 · 간파 계산도 이 값을 씀)
    float WindupTime => data.WindupTime * windupScale + (perilousCombo ? data.PerilousExtraWindup : 0f); // 짧은 예고 배율 (Tactics)

    // 지금 다가오는(또는 휘두르는) 첫 타가 위험 공격인지
    bool PerilousNow => perilousCombo && data != null &&
        (state == State.Windup || (state == State.Attack && currentAttack == data.FirstAttack));

    bool IsPerilousAttack(AttackData attack) => PerilousNow && attack == data.FirstAttack;

    // ===== 위험 공격 =====
    // 예고에 들어가는 순간 한 번 정함: 패링 적응으로 예약됐거나 페이즈별 확률
    void PlanPerilous()
    {
        perilousCombo = false;
        if (!IsBoss) return;
        float chance = phase2 ? data.Phase2PerilousChance : data.PerilousChance;
        if (!perilousNext && Random.value >= chance) return;
        perilousNext = false;
        perilousCombo = true;
        DamageEvents.RaiseLabel(transform.position + Vector3.up * 2.6f * transform.lossyScale.y, "위험!", PerilousLabelColor);
        Log("위험 공격 예고");
    }

    void ClearPerilous() => perilousCombo = false;

    bool PerilousColor(out Color color)
    {
        color = BaseTint;
        if (!PerilousNow) return false;
        if (state == State.Attack && stateTime > currentAttack.HitEnd * currentAttack.Duration) return false;
        float pulse = 0.5f + 0.5f * Mathf.Sin(localTime * 30f); // 잡기보다 빠르게 깜빡임
        color = Color.Lerp(BaseTint, perilousColor, 0.55f + 0.45f * pulse);
        return true;
    }

    // ===== 패링 적응 =====
    void CountParried()
    {
        if (localTime - lastParriedTime > data.ParryMemory) parryStreak = 0;
        parryStreak++;
        lastParriedTime = localTime;
        if (parryStreak < data.ParriesBeforePerilous) return;
        parryStreak = 0;
        perilousNext = true;
        Log("패링 적응 → 다음 연타 위험 공격");
    }

    // ===== 일섬 간파 =====
    // 핵심: 섬뢰일도 피해를 막고, 하던 동작을 끊은 뒤 짧은 예고로 바로 첫 타 → 일섬만 반복하면 반격을 맞음
    bool TryDeflectSkill(DamageInfo info)
    {
        if (!info.InstantKill || info.ForceKill || info.Weight == AttackWeight.Execution) return false;
        if (postureBroken || executed || localTime < deflectReadyTime || !CanDeflectInState()) return false;

        deflectReadyTime = localTime + data.SkillDeflectCooldown * CooldownScale;
        if (swingActive) EndSwing();
        currentAttack = null;
        DestroyTelegraph();
        if (!controller.enabled) controller.enabled = true;
        moveSpeed = 0f;

        Vector3 to = Flat(target.position - transform.position);
        if (to.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
        perilousCombo = false;
        TryTakeToken();
        SetState(State.Windup);
        stateTime = Mathf.Max(0f, WindupTime - CounterWindup);

        Flash(parryColor);
        DamageEvents.RaiseLabel(transform.position + Vector3.up * 2.6f * transform.lossyScale.y, "간파!", DeflectLabelColor);
        if (bossCamera != null) bossCamera.Shake(0.12f, 0.15f);
        Log("일섬 간파 → 반격");
        return true;
    }

    bool CanDeflectInState() =>
        state == State.Chase || state == State.Idle || state == State.Windup || state == State.Attack
        || state == State.BossGrabWindup || state == State.BossLeapWindup || state == State.BossLeapLand;

    // ===== 2페이즈 재돌입 =====
    // 연타가 끝나는 순간 확률로 예고 없이 첫 타부터 다시 (가까울 때만, 연속 한 번)
    bool TryRestartCombo()
    {
        if (!IsBoss || !phase2 || data.FirstAttack == null) return false;
        if (restartedCombo)
        {
            restartedCombo = false;
            return false;
        }
        if (DistanceToTarget() > data.AttackRange * 1.5f || Random.value >= data.Phase2ComboRestartChance) return false;
        restartedCombo = true;
        StartAttack(data.FirstAttack);
        Log("2페이즈 재돌입");
        return true;
    }

    float ComboStopChance => data.ComboStopChance * (IsBoss && phase2 ? data.Phase2ComboStopScale : 1f);

    // 회복 시작 후 지난 시간만큼 빨라지는 체간 회복 배율
    float PostureRegenScale(float sinceHit) => 1f + data.PostureRegenRamp * Mathf.Max(0f, sinceHit - data.PostureRegenDelay);

    // ===== 연습장 1:1 (BossDuel) =====
    public void DebugSetHealth01(float ratio)
    {
        if (data == null || state == State.Dead) return;
        float max = data.MaxHealth * healthScale;
        health = Mathf.Clamp(max * ratio, 1f, max);
    }
}
