using System;
using System.Collections.Generic;
using UnityEngine;
using SoulsHunter.Combat;
using SoulsHunter.Progression;

namespace SoulsHunter.Items
{
    /// <summary>
    /// 몬스터 보상 (몬헌식).
    /// - 토벌 보상: 쓰러뜨리면 지급
    /// - 부위 파괴 보상: 해당 부위(Hurtbox의 Part Name)를 파괴하면 지급
    /// 보상은 바로 플레이어 가방에 들어간다.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class MonsterRewards : MonoBehaviour
    {
        [Serializable]
        public class Reward
        {
            public ItemData item;
            public int min = 1;
            public int max = 1;
            [Range(0f, 1f)] public float chance = 1f;
        }

        [Serializable]
        public class PartReward
        {
            [Tooltip("Hurtbox의 Part Name과 같아야 함 (예: Head, Tail)")]
            public string partName = "Head";
            public List<Reward> rewards = new();
        }

        [SerializeField] List<Reward> killRewards = new();
        [SerializeField] List<PartReward> partRewards = new();
        [Tooltip("쓰러뜨리면 'HUNT COMPLETE' 배너를 띄운다 (대형 몬스터용)")]
        [SerializeField] bool announceHunt = true;

        [Header("혼")]
        [Tooltip("쓰러뜨리면 얻는 혼")]
        [SerializeField] int souls;
        [Tooltip("일정 확률로 떨어지는 희귀 혼 (보스 전용, 핵심 노드 해금에 쓴다)")]
        [SerializeField] ItemData rareSoul;
        [SerializeField, Range(0f, 1f)] float rareSoulChance = 0.2f;
        [Tooltip("이 부위를 파괴한 상태로 쓰러뜨리면 희귀 혼 확률이 더해진다")]
        [SerializeField] string rareSoulBonusPart = "Head";
        [SerializeField, Range(0f, 1f)] float rareSoulPartBonus = 0.15f;

        readonly HashSet<string> brokenParts = new();

        public int Souls => souls;
        public ItemData RareSoul => rareSoul;

        Health health;
        PlayerInventory inventory;

        /// <summary>대형 몬스터(announceHunt)를 쓰러뜨렸을 때 (토벌 완료 배너 등)</summary>
        public static event Action<Health> Slain;

        void Awake()
        {
            health = GetComponent<Health>();
            health.Died += () =>
            {
                Give(killRewards);
                GiveSouls();
                if (announceHunt) Slain?.Invoke(health);
                brokenParts.Clear();
            };
            foreach (var hb in GetComponentsInChildren<Hurtbox>(true))
                hb.Broken += OnPartBroken;
        }

        void GiveSouls()
        {
            var skills = PlayerSkills.Instance;
            if (skills) skills.AddSouls(souls);
            if (!rareSoul || !inventory) return;
            float chance = rareSoulChance + PlayerSkills.Value(SkillStat.RareSoulChance);
            if (!string.IsNullOrEmpty(rareSoulBonusPart) && brokenParts.Contains(rareSoulBonusPart)) chance += rareSoulPartBonus;
            if (UnityEngine.Random.value < chance)
            {
                inventory.Add(rareSoul, 1);
                SoulsHunter.Core.GameEvents.Notify($"<color=#E8B8FF>희귀한 혼을 거두었어요: {rareSoul.DisplayName}</color>");
            }
        }

        void Start()
        {
            var player = GameObject.FindWithTag("Player");
            if (player) inventory = player.GetComponent<PlayerInventory>();
        }

        void OnPartBroken(Hurtbox part)
        {
            brokenParts.Add(part.PartName);
            foreach (var pr in partRewards)
                if (pr.partName == part.PartName) Give(pr.rewards);
        }

        void Give(List<Reward> rewards)
        {
            if (!inventory) return;
            foreach (var r in rewards)
            {
                if (!r.item || UnityEngine.Random.value > r.chance) continue;
                int amount = UnityEngine.Random.Range(r.min, Mathf.Max(r.min, r.max) + 1);
                if (UnityEngine.Random.value < PlayerSkills.Value(SkillStat.MaterialBonus)) amount++;
                inventory.Add(r.item, amount);
            }
        }

        // 몬스터가 다시 나타날 때(웨이브 재시작 등) 파괴 기록을 지운다
        void OnEnable() => brokenParts.Clear();

#if UNITY_EDITOR
        public void EditorSetupSouls(int soulAmount, ItemData rare, float rareChance)
        {
            souls = soulAmount;
            rareSoul = rare;
            rareSoulChance = rareChance;
        }

        public void EditorSetup(List<Reward> kill, List<PartReward> parts, bool announce = true)
        {
            killRewards = kill;
            partRewards = parts;
            announceHunt = announce;
        }
#endif
    }
}
