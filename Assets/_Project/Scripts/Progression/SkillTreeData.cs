using System;
using System.Collections.Generic;
using UnityEngine;
using SoulsHunter.Items;

namespace SoulsHunter.Progression
{
    /// <summary>스킬 노드가 올려 주는 능력치 종류</summary>
    public enum SkillStat
    {
        AttackPower,        // 공격력 +%
        WeakPointDamage,    // 약점 부위 피해 +%
        PartBreak,          // 부위 파괴 누적 +%
        DamageTaken,        // 받는 피해 +% (음수면 감소)
        MaxHealth,          // 최대 체력 +
        GuardStamina,       // 가드 스태미나 소모 -%
        MaxStamina,         // 최대 스태미나 +
        StaminaRegen,       // 스태미나 회복 속도 +%
        DodgeInvulnerability, // 구르기 무적 시간 +초
        FlaskHeal,          // 회복약 회복량 +(최대 체력 비율)
        SoulGain,           // 얻는 혼 +%
        MaterialBonus,      // 소재를 하나 더 얻을 확률
        RareSoulChance,     // 희귀 혼 확률 +
        ExtraFaints,        // 퀘스트 허용 쓰러짐 +회
    }

    /// <summary>트리의 갈래 (색)</summary>
    public enum SkillBranch { Root, Offense, Defense, Survival, Hunt }

    [Serializable]
    public class SkillEffect
    {
        public SkillStat stat;
        public float value;

        public SkillEffect() { }
        public SkillEffect(SkillStat stat, float value) { this.stat = stat; this.value = value; }

        /// <summary>"공격력 +5%" 같은 설명</summary>
        public string Describe()
        {
            string Pct(float v) => $"{(v >= 0f ? "+" : "")}{v * 100f:0.#}%";
            return stat switch
            {
                SkillStat.AttackPower => $"공격력 {Pct(value)}",
                SkillStat.WeakPointDamage => $"약점 부위 피해 {Pct(value)}",
                SkillStat.PartBreak => $"부위 파괴 위력 {Pct(value)}",
                SkillStat.DamageTaken => value <= 0f ? $"받는 피해 -{-value * 100f:0.#}%" : $"받는 피해 +{value * 100f:0.#}%",
                SkillStat.MaxHealth => $"최대 체력 +{value:0}",
                SkillStat.GuardStamina => $"가드 스태미나 소모 -{value * 100f:0.#}%",
                SkillStat.MaxStamina => $"최대 스태미나 +{value:0}",
                SkillStat.StaminaRegen => $"스태미나 회복 속도 {Pct(value)}",
                SkillStat.DodgeInvulnerability => $"구르기 무적 시간 +{value:0.00}초",
                SkillStat.FlaskHeal => $"회복약 회복량 +최대 체력의 {value * 100f:0.#}%",
                SkillStat.SoulGain => $"얻는 혼 {Pct(value)}",
                SkillStat.MaterialBonus => $"소재를 하나 더 얻을 확률 {value * 100f:0.#}%",
                SkillStat.RareSoulChance => $"희귀 혼 확률 +{value * 100f:0.#}%p",
                SkillStat.ExtraFaints => $"퀘스트에서 쓰러져도 되는 횟수 +{value:0}",
                _ => stat.ToString(),
            };
        }
    }

    [Serializable]
    public class SkillNode
    {
        [Tooltip("저장용 ID (바꾸면 해금 기록이 사라진다)")]
        public string id;
        public string title;
        [Tooltip("노드 위에 보이는 짧은 글자")]
        public string glyph;
        public SkillBranch branch;
        [Tooltip("트리 안 위치 (칸 단위, 가운데가 0,0)")]
        public Vector2 position;
        [Tooltip("큰 노드 (희귀 혼이 필요한 핵심 노드)")]
        public bool keystone;
        public List<SkillEffect> effects = new();
        [Tooltip("연결된 노드 ID. 연결된 노드 하나라도 해금돼 있어야 찍을 수 있다")]
        public List<string> links = new();
        [Tooltip("핵심 노드에 필요한 희귀 혼")]
        public ItemData rareCost;
        public int rareAmount = 1;

        public string DescribeEffects()
        {
            var parts = new List<string>();
            foreach (var e in effects) parts.Add(e.Describe());
            return string.Join("\n", parts);
        }
    }

    /// <summary>
    /// 혼의 스킬 트리. 노드 목록과 비용 규칙.
    /// 노드 비용(혼)은 지금까지 찍은 노드 수에 따라 오른다 (어느 갈래를 찍든 같다).
    /// </summary>
    [CreateAssetMenu(menuName = "SoulsHunter/Skill Tree", fileName = "SkillTree")]
    public class SkillTreeData : ScriptableObject
    {
        [Tooltip("첫 노드의 혼 비용")]
        public int baseCost = 100;
        [Tooltip("노드를 하나 찍을 때마다 다음 비용이 이 배율만큼 오른다")]
        public float costGrowth = 1.10f;
        public List<SkillNode> nodes = new();

        /// <summary>노드를 count개 찍은 상태에서 다음 노드의 혼 비용</summary>
        public int CostAt(int count) =>
            Mathf.RoundToInt(baseCost * Mathf.Pow(costGrowth, Mathf.Max(0, count)) / 10f) * 10;

        /// <summary>노드 count개를 찍는 데 든 혼 합계 (초기화 환급용)</summary>
        public int TotalCost(int count)
        {
            int sum = 0;
            for (int i = 0; i < count; i++) sum += CostAt(i);
            return sum;
        }

        public SkillNode Find(string id) => nodes.Find(n => n.id == id);
    }
}
