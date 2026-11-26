using System;
using UnityEngine;

namespace SoulsHunter.Combat
{
    public struct DamageInfo
    {
        public float Amount;
        /// <summary>강인도 피해. 누적되어 강인도가 깨지면 경직(Stagger)된다.</summary>
        public float PoiseDamage;
        public Vector3 Point;
        public Vector3 Direction;
        public GameObject Source;
        /// <summary>맞은 부위 이름 (Hurtbox.PartName)</summary>
        public string Part;
    }

    /// <summary>
    /// 체력 + 강인도. 플레이어, 적, 몬스터 공용.
    /// </summary>
    public class Health : MonoBehaviour
    {
        [SerializeField] string displayName = "";
        [SerializeField] float maxHealth = 100f;

        [Header("Poise (강인도)")]
        [Tooltip("0이면 경직되지 않음")]
        [SerializeField] float maxPoise = 30f;
        [Tooltip("마지막 피격 후 이 시간이 지나면 강인도가 회복된다")]
        [SerializeField] float poiseResetDelay = 3f;

        float poise;
        float lastHitTime = float.NegativeInfinity;

        public string DisplayName => string.IsNullOrEmpty(displayName) ? gameObject.name : displayName;
        public float Current { get; private set; }
        public float Max => maxHealth;
        public float Normalized => maxHealth > 0f ? Current / maxHealth : 0f;
        public bool IsDead => Current <= 0f;

        /// <summary>true를 반환하면 피해를 무시한다 (구르기 무적 등).</summary>
        public Func<bool> InvulnerabilityCheck;

        public event Action<DamageInfo> Damaged;
        public event Action<DamageInfo> Staggered;
        public event Action Died;
        /// <summary>(현재값, 최대값)</summary>
        public event Action<float, float> Changed;

        void Awake() => ResetHealth();

        void Update()
        {
            if (poise < maxPoise && Time.time - lastHitTime > poiseResetDelay) poise = maxPoise;
        }

        /// <returns>실제로 피해가 들어갔으면 true</returns>
        public bool TakeDamage(DamageInfo info)
        {
            if (IsDead) return false;
            if (InvulnerabilityCheck != null && InvulnerabilityCheck()) return false;

            lastHitTime = Time.time;
            Current = Mathf.Max(0f, Current - info.Amount);
            Changed?.Invoke(Current, maxHealth);
            Damaged?.Invoke(info);

            if (maxPoise > 0f && info.PoiseDamage > 0f)
            {
                poise -= info.PoiseDamage;
                if (poise <= 0f)
                {
                    poise = maxPoise;
                    Staggered?.Invoke(info);
                }
            }

            if (IsDead) Died?.Invoke();
            return true;
        }

        public void Heal(float amount)
        {
            if (IsDead) return;
            Current = Mathf.Min(maxHealth, Current + amount);
            Changed?.Invoke(Current, maxHealth);
        }

        public void ResetHealth()
        {
            Current = maxHealth;
            poise = maxPoise;
            Changed?.Invoke(Current, maxHealth);
        }
    }
}
