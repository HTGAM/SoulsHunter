using System;
using UnityEngine;

namespace SoulsHunter.Combat
{
    /// <summary>
    /// 피격 판정 콜라이더에 붙인다. 몬스터 헌터식 부위(머리, 꼬리 등)마다 하나씩 두고
    /// 부위별 데미지 배율(육질)과 부위 파괴 수치를 따로 줄 수 있다.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class Hurtbox : MonoBehaviour
    {
        [SerializeField] Health health;
        [SerializeField] string partName = "Body";
        [Tooltip("이 부위에 들어가는 데미지 배율 (약점 > 1, 단단한 부위 < 1)")]
        [SerializeField] float damageMultiplier = 1f;

        [Header("Part Break (0이면 파괴 불가)")]
        [SerializeField] float breakThreshold = 0f;

        float partDamage;

        public Health Health => health;
        public string PartName => partName;
        public float DamageMultiplier => damageMultiplier;
        public bool IsBroken { get; private set; }

        /// <summary>부위 파괴 시 (부위 이름)</summary>
        public event Action<Hurtbox> Broken;

        void Awake()
        {
            if (!health) health = GetComponentInParent<Health>();
        }

        /// <summary>원래 데미지를 받아 배율을 적용하고 Health에 전달한다.</summary>
        public bool ReceiveHit(DamageInfo info)
        {
            if (!health) return false;
            info.Amount *= damageMultiplier;
            info.Part = partName;
            if (!health.TakeDamage(info)) return false;

            if (breakThreshold > 0f && !IsBroken)
            {
                partDamage += info.Amount;
                if (partDamage >= breakThreshold)
                {
                    IsBroken = true;
                    Broken?.Invoke(this);
                }
            }
            return true;
        }

        public void ResetPart()
        {
            partDamage = 0f;
            IsBroken = false;
        }
    }
}
