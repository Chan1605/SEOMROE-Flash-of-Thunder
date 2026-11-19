using UnityEngine;

// 보스 뇌광 연참 예고: 바닥에 푸른 띠 + 보스 쪽에서 끝으로 차오르는 밝은 띠 (가득 차는 순간 돌진)
public class BossLineTelegraph : MonoBehaviour
{
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    const float Thickness = 0.01f;

    Transform area, fill;
    Material areaMat, fillMat;
    Vector3 origin, direction;
    float length;

    public static BossLineTelegraph Create(Material source)
    {
        var go = new GameObject("BossLineTelegraph");
        var t = go.AddComponent<BossLineTelegraph>();
        Material baseMat = source != null ? source : BossTelegraph.RuntimeMaterial();
        t.areaMat = new Material(baseMat);
        t.fillMat = new Material(baseMat);
        t.area = t.Strip("Area", t.areaMat, new Color(0.45f, 0.7f, 1f, 0.22f), 0f);
        t.fill = t.Strip("Fill", t.fillMat, new Color(0.75f, 0.9f, 1f, 0.5f), 0.01f);
        return t;
    }

    // 시작점 · 방향 · 길이 · 폭. 높이는 시작점 바닥에 맞춤
    public void Set(Vector3 from, Vector3 dir, float len, float width)
    {
        if (Physics.Raycast(from + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 6f, ~0, QueryTriggerInteraction.Ignore)
            && hit.collider.GetComponentInParent<IDamageable>() == null)
            from.y = hit.point.y;
        origin = from + Vector3.up * 0.06f;
        direction = dir;
        length = Mathf.Max(0.1f, len);
        transform.SetPositionAndRotation(origin, Quaternion.LookRotation(dir, Vector3.up));
        transform.localScale = new Vector3(width, 1f, 1f);
        area.localScale = new Vector3(1f, Thickness, length);
        area.localPosition = new Vector3(0f, 0f, length * 0.5f);
    }

    public void SetProgress(float k)
    {
        k = Mathf.Clamp01(k);
        fill.localScale = new Vector3(0.8f, Thickness, length * k);
        fill.localPosition = new Vector3(0f, 0.01f, length * k * 0.5f);
    }

    Transform Strip(string name, Material mat, Color color, float lift)
    {
        GameObject strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(strip.GetComponent<Collider>());
        strip.name = name;
        strip.transform.SetParent(transform, false);
        strip.transform.localPosition = Vector3.up * lift;
        var r = strip.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        mat.SetColor(BaseColorId, color);
        r.sharedMaterial = mat;
        return strip.transform;
    }

    void OnDestroy()
    {
        if (areaMat != null) Destroy(areaMat);
        if (fillMat != null) Destroy(fillMat);
    }
}
