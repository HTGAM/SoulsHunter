using System.Text;
using UnityEngine;
using SoulsHunter.Core;

namespace SoulsHunter.World
{
    /// <summary>
    /// 안전구역의 퀘스트 게시판. 상호작용할 때마다 다음 퀘스트를 받는다 (목록을 돌아가며).
    /// 받은 퀘스트는 출발 관문(DepartureGate)에서 출발한다.
    /// 퀘스트 목록과 진행은 HuntManager가 갖고 있다.
    /// </summary>
    public class QuestBoard : Interactable
    {
        int nextIndex;

        HuntManager Manager => HuntManager.Instance;

        public override string Prompt
        {
            get
            {
                var m = Manager;
                var next = m ? NextUnlocked(m) : null;
                if (next == null) return "<color=#AAAAAA>받을 수 있는 퀘스트가 없어요</color>";
                string verb = m.Accepted == null ? "퀘스트 받기" : "퀘스트 바꾸기";
                return $"{verb}: <b>{next.title}</b>{ClearMark(m, next)}\n<size=20>{Summary(next)}{Best(m, next)}{LockedHint(m)}</size>";
            }
        }

        /// <summary>nextIndex부터 돌면서 처음 만나는 열린 퀘스트 (nextIndex도 그 자리로 옮긴다)</summary>
        HuntQuest NextUnlocked(HuntManager m)
        {
            int n = m.Quests.Count;
            for (int i = 0; i < n; i++)
            {
                int idx = (nextIndex + i) % n;
                if (m.IsUnlocked(m.Quests[idx])) { nextIndex = idx; return m.Quests[idx]; }
            }
            return null;
        }

        /// <summary>다음으로 열릴 퀘스트 안내</summary>
        static string LockedHint(HuntManager m)
        {
            foreach (var q in m.Quests)
            {
                if (m.IsUnlocked(q)) continue;
                return $"\n<color=#888888>잠긴 퀘스트 {m.LockedCount}개 · 다음: '{q.title}' — {m.MissingRequirements(q)} 클리어 필요</color>";
            }
            return "";
        }

        public override bool CanInteract(GameObject player) =>
            base.CanInteract(player) && Manager && Manager.CurrentPhase == HuntManager.Phase.Idle;

        void Start()
        {
            // 안 깬 퀘스트부터 보여준다
            var m = Manager;
            if (!m) return;
            for (int i = 0; i < m.Quests.Count; i++)
                if (m.IsUnlocked(m.Quests[i]) && !m.IsCleared(m.Quests[i])) { nextIndex = i; return; }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            HuntManager.QuestEnded += OnQuestEnded;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            HuntManager.QuestEnded -= OnQuestEnded;
        }

        // 새로 열린 퀘스트가 있으면 게시판이 그걸 먼저 보여준다
        void OnQuestEnded(HuntResult r)
        {
            var m = Manager;
            if (!m || r.unlocked.Count == 0) return;
            int idx = IndexOf(m, r.unlocked[0]);
            if (idx >= 0) nextIndex = idx;
        }

        static int IndexOf(HuntManager m, HuntQuest q)
        {
            for (int i = 0; i < m.Quests.Count; i++) if (m.Quests[i] == q) return i;
            return -1;
        }

        public override void Interact(GameObject player)
        {
            var m = Manager;
            var quest = m ? NextUnlocked(m) : null;
            if (quest == null) return;
            m.Accept(quest);
            nextIndex = (nextIndex + 1) % m.Quests.Count;
            GameEvents.Notify($"퀘스트를 받았어요: <color=#FFD27A>{quest.title}</color>  — 출발 관문으로 가세요.");
        }

        static string Best(HuntManager m, HuntQuest q)
        {
            var rec = m.GetRecord(q);
            if (rec == null || rec.bestTime < 0f) return "";
            int s = Mathf.FloorToInt(rec.bestTime);
            return $"\n<color=#FFD27A>최고 기록  {s / 60:00}:{s % 60:00}  ·  최고 랭크 {rec.bestRank}</color>";
        }

        static string ClearMark(HuntManager m, HuntQuest q) =>
            m.IsCleared(q) ? " <color=#9FD89F>(클리어)</color>" : "";

        static string Summary(HuntQuest q)
        {
            var sb = new StringBuilder();
            sb.Append($"{q.arenaName} · 웨이브 {q.waves.Count} · {q.maxFaints}회 쓰러지면 실패");
            bool first = true;
            foreach (var r in q.rewards)
            {
                if (!r.item) continue;
                sb.Append(first ? " · 보상: " : ", ");
                sb.Append(r.item.DisplayName);
                first = false;
            }
            return sb.ToString();
        }
    }
}
