using System;
using UnityEngine;
using SoulsHunter.Combat;
using SoulsHunter.Core;

namespace SoulsHunter.Player
{
    /// <summary>
    /// 가드와 패링.
    /// - 가드(Ctrl / L1 누르고 있기): 정면 공격의 피해를 크게 줄이고 대신 스태미나를 쓴다.
    ///   스태미나가 모자라면 가드가 깨져(가드 브레이크) 경직되고 피해를 거의 다 받는다.
    /// - 패링: 가드를 누른 직후 짧은 순간에 맞으면 피해 없이 튕겨내고, 공격자의 강인도를 크게 깎는다.
    /// 가드 중에는 천천히 걷고, 공격하거나 구르면 가드가 풀린다.
    /// </summary>
    [DefaultExecutionOrder(-40)] // PlayerLocomotion보다 먼저 이동 속도 제한을 건다
    [RequireComponent(typeof(Health), typeof(PlayerLocomotion), typeof(PlayerStamina))]
    public class PlayerGuard : MonoBehaviour
    {
        [Header("Guard")]
        [Tooltip("막은 공격의 피해 감소율 (0.85 = 15%만 받음)")]
        [SerializeField, Range(0f, 1f)] float damageReduction = 0.85f;
        [Tooltip("막은 공격의 강인도 피해 비율")]
        [SerializeField, Range(0f, 1f)] float poiseTaken = 0.3f;
        [Tooltip("막을 때 원래 피해 1당 소모하는 스태미나")]
        [SerializeField] float staminaPerDamage = 0.9f;
        [SerializeField] float minStaminaCost = 8f;
        [Tooltip("정면 기준 막을 수 있는 각도")]
        [SerializeField] float guardAngle = 150f;
        [SerializeField, Range(0f, 1f)] float moveSpeedWhileGuarding = 0.45f;

        [Header("Guard Break")]
        [SerializeField] float guardBreakStun = 1.1f;

        [Header("Parry")]
        [Tooltip("가드를 누른 뒤 이 시간(초) 안에 맞으면 패링")]
        [SerializeField] float parryWindow = 0.18f;
        [Tooltip("패링 후 다시 패링 판정이 생기려면 가드를 떼었다 눌러야 한다. 이 시간 동안은 연타 패링 불가")]
        [SerializeField] float parryCooldown = 0.5f;
        [Tooltip("패링 시 공격자에게 주는 강인도 피해")]
        [SerializeField] float parryPoiseDamage = 70f;

        [Header("Visual (선택)")]
        [Tooltip("가드 중 들어 올릴 방패 모델")]
        [SerializeField] Transform shield;
        [SerializeField] Vector3 shieldRaisedOffset = new(0.15f, 0.25f, 0.25f);

        Health health;
        PlayerLocomotion locomotion;
        PlayerStamina stamina;
        PlayerInputHandler input;
        PlayerCombat combat;
        PlayerFlask flask;
        PlayerWeapon weapon;

        float guardStartTime = float.NegativeInfinity;
        float lastParryTime = float.NegativeInfinity;
        Vector3 shieldRestPos;
        float shieldBlend;

        public bool IsGuarding { get; private set; }
        public bool InParryWindow => IsGuarding && Time.time - guardStartTime <= parryWindow
                                                && Time.time - lastParryTime >= parryCooldown;

        /// <summary>(막은 공격)</summary>
        public event Action<DamageInfo> Blocked;
        public event Action<DamageInfo> Parried;
        public event Action GuardBroken;

        static readonly int GuardHash = Animator.StringToHash("Guard");
        static readonly int ParryHash = Animator.StringToHash("Parry");
        static readonly int GuardBreakHash = Animator.StringToHash("GuardBreak");

        void Awake()
        {
            health = GetComponent<Health>();
            locomotion = GetComponent<PlayerLocomotion>();
            stamina = GetComponent<PlayerStamina>();
            input = GetComponent<PlayerInputHandler>();
            combat = GetComponent<PlayerCombat>();
            flask = GetComponent<PlayerFlask>();
            weapon = GetComponent<PlayerWeapon>();
            if (shield) shieldRestPos = shield.localPosition;
        }

        WeaponData Weapon => weapon ? weapon.Equipped : null;

        /// <summary>지금 무기로 가드할 수 있는지 (쌍검·해머·조충곤은 불가)</summary>
        public bool WeaponCanGuard => !Weapon || Weapon.canGuard;

