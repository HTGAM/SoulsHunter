using System.Collections.Generic;
using UnityEngine;

namespace SoulsHunter.World
{
    /// <summary>
    /// 플레이어가 다가가서 상호작용 키(E / Y)로 쓰는 물체의 공통 부모.
    /// 화톳불, 문, 상자, NPC 등이 이걸 상속한다.
    /// </summary>
    public abstract class Interactable : MonoBehaviour
    {
        static readonly List<Interactable> active = new();
        public static IReadOnlyList<Interactable> Active => active;

        [SerializeField] float interactRange = 2.5f;

        public float InteractRange => interactRange;

        /// <summary>화면에 표시할 안내 문구 (예: "휴식한다")</summary>
        public abstract string Prompt { get; }

        public virtual bool CanInteract(GameObject player) => isActiveAndEnabled;

        public abstract void Interact(GameObject player);

        protected virtual void OnEnable() => active.Add(this);
        protected virtual void OnDisable() => active.Remove(this);

        protected virtual void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, interactRange);
        }
    }
}
