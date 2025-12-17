using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    private Transform _target;      // 추적할 대상 (내 플레이어)
    public Vector3 offset = new Vector3(0, 10, -10); // 카메라와의 거리
    public float smoothSpeed = 0.125f;               // 따라가는 부드러움 정도

    void LateUpdate()
    {
        // 1. 내 캐릭터(ID 9999)를 찾지 못했다면 검색 시도
        if (_target == null)
        {
            GameObject myPlayer = PlayerManager.Instance.GetPlayer(PlayerManager.Instance.MyPlayerID);
            if (myPlayer != null)
            {
                _target = myPlayer.transform;
            }
            return;
        }

        // 2. 부드러운 카메라 이동 (Lerp)
        Vector3 desiredPosition = _target.position + offset;
        Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed);
        transform.position = smoothedPosition;

        // 3. 항상 캐릭터를 바라보게 설정
        transform.LookAt(_target);
    }
}