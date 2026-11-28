using UnityEngine;
using SoulsHunter.Core;

namespace SoulsHunter.World
{
    /// <summary>
    /// 안전구역의 출발 관문. 받은 퀘스트가 있으면 상호작용해서 투기장으로 출발한다.
    /// </summary>
    public class DepartureGate : Interactable
    {
        [Tooltip("퀘스트를 받으면 켜지는 표시 (빛나는 문 등, 선택)")]
        [SerializeField] GameObject readyVisual;

        // 관문은 크고 눈에 잘 띄므로 머리 위 표시를 띄우지 않는다
        public override bool ShowMarker => false;

        HuntManager Manager => HuntManager.Instance;

        public override string Prompt
        {
            get
            {
                var m = Manager;
                if (!m || m.Accepted == null) return "<color=#AAAAAA>출발 — 먼저 게시판에서 퀘스트를 받으세요</color>";
                return $"<b>출발</b>: {m.Accepted.title}  <color=#AAAAAA>({m.Accepted.arenaName})</color>";
            }
        }

        public override bool CanInteract(GameObject player) =>
            base.CanInteract(player) && Manager && Manager.CurrentPhase == HuntManager.Phase.Idle;

        void Update()
        {
            if (readyVisual)
            {
                bool ready = Manager && Manager.Accepted != null && Manager.CurrentPhase == HuntManager.Phase.Idle;
                if (readyVisual.activeSelf != ready) readyVisual.SetActive(ready);
            }
        }

        public override void Interact(GameObject player)
        {
            var m = Manager;
            if (!m) return;
            if (m.Accepted == null)
            {
                GameEvents.Notify("먼저 퀘스트 게시판에서 퀘스트를 받으세요.");
                return;
            }
            m.Depart();
        }

#if UNITY_EDITOR
        public void EditorSetup(GameObject visual) => readyVisual = visual;
#endif
    }
}
