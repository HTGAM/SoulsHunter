using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using SoulsHunter.Combat;
using SoulsHunter.Core;
using SoulsHunter.Items;
using SoulsHunter.Player;

namespace SoulsHunter.World
{
    /// <summary>
    /// 사냥 퀘스트 하나 (몬헌식). 제한 시간 안에 대상을 정해진 수만큼 쓰러뜨리면 클리어.
    /// 제한 시간이 지나거나 정해진 횟수만큼 쓰러지면 실패.
    /// </summary>
    [Serializable]
    public class HuntQuest
    {
        [Tooltip("저장용 ID (바꾸면 클리어 기록이 사라진다)")]
        public string id = "quest";
        public string title = "Hunt";
        [TextArea] public string description = "";
        [Tooltip("이 중 아무나 쓰러뜨리면 1회로 센다")]
        public List<Health> targets = new();
        public int requiredKills = 1;
        [Tooltip("제한 시간(초)")]
        public float timeLimit = 900f;
        [Tooltip("이 횟수만큼 쓰러지면 실패 (몬헌의 3수레)")]
        public int maxFaints = 3;
        public List<MonsterRewards.Reward> rewards = new();
    }

    /// <summary>
    /// 퀘스트 게시판. 상호작용하면 다음 퀘스트를 받고, 진행 중이면 포기한다.
    /// 받은 퀘스트는 월드를 초기화하고(몬스터 부활, 체력 회복) 시작한다.
    /// 클리어하면 보상을 가방에 넣고 클리어 기록을 남긴다 (SaveSystem이 저장).
    /// </summary>
    public class QuestBoard : Interactable
    {
        [SerializeField] List<HuntQuest> quests = new();

        HuntQuest offered, active;
        int offeredIndex;
        int kills, faints;
        float startTime;
        Health playerHealth;
        PlayerInventory playerInventory;
        readonly HashSet<string> cleared = new();
        readonly List<(Health health, Action handler)> subscriptions = new();

        /// <summary>지금 진행 중인 퀘스트 (어느 게시판이든). 없으면 null.</summary>
        public static QuestBoard Running { get; private set; }

        public HuntQuest ActiveQuest => active;
        public int Kills => kills;
        public int Faints => faints;
        public float Elapsed => active != null ? Time.time - startTime : 0f;
        public float Remaining => active != null ? Mathf.Max(0f, active.timeLimit - Elapsed) : 0f;
        public IReadOnlyCollection<string> ClearedIds => cleared;
        public bool IsCleared(HuntQuest q) => q != null && cleared.Contains(q.id);

        /// <summary>(퀘스트)</summary>
        public static event Action<HuntQuest> QuestStarted;
        /// <summary>(퀘스트, 성공 여부, 걸린 시간)</summary>
        public static event Action<HuntQuest, bool, float> QuestEnded;

        public override string Prompt
        {
            get
            {
                if (active != null) return $"퀘스트 포기  <color=#AAAAAA>({active.title})</color>";
                if (Running) return "<color=#AAAAAA>다른 퀘스트 진행 중</color>";
                if (offered == null) return "<color=#AAAAAA>받을 수 있는 퀘스트가 없어요</color>";
                string mark = IsCleared(offered) ? " <color=#9FD89F>(클리어)</color>" : "";
                return $"퀘스트 받기: <b>{offered.title}</b>{mark}\n<size=20>{Summary(offered)}</size>";
            }
        }

        void Start()
        {
            var player = GameObject.FindWithTag("Player");
            if (player)
            {
                playerHealth = player.GetComponent<Health>();
                playerInventory = player.GetComponent<PlayerInventory>();
                if (playerHealth) playerHealth.Died += OnPlayerDied;
            }
            PickOffer();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (active != null) End(false, silent: true);
        }

        void OnDestroy()
        {
            if (playerHealth) playerHealth.Died -= OnPlayerDied;
        }

        void Update()
        {
            if (active != null && Elapsed >= active.timeLimit)
            {
                GameEvents.Notify("<color=#FF6A5A>제한 시간이 끝났어요.</color>");
                End(false);
            }
        }

