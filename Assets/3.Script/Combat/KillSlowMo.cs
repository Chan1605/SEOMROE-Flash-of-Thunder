using UnityEngine;

// 마지막 적 · 보스를 쓰러뜨린 순간의 마무리 연출: 적의 시간만 잠깐 느리게(쓰러지는 동작 · 미끄러짐) + 카메라 당김
// Time.timeScale은 건드리지 않음 → 히트스톱 · 차지 슬로 · 처형처럼 timeScale을 저장했다 되돌리는 기능과 엉키지 않음
public static class KillSlowMo
{
    public static void Play(float scale, float realSeconds)
    {
        CombatTime.SlowEnemies(scale, realSeconds, realSeconds * 0.35f);
        ThirdPersonCamera cam = Object.FindAnyObjectByType<ThirdPersonCamera>();
        if (cam != null) cam.Punch(0.08f, 0.06f, realSeconds * 0.5f, 0.3f, 0f);
    }
}
