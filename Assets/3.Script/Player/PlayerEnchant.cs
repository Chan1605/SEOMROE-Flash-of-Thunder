using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// 뇌신 강림 (R) A/B 비교 끝 → B 방식만 남김 (뇌흔 폭발 · 예전 뇌검 각성 · F7 전환 삭제)
// 뇌기 avatarCost칸 → avatarDuration초 동안
//  - 공속 증가 (PlayerStats.SpeedMultiplier) · 휘두를 때마다 정면으로 검기(EnchantWave)
//  - 평타 적중 시 주변 적 chainCount명에게 번개가 튐 · 체간 배율 · 아머 무시 (PlayerCombat이 IsActive를 봄)
//  - 끝나는 순간 주변 방전: 강림 중 맞힌 수만큼 피해 · 범위가 커짐
//  - 강림 중 다시 R → 뇌신참: 느린 큰 베기 예고(적 거의 멈춤) → 거대한 검기, 강림은 그 자리에서 끝남 (방전 없음) // 옆 카메라 연출 뺌
//          피해 · 체간 = 기본 + 강림 중 맞힌 수 × 배율 (상한 있음)
// [변경] 스킬트리(검술): 뇌신 지속(시간 +durationBonus) → 뇌신참(강림 중 R) → 연쇄 뇌격(번개 튐 +chainBonus명). 처음엔 강림만
// 강림 중 Q는 '각성 발도'(스킬트리)로 남은 시간을 떼어 씀. 남은 시간은 HUD 링(EnchantRing), 연출은 PlayerVfx가 이벤트를 받아 그림
public class PlayerEnchant : MonoBehaviour
{
    [SerializeField] InputAction enchantAction = new InputAction("Enchant", InputActionType.Button, "<Keyboard>/r");
    [SerializeField] PlayerThunder thunder;
    [SerializeField] WeaponHitbox weapon;
    [SerializeField] PlayerSkill skill;
    [SerializeField] PlayerHealth health;
    [SerializeField] PlayerExecution execution;
    [SerializeField] ThirdPersonCamera followCamera;
    [SerializeField] AttackData finisherAttack; // 뇌신참 모션 (기본 Combo_03_3, 판정 시작 전까지 느리게 재생)

    [Header("뇌신 강림")]
    [SerializeField] float avatarCost = 2f;
    [SerializeField] float avatarDuration = 6f;
    [SerializeField] float durationBonus = 3f;     // [변경] 뇌신 지속 해금 시
    [SerializeField] float avatarSpeed = 1.25f;    // 공속 배율
    [SerializeField] float postureScale = 1.5f;

    [Header("검기")]
    [SerializeField] float waveDamage = 10f;
    [SerializeField] float wavePosture = 6f;
    [SerializeField] float waveRange = 7f;
    [SerializeField] float waveWidth = 3f;
    [SerializeField] float waveSpeed = 26f; // 초당 이동 거리

    [Header("번개 튐")]
    [SerializeField] int chainCount = 2;
    [SerializeField] int chainBonus = 2;           // [변경] 연쇄 뇌격 해금 시
    [SerializeField] float chainRadius = 6f;
    [SerializeField] float chainDamage = 8f;
    [SerializeField] float chainPosture = 4f;

    // 강림 중 R
    [Header("뇌신참")]
    [SerializeField] float finisherMinTime = 1f;      // 강림 시작 뒤 이 시간이 지나야 받음 (연타 실수 방지)
    [SerializeField] float finisherWindup = 0.9f;     // 예고(느린 베기) 시간, 실제 시간
    [SerializeField] float finisherEnemySlow = 0.05f; // 예고 동안 적 속도
    [SerializeField] float finisherDamage = 60f;
    [SerializeField] float finisherDamagePerHit = 4f;
    [SerializeField] float finisherMaxBonus = 80f;
    [SerializeField] float finisherPosture = 50f;
    [SerializeField] float finisherPosturePerHit = 3f;
    [SerializeField] float finisherMaxPostureBonus = 60f;
    [SerializeField] float finisherRange = 16f;
    [SerializeField] float finisherWidth = 9f;
    [SerializeField] float finisherScale = 2.2f;      // 검기 두께 · 빛 배율
    [SerializeField] float finisherSpeed = 17.5f;     // 초당 이동 거리 (예전 값 = 26 ÷ √2.2)

