using UnityEngine;
using UnityEngine.Rendering;

// 구역 전용 후처리: 카메라가 구역 중심에서 radius 안이면 가득, radius+blend까지 서서히 빠짐
// 콜라이더 없는 전역 볼륨 + 거리로 weight 조절 (트리거 콜라이더가 카메라 충돌 · 공격 판정에 걸리지 않게)
[ExecuteAlways]
[RequireComponent(typeof(Volume))]
public class ZoneLookVolume : MonoBehaviour
{
    [SerializeField] float radius = 30f;
    [SerializeField] float blend = 15f;

    Volume volume;

    void OnEnable()
    {
        volume = GetComponent<Volume>();
        volume.isGlobal = true;
    }

    void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null || volume == null) return;
        Vector3 d = cam.transform.position - transform.position;
        d.y = 0f;
        volume.weight = 1f - Mathf.Clamp01((d.magnitude - radius) / Mathf.Max(0.01f, blend));
    }
}
