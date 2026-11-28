using System;
using System.Collections.Generic;
using UnityEngine;
using SoulsHunter.Combat;
using SoulsHunter.Items;
using SoulsHunter.Player;

namespace SoulsHunter.Progression
{
    /// <summary>
    /// 혼과 스킬 트리 진행. 플레이어에 붙인다.
    /// - 혼: 몬스터 토벌·퀘스트 클리어로 얻는다. 쓰러져도 잃지 않는다.
    /// - 노드: 혼(찍은 수에 따라 비싸짐) + 핵심 노드는 희귀 혼(가방 아이템)을 써서 해금.
    /// - 초기화: 쓴 혼과 희귀 혼을 모두 돌려받는다.
    /// 다른 시스템은 PlayerSkills.Value(스탯)으로 보너스를 읽는다.
    /// </summary>
    public class PlayerSkills : MonoBehaviour
    {
        [SerializeField] SkillTreeData tree;

        public static PlayerSkills Instance { get; private set; }

        public SkillTreeData Tree => tree;
        public int Souls { get; private set; }
        public int UnlockedCount => unlocked.Count;
        public IReadOnlyCollection<string> Unlocked => unlocked;
        public int NextCost => tree ? tree.CostAt(unlocked.Count) : 0;

        /// <summary>(얻은 양) 혼을 얻었을 때</summary>
        public event Action<int> SoulsGained;
        /// <summary>혼·해금 상태가 바뀌었을 때</summary>
        public event Action Changed;

        readonly HashSet<string> unlocked = new();
        readonly Dictionary<SkillStat, float> totals = new();
        readonly Dictionary<string, List<string>> neighbours = new();

        PlayerInventory inventory;
        Health health;
        PlayerStamina stamina;

        /// <summary>스탯 보너스 합계 (스킬이 없으면 0)</summary>
        public static float Value(SkillStat stat) =>
            Instance && Instance.totals.TryGetValue(stat, out float v) ? v : 0f;

        void Awake()
        {
            inventory = GetComponent<PlayerInventory>();
            health = GetComponent<Health>();
            stamina = GetComponent<PlayerStamina>();
            BuildNeighbours();
        }

        void OnEnable()
        {
            Instance = this;
            if (health) health.IncomingDamageModifiers += ModifyIncoming;
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
            if (health) health.IncomingDamageModifiers -= ModifyIncoming;
        }

        float ModifyIncoming(float amount) => amount * Mathf.Max(0.1f, 1f + Value(SkillStat.DamageTaken));

        void BuildNeighbours()
        {
            neighbours.Clear();
            if (!tree) return;
            void Link(string a, string b)
            {
                if (!neighbours.TryGetValue(a, out var list)) neighbours[a] = list = new List<string>();
                if (!list.Contains(b)) list.Add(b);
            }
            foreach (var n in tree.nodes)
                foreach (var l in n.links)
                {
                    Link(n.id, l);
                    Link(l, n.id);
                }
        }

        // ───────── 혼 ─────────

        /// <summary>혼을 얻는다. applyBonus면 '얻는 혼 +%' 스킬이 적용된다.</summary>
        public void AddSouls(int amount, bool applyBonus = true)
        {
            if (amount <= 0) return;
            if (applyBonus) amount = Mathf.RoundToInt(amount * (1f + Value(SkillStat.SoulGain)));
            Souls += amount;
            SoulsGained?.Invoke(amount);
            Changed?.Invoke();
        }

        // ───────── 노드 ─────────

        public bool IsUnlocked(SkillNode n) => n != null && (n.branch == SkillBranch.Root || unlocked.Contains(n.id));

        /// <summary>이웃 노드가 해금돼 있어서 찍을 수 있는 위치인가 (비용은 따지지 않음)</summary>
        public bool IsReachable(SkillNode n)
        {
            if (n == null || IsUnlocked(n)) return false;
            if (!neighbours.TryGetValue(n.id, out var list)) return false;
            foreach (var id in list)
                if (IsUnlocked(tree.Find(id))) return true;
            return false;
        }

        public bool HasRareCost(SkillNode n) =>
            !n.rareCost || n.rareAmount <= 0 || (inventory && inventory.Count(n.rareCost) >= n.rareAmount);

        public bool CanUnlock(SkillNode n) => IsReachable(n) && Souls >= NextCost && HasRareCost(n);

        public bool TryUnlock(SkillNode n)
        {
            if (!CanUnlock(n)) return false;
            Souls -= NextCost;
            if (n.rareCost && n.rareAmount > 0) inventory.Remove(n.rareCost, n.rareAmount);
            unlocked.Add(n.id);
            Recalculate();
            return true;
        }

        /// <summary>모든 노드를 되돌리고 쓴 혼·희귀 혼을 돌려받는다.</summary>
        public void ResetAll()
        {
            if (!tree || unlocked.Count == 0) return;
            Souls += tree.TotalCost(unlocked.Count);
            foreach (var id in unlocked)
            {
                var n = tree.Find(id);
                if (n != null && n.rareCost && n.rareAmount > 0 && inventory) inventory.Add(n.rareCost, n.rareAmount);
            }
            unlocked.Clear();
            Recalculate();
        }

        void Recalculate()
        {
            totals.Clear();
            if (tree)
                foreach (var n in tree.nodes)
                {
                    if (!IsUnlocked(n)) continue;
                    foreach (var e in n.effects)
                        totals[e.stat] = (totals.TryGetValue(e.stat, out float v) ? v : 0f) + e.value;
                }
            if (health) health.SetMaxBonus(Value(SkillStat.MaxHealth));
            if (stamina) stamina.SetMaxBonus(Value(SkillStat.MaxStamina));
            Changed?.Invoke();
        }

        // ───────── 저장 ─────────

        /// <summary>저장 데이터로 상태를 되돌린다 (없는 노드 ID는 무시)</summary>
        public void Restore(int souls, IEnumerable<string> ids)
        {
            Souls = Mathf.Max(0, souls);
            unlocked.Clear();
            if (ids != null && tree)
                foreach (var id in ids)
                    if (!string.IsNullOrEmpty(id) && tree.Find(id) != null) unlocked.Add(id);
            Recalculate();
        }

        void Start() => Recalculate();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

#if UNITY_EDITOR
        public void EditorSetup(SkillTreeData data)
        {
            tree = data;
            BuildNeighbours();
        }
#endif
    }
}