    // 뇌신참 손맛: 발사 순간 세상이 잠깐 멈춤(히트스톱) + 카메라가 확 당겨졌다 풀림 + 검기 임팩트 묶음
    [Header("뇌신참 · 임팩트")]
    [SerializeField] float releaseFreeze = 0.07f;     // 발사 순간 전체 정지 시간 (실제 시간)
    [SerializeField] float releasePunch = 0.14f;      // 카메라 당김 비율
    [SerializeField] float releaseShake = 0.6f;
    [SerializeField] EnchantWave.Impact finisherImpact = new EnchantWave.Impact();

    [Header("Discharge")]
    [SerializeField] float dischargeDamage = 20f;
    [SerializeField] float dischargePostureDamage = 20f;
    [SerializeField] float dischargeRadius = 3f;
    [SerializeField] float dischargePerHit = 1.5f; // 강림 중 맞힌 수만큼 방전 피해 증가
    [SerializeField] float dischargeMaxBonus = 40f;

    // 빔 검 소리: 강림 검기마다 · 뇌신참 예고(차오름) · 발사
    [Header("Sound")]
    [SerializeField] AudioClip[] waveClips;
    [SerializeField] float waveVolume = 0.5f;
    [SerializeField] float waveMinGap = 0.07f;    // 공속이 빨라도 이 간격보다 촘촘하면 건너뜀
    [SerializeField] AudioClip finisherChargeClip;
    [SerializeField] float chargeVolume = 0.8f;
    [SerializeField] AudioClip finisherReleaseClip;
    [SerializeField] float releaseVolume = 1f;

    [SerializeField] bool logEnchant = true;

    static readonly Color LabelColor = new Color(0.6f, 0.85f, 1f);

    readonly Collider[] buffer = new Collider[24];
    readonly HashSet<IDamageable> hitSet = new HashSet<IDamageable>();
    float remaining, total;
    int hits;
    float startTime;                
    bool finishing, finisherSwung;  
    Image flash;                     // 뇌신참 섬광
    AudioSource waveSource, finisherSource;
    float lastWaveSound;
    int lastWaveClip = -1;
    PlayerStats stats;
    PlayerCombat combat;
    PlayerLockOn lockOn;

    public bool IsActive => remaining > 0f;
    public float Remaining01 => total > 0f ? remaining / total : 0f;
    public float PostureScale => postureScale;
    public float Remaining => remaining;           // HUD 링용
    public float MinCells => avatarCost;

    // 강림 중 Q: 남은 시간을 떼어 "발도 시간"으로 바꿈. 그동안 강림 시계는 멈춤(Paused) → 실제로 쓰는 시간은 뗀 만큼만
    public bool Paused { get; set; }

    // 실제로 뗀 시간을 돌려줌 (남은 시간이 모자라면 남은 만큼). 0이 되면 IsActive가 꺼지므로 최소값을 남겨 끝날 때 방전되게 함
    public float ConsumeTime(float seconds)
    {
        if (!IsActive) return 0f;
        float before = remaining;
        remaining = Mathf.Max(0.01f, remaining - seconds);
        return before - remaining;
    }

    public event Action Started;
    public event Action<Vector3> Struck;            // 번개가 튄 적 위치
    public event Action<Vector3, float> Discharged; // 방전 중심 · 반경
    public event Action Ended;

