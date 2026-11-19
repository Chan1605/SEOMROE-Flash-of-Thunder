using System.Collections;
using UnityEngine;

// 일반 적 행동 다양화 (수치는 EnemyData "Tactics"). 보스 · 수련 상대 · 허수아비는 쓰지 않음
//  회피: 플레이어가 나를 향해 공격을 시작하면 확률로 뒤 · 비스듬히 빠짐 → 확률로 곧바로 짧은 예고 반격
//  달려들기: 공격 차례가 비었는데 거리가 벌어져 있으면 달려와 짧은 예고로 베기 (예고 시간은 도착한 뒤부터 흐름)
//  끼어들기: 플레이어가 다른 쪽을 칠 때 가까이 있으면 짧은 예고로 옆을 노림
//  쓰러짐: 맞은 방향으로 미끄러지며 쓰러지고 잿빛으로 식음. 구역 웨이브의 마지막 적은 짧은 슬로
public partial class EnemySwordsman
{
    [Header("Tactics Look")]
    [SerializeField] Color corpseColor = new Color(0.07f, 0.07f, 0.08f);

    float windupScale = 1f;
    bool lunging;
    float lungeTime, lungeReady, evadeReady, punishReady;
    Vector3 evadeVelocity;
    Vector3 lastHitDirection;
    PlayerCombat playerCombat;

    bool UsesTactics => data != null && !IsBoss && !trainer && !passive && target != null;

    void InitTactics(GameObject player)
    {
        if (player == null || IsBoss) return;
        playerCombat = player.GetComponent<PlayerCombat>();
        if (playerCombat != null) playerCombat.AttackStarted += OnPlayerAttackStarted;
    }

    void OnDestroy()
    {
        if (playerCombat != null) playerCombat.AttackStarted -= OnPlayerAttackStarted;
    }

    // 핵심: 플레이어 공격이 "나를 향하면" 회피, "다른 쪽을 향하면" 끼어들기 후보
    void OnPlayerAttackStarted(AttackData attack, AttackInput input)
    {
        if (!UsesTactics || !isActiveAndEnabled || state != State.Chase) return;

        Vector3 toMe = Flat(transform.position - target.position);
        float dist = toMe.magnitude;
        if (dist < 0.01f) return;
        bool aimedAtMe = Vector3.Angle(Flat(target.forward), toMe) <= 55f;

        if (aimedAtMe)
        {
            if (!IsGuardian && dist <= data.EvadeRange && localTime >= evadeReady && Random.value < data.EvadeChance)
                StartEvade(toMe / dist);
            return;
        }

        if (dist <= data.AttackRange + 0.8f && localTime >= punishReady && Random.value < data.PunishChance && TryTakeToken())
        {
            punishReady = localTime + data.PunishCooldown;
            StartQuickWindup(data.PunishWindupScale);
            Log("끼어들기");
        }
    }

    void StartQuickWindup(float scale)
    {
        if (IsGuardian) LowerGuardForAttack();
        PlanFeint();
        lunging = false;
        windupScale = scale;
        SetState(State.Windup);
    }

    // ===== 회피 =====
    void StartEvade(Vector3 away)
    {
        Vector3 dir = Quaternion.Euler(0f, Random.Range(-60f, 60f), 0f) * away; // 바로 뒤 ~ 비스듬히
        evadeVelocity = dir * (data.EvadeDistance / Mathf.Max(0.05f, data.EvadeTime)) * 2f; // 선형 감속 → 평균의 2배로 출발
        evadeReady = localTime + data.EvadeCooldown;
        moveSpeed = 0f;
        SetState(State.Evade);

        if (animator != null && animator.HasState(0, StrafeHash))
        {
            Vector3 local = transform.InverseTransformDirection(dir);
            animator.SetFloat(StrafeXHash, local.x);
            animator.SetFloat(StrafeZHash, local.z);
            animator.CrossFadeInFixedTime(StrafeHash, 0.06f, 0, 0f);
        }
        Log("회피");
    }

