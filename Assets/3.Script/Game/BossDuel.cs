using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// 연습장 보스 1:1 (TrainingMode가 bossPrefab이 있으면 붙임)
//  F5: 흑뢰 소환 (싸우는 중이면 처음부터 다시) · F6: 보스 체력을 2페이즈 직전으로
//  싸우는 동안: 다른 수련 상대 · 허수아비를 숨기고, 연습장 혜택(뇌기 · 궁극기 무한 · 자동 회복)을 끔 → 본편과 같은 조건
//  시작할 때 체력 · 뇌기 가득, 궁극기 0칸. 이기거나 지면 몇 초 뒤 원래 연습장으로 돌아감
public class BossDuel : MonoBehaviour
{
    const float SpawnDistance = 9f;
    const float EndDelay = 3f;

    TrainingMode mode;
    GameObject bossPrefab;
    Font font;
    EnemySwordsman boss;
    PlayerHealth health;
    PlayerThunder thunder;
    PlayerUltimate ultimate;
    readonly List<GameObject> hidden = new List<GameObject>();
    Text banner;
    float bannerUntil;

    public void Init(TrainingMode owner, GameObject prefab, Font bodyFont)
    {
        mode = owner;
        bossPrefab = prefab;
        font = bodyFont;
    }

    void Start()
    {
        health = FindAnyObjectByType<PlayerHealth>();
        thunder = FindAnyObjectByType<PlayerThunder>();
        ultimate = FindAnyObjectByType<PlayerUltimate>();
        if (health != null) health.Died += OnPlayerDied;
        BuildBanner();
    }

    void OnDestroy()
    {
        if (health != null) health.Died -= OnPlayerDied;
    }

    void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb != null && !GamePause.IsPaused)
        {
            if (kb.f5Key.wasPressedThisFrame) Begin();
            if (kb.f6Key.wasPressedThisFrame && boss != null && boss.CanBeTargeted)
            {
                boss.DebugSetHealth01(0.03f); // [변경] 2페이즈는 1페이즈 체력을 다 깎으면 시작 → 거의 다 깎인 상태로
                Show("2페이즈 직전으로", 2f);
            }
        }
        banner.color = new Color(1f, 1f, 1f, Mathf.Clamp01((bannerUntil - Time.unscaledTime) * 2f));
    }

    // ===== 시작 · 끝 =====
    void Begin()
    {
        if (health == null || health.IsDead) return;
        CancelInvoke();
        Clear();

        // 다른 적은 잠시 숨김 (부활하는 수련 상대가 끼어들지 않게)
        foreach (EnemySwordsman e in FindObjectsByType<EnemySwordsman>(FindObjectsSortMode.None)) Hide(e.gameObject);
        foreach (TrainingDummy d in FindObjectsByType<TrainingDummy>(FindObjectsSortMode.None)) Hide(d.gameObject);

        Transform player = health.transform;
        Vector3 forward = Vector3.ProjectOnPlane(player.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
        Vector3 pos = GroundAt(player.position + forward * SpawnDistance, player.position.y);
        Quaternion rot = Quaternion.LookRotation(-forward, Vector3.up);

        boss = Instantiate(bossPrefab, pos, rot).GetComponent<EnemySwordsman>();
        if (boss == null) return;
        boss.DisableRespawn();
        boss.MarkTemporary(); // 쓰러지면 가라앉아 사라짐
        boss.Alert();
        boss.Died += OnBossDied;

        mode.DuelRules = true;
        health.HealFull();
        if (thunder != null) thunder.Add(thunder.MaxGauge - thunder.Gauge);
        if (ultimate != null) ultimate.SetGauge(0);
        Show("흑뢰 1:1  ·  F5 다시  ·  F6 2페이즈 직전", 4f);
    }

    void OnBossDied(EnemySwordsman e)
    {
        Show("격파!", EndDelay);
        Invoke(nameof(End), EndDelay);
    }

    void OnPlayerDied()
    {
        if (boss == null) return;
        Show("패배  ·  F5로 다시 도전", EndDelay);
        Invoke(nameof(End), EndDelay * 0.5f); // TrainingMode가 부활시키기 전에 보스를 치움
    }

    void End()
    {
        Clear();
        Show("연습장으로 돌아옴", 2f);
    }

    // 보스 제거 + 숨긴 적 복원 + 연습장 규칙 복원
    void Clear()
    {
        if (boss != null)
        {
            boss.Died -= OnBossDied;
            if (boss.CanBeTargeted) Destroy(boss.gameObject); // 쓰러진 보스는 스스로 가라앉아 사라짐
        }
        boss = null;
        foreach (GameObject go in hidden)
            if (go != null) go.SetActive(true);
        hidden.Clear();
        if (mode != null) mode.DuelRules = false;
    }

    void Hide(GameObject go)
    {
        if (!go.activeSelf) return;
        go.SetActive(false);
        hidden.Add(go);
    }

    // 소환 자리의 바닥 높이 (지형 · 바닥 콜라이더). 못 찾으면 플레이어 높이
    static Vector3 GroundAt(Vector3 pos, float fallbackY)
    {
        float best = float.MaxValue;
        float y = fallbackY;
        foreach (RaycastHit hit in Physics.RaycastAll(pos + Vector3.up * 20f, Vector3.down, 40f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.GetComponentInParent<CharacterController>() != null || hit.distance >= best) continue; // 캐릭터 몸은 건너뜀
            best = hit.distance;
            y = hit.point.y;
        }
        pos.y = y;
        return pos;
    }

    // ===== 안내 문구 (화면 위 가운데) =====
    void BuildBanner()
    {
        Canvas canvas = UIKit.CreateCanvas(transform, "BossDuelBanner", 25);
        banner = UIKit.Text(canvas.transform, "", font, 30, Color.white, TextAnchor.MiddleCenter);
        RectTransform r = banner.rectTransform;
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 1f);
        r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = new Vector2(0f, -150f);
        r.sizeDelta = new Vector2(1200f, 50f);
        banner.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2f, -2f);
    }

    void Show(string message, float seconds)
    {
        banner.text = message;
        bannerUntil = Time.unscaledTime + seconds;
    }
}
