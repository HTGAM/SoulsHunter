namespace SoulsHunter.Player
{
    /// <summary>
    /// 플레이어 무기. 강화할수록 PlayerCombat의 모든 공격 피해가 배율만큼 늘어난다.
    /// </summary>
    public class PlayerWeapon : UpgradableEquipment
    {
        /// <summary>현재 피해 배율 (1 = 기본)</summary>
        public float DamageMultiplier => 1f + TotalBonus;

        public override string SlotName => "무기";
        public override string StatSummary => $"공격력 x{DamageMultiplier:0.00}";
    }
}
