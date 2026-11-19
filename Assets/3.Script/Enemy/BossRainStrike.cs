using UnityEngine;

// 뇌운 낙뢰 한 발: 바닥 원 예고(BossTelegraph)가 delay 동안 차오르면 낙뢰 + 원 안이면 피해. 적 시계(CombatTime)로 흐름
public class BossRainStrike : MonoBehaviour
{
    BossTelegraph marker;
    Transform player;
    BossSfx sfx;
    DamageInfo damage;
    float radius, delay, time;

    public static BossRainStrike Create(Material material, Vector3 position, float radius, float delay, Transform player, BossSfx sfx, DamageInfo damage)
    {
        var go = new GameObject("BossRainStrike");
        var s = go.AddComponent<BossRainStrike>();
        s.marker = BossTelegraph.Create(material, radius, position);
        s.marker.transform.SetParent(go.transform, true);
        s.radius = radius;
        s.delay = Mathf.Max(0.1f, delay);
        s.player = player;
        s.sfx = sfx;
        s.damage = damage;
        return s;
    }

    void Update()
    {
        time += CombatTime.EnemyDeltaTime;
        if (marker != null) marker.SetProgress(time / delay);
        if (time < delay) return;
        Strike();
        Destroy(gameObject);
    }

    void Strike()
    {
        Vector3 ground = marker != null ? marker.transform.position : transform.position;
        Vector2 j = Random.insideUnitCircle * 3f;
        ThunderBolt3D.Strike(ground + new Vector3(j.x, 26f, j.y), ground, 1.25f, 3);
        if (sfx != null) sfx.PlayAt(BossSfx.Cue.Thunder, ground, 0.8f);

        if (player == null) return;
        Vector3 to = player.position - ground;
        if (Mathf.Abs(to.y) > 2.5f) return;
        to.y = 0f;
        if (to.magnitude > radius) return;
        DamageInfo info = damage;
        info.HitPoint = player.position + Vector3.up;
        info.Direction = to.sqrMagnitude > 0.01f ? to.normalized : Vector3.forward;
        player.GetComponentInParent<IDamageable>()?.ReceiveDamage(info);
    }
}
