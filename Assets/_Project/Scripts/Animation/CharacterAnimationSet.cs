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
        }

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
