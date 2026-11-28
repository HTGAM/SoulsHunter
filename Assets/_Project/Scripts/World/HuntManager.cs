using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SoulsHunter.Combat;
using SoulsHunter.Core;
using SoulsHunter.Enemy;
using SoulsHunter.Items;
using SoulsHunter.Player;
using SoulsHunter.Progression;

namespace SoulsHunter.World
{
    /// <summary>투기장 웨이브 하나. 안의 몬스터를 모두 쓰러뜨리면 다음 웨이브로.</summary>
    [Serializable]
    public class HuntWave
    {
        public string label = "";
        public List<Health> enemies = new();
    }

    /// <summary>
    /// 사냥 퀘스트 하나. 안전구역에서 받고, 출발하면 투기장에서 웨이브를 차례로 상대한다.
    /// 정해진 횟수만큼 쓰러지면 실패. 그 전까지는 몬스터 체력이 그대로 유지된다.
    /// </summary>
    [Serializable]
    public class HuntQuest
    {
        [Tooltip("저장용 ID (바꾸면 클리어 기록이 사라진다)")]
        public string id = "quest";
        public string title = "Hunt";
        [TextArea] public string description = "";
        [Tooltip("투기장 이름 (표시용)")]
        public string arenaName = "투기장";
        [Tooltip("출발하면 플레이어가 서는 곳. 쓰러지면 여기서 다시 일어난다.")]
        public Transform arenaSpawn;
        public List<HuntWave> waves = new();
        [Tooltip("이 횟수만큼 쓰러지면 실패 (몬헌의 3수레)")]
        public int maxFaints = 3;
        [Tooltip("이 시간(초) 안에 한 번도 안 쓰러지고 깨면 S 랭크")]
        public float parTime = 300f;
        public List<MonsterRewards.Reward> rewards = new();
        [Tooltip("클리어 보상 혼 (첫 클리어는 두 배, S 랭크는 +20%)")]
        public int soulReward;
        [Tooltip("처음 깼을 때만 추가로 주는 보상")]
        public List<MonsterRewards.Reward> firstClearRewards = new();
        [Tooltip("이 퀘스트들(id)을 모두 깨야 게시판에 나타난다")]
        public List<string> requires = new();

        public int TotalEnemies
        {
            get
            {
                int n = 0;
                foreach (var w in waves) n += w.enemies.Count;
                return n;
            }
        }
    }

    /// <summary>퀘스트 하나의 최고 기록</summary>
    [Serializable]
    public class QuestRecord
    {
        public string id;
        public float bestTime = -1f;
        public string bestRank = "";
        public int clears;
    }

    /// <summary>퀘스트가 끝났을 때의 결과 (결과 화면에 표시)</summary>
    public class HuntResult
    {
        public HuntQuest quest;
        public bool success;
        public float time;
        public int faints;
        /// <summary>S / A / B (실패면 빈 문자열)</summary>
        public string rank = "";
        public bool newRecord;
        /// <summary>이번 사냥에서 얻은 혼 (토벌 + 퀘스트 보상)</summary>
        public int souls;
        /// <summary>처음 깼을 때 (첫 클리어 보상 지급)</summary>
        public bool firstClear;
        /// <summary>이번 클리어로 새로 열린 퀘스트</summary>
        public readonly List<HuntQuest> unlocked = new();
        /// <summary>이번 사냥에서 얻은 아이템 (갈무리·부위 파괴·퀘스트 보상 모두)</summary>
        public readonly List<(ItemData item, int amount)> items = new();
    }

    /// <summary>
    /// 사냥 흐름 전체를 관리한다 (씬에 하나).
    /// 안전구역: 퀘스트 게시판에서 퀘스트를 받고(Accept) 출발 관문에서 출발(Depart).
    /// 위험구역(투기장): 웨이브를 모두 쓰러뜨리면 클리어 → 보상 → 잠시 후 안전구역으로 귀환.
    /// 정해진 횟수만큼 쓰러지면 실패 → 안전구역으로 귀환. 일시정지 메뉴에서 포기할 수도 있다.
    /// 투기장 몬스터는 자기 웨이브가 시작될 때만 켜진다.
    /// </summary>
    public class HuntManager : MonoBehaviour
    {
        public enum Phase { Idle, Hunting, Returning }

