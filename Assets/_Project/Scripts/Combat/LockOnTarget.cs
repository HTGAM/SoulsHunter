using System.Collections.Generic;
using UnityEngine;

namespace SoulsHunter.Combat
{
    /// <summary>
    /// 락온할 수 있는 대상에 붙인다 (적, 보스, 보스의 부위 등).
    /// </summary>
    public class LockOnTarget : MonoBehaviour
    {
        static readonly List<LockOnTarget> active = new();
        public static IReadOnlyList<LockOnTarget> Active => active;

        [Tooltip("카메라가 바라볼 지점. 비워두면 오브젝트 위치 + Offset")]
        [SerializeField] Transform lockPoint;
        [SerializeField] Vector3 offset = new(0f, 1f, 0f);
        [SerializeField] bool canBeTargeted = true;

        /// <summary>죽었을 때 false로 바꾸면 락온이 풀린다.</summary>
        public bool CanBeTargeted
        {
            get => canBeTargeted && isActiveAndEnabled;
            set => canBeTargeted = value;
        }

        public Vector3 LockPosition => lockPoint ? lockPoint.position : transform.position + transform.rotation * offset;

        void OnEnable() => active.Add(this);
        void OnDisable() => active.Remove(this);

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(LockPosition, 0.2f);
        }
    }
}
