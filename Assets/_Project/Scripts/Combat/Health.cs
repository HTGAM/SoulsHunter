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
        /// <summary>부위 데미지 배율 (약점 > 1, 단단한 부위 < 1). Hurtbox를 거치지 않았으면 0.</summary>
        public float PartMultiplier;
        /// <summary>부위 파괴 누적치 배율 (0이면 1로 취급). 체력 피해에는 영향 없음.</summary>
        public float BreakMultiplier;
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
        float maxBonus;

        public string DisplayName => string.IsNullOrEmpty(displayName) ? gameObject.name : displayName;
        public float Current { get; private set; }
        public float Max => maxHealth + maxBonus;
        public float Normalized => Max > 0f ? Current / Max : 0f;
        public bool IsDead => Current <= 0f;

        /// <summary>true를 반환하면 피해를 무시한다 (구르기 무적 등).</summary>
        public Func<bool> InvulnerabilityCheck;

        /// <summary>
        /// 피격 직전에 끼어든다 (가드, 패링 등). info를 고칠 수 있다.
        /// true를 반환하면 공격을 완전히 막은 것: 피해·이벤트 없이 '맞은 것'으로 처리된다
        /// (TakeDamage가 true를 반환하므로 공격자는 같은 공격으로 다시 때리지 않는다).
        /// </summary>
        public delegate bool HitInterceptor(ref DamageInfo info);

        readonly System.Collections.Generic.List<HitInterceptor> interceptors = new();

        public event HitInterceptor Intercept
        {
            add => interceptors.Add(value);
            remove => interceptors.Remove(value);
        }

        readonly System.Collections.Generic.List<Func<float, float>> damageModifiers = new();

        /// <summary>받는 피해량을 바꾸는 함수 (방어구 등). 등록된 순서대로 모두 적용된다.</summary>
        public event Func<float, float> IncomingDamageModifiers
        {
            add => damageModifiers.Add(value);
            remove => damageModifiers.Remove(value);
        }

        public event Action<DamageInfo> Damaged;

        /// <summary>어떤 Health든 피해를 입었을 때 (데미지 숫자 표시 등)</summary>
        public static event Action<Health, DamageInfo> AnyDamaged;
        /// <summary>어떤 대상이든 강인도가 깨졌을 때 (이펙트·소리용)</summary>
        public static event Action<Health, DamageInfo> AnyStaggered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => AnyDamaged = null;
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

            foreach (var intercept in interceptors)
                if (intercept(ref info)) return true;
            foreach (var modify in damageModifiers) info.Amount = Mathf.Max(0f, modify(info.Amount));

            lastHitTime = Time.time;
            Current = Mathf.Max(0f, Current - info.Amount);
            Changed?.Invoke(Current, Max);
            Damaged?.Invoke(info);
            AnyDamaged?.Invoke(this, info);

            if (maxPoise > 0f && info.PoiseDamage > 0f)
            {
                poise -= info.PoiseDamage;
                if (poise <= 0f)
                {
                    poise = maxPoise;
                    Staggered?.Invoke(info);
                    AnyStaggered?.Invoke(this, info);
                }
            }

            if (IsDead) Died?.Invoke();
            return true;
        }

        public void Heal(float amount)
        {
            if (IsDead) return;
            Current = Mathf.Min(Max, Current + amount);
            Changed?.Invoke(Current, Max);
        }

        /// <summary>최대 체력 추가치 (스킬 등). 늘어난 만큼 현재 체력도 같이 오른다.</summary>
        public void SetMaxBonus(float bonus)
        {
            bonus = Mathf.Max(0f, bonus);
            if (Mathf.Approximately(bonus, maxBonus)) return;
            float delta = bonus - maxBonus;
            maxBonus = bonus;
            if (!IsDead) Current = Mathf.Clamp(Current + Mathf.Max(0f, delta), 0f, Max);
            Changed?.Invoke(Current, Max);
        }

        public void ResetHealth()
        {
            Current = Max;
            poise = maxPoise;
            Changed?.Invoke(Current, Max);
        }
    }
}
