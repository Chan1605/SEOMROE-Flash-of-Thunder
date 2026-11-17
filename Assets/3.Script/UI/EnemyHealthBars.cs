using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 일반 적 머리 위 체력바: 플레이어 가까이(showDistance 안)에 있는 적만 표시, 멀어지면 서서히 사라짐
//  - 보스는 화면 아래 큰 바(BossHealthBar)가 따로 있어 제외
//  - 깎이면 흰 잔상이 잠깐 남았다 따라 줄어듦 · 수문장 · 돌격병은 금색 테두리
//  - 적마다 UI를 붙이지 않고 바 몇 개를 돌려 씀 (DamageNumbers가 같은 캔버스 아래에 만들어 둠 → 피해 숫자보다 뒤에 그려짐)
public class EnemyHealthBars : MonoBehaviour
{
    [SerializeField] float showDistance = 10f;
    [SerializeField] float fadeDistance = 2f;   // showDistance 안쪽 이 거리 동안 흐려짐
    [SerializeField] float headHeight = 2.15f;
    [SerializeField] Vector2 size = new Vector2(90f, 7f);
    [SerializeField] float lagDelay = 0.35f;     // 잔상이 따라 줄기 시작할 때까지
    [SerializeField] float lagSpeed = 1.2f;      // 잔상이 줄어드는 속도 (초당 비율)

    static readonly Color Back = new Color(0f, 0f, 0f, 0.6f);
    static readonly Color Fill = new Color(0.86f, 0.2f, 0.18f);
    static readonly Color Lag = new Color(1f, 0.92f, 0.8f, 0.9f);
    static readonly Color Border = new Color(0f, 0f, 0f, 0.8f);
    static readonly Color StrongBorder = new Color(0.95f, 0.8f, 0.45f, 0.95f);

    class Bar
    {
        public RectTransform root;
        public CanvasGroup group;
        public Image border, fill, lag;
        public EnemySwordsman target;
        public float shown, lagValue, lastHealth, lagWait;
    }

    readonly List<Bar> bars = new List<Bar>();
    readonly Dictionary<EnemySwordsman, Bar> byEnemy = new Dictionary<EnemySwordsman, Bar>();
    readonly List<EnemySwordsman> remove = new List<EnemySwordsman>();
    RectTransform area;
    Camera cam;
    Transform player;

    public static EnemyHealthBars Create(Transform parent, int siblingIndex)
    {
        var go = new GameObject("EnemyHealthBars", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.transform.SetSiblingIndex(siblingIndex);
        UIKit.Stretch((RectTransform)go.transform);
        return go.AddComponent<EnemyHealthBars>();
    }

    void Awake() => area = (RectTransform)transform;

    // 핵심: 가까운 일반 적마다 바를 하나씩 붙이고, 머리 위 화면 좌표로 옮김. 거리 · 화면 밖이면 흐려져 반납
    void LateUpdate()
    {
        if (cam == null) cam = Camera.main;
        if (player == null)
        {
            PlayerHealth h = FindAnyObjectByType<PlayerHealth>();
            if (h != null) player = h.transform;
        }
        if (cam == null || player == null) return;

        foreach (EnemySwordsman e in EnemySwordsman.Alive)
        {
            if (e == null || e.IsBoss || !e.CanBeTargeted || byEnemy.ContainsKey(e)) continue;
            if (Distance(e) > showDistance) continue;
            byEnemy[e] = Take(e);
        }

        remove.Clear();
        foreach (KeyValuePair<EnemySwordsman, Bar> pair in byEnemy)
            if (!UpdateBar(pair.Value)) remove.Add(pair.Key);
        foreach (EnemySwordsman e in remove) Release(e);
    }

    float Distance(EnemySwordsman e)
    {
        Vector3 d = e.transform.position - player.position;
        d.y = 0f;
        return d.magnitude;
    }

    // false = 반납할 때가 됨
    bool UpdateBar(Bar b)
    {
        EnemySwordsman e = b.target;
        bool alive = e != null && e.isActiveAndEnabled && e.CanBeTargeted;
        float want = 0f;
        Vector3 screen = Vector3.zero;
        if (alive)
        {
            float d = Distance(e);
            want = Mathf.Clamp01((showDistance - d) / Mathf.Max(0.01f, fadeDistance));
            screen = cam.WorldToScreenPoint(e.transform.position + Vector3.up * headHeight * e.transform.lossyScale.y);
            if (screen.z <= 0f) want = 0f;
        }
        b.shown = Mathf.MoveTowards(b.shown, want, Time.unscaledDeltaTime * 4f);
        b.group.alpha = b.shown;
        if (!alive && b.shown <= 0f) return false;
        if (alive && want <= 0f && b.shown <= 0f) return false;
        if (!alive) return true;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(area, screen, null, out Vector2 local);
        b.root.anchoredPosition = local;

        float hp = e.Health01;
        if (hp < b.lastHealth) b.lagWait = lagDelay; // 새로 깎이면 잔상을 잠깐 붙잡아 둠
        b.lastHealth = hp;
        if (b.lagWait > 0f) b.lagWait -= Time.unscaledDeltaTime;
        else b.lagValue = Mathf.MoveTowards(b.lagValue, hp, Time.unscaledDeltaTime * lagSpeed);
        if (b.lagValue < hp) b.lagValue = hp;
        b.fill.fillAmount = hp;
        b.lag.fillAmount = b.lagValue;
        return true;
    }

    Bar Take(EnemySwordsman e)
    {
        Bar b = bars.Find(x => x.target == null);
        if (b == null)
        {
            b = Build();
            bars.Add(b);
        }
        b.target = e;
        b.shown = 0f;
        b.lastHealth = b.lagValue = e.Health01;
        b.lagWait = 0f;
        b.border.color = e.Kind == EnemyKind.Swordsman ? Border : StrongBorder;
        b.root.gameObject.SetActive(true);
        return b;
    }

    void Release(EnemySwordsman e)
    {
        if (!byEnemy.TryGetValue(e, out Bar b)) return;
        b.target = null;
        b.root.gameObject.SetActive(false);
        byEnemy.Remove(e);
    }

    Bar Build()
    {
        var b = new Bar();
        var go = new GameObject("Bar", typeof(RectTransform));
        go.transform.SetParent(area, false);
        b.root = (RectTransform)go.transform;
        b.root.anchorMin = b.root.anchorMax = new Vector2(0.5f, 0.5f);
        b.root.sizeDelta = size;
        b.group = go.AddComponent<CanvasGroup>();
        b.group.blocksRaycasts = false;

        b.border = Part(b.root, Border, new Vector2(size.x + 4f, size.y + 4f));
        Part(b.root, Back, size);
        b.lag = Part(b.root, Lag, size);
        b.fill = Part(b.root, Fill, size);
        foreach (Image img in new[] { b.lag, b.fill })
        {
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            img.fillOrigin = 0;
        }
        return b;
    }

    static Image Part(RectTransform parent, Color color, Vector2 sizeDelta)
    {
        Image img = UIKit.Image(parent, null, color);
        img.raycastTarget = false;
        img.rectTransform.sizeDelta = sizeDelta;
        if (img.sprite == null) img.sprite = WhiteSprite(); // Filled는 스프라이트가 있어야 잘림
        return img;
    }

    static Sprite white;
    static Sprite WhiteSprite()
    {
        if (white != null) return white;
        Texture2D t = Texture2D.whiteTexture;
        white = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f));
        return white;
    }
}
