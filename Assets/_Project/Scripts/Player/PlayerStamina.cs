using System;
using UnityEngine;
using SoulsHunter.Progression;

namespace SoulsHunter.Player
{
    /// <summary>
    /// 스태미나. 소울 시리즈처럼 1이라도 남아 있으면 행동할 수 있고,
    /// 마지막으로 사용한 뒤 잠깐 기다렸다가 회복된다.
    /// </summary>
    public class PlayerStamina : MonoBehaviour
    {
        [SerializeField] float maxStamina = 100f;
        [SerializeField] float regenPerSecond = 40f;
        [Tooltip("마지막 사용 후 회복이 시작되기까지의 시간(초)")]
        [SerializeField] float regenDelay = 0.8f;

        float lastUseTime = float.NegativeInfinity;
        float maxBonus;

        /// <summary>최대 스태미나 추가치 (스킬 등)</summary>
        public void SetMaxBonus(float bonus)
        {
            maxBonus = Mathf.Max(0f, bonus);
            Changed?.Invoke(Current, Max);
        }

        public float Current { get; private set; }
        public float Max => maxStamina + maxBonus;
        public float Normalized => Max > 0f ? Current / Max : 0f;
        public bool CanUse => Current > 0f;

        /// <summary>(현재값, 최대값)</summary>
        public event Action<float, float> Changed;

        void Awake() => Current = Max;

        void Update()
        {
            if (Current >= Max || Time.time - lastUseTime < regenDelay) return;
            Set(Current + regenPerSecond * (1f + PlayerSkills.Value(SkillStat.StaminaRegen)) * Time.deltaTime);
        }

        /// <summary>한 번에 소모하는 행동(구르기, 공격). 스태미나가 0이면 실패한다.</summary>
        public bool TryConsume(float amount)
        {
            if (!CanUse) return false;
            lastUseTime = Time.time;
            Set(Current - amount);
            return true;
        }

        /// <summary>지속 소모(달리기, 가드 유지). 매 프레임 호출한다.</summary>
        public void Drain(float amount)
        {
            lastUseTime = Time.time;
            Set(Current - amount);
        }

        public void Refill() => Set(Max);

        void Set(float value)
        {
            float clamped = Mathf.Clamp(value, 0f, Max);
            if (Mathf.Approximately(clamped, Current)) return;
            Current = clamped;
            Changed?.Invoke(Current, Max);
        }
    }
}