        [Tooltip("안전구역 시작 위치 (귀환·안전구역에서 쓰러졌을 때)")]
        [SerializeField] Transform hubSpawn;
        [SerializeField] List<HuntQuest> quests = new();
        [Tooltip("웨이브 사이 쉬는 시간(초)")]
        [SerializeField] float waveDelay = 4f;
        [Tooltip("클리어 후 귀환까지 시간(초). 이 사이에 숨을 돌린다.")]
        [SerializeField] float clearReturnDelay = 10f;
        [Tooltip("실패·포기 후 귀환까지 시간(초)")]
        [SerializeField] float failReturnDelay = 5f;
        [Tooltip("쓰러진 몬스터를 치우기까지 시간(초)")]
        [SerializeField] float corpseHideDelay = 2.5f;

        public static HuntManager Instance { get; private set; }

        public IReadOnlyList<HuntQuest> Quests => quests;
        public HuntQuest Accepted { get; private set; }
        public HuntQuest Active { get; private set; }
        public Phase CurrentPhase { get; private set; }
        public int WaveIndex { get; private set; }
        public int Faints { get; private set; }
        public int AliveInWave => alive.Count;
        public float Elapsed => Active != null ? (endTime > 0f ? endTime : Time.time) - startTime : 0f;
        public float ReturnIn => CurrentPhase == Phase.Returning ? Mathf.Max(0f, returnAt - Time.time) : 0f;
        public IReadOnlyCollection<string> ClearedIds => cleared;
        public bool IsCleared(HuntQuest q) => q != null && cleared.Contains(q.id);

        /// <summary>선행 퀘스트를 모두 깨서 받을 수 있는가</summary>
        public bool IsUnlocked(HuntQuest q)
        {
            if (q == null) return false;
            foreach (var id in q.requires)
                if (!string.IsNullOrEmpty(id) && !cleared.Contains(id)) return false;
            return true;
        }

        /// <summary>아직 잠긴 퀘스트 수</summary>
        public int LockedCount
        {
            get
            {
                int n = 0;
                foreach (var q in quests) if (!IsUnlocked(q)) n++;
                return n;
            }
        }

        /// <summary>선행 퀘스트 이름 중 아직 안 깬 것 (게시판 안내용)</summary>
        public string MissingRequirements(HuntQuest q)
        {
            var names = new List<string>();
            foreach (var id in q.requires)
            {
                if (string.IsNullOrEmpty(id) || cleared.Contains(id)) continue;
                var req = quests.Find(x => x.id == id);
                names.Add(req != null ? req.title : id);
            }
            return string.Join(", ", names);
        }

        /// <summary>(퀘스트) 게시판에서 받았을 때</summary>
        public static event Action<HuntQuest> QuestAccepted;
        /// <summary>(퀘스트) 출발해서 투기장에 도착했을 때</summary>
        public static event Action<HuntQuest> QuestStarted;
        /// <summary>(퀘스트, 웨이브 번호 0~, 전체 웨이브 수)</summary>
        public static event Action<HuntQuest, int, int> WaveStarted;
        /// <summary>(결과) 클리어·실패 직후</summary>
        public static event Action<HuntResult> QuestEnded;
        /// <summary>안전구역으로 돌아왔을 때</summary>
        public static event Action Returned;

        GameObject player;
        Health playerHealth;
        PlayerDamageHandler damageHandler;
        PlayerLocomotion locomotion;
        PlayerInventory inventory;
        PlayerSkills skills;
        int gainedSouls;

        readonly HashSet<Health> alive = new();
        readonly HashSet<string> cleared = new();
        readonly Dictionary<string, QuestRecord> records = new();
        readonly List<(ItemData item, int amount)> gained = new();

        /// <summary>쓰러져도 되는 횟수 (스킬 보너스 포함)</summary>

