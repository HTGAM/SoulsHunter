using System;
using System.Collections.Generic;
using UnityEngine;

namespace SoulsHunter.Player
{
    /// <summary>
    /// 무기 종류 하나 (한손검, 대검, 쌍검, 해머, 랜스, 조충곤 …).
    /// 공격 동작, 이동·회피·가드 성능, 특수 행동, 겉모습을 모두 여기서 정한다.
    /// Project 창에서 우클릭 → Create → SoulsHunter → Weapon 으로 만들 수도 있다.
    /// </summary>
    [CreateAssetMenu(menuName = "SoulsHunter/Weapon", fileName = "New Weapon")]
    public class WeaponData : ScriptableObject
    {
        public enum Special
        {
            None,
            /// <summary>쌍검: 켜면 스태미나를 계속 쓰는 대신 공격이 빠르고 세진다</summary>
            DemonMode,
            /// <summary>조충곤: 높이 도약. 공중에서 약공격 = 공중 베기 (맞으면 다시 튀어오름)</summary>
            Vault,
            /// <summary>랜스: 잠깐 카운터 자세. 그 사이 정면 공격을 받으면 피해 없이 막고 반격 찌르기</summary>
            Counter,
        }

        [Serializable]
        public class VisualPart
        {
            public PrimitiveType shape = PrimitiveType.Cube;
            public Vector3 position;
            public Vector3 rotation;
            public Vector3 scale = Vector3.one;
            public Color color = Color.gray;
            [Tooltip("가드 때 들어 올리는 방패")]
            public bool isShield;
        }

        [SerializeField] string displayName = "Weapon";
        [SerializeField, TextArea] string description = "";

        [Header("Attacks")]
        public List<AttackStep> lightCombo = new();
        public List<AttackStep> heavyCombo = new();

        [Header("Movement")]
        [Range(0.5f, 1.3f)] public float moveSpeedMultiplier = 1f;
        [Tooltip("구르기 거리 배율 (랜스처럼 짧은 스텝이면 작게)")]
        [Range(0.3f, 1.3f)] public float dodgeDistanceMultiplier = 1f;

        [Header("Guard")]
        public bool canGuard = true;
        [Tooltip("막은 공격의 피해 감소율")]
        [Range(0f, 1f)] public float guardReduction = 0.85f;
        [Tooltip("막을 때 스태미나 소모 배율 (작을수록 단단함)")]
        public float guardStaminaMultiplier = 1f;
        [Tooltip("정면 기준 막을 수 있는 각도")]
        public float guardAngle = 150f;

        [Header("Special (F / 게임패드 L2)")]
        public Special special = Special.None;
        [Tooltip("특수 행동에 쓰는 공격 (랜스: 반격 찌르기, 조충곤: 공중 베기)")]
        public AttackStep specialAttack = new();

        [Header("Special: Demon Mode (쌍검)")]
        public float demonDamageMultiplier = 1.25f;
        public float demonAttackSpeed = 1.3f;
        public float demonStaminaPerSecond = 12f;

        [Header("Special: Vault (조충곤)")]
        public float vaultUpVelocity = 11f;
        public float vaultForwardSpeed = 6f;
        public float vaultStaminaCost = 18f;

        [Header("Special: Counter (랜스)")]
        public float counterWindow = 0.7f;
        public float counterStaminaCost = 10f;

        [Header("Look")]
        public List<VisualPart> visuals = new();

        public string DisplayName => displayName;
        public string Description => description;

        /// <summary>특수 행동 안내 (HUD 표시용)</summary>
        public string SpecialHint => special switch
        {
            Special.DemonMode => "[F] 귀인화",
            Special.Vault => "[F] 도약 → 공중에서 좌클릭",
            Special.Counter => "[F] 카운터 자세",
            _ => "",
        };

#if UNITY_EDITOR
        public void EditorSetup(string name, string desc)
        {
            displayName = name;
            description = desc;
        }
#endif
    }
}