        float Reduction => Weapon ? Weapon.guardReduction : damageReduction;
        float StaminaPerDamage => staminaPerDamage * (Weapon ? Weapon.guardStaminaMultiplier : 1f);
        float Angle => Weapon ? Weapon.guardAngle : guardAngle;

        /// <summary>가드 때 들어 올릴 방패 (무기 모델이 바뀔 때 WeaponModel이 정해준다)</summary>
        public void SetShield(Transform t)
        {
            shield = t;
            shieldBlend = 0f;
            if (shield) shieldRestPos = shield.localPosition;
        }

        void OnEnable() => health.Intercept += OnIncomingHit;
        void OnDisable()
        {
            health.Intercept -= OnIncomingHit;
            IsGuarding = false;
        }

        void Update()
        {
            bool canGuard = WeaponCanGuard && input && input.GuardHeld && !health.IsDead && !locomotion.IsStunned && !locomotion.IsDodging &&
                            !(combat && combat.IsAttacking) && !(flask && flask.IsDrinking);

            if (canGuard && !IsGuarding)
            {
                IsGuarding = true;
                // 버튼을 새로 눌렀을 때만 패링 판정 (공격 후 누르고 있던 채로 가드가 다시 켜지면 패링 없음)
                guardStartTime = input.GuardPressed ? Time.time : float.NegativeInfinity;
            }
            else if (!canGuard) IsGuarding = false;
            else if (input.GuardPressed) guardStartTime = Time.time;

            if (IsGuarding) locomotion.LimitSpeedThisFrame(moveSpeedWhileGuarding);
            locomotion.Animator.TrySetBool(GuardHash, IsGuarding);

            UpdateShield();
        }

        void UpdateShield()
        {
            if (!shield) return;
            shieldBlend = Mathf.MoveTowards(shieldBlend, IsGuarding ? 1f : 0f, Time.deltaTime * 10f);
            shield.localPosition = shieldRestPos + shieldRaisedOffset * shieldBlend;
        }

        bool OnIncomingHit(ref DamageInfo info)
        {
            if (!IsGuarding) return false;

            // info.Direction은 공격자 → 플레이어 방향. 정면에서 온 공격만 막는다.
            Vector3 fromAttacker = -info.Direction;
            fromAttacker.y = 0f;
            if (fromAttacker.sqrMagnitude > 0.0001f && Vector3.Angle(transform.forward, fromAttacker) > Angle * 0.5f)
                return false;

            if (InParryWindow)
            {
                Parry(info);
                return true; // 피해 없음
            }

            float cost = Mathf.Max(minStaminaCost * (Weapon ? Weapon.guardStaminaMultiplier : 1f), info.Amount * StaminaPerDamage);
            if (stamina.Current >= cost)
            {
                stamina.Drain(cost);
                info.Amount *= 1f - Reduction;
                info.PoiseDamage *= poiseTaken;
                Blocked?.Invoke(info);
                return false; // 줄어든 피해는 그대로 받는다 (넉백 포함)
            }

            // 가드 브레이크: 남은 스태미나만큼만 막고 경직
            float blockedPortion = cost > 0f ? Mathf.Clamp01(stamina.Current / cost) * 0.5f : 0f;
            stamina.Drain(stamina.Current);
            info.Amount *= 1f - Reduction * blockedPortion;
            IsGuarding = false;
            locomotion.Stun(guardBreakStun);
            locomotion.Animator.TrySetTrigger(GuardBreakHash);
            GameEvents.Notify("<color=#FF6A5A>가드 브레이크!</color>");
            GuardBroken?.Invoke();
            return false;
        }

        void Parry(DamageInfo info)
        {
            lastParryTime = Time.time;
            guardStartTime = float.NegativeInfinity;
            locomotion.Animator.TrySetTrigger(ParryHash);

            // 공격자의 강인도를 크게 깎는다 → 경직되면 반격 기회
            if (info.Source && info.Source.TryGetComponent(out Health attacker) && attacker != health)
            {
                attacker.TakeDamage(new DamageInfo
                {
                    Amount = 0f,
                    PoiseDamage = parryPoiseDamage,
                    Point = info.Point,
                    Direction = -info.Direction,
                    Source = gameObject,
                    Part = "Parry",
                });
            }
            GameEvents.Notify("<color=#9FD8FF>패링!</color>");
            Parried?.Invoke(info);
        }
    }
}