    void Awake()
    {
        if (thunder == null) thunder = GetComponent<PlayerThunder>();
        if (weapon == null) weapon = GetComponentInChildren<WeaponHitbox>();
        if (skill == null) skill = GetComponent<PlayerSkill>();
        if (health == null) health = GetComponent<PlayerHealth>();
        if (execution == null) execution = GetComponent<PlayerExecution>();
        if (followCamera == null) followCamera = FindAnyObjectByType<ThirdPersonCamera>();
        stats = GetComponent<PlayerStats>();
        combat = GetComponent<PlayerCombat>();
        waveSource = NewSource("Sfx_EnchantWave");   // 검기 소리는 피치를 매번 바꾸므로 따로
        finisherSource = NewSource("Sfx_Finisher");
    }

    void OnEnable()
    {
        enchantAction.Enable();
        if (weapon != null) weapon.OnHit += OnWeaponHit;
        if (combat != null) combat.SwingStarted += OnSwingStarted;
    }

    void OnDisable()
    {
        enchantAction.Disable();
        if (weapon != null) weapon.OnHit -= OnWeaponHit;
        if (combat != null) combat.SwingStarted -= OnSwingStarted;
        if (finishing) // 뇌신참 도중 꺼지면 잠금 · 카메라 원래대로
        {
            StopAllCoroutines();
            EndFinisher();
            if (Mathf.Approximately(Time.timeScale, 0.03f)) Time.timeScale = 1f; // 발사 정지 도중 꺼졌을 때
        }
        if (IsActive) Finish(false);
    }

    void Update()
    {
        if (GamePause.IsPaused) return; // 일시정지 중엔 입력·진행을 멈춤
        if (!IsActive)
        {
            if (!finishing && enchantAction.WasPressedThisFrame()) TryStart(); // 뇌신참 직후 바로 다시 강림되지 않게
            return;
        }

        if (!finishing && enchantAction.WasPressedThisFrame() && Time.time - startTime >= finisherMinTime && CanAct()
            && SkillTree.IsUnlocked(SkillId.EnchantFinisher)) // [변경] 뇌신참은 해금 후
        {
            StartCoroutine(Finisher());
            return;
        }
        if (Paused) return; // 발도 시간 동안은 강림 시계 정지
        remaining -= Time.deltaTime;
        if (remaining <= 0f) Finish(true);
    }

    bool TryStart()
    {
        if (thunder == null || thunder.Gauge < avatarCost - 0.001f)
        {
            if (logEnchant) Debug.Log($"[Enchant] 뇌기 부족 ({(thunder != null ? thunder.Gauge : 0f):0.0} / {avatarCost})");
            return false;
        }
        if (!CanAct()) return false;

        thunder.TrySpend(avatarCost);
        startTime = Time.time;
        thunder.Enchanted = true;
        total = remaining = avatarDuration + (SkillTree.IsUnlocked(SkillId.EnchantDuration) ? durationBonus : 0f); // [변경]
        hits = 0;
        if (stats != null) stats.SpeedMultiplier = avatarSpeed;

        if (followCamera != null) followCamera.Shake(0.15f, 0.2f);
        DamageEvents.RaiseLabel(transform.position + Vector3.up * 2.2f, "뇌신 강림", LabelColor);
        Started?.Invoke();
        if (logEnchant) Debug.Log($"[Enchant] 뇌신 강림 · {total:0.0}초");
        return true;
    }

    bool CanAct() => // 강림 시작 · 뇌신참 공통 조건
        !((skill != null && skill.IsActive) || (health != null && (health.IsDead || health.IsStunned)) || (execution != null && execution.IsActive));

    void Finish(bool discharge)
    {
        remaining = 0f;
        Paused = false;
        if (stats != null) stats.SpeedMultiplier = 1f;
        if (discharge) Discharge();
        if (thunder != null) thunder.Enchanted = false;
        Ended?.Invoke();
        if (logEnchant) Debug.Log("[Enchant] 강림 종료" + (discharge ? $" · 방전 (맞힌 수 {hits})" : ""));
    }