        public override bool CanInteract(GameObject player) => base.CanInteract(player) && (active != null || !Running);

        public override void Interact(GameObject player)
        {
            if (active != null)
            {
                GameEvents.Notify("퀘스트를 포기했어요.");
                End(false);
                return;
            }
            if (offered != null && !Running) Begin(offered);
        }

        // ───────── 진행 ─────────

        void Begin(HuntQuest quest)
        {
            active = quest;
            Running = this;
            GameState.QuestActive = true;
            kills = faints = 0;
            startTime = Time.time;

            foreach (var t in quest.targets)
            {
                if (!t) continue;
                Action handler = OnTargetDied;
                t.Died += handler;
                subscriptions.Add((t, handler));
            }

            // 새 사냥: 몬스터·플레이어를 처음 상태로
            GameEvents.RaiseWorldReset();
            if (playerHealth)
            {
                playerHealth.ResetHealth();
                if (playerHealth.TryGetComponent(out PlayerFlask flask)) flask.Refill();
                if (playerHealth.TryGetComponent(out PlayerStamina stamina)) stamina.Refill();
            }
            QuestStarted?.Invoke(quest);
        }

        void OnTargetDied()
        {
            if (active == null) return;
            kills++;
            if (kills >= active.requiredKills) End(true);
        }

        void OnPlayerDied()
        {
            if (active == null) return;
            faints++;
            if (faints >= active.maxFaints)
            {
                GameEvents.Notify("<color=#FF6A5A>정해진 횟수만큼 쓰러졌어요.</color>");
                End(false);
            }
        }

        void End(bool success, bool silent = false)
        {
            var quest = active;
            float time = Elapsed;
            foreach (var (h, handler) in subscriptions)
                if (h) h.Died -= handler;
            subscriptions.Clear();
            active = null;
            if (Running == this) Running = null;
            GameState.QuestActive = Running;

            if (success)
            {
                cleared.Add(quest.id);
                GiveRewards(quest);
                offeredIndex++;
                PickOffer();
                GameEvents.RaiseProgressChanged();
            }
            if (!silent) QuestEnded?.Invoke(quest, success, time);
        }

        void GiveRewards(HuntQuest quest)
        {
            if (!playerInventory) return;
            foreach (var r in quest.rewards)
            {
                if (!r.item || UnityEngine.Random.value > r.chance) continue;
                playerInventory.Add(r.item, UnityEngine.Random.Range(r.min, Mathf.Max(r.min, r.max) + 1));
            }
        }

        /// <summary>안 깬 퀘스트를 우선 보여준다. 다 깼으면 순서대로 돌아가며 다시 받을 수 있다.</summary>
        void PickOffer()
        {
            offered = null;
            if (quests.Count == 0) return;
            for (int i = 0; i < quests.Count; i++)
            {
                var q = quests[i];
                if (!IsCleared(q)) { offered = q; offeredIndex = i; return; }
            }
            offeredIndex %= quests.Count;
            offered = quests[offeredIndex];
        }

        static string Summary(HuntQuest q)
        {
            var sb = new StringBuilder();
            sb.Append($"제한 {Mathf.RoundToInt(q.timeLimit / 60f)}분 · {q.maxFaints}회 쓰러지면 실패");
            if (q.rewards.Count > 0)
            {
                sb.Append(" · 보상: ");
                for (int i = 0; i < q.rewards.Count; i++)
                {
                    var r = q.rewards[i];
                    if (!r.item) continue;
                    if (i > 0) sb.Append(", ");
                    sb.Append(r.item.DisplayName);
                }
            }
            return sb.ToString();
        }

        /// <summary>저장 데이터로 클리어 기록을 되돌린다.</summary>
        public void RestoreCleared(IEnumerable<string> ids)
        {
            cleared.Clear();
            foreach (var id in ids) if (!string.IsNullOrEmpty(id)) cleared.Add(id);
            offeredIndex = 0;
            PickOffer();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Running = null;
            QuestStarted = null;
            QuestEnded = null;
        }

#if UNITY_EDITOR
        public void EditorSetup(List<HuntQuest> list) => quests = list;
#endif
    }
}
