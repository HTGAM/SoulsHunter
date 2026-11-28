using System;
using UnityEngine;
using SoulsHunter.Combat;
using SoulsHunter.Core;

namespace SoulsHunter.Player
{
    /// <summary>
    /// 회복약 (에스투스 병 방식). 횟수 제한이 있고 화톳불 휴식/부활 때 다시 채워진다.
    /// 마시는 동안 천천히만 걸을 수 있고, 회피·공격이 불가능하다.
    /// 회복 시점 전에 경직되면 회복되지 않고 1회 소모된다 (소울류와 같음).
    /// </summary>
    [DefaultExecutionOrder(-50)] // PlayerLocomotion보다 먼저 속도 배율을 정한다
    [RequireComponent(typeof(PlayerLocomotion), typeof(Health), typeof(PlayerInputHandler))]
    public class PlayerFlask : MonoBehaviour
    {
        [SerializeField] int maxCharges = 4;
        [Tooltip("최대 체력 대비 회복량")]
        [SerializeField, Range(0f, 1f)] float healPercent = 0.45f;
        [Tooltip("마시는 동작 전체 시간(초)")]
        [SerializeField] float drinkDuration = 1.1f;
        [Tooltip("시작 후 이 시간에 실제로 회복된다")]
        [SerializeField] float healTime = 0.6f;
        [SerializeField, Range(0f, 1f)] float moveSpeedWhileDrinking = 0.3f;

        PlayerLocomotion locomotion;
        PlayerInputHandler input;
        PlayerCombat combat;
        PlayerFlaskUpgrade upgrade;
        Health health;
        float timer;
        bool healed;

        public int Charges { get; private set; }
        public int MaxCharges => maxCharges + (upgrade ? upgrade.ExtraCharges : 0);
        public bool IsDrinking { get; private set; }

        /// <summary>(남은 횟수, 최대)</summary>
        public event Action<int, int> ChargesChanged;

        static readonly int DrinkHash = Animator.StringToHash("Drink");

        void Awake()
        {
            locomotion = GetComponent<PlayerLocomotion>();
            input = GetComponent<PlayerInputHandler>();
            combat = GetComponent<PlayerCombat>();
            upgrade = GetComponent<PlayerFlaskUpgrade>();
            health = GetComponent<Health>();
            Charges = MaxCharges;

            var damage = GetComponent<PlayerDamageHandler>();
            if (damage) damage.Respawned += Refill;
        }

        void Update()
        {
            if (IsDrinking) TickDrink();
            else if (input.UseItemPressed) TryDrink();

            locomotion.SpeedMultiplier = IsDrinking ? moveSpeedWhileDrinking : 1f;
            locomotion.DodgeBlocked = IsDrinking;
        }

        void TryDrink()
        {
            if (Charges <= 0 || health.IsDead) return;
            if (locomotion.IsDodging || locomotion.IsStunned) return;
            if (combat && combat.IsAttacking) return;

            Charges--;
            ChargesChanged?.Invoke(Charges, MaxCharges);
            IsDrinking = true;
            healed = false;
            timer = 0f;
            locomotion.Animator.TrySetTrigger(DrinkHash);
        }

        void TickDrink()
        {
            // 맞아서 경직되면 마시기 중단
            if (locomotion.IsStunned || health.IsDead)
            {
                IsDrinking = false;
                return;
            }

            timer += Time.deltaTime;
            if (!healed && timer >= healTime)
            {
                healed = true;
                health.Heal(health.Max * healPercent);
            }
            if (timer >= drinkDuration) IsDrinking = false;
        }

        public void Refill()
        {
            Charges = MaxCharges;
            IsDrinking = false;
            ChargesChanged?.Invoke(Charges, MaxCharges);
        }
    }
}
