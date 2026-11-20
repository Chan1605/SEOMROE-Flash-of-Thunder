using UnityEngine;

// 보스 범위 예고 (도약 착지 · 뇌운 낙뢰): 가득 차는 순간 공격
// [변경] SG_GroundCircle 재질(Resources/Fx/Boss_GroundCircle)이 있으면 그것으로:
//  바닥 원판(가장자리로 갈수록 진해지는 붉은 영역 + 육각 무늬) + 가운데서 바깥으로 퍼지는 고리(_Progress)
//  재질이 없으면 예전처럼 반투명 원판 두 장
public class BossTelegraph : MonoBehaviour
{
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ProgressId = Shader.PropertyToID("_Progress");
    static readonly int RingWidthId = Shader.PropertyToID("_RingWidth");
    static readonly int GlobalAlphaId = Shader.PropertyToID("_Global_Alpha");
    static readonly int HexTextureId = Shader.PropertyToID("_Hex_Texture");
    static readonly int HexStrengthId = Shader.PropertyToID("_Hex_Strength");
    static Material runtimeMaterial, circleMaterial;
    static Texture2D hexTexture;
    static bool circleLoaded;
    const float Thickness = 0.005f; // 원기둥 높이 2 × 0.005 = 1cm 두께

    Transform fill;
    Material areaMat, fillMat;
    bool circle;

    public static BossTelegraph Create(Material source, float radius, Vector3 position)
    {
        var go = new GameObject("BossTelegraph");
        var t = go.AddComponent<BossTelegraph>();
        Material ground = CircleMaterial();
        if (ground != null)
        {
            t.circle = true;
            t.areaMat = new Material(ground);
            t.fillMat = new Material(ground);
            if (hexTexture != null)
            {
                t.areaMat.SetTexture(HexTextureId, hexTexture);
                t.fillMat.SetTexture(HexTextureId, hexTexture);
            }
            // 영역: 고리 폭을 원 전체로 → 가운데 투명 ~ 가장자리 진한 붉은 원판
            t.areaMat.SetFloat(ProgressId, 0.5f);
            t.areaMat.SetFloat(RingWidthId, 0.5f);
            t.areaMat.SetFloat(GlobalAlphaId, 0.32f);
            // 차오르는 고리: 얇은 띠가 가운데서 바깥으로
            t.fillMat.SetFloat(RingWidthId, 0.09f);
            t.fillMat.SetFloat(HexStrengthId, 0.35f);
            t.fillMat.SetFloat(ProgressId, 0f);
            Quad("Area", go.transform, t.areaMat, 0f);
            t.fill = Quad("Fill", go.transform, t.fillMat, 0.01f);
        }
        else
        {
            Material baseMat = source != null ? source : RuntimeMaterial();
            t.areaMat = new Material(baseMat);
            t.fillMat = new Material(baseMat);
            Disc("Area", go.transform, t.areaMat, new Color(1f, 0.12f, 0.08f, 0.22f), 0f);
            t.fill = Disc("Fill", go.transform, t.fillMat, new Color(1f, 0.25f, 0.1f, 0.45f), 0.02f);
            t.fill.localScale = new Vector3(0f, Thickness, 0f);
        }
        go.transform.localScale = new Vector3(radius * 2f, 1f, radius * 2f);
        t.SetPosition(position);
        return t;
    }

    public void SetPosition(Vector3 position)
    {
        // 지형 높이에 붙임 (플레이어가 공중이어도 바닥에 표시)
        if (Physics.Raycast(position + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 10f, ~0, QueryTriggerInteraction.Ignore)
            && hit.collider.GetComponentInParent<IDamageable>() == null)
            position.y = hit.point.y;
        transform.position = position + Vector3.up * 0.05f;
    }

    public void SetProgress(float k)
    {
        k = Mathf.Clamp01(k);
        if (circle) fillMat.SetFloat(ProgressId, k * 0.5f); // UV 반지름 0.5 = 원 가장자리
        else fill.localScale = new Vector3(k, Thickness, k);
    }

    void OnDestroy()
    {
        if (areaMat != null) Destroy(areaMat);
        if (fillMat != null) Destroy(fillMat);
    }

    // 육각 무늬 질감(Resources/Fx/Hex_Grid)은 복사본에만 넣음 (에디터에서 원본 에셋이 바뀌지 않게)
    static Material CircleMaterial()
    {
        if (circleLoaded) return circleMaterial;
        circleLoaded = true;
        circleMaterial = Resources.Load<Material>("Fx/Boss_GroundCircle");
        hexTexture = Resources.Load<Texture2D>("Fx/Hex_Grid");
        return circleMaterial;
    }

    static Transform Quad(string name, Transform parent, Material mat, float lift)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(quad.GetComponent<Collider>());
        quad.name = name;
        quad.transform.SetParent(parent, false);
        quad.transform.localPosition = Vector3.up * lift;
        quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // 앞면이 위를 봄
        var r = quad.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.sharedMaterial = mat;
        return quad.transform;
    }

    static Transform Disc(string name, Transform parent, Material mat, Color color, float lift)
    {
        GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(disc.GetComponent<Collider>());
        disc.name = name;
        disc.transform.SetParent(parent, false);
        disc.transform.localPosition = Vector3.up * lift;
        disc.transform.localScale = new Vector3(1f, Thickness, 1f);
        var r = disc.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        mat.SetColor(BaseColorId, color);
        r.sharedMaterial = mat;
        return disc.transform;
    }

    // URP Unlit을 반투명(알파 블렌드)으로
    public static Material RuntimeMaterial()
    {
        if (runtimeMaterial != null) return runtimeMaterial;
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        runtimeMaterial = new Material(shader != null ? shader : Shader.Find("Sprites/Default")) { name = "BossTelegraph (Runtime)" };
        MakeTransparent(runtimeMaterial);
        return runtimeMaterial;
    }

    public static void MakeTransparent(Material m)
    {
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }
}
