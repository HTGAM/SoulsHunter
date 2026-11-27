using System;
using System.Collections.Generic;
using UnityEngine;
using SoulsHunter.Player;

namespace SoulsHunter.Animations
{
    /// <summary>
    /// 캐릭터 하나가 쓰는 애니메이션 묶음.
    /// SoulsHunter > Character Setup 창이 폴더의 애니메이션을 파일 이름으로 자동 분류해서 만들어 준다.
    /// 잘못 들어간 건 인스펙터에서 직접 바꾸면 된다.
    /// </summary>
    [CreateAssetMenu(menuName = "SoulsHunter/Character Animation Set", fileName = "PlayerAnimations")]
    public class CharacterAnimationSet : ScriptableObject
    {
        [Serializable]
        public class AttackClip
        {
            [Tooltip("비우면 모든 무기에 사용")]
            public WeaponData weapon;
            public PlayerCombat.AttackType type = PlayerCombat.AttackType.Light;
            [Tooltip("콤보 몇 번째 공격인지 (0부터). -1이면 아무 단계나")]
            public int comboIndex = -1;
            public AnimationClip clip;
            [Tooltip("클립에서 무기가 맞는 순간 (0~1). 이 지점이 코드의 판정 시작(windup 끝)에 맞춰진다")]
            [Range(0.05f, 0.95f)] public float impactPoint = 0.4f;
            [Tooltip("이 클립만 따로 빠르게/느리게 (1 = 세트 기본 속도)")]
            [Range(0.5f, 2f)] public float speed = 1f;
            [Tooltip("타격 지점들 (0~1). 한 클립에 여러 번 휘두르면 여러 개 → 그만큼 따로 맞는다.\n" +
                     "Character Setup의 '타격 지점 자동 찾기'가 손 움직임으로 채운다. 비어 있으면 Impact Point 하나만 사용")]
            public List<float> hitPoints = new();

            /// <summary>실제로 쓸 타격 지점 목록</summary>
            public IReadOnlyList<float> HitPoints => hitPoints != null && hitPoints.Count > 0 ? hitPoints : new[] { impactPoint };
        }

        [Header("Attack Timing")]
        [Tooltip("켜면 공격 시간 전체를 애니메이션 길이에 맞춘다 (동작이 끝까지 자연스럽게 재생됨, 대신 공격 간격이 길어질 수 있음)")]
        public bool matchAttackDurationToClip = true;
        [Tooltip("공격 애니메이션 재생 속도. 1 = 원본 속도, 클수록 빠르고 공격 간격이 짧아짐")]
        [Range(0.5f, 2f)] public float attackPlaybackSpeed = 1.2f;
        [Tooltip("타격 하나가 판정을 내는 길이 (클립 길이 대비 0~1)")]
        [Range(0.03f, 0.3f)] public float hitWindow = 0.08f;
        [Tooltip("여러 번 때리는 클립의 피해 합계 배율. 예: 1.3이고 3타면 한 대당 피해 x0.43")]
        [Range(0.5f, 3f)] public float multiHitTotalDamage = 1.3f;

        [Header("Locomotion (반복 재생)")]
        public AnimationClip idle;
        public AnimationClip walk;
        public AnimationClip run;
        public AnimationClip sprint;
        public AnimationClip runBackward;
        public AnimationClip strafeLeft;
        public AnimationClip strafeRight;

        [Header("Dodge")]
        public AnimationClip roll;
        public AnimationClip backstep;

        [Header("Reactions")]
        public AnimationClip hit;
        public AnimationClip stagger;
        public AnimationClip death;

        [Header("Actions")]
        public AnimationClip drink;
        [Tooltip("상체에만 적용되는 가드 자세 (반복)")]
        public AnimationClip guard;
        public AnimationClip parry;
        public AnimationClip guardBreak;
        public AnimationClip jump;
        public AnimationClip counter;

        [Header("Attacks")]
        public List<AttackClip> attacks = new();

        /// <summary>무기·종류·단계에 가장 잘 맞는 공격 애니메이션을 찾는다.</summary>
        public AttackClip FindAttack(WeaponData weapon, PlayerCombat.AttackType type, int comboIndex)
        {
            AttackClip best = null;
            int bestScore = -1;
            foreach (var a in attacks)
            {
                if (!a.clip) continue;
                if (a.weapon && a.weapon != weapon) continue;
                if (a.comboIndex >= 0 && a.comboIndex != comboIndex) continue;

                int score = 0;
                if (a.type == type) score += 4;
                else if (type == PlayerCombat.AttackType.Special && a.type == PlayerCombat.AttackType.Heavy) score += 1;
                else if (a.type != PlayerCombat.AttackType.Light) continue; // 맞는 게 없으면 약공격으로 대체
                if (a.weapon) score += 2;
                if (a.comboIndex >= 0) score += 1;

                if (score > bestScore) { bestScore = score; best = a; }
            }

            // 단계가 맞는 게 없으면 같은 종류 중 (단계 % 개수)번째
            if (best == null || (best.comboIndex < 0 && comboIndex > 0))
            {
                var same = attacks.FindAll(a => a.clip && a.type == type && (!a.weapon || a.weapon == weapon));
                if (same.Count > 0) best = same[comboIndex % same.Count];
            }
            return best;
        }
    }
}
