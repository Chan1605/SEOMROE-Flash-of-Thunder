using UnityEngine;

// 패링 타이밍 고지 (EnemyData.parryTell: 보스 · 정예 / 수련 상대는 항상)
// 잡기 예고처럼: 패링할 수 있는 공격이 정해진 순간(판정 parryTellLead초 전부터) 칼날에 파란 빛이 켜지고,
//  판정이 다가올수록 커지고 밝아지며 맥박침 → 판정 순간 가장 크게 번쩍이고 꺼짐 (ParryGlint가 ParryTell01을 읽어 그림)
//  위험 공격(붉은 깜빡임 · 패링 불가)과 잡기 · 도약 · 연참처럼 패링 불가인 공격에는 안 나옴
//  [변경] 연타는 첫 타에만 (2타부터는 첫 타 리듬으로 이어서 막음)
public partial class EnemySwordsman
{
    const float ParryTellRange = 12f; // 이 거리 안의 적만

    float prevUntilHit = -1f, tellStartUntil, tellK;
    int comboHit; // [변경] 0 = 첫 타 (StartAttack이 연타마다 올림)
    ParryGlint parryGlint;

    // 0 = 꺼짐, 0~1 = 판정까지 진행도 (1에 가까울수록 곧 맞음)
    public float ParryTell01 => tellK;
    bool UsesParryTell => data != null && (data.ParryTell || trainer);

    // [변경] 다음에 들어올 타가 연타의 몇 번째인지 (예고 · 첫 타 판정 전 = 0, 다음 타로 넘어가기 직전 = 지금 + 1)
    int UpcomingHitIndex() => state != State.Attack ? 0 : TimeUntilChain() >= 0f ? comboHit + 1 : comboHit;

    void UpdateParryTell()
    {
        tellK = 0f;
        if (!UsesParryTell || target == null) return;
        float until = state == State.Dead || state == State.Executed ? -1f : SecondsUntilHit();
        if (until < 0f || !IsParryable || Flat(target.position - transform.position).magnitude > ParryTellRange)
        {
            prevUntilHit = -1f;
            tellStartUntil = 0f;
            return;
        }

        // 새 타가 정해진 순간(남은 시간이 갑자기 늘어남)이나 남은 시간이 lead를 위에서 아래로 지나는 순간 시작
        float lead = data.ParryTellLead;
        bool newHit = prevUntilHit < 0f || until > prevUntilHit + 0.05f;
        if (until > 0.05f && until <= lead && (newHit || prevUntilHit > lead) && UpcomingHitIndex() == 0) // [변경] 첫 타만
        {
            tellStartUntil = until;
            if (parryGlint == null) parryGlint = ParryGlint.Create(this, weapon != null ? weapon.transform : transform);
            parryGlint.Begin();
        }
        prevUntilHit = until;

        if (tellStartUntil > 0f && until > 0f && until <= tellStartUntil) tellK = Mathf.Max(0.01f, 1f - until / tellStartUntil);
        else if (until <= 0f && tellStartUntil > 0f)
        {
            tellStartUntil = 0f;
            if (parryGlint != null) parryGlint.Burst(); // 판정 순간 크게 번쩍
        }
    }
}