    // 휘두를 때마다 정면으로 검기 (판정이 켜지는 순간)
    void OnSwingStarted(AttackData attack)
    {
        if (finishing) // 뇌신참 모션의 판정 시작 = 발사 순간
        {
            if (attack == finisherAttack) finisherSwung = true;
            return;
        }
        if (!IsActive) return;
        Vector3 forward = WaveDirection(); // 락온 중엔 몸 방향과 상관없이 대상 쪽으로
        PlayWaveSound();
        EnchantWave.Fire(gameObject, transform.position + Vector3.up * 1.1f + forward * 0.6f, forward,
            waveRange, waveWidth, waveDamage, wavePosture, LabelColor, 1f, false, waveSpeed); // waveSpeed
    }

    // 락온 대상이 있으면 대상 쪽(수평), 없으면 몸이 보는 쪽
    Vector3 WaveDirection()
    {
        if (lockOn == null) lockOn = GetComponent<PlayerLockOn>();
        if (lockOn != null && lockOn.Target != null)
        {
            Vector3 to = lockOn.Target.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.01f) return to.normalized;
        }
        return transform.forward;
    }

    void OnWeaponHit(IDamageable target, DamageInfo info, HitResult result)
    {
        if (!IsActive || result != HitResult.Hit || info.Attacker != gameObject) return;
        Component c = (Component)target;
        hits++;
        Chain(c);
        Struck?.Invoke(c.transform.position);
    }

    // 핵심: 맞은 적에서 가까운 적 chainCount명에게 번개가 튐 (weapon.OnHit를 다시 부르지 않게 ReceiveDamage 직접)
    void Chain(Component from)
    {
        Vector3 origin = from.transform.position + Vector3.up * 1.2f;
        var near = new List<(IDamageable target, Component c, float dist)>();
        int count = Physics.OverlapSphereNonAlloc(from.transform.position, chainRadius, buffer, ~0, QueryTriggerInteraction.Ignore);
        hitSet.Clear();
        hitSet.Add(from.GetComponent<IDamageable>());
        for (int i = 0; i < count; i++)
        {
            Collider col = buffer[i];
            if (col.transform.IsChildOf(transform)) continue;
            IDamageable t = col.GetComponentInParent<IDamageable>();
            if (t == null || !t.CanBeTargeted || !hitSet.Add(t)) continue;
            Component c = (Component)t;
            near.Add((t, c, (c.transform.position - from.transform.position).sqrMagnitude));
        }
        near.Sort((a, b) => a.dist.CompareTo(b.dist));
        int maxChain = chainCount + (SkillTree.IsUnlocked(SkillId.EnchantChain) ? chainBonus : 0); // [변경]
        for (int i = 0; i < near.Count && i < maxChain; i++)
        {
            Vector3 to = near[i].c.transform.position + Vector3.up * 1.2f;
            ThunderBolt3D.Strike(origin, to, 0.35f, 0);
            near[i].target.ReceiveDamage(new DamageInfo
            {
                Attacker = gameObject,
                Damage = chainDamage,
                PostureDamage = chainPosture,
                Parryable = false,
                Weight = AttackWeight.Light,
                HitPoint = to,
                Direction = Vector3.ProjectOnPlane(to - origin, Vector3.up).normalized
            });
        }
    }

    // ===== 뇌신참 =====
    // 핵심: 강림 시계를 멈추고 → 모션을 느리게 재생하며 칼에 번개를 모음(적은 거의 멈춤) → 판정 순간 거대한 검기 → 강림 끝
    IEnumerator Finisher()
    {
        finishing = true;
        finisherSwung = false;
        Paused = true;
        var locomotion = GetComponent<PlayerLocomotion>();
        if (locomotion != null) locomotion.MovementLocked = true;
        if (combat != null) combat.InputLocked = true;
        FaceTarget();

        // 판정 시작까지가 finisherWindup초가 되도록 공속을 낮춤 (공속 0.1 같은 느린 베기)
        if (finisherAttack != null && combat != null)
        {
            float baseSpeed = stats != null ? stats.AttackSpeed / stats.SpeedMultiplier : 1f;
            float natural = finisherAttack.HitStart * finisherAttack.Duration / Mathf.Max(0.01f, baseSpeed);
            if (stats != null) stats.SpeedMultiplier = Mathf.Clamp(natural / Mathf.Max(0.05f, finisherWindup), 0.1f, 1f);
            combat.StartChargedAttack(finisherAttack, 1f, 0);
        }
        CombatTime.SlowEnemies(finisherEnemySlow, finisherWindup + 0.15f, 0.2f);
        // 옆 뒤 시네마틱 카메라 삭제 (평소 카메라 그대로)
        DamageEvents.RaiseLabel(transform.position + Vector3.up * 2.4f, "뇌신참", LabelColor);
        if (finisherChargeClip != null) // 차오르는 소리가 예고 끝(발사)에 맞춰 정점에 닿도록 재생 속도를 맞춤
        {
            finisherSource.pitch = Mathf.Clamp(finisherChargeClip.length / Mathf.Max(0.05f, finisherWindup), 0.7f, 1.6f);
            finisherSource.clip = finisherChargeClip;
            finisherSource.volume = chargeVolume * AudioOptions.SfxVolume;
            finisherSource.Play();
        }

        // 예고: 하늘에서 칼로 번개가 모임
        float bolt = 0f;
        for (float t = 0f; t < finisherWindup + 0.4f && !finisherSwung; t += Time.unscaledDeltaTime)
        {
            bolt -= Time.unscaledDeltaTime;
            if (bolt <= 0f)
            {
                bolt = 0.09f;
                Vector3 sky = transform.position + Vector3.up * 7f + UnityEngine.Random.insideUnitSphere * 2.5f;
                ThunderBolt3D.Strike(sky, transform.position + Vector3.up * 1.3f + transform.forward * 0.4f, 0.45f, 1);
            }
            if (finisherAttack == null && t >= finisherWindup) break;
            yield return null;
        }

        // 발사
        if (stats != null) stats.SpeedMultiplier = 1f;
        if (followCamera != null) // 흔들림 + 당김
        {
            followCamera.Shake(releaseShake, 0.45f);
            followCamera.Punch(releasePunch, 0.04f, 0.06f, 0.35f, 2f);
        }
        StartCoroutine(Flash());
        finisherSource.Stop(); // 차오름을 끊고 발사음
        finisherSource.pitch = 1f;
        if (finisherReleaseClip != null) finisherSource.PlayOneShot(finisherReleaseClip, releaseVolume * AudioOptions.SfxVolume);
        Vector3 forward = transform.forward;
        float damage = finisherDamage + Mathf.Min(finisherMaxBonus, hits * finisherDamagePerHit);
        float posture = finisherPosture + Mathf.Min(finisherMaxPostureBonus, hits * finisherPosturePerHit);
        EnchantWave.Fire(gameObject, transform.position + Vector3.up * 1.1f + forward * 0.8f, forward,
            finisherRange, finisherWidth, damage, posture, LabelColor, finisherScale, true, finisherSpeed, finisherImpact); // 임팩트
        CombatTime.SlowEnemies(0.05f, 0.12f, 0.1f); // 발사 순간 짧은 멈춤
        // 발사 순간 화면 베기는 뺌 (날아가는 장면을 끊음) → 검기가 여러 명을 베거나 터질 때로 옮김 (EnchantWave.Impact.screenCutHits)
        if (releaseFreeze > 0f) StartCoroutine(Freeze(releaseFreeze));
        if (logEnchant) Debug.Log($"[Enchant] 뇌신참 · 맞힌 수 {hits} → 피해 {damage:0} · 체간 {posture:0}");
        Finish(false); // 강림은 여기서 끝 (방전 없음)

        yield return new WaitForSecondsRealtime(0.35f); // 휘두른 뒤 자세가 풀릴 때까지
        EndFinisher();
    }

    // 빔 검 소리
    AudioSource NewSource(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var s = go.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.spatialBlend = 0f;
        return s;
    }

    void PlayWaveSound()
    {
        if (waveClips == null || waveClips.Length == 0 || Time.time - lastWaveSound < waveMinGap) return;
        lastWaveSound = Time.time;
        int i = UnityEngine.Random.Range(0, waveClips.Length);
        if (waveClips.Length > 1 && i == lastWaveClip) i = (i + 1) % waveClips.Length;
        lastWaveClip = i;
        if (waveClips[i] == null) return;
        waveSource.pitch = UnityEngine.Random.Range(0.97f, 1.03f);
        waveSource.PlayOneShot(waveClips[i], waveVolume * AudioOptions.SfxVolume);
    }

    // 발사 순간 전체를 아주 잠깐 거의 멈춤 → 검기가 "튀어 나가는" 느낌. 다른 기능이 시간을 바꿨으면 건드리지 않음
    IEnumerator Freeze(float seconds)
    {
        if (!Mathf.Approximately(Time.timeScale, 1f)) yield break;
        const float Scale = 0.03f;
        Time.timeScale = Scale;
        yield return new WaitForSecondsRealtime(seconds);
        if (Mathf.Approximately(Time.timeScale, Scale)) Time.timeScale = 1f;
    }

    void EndFinisher()
    {
        finishing = false;
        if (stats != null) stats.SpeedMultiplier = 1f;
        var locomotion = GetComponent<PlayerLocomotion>();
        if (locomotion != null) locomotion.MovementLocked = false;
        if (combat != null) combat.InputLocked = false;
    }

    // 락온 대상이 있으면 그쪽, 없으면 카메라가 보는 쪽
    void FaceTarget()
    {
        if (lockOn == null) lockOn = GetComponent<PlayerLockOn>(); // 검기와 같은 참조 사용
        Vector3 dir = lockOn != null && lockOn.Target != null ? lockOn.Target.position - transform.position
            : followCamera != null ? followCamera.transform.forward : transform.forward;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
    }

    IEnumerator Flash()
    {
        if (flash == null)
        {
            Canvas canvas = UIKit.CreateCanvas(transform, "FinisherFlash", 450);
            flash = UIKit.Image(canvas.transform, null, new Color(0.85f, 0.93f, 1f, 0f));
            flash.raycastTarget = false;
            UIKit.Stretch(flash.rectTransform);
        }
        for (float t = 0f; t < 0.3f; t += Time.unscaledDeltaTime)
        {
            flash.color = new Color(0.85f, 0.93f, 1f, 0.55f * (1f - t / 0.3f));
            yield return null;
        }
        flash.color = new Color(0.85f, 0.93f, 1f, 0f);
    }

    void Discharge()
    {
        float bonus = Mathf.Min(dischargeMaxBonus, hits * dischargePerHit);
        float radius = dischargeRadius + Mathf.Min(2f, hits * 0.05f);
        hitSet.Clear();
        int count = Physics.OverlapSphereNonAlloc(transform.position, radius, buffer, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider col = buffer[i];
            if (col.transform.IsChildOf(transform)) continue;
            IDamageable target = col.GetComponentInParent<IDamageable>();
            if (target == null || !target.CanBeTargeted || !hitSet.Add(target)) continue;

            target.ReceiveDamage(new DamageInfo
            {
                Attacker = gameObject,
                Damage = dischargeDamage + bonus,
                PostureDamage = dischargePostureDamage + bonus * 0.5f,
                Parryable = false,
                Weight = AttackWeight.Heavy,
                HitPoint = col.ClosestPoint(transform.position + Vector3.up),
                Direction = Vector3.ProjectOnPlane(col.transform.position - transform.position, Vector3.up).normalized
            });
        }
        if (followCamera != null) followCamera.Shake(0.25f, 0.3f);
        Discharged?.Invoke(transform.position, radius);
    }
}
