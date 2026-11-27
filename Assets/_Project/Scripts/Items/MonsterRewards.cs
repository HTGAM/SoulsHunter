using System;
using System.Collections.Generic;
using UnityEngine;
using SoulsHunter.Combat;

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

        Health health;
        PlayerInventory inventory;

        /// <summary>보상이 있는 몬스터를 쓰러뜨렸을 때 (토벌 완료 배너 등)</summary>
        public static event Action<Health> Slain;

        void Awake()
        {
            health = GetComponent<Health>();
            health.Died += () =>
            {
                Give(killRewards);
                Slain?.Invoke(health);
            };
            foreach (var hb in GetComponentsInChildren<Hurtbox>())
                hb.Broken += OnPartBroken;
        }

        void Start()
        {
            var player = GameObject.FindWithTag("Player");
            if (player) inventory = player.GetComponent<PlayerInventory>();
        }

        void OnPartBroken(Hurtbox part)
        {
            foreach (var pr in partRewards)
                if (pr.partName == part.PartName) Give(pr.rewards);
        }

        void Give(List<Reward> rewards)
        {
            if (!inventory) return;
            foreach (var r in rewards)
            {
                if (!r.item || UnityEngine.Random.value > r.chance) continue;
                inventory.Add(r.item, UnityEngine.Random.Range(r.min, Mathf.Max(r.min, r.max) + 1));
            }
        }

#if UNITY_EDITOR
        public void EditorSetup(List<Reward> kill, List<PartReward> parts)
        {
            killRewards = kill;
            partRewards = parts;
        }
#endif
    }
}
