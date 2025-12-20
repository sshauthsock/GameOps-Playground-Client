using UnityEngine;

public class Billboard : MonoBehaviour
{
    private Transform _camTransform;

    void Start()
    {
        var text = GetComponentInChildren<TMPro.TextMeshProUGUI>();
        if (text != null) text.isTextObjectScaleStatic = true; // 불필요한 계산 및 가이드 방지
        if (Camera.main != null)
            _camTransform = Camera.main.transform;
    }

    void LateUpdate() // Update가 아닌 LateUpdate에서 실행해야 떨림이 없습니다.
    {
        if (_camTransform == null) return;

        // 부모의 회전을 완전히 무시하고 카메라의 회전값과 일치시킵니다.
        transform.rotation = _camTransform.rotation;
    }
}