        public static int FaintLimit(HuntQuest q) => q == null ? 0 : q.maxFaints + Mathf.RoundToInt(PlayerSkills.Value(SkillStat.ExtraFaints));


        public IEnumerable<QuestRecord> Records => records.Values;
        public QuestRecord GetRecord(HuntQuest q) => q != null && records.TryGetValue(q.id, out var r) ? r : null;
        float startTime, endTime, returnAt;
        Coroutine flow;

        void Awake()
        {
            Instance = this;
            // 모든 퀘스트 몬스터의 죽음을 듣는다 (지금 웨이브에 있는 것만 센다)
            foreach (var q in quests)
                foreach (var w in q.waves)
                    foreach (var h in w.enemies)
                        if (h)
                        {
                            var captured = h;
                            h.Died += () => OnEnemyDied(captured);
                        }
        }

        void Start()
        {
            player = GameObject.FindWithTag("Player");
            if (player)
            {
                playerHealth = player.GetComponent<Health>();
                damageHandler = player.GetComponent<PlayerDamageHandler>();
                locomotion = player.GetComponent<PlayerLocomotion>();
                inventory = player.GetComponent<PlayerInventory>();
                if (playerHealth) playerHealth.Died += OnPlayerDied;
                if (inventory) inventory.ItemAdded += OnItemAdded;
                skills = player.GetComponent<PlayerSkills>();
                if (skills) skills.SoulsGained += OnSoulsGained;
            }
            SetHubRespawn();
            HideAllEnemies();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (playerHealth) playerHealth.Died -= OnPlayerDied;
            if (inventory) inventory.ItemAdded -= OnItemAdded;
            if (skills) skills.SoulsGained -= OnSoulsGained;
            GameState.QuestActive = false;
        }

        void OnSoulsGained(int amount)

        {

            if (CurrentPhase != Phase.Idle) gainedSouls += amount;

        }


        void OnItemAdded(ItemData item, int amount)
        {
            if (CurrentPhase == Phase.Idle) return;
            for (int i = 0; i < gained.Count; i++)
                if (gained[i].item == item) { gained[i] = (item, gained[i].amount + amount); return; }
            gained.Add((item, amount));
        }

        // ───────── 안전구역 ─────────

        /// <summary>퀘스트 받기 (안전구역에서만)</summary>
        public void Accept(HuntQuest quest)
        {
            if (CurrentPhase != Phase.Idle || quest == null || !IsUnlocked(quest)) return;
            Accepted = quest;
            QuestAccepted?.Invoke(quest);
        }

        /// <summary>받은 퀘스트로 출발 → 투기장</summary>
        public void Depart()
        {
            if (CurrentPhase != Phase.Idle || Accepted == null) return;
            if (!Accepted.arenaSpawn || Accepted.waves.Count == 0)
            {
                Debug.LogWarning($"[HuntManager] '{Accepted.title}' 퀘스트에 투기장 위치나 웨이브가 없어요.");
                return;
            }

            Active = Accepted;
            CurrentPhase = Phase.Hunting;
            GameState.QuestActive = true;
            Faints = 0;
            WaveIndex = -1;
            startTime = Time.time;
            endTime = 0f;
            gained.Clear();
            gainedSouls = 0;

            HideAllEnemies();
            RefillPlayer();
            Teleport(Active.arenaSpawn);
            if (damageHandler) damageHandler.SetRespawnPoint(Active.arenaSpawn.position, Active.arenaSpawn.rotation);

            QuestStarted?.Invoke(Active);
            Run(StartWaveAfter(0, 2f));
        }

        // ───────── 투기장 ─────────

        IEnumerator StartWaveAfter(int index, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (CurrentPhase != Phase.Hunting) yield break;

            WaveIndex = index;
            alive.Clear();
            foreach (var h in Active.waves[index].enemies)
            {
                if (!h) continue;
                h.gameObject.SetActive(true);
                if (h.TryGetComponent(out EnemyAI ai))
                {
                    ai.ResetEnemy();
                    ai.ForceAggro();
                }
                else if (h.TryGetComponent(out TrainingDummy dummy)) dummy.ResetAll();
                else h.ResetHealth();
                alive.Add(h);
            }
            WaveStarted?.Invoke(Active, index, Active.waves.Count);

            if (alive.Count == 0) OnWaveCleared(); // 빈 웨이브는 건너뜀
        }

