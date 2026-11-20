using UnityEngine;

// 플레이어가 이 지점 근처(반경)에 들어오면 다음 씬으로 (물리 트리거 없이 거리로 확인)
// spawnAt: 다음 씬에서 플레이어를 옮길 오브젝트 경로 (예: Zone_4_Boss/Generated/RespawnPoint)
//        setFlag: 넘어가기 전에 RunState에 남길 표시 · skipIfFlag: 그 표시가 있으면 이 출구는 꺼짐 (던전은 한 번만)
public class SceneExit : MonoBehaviour
{
    [SerializeField] string sceneName = "InGame"; // 본편 씬 이름 변경
    [SerializeField] float radius = 3.5f;
    [SerializeField] string spawnAt = "";
    [SerializeField] string setFlag = "";
    [SerializeField] string skipIfFlag = "";

    [SerializeField] bool showPortal = true; // [변경] 출구 자리에 소용돌이 포탈 (SG_WarpPortal)
    [SerializeField] float portalHeight = 1.7f;
    [SerializeField] float portalRadius = 1.4f;

    Transform player;
    WarpPortalFx portal;

    void Start()
    {
        PlayerHealth health = FindAnyObjectByType<PlayerHealth>();
        if (health != null) player = health.transform;
        if (showPortal)
        {
            portal = WarpPortalFx.Create(transform, transform.position + Vector3.up * portalHeight, Quaternion.identity, portalRadius,
                new Color(0.45f, 0.75f, 1.6f), new Color(0.02f, 0.06f, 0.2f), true);
            portal.Open(0.8f);
        }
    }

    void Update()
    {
        bool skipped = !string.IsNullOrEmpty(skipIfFlag) && RunState.HasFlag(skipIfFlag);
        if (portal != null && portal.IsOpen == skipped) // [변경] 꺼진 출구는 포탈도 닫음
        {
            if (skipped) portal.Close(0.4f);
            else portal.Open(0.6f);
        }
        if (player == null || SceneTransition.Busy || GamePause.IsPaused) return;
        if (skipped) return;
        Vector3 d = player.position - transform.position;
        d.y = 0f;
        if (d.sqrMagnitude > radius * radius) return;
        if (!string.IsNullOrEmpty(setFlag)) RunState.SetFlag(setFlag);
        SceneTransition.Load(sceneName, true, spawnAt);
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
