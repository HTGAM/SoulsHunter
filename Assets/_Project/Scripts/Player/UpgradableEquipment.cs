using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Serialization;
using SoulsHunter.Items;

namespace SoulsHunter.Player
{
    /// <summary>
    /// 대장간에서 몬스터 소재로 단계를 올리는 장비의 공통 부분 (무기, 방어구).
    /// 단계마다 bonus가 누적되고, 실제 효과는 자식 클래스가 정한다.
    /// </summary>
    public abstract class UpgradableEquipment : MonoBehaviour
    {
        [Serializable]
        public class Cost
        {
            public ItemData item;
            public int amount = 1;
        }

        [Serializable]
        public class UpgradeStep
        {
            public List<Cost> costs = new();
            [Tooltip("이 단계로 올라가면 늘어나는 효과 (0.15 = 15%)")]
            [FormerlySerializedAs("damageBonus")]
            public float bonus = 0.15f;
        }

        [FormerlySerializedAs("weaponName")]
        [SerializeField] string equipmentName = "Equipment";
        [Tooltip("순서대로 +1, +2, … 강화에 필요한 재료")]
        [SerializeField] List<UpgradeStep> upgrades = new();

        public int Level { get; private set; }
        public int MaxLevel => upgrades.Count;
        public bool IsMaxed => Level >= MaxLevel;
        public UpgradeStep NextStep => IsMaxed ? null : upgrades[Level];
        public string DisplayName => Level > 0 ? $"{equipmentName} +{Level}" : equipmentName;

        /// <summary>지금까지 올린 단계의 bonus 합</summary>
        public float TotalBonus
        {
            get
            {
                float sum = 0f;
                for (int i = 0; i < Level && i < upgrades.Count; i++) sum += upgrades[i].bonus;
                return sum;
            }
        }

        /// <summary>장비 종류 이름 (예: "무기", "방어구")</summary>
        public abstract string SlotName { get; }

        /// <summary>현재 효과 요약 (예: "공격력 x1.30")</summary>
        public abstract string StatSummary { get; }

        /// <summary>(새 단계)</summary>
        public event Action<int> LevelChanged;

        public bool CanUpgrade(PlayerInventory inventory)
        {
            var step = NextStep;
            if (step == null || !inventory) return false;
            foreach (var c in step.costs)
                if (c.item && inventory.Count(c.item) < c.amount) return false;
            return true;
        }

        public bool TryUpgrade(PlayerInventory inventory)
        {
            if (!CanUpgrade(inventory)) return false;
            foreach (var c in NextStep.costs)
                if (c.item) inventory.Remove(c.item, c.amount);
            SetLevel(Level + 1);
            return true;
        }

        /// <summary>불러오기 등에서 단계를 직접 정한다.</summary>
        public void SetLevel(int level)
        {
            level = Mathf.Clamp(level, 0, MaxLevel);
            if (level == Level) return;
            Level = level;
            LevelChanged?.Invoke(Level);
        }

        /// <summary>다음 강화 재료를 "철갑 비늘 1/3, …" 형식으로. 모자란 재료는 빨간색.</summary>
        public string DescribeNextCost(PlayerInventory inventory)
        {
            var step = NextStep;
            if (step == null) return "";
            var sb = new StringBuilder();
            foreach (var c in step.costs)
            {
                if (!c.item) continue;
                if (sb.Length > 0) sb.Append(", ");
                int have = inventory ? inventory.Count(c.item) : 0;
                string color = have >= c.amount ? "DDDDDD" : "FF6A5A";
                sb.Append($"<color=#{color}>{c.item.DisplayName} {have}/{c.amount}</color>");
            }
            return sb.ToString();
        }

#if UNITY_EDITOR
        public void EditorSetup(string name, List<UpgradeStep> steps)
        {
            equipmentName = name;
            upgrades = steps;
        }
#endif
    }
}
