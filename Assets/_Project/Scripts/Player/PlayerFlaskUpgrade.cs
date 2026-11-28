using UnityEngine;

namespace SoulsHunter.Player
{
    /// <summary>
    /// 회복약 주머니 강화. 약재상에서 몬스터 소재로 단계를 올리면 회복약 최대 횟수가 늘어난다.
    /// 단계별 bonus는 늘어나는 횟수 (1 = +1회).
    /// </summary>
    [RequireComponent(typeof(PlayerFlask))]
    public class PlayerFlaskUpgrade : UpgradableEquipment
    {
        PlayerFlask flask;

        /// <summary>강화로 늘어난 회복약 횟수</summary>
        public int ExtraCharges => Mathf.RoundToInt(TotalBonus);

        public override string SlotName => "회복약";
        public override string StatSummary => $"회복약 {(flask ? flask.MaxCharges : 0)}회";

        void Awake()
        {
            flask = GetComponent<PlayerFlask>();
            // 강화하거나 불러오면 늘어난 횟수만큼 바로 채운다
            LevelChanged += _ => { if (flask) flask.Refill(); };
        }
    }
}