        void OnEnemyDied(Health h)
        {
            if (CurrentPhase != Phase.Hunting || !alive.Remove(h)) return;
            StartCoroutine(HideLater(h.gameObject, corpseHideDelay));
            if (alive.Count == 0) OnWaveCleared();
        }

        void OnWaveCleared()
        {
            if (WaveIndex + 1 < Active.waves.Count)
            {
                GameEvents.Notify($"웨이브 {WaveIndex + 1} 돌파!  다음 적이 곧 나타나요.");
                Run(StartWaveAfter(WaveIndex + 1, waveDelay));
            }
            else Complete();
        }

        IEnumerator HideLater(GameObject go, float delay)
        {
            yield return new WaitForSeconds(delay);
            // 그사이 다시 살아나 웨이브에 들어간 게 아니면 치운다 (TrainingDummy 부활 방지)
            if (go && go.TryGetComponent(out Health h) && !alive.Contains(h)) go.SetActive(false);
        }

        void OnPlayerDied()
        {
            if (CurrentPhase != Phase.Hunting) return;
            Faints++;
            if (Faints >= FaintLimit(Active))
            {
                GameEvents.Notify($"<color=#FF6A5A>{FaintLimit(Active)}번 쓰러졌어요.</color>");
                Fail();
            }
            else GameEvents.Notify($"<color=#FF6A5A>쓰러졌어요  ({Faints} / {FaintLimit(Active)})</color>  몬스터 체력은 그대로예요.");
        }

        // ───────── 끝 ─────────

        void Complete()
        {
            endTime = Time.time;
            var lockedBefore = new List<HuntQuest>();
            foreach (var q in quests) if (!IsUnlocked(q)) lockedBefore.Add(q);
            bool firstClear = cleared.Add(Active.id);
            GiveRewards(Active.rewards);
            if (firstClear) GiveRewards(Active.firstClearRewards);

            // 퀘스트 보상 혼: 첫 클리어 두 배, S 랭크 +20%

            string rank = RankFor(Active, endTime - startTime, Faints);

            int soulReward = Active.soulReward * (firstClear ? 2 : 1);

            if (rank == "S") soulReward = Mathf.RoundToInt(soulReward * 1.2f);

            if (skills) skills.AddSouls(soulReward);


            var result = MakeResult(true);            result.firstClear = firstClear;
            foreach (var q in lockedBefore)
                if (IsUnlocked(q))
                {
                    result.unlocked.Add(q);
                    GameEvents.Notify($"<color=#FFD27A>새 퀘스트가 열렸어요: {q.title}</color>");
                }
            result.rank = rank;
            if (!records.TryGetValue(Active.id, out var rec)) records[Active.id] = rec = new QuestRecord { id = Active.id };
            rec.clears++;
            if (rec.bestTime < 0f || result.time < rec.bestTime)
            {
                result.newRecord = rec.bestTime >= 0f; // 첫 클리어는 '신기록'으로 치지 않는다
                rec.bestTime = result.time;
            }
            if (RankValue(result.rank) > RankValue(rec.bestRank)) rec.bestRank = result.rank;

            QuestEnded?.Invoke(result);
            GameEvents.RaiseProgressChanged();
            BeginReturn(clearReturnDelay);
        }

        void Fail()
        {
            endTime = Time.time;
            QuestEnded?.Invoke(MakeResult(false));
            SetHubRespawn(); // 쓰러진 채로 실패했다면 안전구역에서 일어난다
            BeginReturn(failReturnDelay);
        }

        HuntResult MakeResult(bool success)
        {
            var r = new HuntResult { quest = Active, success = success, time = Elapsed, faints = Faints, souls = gainedSouls };
            r.items.AddRange(gained);
            return r;
        }

