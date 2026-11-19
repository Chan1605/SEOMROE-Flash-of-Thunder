using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// 연못 거울 반사: 메인 카메라를 수면 기준으로 뒤집은 반사 카메라를 낮은 해상도로 따로 그려 물 셰이더에 넘김
// 물이 화면에 보일 때만 그림. 그림자 · 후처리 · 작은 풀은 빼서 가볍게
[DefaultExecutionOrder(1000)] // 카메라 이동(LateUpdate) 뒤에
[RequireComponent(typeof(Renderer))]
public class PlanarWaterReflection : MonoBehaviour
{
    [SerializeField] float resolutionScale = 0.5f;
    [SerializeField] float clipOffset = 0.05f;
    [SerializeField] LayerMask cullingMask = ~(1 << 5); // UI 제외
    [SerializeField] float maxDistance = 120f;

    static readonly int ReflectionTex = Shader.PropertyToID("_SeomroePlanarReflection");
    static readonly int ReflectionOn = Shader.PropertyToID("_SeomroePlanarOn");
    static PlanarWaterReflection owner; // 같은 프레임엔 가장 가까운 연못 하나만 그림

    // [변경] 발밑 물 튐용: 씬의 연못 목록 + 위치가 물 위인지
    public static readonly System.Collections.Generic.List<PlanarWaterReflection> All = new System.Collections.Generic.List<PlanarWaterReflection>();

    public static bool FindWater(Vector3 position, out float surfaceY)
    {
        foreach (PlanarWaterReflection w in All)
        {
            Vector3 c = w.transform.position;
            float radius = w.transform.lossyScale.x / 1.45f; // 물면 원판은 연못 반지름의 1.45배로 깔림
            Vector2 d = new Vector2(position.x - c.x, position.z - c.z);
            if (d.magnitude < radius && position.y < c.y + 0.35f)
            {
                surfaceY = c.y;
                return true;
            }
        }
        surfaceY = 0f;
        return false;
    }

    void OnEnable() => All.Add(this);

    Renderer surface;
    Camera reflectionCamera;
    RenderTexture target;

    void Awake() => surface = GetComponent<Renderer>();

    void OnDisable()
    {
        All.Remove(this);
        if (owner == this) { owner = null; Shader.SetGlobalFloat(ReflectionOn, 0f); }
        if (reflectionCamera != null) Destroy(reflectionCamera.gameObject);
        if (target != null) { target.Release(); Destroy(target); }
        reflectionCamera = null;
        target = null;
    }

    void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null || !surface.isVisible) { Release(); return; }
        float dist = Vector3.Distance(cam.transform.position, transform.position);
        if (dist > maxDistance) { Release(); return; }
        if (owner != null && owner != this && owner.isActiveAndEnabled
            && Vector3.Distance(cam.transform.position, owner.transform.position) < dist) return;
        owner = this;

        EnsureResources(cam);
        Render(cam);
        Shader.SetGlobalTexture(ReflectionTex, target);
        Shader.SetGlobalFloat(ReflectionOn, 1f);
    }

    void Release()
    {
        if (owner != this) return;
        owner = null;
        Shader.SetGlobalFloat(ReflectionOn, 0f);
    }

    void EnsureResources(Camera cam)
    {
        int w = Mathf.Max(64, (int)(cam.pixelWidth * resolutionScale));
        int h = Mathf.Max(64, (int)(cam.pixelHeight * resolutionScale));
        if (target == null || target.width != w || target.height != h)
        {
            if (target != null) { target.Release(); Destroy(target); }
            target = new RenderTexture(w, h, 24, RenderTextureFormat.DefaultHDR) { name = "PlanarReflection" };
        }
        if (reflectionCamera != null) return;

        var go = new GameObject("PlanarReflectionCamera") { hideFlags = HideFlags.HideAndDontSave };
        reflectionCamera = go.AddComponent<Camera>();
        reflectionCamera.enabled = false;
        var data = go.AddComponent<UniversalAdditionalCameraData>();
        data.renderShadows = false;
        data.renderPostProcessing = false;
        data.requiresDepthTexture = false;
        data.requiresColorTexture = false;
    }

    // 핵심: 수면(높이 y) 기준 반사 행렬 + 수면 아래를 자르는 비스듬한 근평면
    void Render(Camera cam)
    {
        reflectionCamera.CopyFrom(cam);
        reflectionCamera.enabled = false;
        reflectionCamera.cullingMask = cullingMask & cam.cullingMask;
        reflectionCamera.targetTexture = target;

        float y = transform.position.y;
        var plane = new Vector4(0f, 1f, 0f, -y - clipOffset);
        Matrix4x4 reflection = Matrix4x4.identity;
        reflection.m11 = -1f;
        reflection.m13 = 2f * y;

        reflectionCamera.worldToCameraMatrix = cam.worldToCameraMatrix * reflection;
        Vector4 clip = CameraSpacePlane(reflectionCamera, plane);
        reflectionCamera.projectionMatrix = cam.CalculateObliqueMatrix(clip);

        var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
        if (!RenderPipeline.SupportsRenderRequest(reflectionCamera, request)) return;
        GL.invertCulling = true; // 반사 행렬이 앞뒤 면을 뒤집음
        RenderPipeline.SubmitRenderRequest(reflectionCamera, request);
        GL.invertCulling = false;
    }

    static Vector4 CameraSpacePlane(Camera cam, Vector4 worldPlane)
    {
        Matrix4x4 m = cam.worldToCameraMatrix;
        Vector3 n = m.MultiplyVector(new Vector3(worldPlane.x, worldPlane.y, worldPlane.z)).normalized;
        Vector3 p = m.MultiplyPoint(new Vector3(0f, -worldPlane.w, 0f));
        return new Vector4(n.x, n.y, n.z, -Vector3.Dot(p, n));
    }
}