    void UpdateEvade(float dt)
    {
        moveSpeed = 0f;
        FaceTarget(dt, 1f);
        float k = Mathf.Clamp01(stateTime / Mathf.Max(0.05f, data.EvadeTime));
        strafeVelocity = evadeVelocity * (1f - k);
        if (k < 1f) return;

        strafeVelocity = Vector3.zero;
        PlayMoveState(0.12f);
        if (Random.value < data.CounterChance && TryTakeToken())
        {
            StartQuickWindup(data.CounterWindupScale);
            Log("회피 반격");
            return;
        }
        SetState(State.Chase);
    }

    // ===== 달려들기 =====
    bool TryStartLunge(float dist)
    {
        if (!UsesTactics || IsCharger || IsGuardian) return false;
        Vector2 range = data.LungeDistance;
        if (dist < range.x || dist > range.y || localTime < attackReadyTime || localTime < lungeReady) return false;

        lungeReady = localTime + 1f; // 굴림에 실패해도 1초 뒤 다시
        if (Random.value >= data.LungeChance || !TryTakeToken()) return false;

        lungeReady = localTime + data.LungeCooldown;
        StartQuickWindup(data.LungeWindupScale);
        lunging = true;
        lungeTime = 0f;
        Log("달려들기");
        return true;
    }

    // 사거리에 들어올 때까지 달리고, 그동안 예고 시간(stateTime)은 0에 묶어 둠 → 간파 · 회피 판정도 도착 뒤부터 셈
    bool UpdateLunge(float dt)
    {
        if (!lunging) return false;
        lungeTime += dt;
        if (DistanceToTarget() <= data.AttackRange * 0.9f || lungeTime > 1.4f)
        {
            lunging = false;
            stateTime = 0f;
            return false;
        }
        moveSpeed = data.RunSpeed * data.LungeSpeedScale;
        stateTime = 0f;
        return true;
    }

    // ===== 피격 · 쓰러짐 =====
    void HitStop(bool heavy) => freezeUntil = Mathf.Max(freezeUntil, Time.unscaledTime + (heavy ? 0.06f : 0.03f));

    void OnDeathTactics()
    {
        if (executed || deadFalling) return; // 처형 짝 모션 · 공중 사망은 그대로
        if (data.DeathPush > 0f) StartCoroutine(DeathSlide(Flat(lastHitDirection)));
        if (!IsBoss && !respawnEnabled && !trainer && IsLastStanding()) KillSlowMo.Play(0.3f, 0.45f);
    }

    bool IsLastStanding()
    {
        foreach (EnemySwordsman other in alive)
            if (other != null && other != this && other.state != State.Dead && !other.trainer && other.isActiveAndEnabled) return false;
        return true;
    }

    // 충돌을 끈 몸을 맞은 방향으로 미끄러뜨림 (빨리 시작해 감속), 바닥 높이를 따라가고 벽 앞에서 멈춤
    IEnumerator DeathSlide(Vector3 dir)
    {
        if (dir.sqrMagnitude < 0.01f) dir = -transform.forward;
        dir.Normalize();
        const float time = 0.35f;
        float total = data.DeathPush, moved = 0f;
        for (float t = 0f; t < time; t += Time.deltaTime * CombatTime.EnemyScale)
        {
            float k = Mathf.Clamp01(t / time);
            float want = total * (1f - (1f - k) * (1f - k));
            float step = want - moved;
            moved = want;
            if (step > 0f && Blocked(dir, step + 0.35f)) yield break;

            Vector3 next = transform.position + dir * step;
            if (Physics.Raycast(next + Vector3.up * 1.2f, Vector3.down, out RaycastHit hit, 3f, ~0, QueryTriggerInteraction.Ignore)
                && !hit.transform.IsChildOf(transform) && hit.collider.GetComponentInParent<IDamageable>() == null)
                next.y = hit.point.y;
            transform.position = next;
            yield return null;
        }
    }

    bool Blocked(Vector3 dir, float distance)
    {
        foreach (RaycastHit hit in Physics.RaycastAll(transform.position + Vector3.up * 0.6f, dir, distance, ~0, QueryTriggerInteraction.Ignore))
            if (!hit.transform.IsChildOf(transform) && hit.collider.GetComponentInParent<IDamageable>() == null) return true;
        return false;
    }
}
