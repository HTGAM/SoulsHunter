using UnityEngine;
using SoulsHunter.Combat;
using SoulsHunter.Core;

namespace SoulsHunter.Player
{
    /// <summary>
    /// 무기마다 다른 특수 행동 (F / 게임패드 L2). 어떤 행동인지는 WeaponData.special이 정한다.
    /// - 귀인화(쌍검): 켜고 끈다. 켜져 있는 동안 스태미나가 계속 줄고, 공격이 빠르고 세진다. 스태미나가 바닥나면 꺼진다.
    /// - 도약(조충곤): 높이 뛰어오른다. 공중에서 약공격 = 공중 베기 (맞으면 다시 튀어올라 연속 공중 공격 가능).
    /// - 카운터(랜스): 잠깐 방패를 앞세운 자세. 그 사이 정면 공격을 받으면 피해 없이 막고 바로 반격 찌르기.
    /// </summary>
    [DefaultExecutionOrder(-45)] // PlayerCombat 다음, PlayerLocomotion 전
    [RequireComponent(typeof(PlayerCombat), typeof(PlayerLocomotion), typeof(PlayerStamina))]
    public class PlayerWeaponSpecial : MonoBehaviour
    {
        PlayerCombat combat;
        PlayerLocomotion locomotion;
        PlayerStamina stamina;
        PlayerInputHandler input;
        PlayerWeapon weapon;
        PlayerFlask flask;
        Health health;

        float counterUntil = float.NegativeInfinity;

        public bool DemonModeActive { get; private set; }
        public bool InCounterStance => Time.time < counterUntil;

        WeaponData Weapon => weapon ? weapon.Equipped : null;

        static readonly int DemonHash = Animator.StringToHash("DemonMode");
        static readonly int VaultHash = Animator.StringToHash("Vault");
        static readonly int CounterHash = Animator.StringToHash("Counter");

        /// <summary>HUD에 보여줄 상태 문구</summary>
        public string StatusText
        {
            get
            {
                var w = Weapon;
                if (!w) return "";
                if (DemonModeActive) return "<color=#FF6A5A><b>귀인화</b></color>  [F] 해제";
                if (InCounterStance) return "<color=#9FD8FF><b>카운터 자세</b></color>";
                return w.SpecialHint;
            }
        }

        void Awake()
        {
            combat = GetComponent<PlayerCombat>();
            locomotion = GetComponent<PlayerLocomotion>();
            stamina = GetComponent<PlayerStamina>();
            input = GetComponent<PlayerInputHandler>();
            weapon = GetComponent<PlayerWeapon>();
            flask = GetComponent<PlayerFlask>();
            health = GetComponent<Health>();
            if (weapon) weapon.WeaponChanged += _ => ResetSpecial();
        }

        void OnEnable() { if (health) health.Intercept += OnIncomingHit; }
        void OnDisable()
        {
            if (health) health.Intercept -= OnIncomingHit;
            ResetSpecial();
        }

        void Update()
        {
            var w = Weapon;
            if (!w || health.IsDead)
            {
                if (DemonModeActive) SetDemon(false);
                return;
            }

            switch (w.special)
            {
                case WeaponData.Special.DemonMode: UpdateDemon(w); break;
                case WeaponData.Special.Vault: UpdateVault(w); break;
                case WeaponData.Special.Counter: UpdateCounter(w); break;
            }
        }

        bool Busy => locomotion.IsStunned || locomotion.IsDodging || (flask && flask.IsDrinking);

        // ───────── 귀인화 (쌍검) ─────────

        void UpdateDemon(WeaponData w)
        {
            if (input.SpecialPressed && !locomotion.IsStunned)
            {
                if (DemonModeActive) SetDemon(false);
                else if (stamina.Current > 15f) SetDemon(true);
            }

            if (!DemonModeActive) return;
            stamina.Drain(w.demonStaminaPerSecond * Time.deltaTime);
            if (!stamina.CanUse) SetDemon(false);
        }

        void SetDemon(bool on)
        {
            DemonModeActive = on;
            var w = Weapon;
            combat.ExternalDamageMultiplier = on && w ? w.demonDamageMultiplier : 1f;
            combat.AttackSpeedMultiplier = on && w ? w.demonAttackSpeed : 1f;
            locomotion.Animator.TrySetBool(DemonHash, on);
            GameEvents.Notify(on ? "<color=#FF6A5A>귀인화!</color>  공격이 빨라지고 강해지지만 스태미나가 계속 줄어요."
                                 : "귀인화가 풀렸어요.");
        }

        // ───────── 도약 (조충곤) ─────────

        void UpdateVault(WeaponData w)
        {
            if (!input.SpecialPressed || Busy || combat.IsAttacking || !locomotion.IsGrounded) return;
            if (!stamina.TryConsume(w.vaultStaminaCost)) return;

            // 입력 방향(없으면 정면)으로 뛰어오른다
            Vector3 dir = locomotion.GetIntendedFacing();
            locomotion.FaceTowards(dir, 360f);
            locomotion.Launch(w.vaultUpVelocity, dir * w.vaultForwardSpeed);
            locomotion.Animator.TrySetTrigger(VaultHash);
        }

        // ───────── 카운터 (랜스) ─────────

        void UpdateCounter(WeaponData w)
        {
            if (InCounterStance)
            {
                locomotion.LimitSpeedThisFrame(0f); // 자세 중에는 제자리
                locomotion.FaceTowards(locomotion.GetIntendedFacing(), 360f * Time.deltaTime);
                return;
            }
            if (!input.SpecialPressed || Busy || combat.IsAttacking) return;
            if (!stamina.TryConsume(w.counterStaminaCost)) return;

            counterUntil = Time.time + w.counterWindow;
            locomotion.FaceTowards(locomotion.GetIntendedFacing(), 360f);
            locomotion.Animator.TrySetTrigger(CounterHash);
        }

        bool OnIncomingHit(ref DamageInfo info)
        {
            var w = Weapon;
            if (!w || w.special != WeaponData.Special.Counter || !InCounterStance) return false;

            Vector3 fromAttacker = -info.Direction;
            fromAttacker.y = 0f;
            if (fromAttacker.sqrMagnitude > 0.0001f && Vector3.Angle(transform.forward, fromAttacker) > w.guardAngle * 0.5f)
                return false;

            // 막고 반격
            counterUntil = float.NegativeInfinity;
            if (fromAttacker.sqrMagnitude > 0.0001f) locomotion.FaceTowards(fromAttacker, 360f);
            combat.StartSpecialAttack(w.specialAttack);
            GameEvents.Notify("<color=#9FD8FF>카운터!</color>");
            return true;
        }

        void ResetSpecial()
        {
            if (DemonModeActive) SetDemon(false);
            counterUntil = float.NegativeInfinity;
        }
    }
}