        /// <summary>S: 안 쓰러지고 기준 시간 안 / A: 1번 이하로 쓰러지고 기준의 1.6배 안 / B: 그 외</summary>
        public static string RankFor(HuntQuest q, float time, int faints)
        {
            if (faints == 0 && time <= q.parTime) return "S";
            if (faints <= 1 && time <= q.parTime * 1.6f) return "A";
            return "B";
        }

        static int RankValue(string rank) => rank switch { "S" => 3, "A" => 2, "B" => 1, _ => 0 };

        /// <summary>퀘스트 포기 (일시정지 메뉴)</summary>
        public void Abandon()
        {
            if (CurrentPhase != Phase.Hunting) return;
            GameEvents.Notify("퀘스트를 포기했어요.");
            Fail();
        }

        void BeginReturn(float delay)
        {
            CurrentPhase = Phase.Returning;
            returnAt = Time.time + delay;
            Run(ReturnAfter(delay));
        }

        IEnumerator ReturnAfter(float delay)
        {
            yield return new WaitForSeconds(delay);
            // 아직 쓰러져 있으면 일어날 때까지 기다린다 (부활 위치는 이미 안전구역)
            while (playerHealth && playerHealth.IsDead) yield return null;
            ReturnToHub();
        }

        void ReturnToHub()
        {
            HideAllEnemies();
            SetHubRespawn();
            Teleport(hubSpawn);
            RefillPlayer();

            Active = null;
            Accepted = null;
            CurrentPhase = Phase.Idle;
            WaveIndex = -1;
            alive.Clear();
            GameState.QuestActive = false;
            Returned?.Invoke();
        }

        // ───────── 보조 ─────────

        void Run(IEnumerator routine)
        {
            if (flow != null) StopCoroutine(flow);
            flow = StartCoroutine(routine);
        }

        void GiveRewards(List<MonsterRewards.Reward> rewards)
        {
            if (!inventory || rewards == null) return;
            foreach (var r in rewards)
            {
                if (!r.item || UnityEngine.Random.value > r.chance) continue;
                inventory.Add(r.item, UnityEngine.Random.Range(r.min, Mathf.Max(r.min, r.max) + 1));
            }
        }

        void HideAllEnemies()
        {
            foreach (var q in quests)
                foreach (var w in q.waves)
                    foreach (var h in w.enemies)
                        if (h) h.gameObject.SetActive(false);
        }

        void RefillPlayer()
        {
            if (!player) return;
            if (playerHealth && !playerHealth.IsDead) playerHealth.ResetHealth();
            if (player.TryGetComponent(out PlayerFlask flask)) flask.Refill();
            if (player.TryGetComponent(out PlayerStamina stamina)) stamina.Refill();
        }

        void Teleport(Transform to)
        {
            if (to && locomotion) locomotion.Teleport(to.position, to.rotation);
        }

        void SetHubRespawn()
        {
            if (damageHandler && hubSpawn) damageHandler.SetRespawnPoint(hubSpawn.position, hubSpawn.rotation);
        }

        /// <summary>저장 데이터로 클리어 기록을 되돌린다.</summary>
        public void RestoreCleared(IEnumerable<string> ids)
        {
            cleared.Clear();
            foreach (var id in ids) if (!string.IsNullOrEmpty(id)) cleared.Add(id);
        }

        /// <summary>저장 데이터로 최고 기록을 되돌린다.</summary>
        public void RestoreRecords(IEnumerable<QuestRecord> list)
        {
            records.Clear();
            foreach (var r in list)
            {
                if (r == null || string.IsNullOrEmpty(r.id)) continue;
                records[r.id] = r;
                if (r.clears > 0) cleared.Add(r.id);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            QuestAccepted = null;
            QuestStarted = null;
            WaveStarted = null;
            QuestEnded = null;
            Returned = null;
        }

#if UNITY_EDITOR
        public void EditorSetup(Transform hub, List<HuntQuest> list)
        {
            hubSpawn = hub;
            quests = list;
        }
#endif
    }
}
