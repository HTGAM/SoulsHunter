using UnityEngine;

namespace SoulsHunter.UI
{
    /// <summary>타이틀 화면 배경: 카메라가 대상 주위를 천천히 돈다.</summary>
    public class TitleBackdrop : MonoBehaviour
    {
        [SerializeField] Transform target;
        [SerializeField] Vector3 targetOffset = new(0f, 2.5f, 0f);
        [SerializeField] float distance = 13f;
        [SerializeField] float height = 3.5f;
        [Tooltip("초당 회전 각도")]
        [SerializeField] float orbitSpeed = 4f;
        [SerializeField] float startAngle = 200f;

        float angle;

        void Start() => angle = startAngle;

        void LateUpdate()
        {
            Vector3 center = (target ? target.position : Vector3.zero) + targetOffset;
            angle += orbitSpeed * Time.unscaledDeltaTime;
            // 살짝 오르내리며 숨쉬는 느낌
            float bob = Mathf.Sin(Time.unscaledTime * 0.35f) * 0.4f;
            Vector3 offset = Quaternion.Euler(0f, angle, 0f) * new Vector3(0f, height + bob, -distance);
            transform.position = center + offset;
            transform.rotation = Quaternion.LookRotation(center - transform.position);
        }

#if UNITY_EDITOR
        public void EditorSetup(Transform lookTarget) => target = lookTarget;
#endif
    }
}
