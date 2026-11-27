using System;
using System.Collections.Generic;
using UnityEngine;

namespace SoulsHunter.Player
{
    /// <summary>
    /// 플레이어 무기.
    /// - 무기 종류(WeaponData): 안전구역의 무기 거치대에서 바꾼다. 공격 동작·특수 행동·겉모습이 바뀐다.
    /// - 강화 단계: 대장간에서 올린다. 모든 무기 종류에 똑같이 적용된다 (피해 배율).
    /// </summary>
    public class PlayerWeapon : UpgradableEquipment
    {
        [Header("Weapon Types")]
        [Tooltip("고를 수 있는 무기 종류 (거치대에서 이 순서로 돌아간다)")]
        [SerializeField] List<WeaponData> available = new();
        [SerializeField] WeaponData equipped;

        public IReadOnlyList<WeaponData> Available => available;
        public WeaponData Equipped => equipped;

        /// <summary>(새 무기)</summary>
        public event Action<WeaponData> WeaponChanged;

        /// <summary>현재 피해 배율 (1 = 기본)</summary>
        public float DamageMultiplier => 1f + TotalBonus;

        public override string SlotName => "무기";
        public override string StatSummary => $"공격력 x{DamageMultiplier:0.00}";
        public override string DisplayName
        {
            get
            {
                if (!equipped) return base.DisplayName;
                return Level > 0 ? $"{equipped.DisplayName} +{Level}" : equipped.DisplayName;
            }
        }

        void Awake()
        {
            if (!equipped && available.Count > 0) equipped = available[0];
        }

        void Start() => WeaponChanged?.Invoke(equipped); // 모델·이동 성능 초기화

        public void Equip(WeaponData weapon)
        {
            if (!weapon || weapon == equipped) return;
            equipped = weapon;
            WeaponChanged?.Invoke(weapon);
        }

        /// <summary>목록에서 다음 무기 (거치대 안내용)</summary>
        public WeaponData Next
        {
            get
            {
                if (available.Count == 0) return null;
                int i = available.IndexOf(equipped);
                return available[(i + 1) % available.Count];
            }
        }

        /// <summary>저장 데이터의 이름(에셋 이름)으로 장착</summary>
        public bool EquipByName(string assetName)
        {
            foreach (var w in available)
                if (w && w.name == assetName) { Equip(w); return true; }
            return false;
        }

#if UNITY_EDITOR
        public void EditorSetupWeapons(List<WeaponData> list, WeaponData start)
        {
            available = list;
            equipped = start;
        }
#endif
    }
}
