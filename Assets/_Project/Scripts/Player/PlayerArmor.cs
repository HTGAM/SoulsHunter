using UnityEngine;
using SoulsHunter.Combat;

namespace SoulsHunter.Player
{
    /// <summary>
    /// 플레이어 방어구. 강화할수록 받는 피해가 줄어든다 (bonus 0.1 = 10% 감소, 최대 80%).
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class PlayerArmor : UpgradableEquipment
    {
        const float MaxReduction = 0.8f;

        Health health;

        /// <summary>받는 피해 감소율 (0 ~ 0.8)</summary>
        public float Reduction => Mathf.Clamp(TotalBonus, 0f, MaxReduction);

        public override string SlotName => "방어구";
        public override string StatSummary => $"받는 피해 -{Reduction * 100f:0}%";

        void Awake() => health = GetComponent<Health>();

        void OnEnable() => health.IncomingDamageModifiers += Reduce;
        void OnDisable() => health.IncomingDamageModifiers -= Reduce;

        float Reduce(float amount) => amount * (1f - Reduction);
    }
}
