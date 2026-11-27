using System;
using System.Collections.Generic;
using UnityEngine;

namespace SoulsHunter.Animations
{
    /// <summary>
    /// 몬스터 한 종류의 애니메이션 묶음.
    /// 공격은 EnemyAI의 공격 이름으로 찾는다. 공격마다 '때리는 순간'(hitPoint)이 있어서,
    /// 그 순간이 공격 판정 시작과 맞도록 재생 속도와 공격 타이밍을 맞춘다.
    /// SoulsHunter/Apply Monster Models 메뉴가 만들고, 클립은 여기서 바꿔 끼울 수 있다.
    /// </summary>
    [CreateAssetMenu(menuName = "SoulsHunter/Monster Animation Set")]
    public class MonsterAnimationSet : ScriptableObject
    {
        [Serializable]
        public class AttackAnim
        {
            [Tooltip("EnemyAI 공격 이름 (Swipe, Bite ...)")]
            public string attack;
            [Tooltip("비워두면 이동 애니메이션으로 대신한다 (돌진 공격에 알맞음)")]
            public AnimationClip clip;
            [Tooltip("클립에서 때리는 순간 (0~1). 자동 설정 메뉴가 가장 빠르게 움직이는 순간으로 잡는다")]
            [Range(0f, 1f)] public float hitPoint = 0.5f;
            [Tooltip("공격 타이밍에 맞추느라 재생 속도를 바꿀 때의 범위")]
            public float minSpeed = 0.7f;
            public float maxSpeed = 1.5f;
        }

        [Header("Locomotion")]
        public AnimationClip idle;
        public AnimationClip walk;
        public AnimationClip run;

        [Header("Reactions")]
        [Tooltip("움찔 (작은 몬스터만, flinchOnHit)")]
        public AnimationClip hit;
        public AnimationClip stagger;
        public AnimationClip death;
        [Tooltip("분노할 때 포효")]
        public AnimationClip roar;

        [Header("Attacks")]
        public List<AttackAnim> attacks = new();

        [Header("Settings")]
        [Tooltip("맞을 때마다 움찔한다 (보스는 끄고 경직 때만 반응)")]
        public bool flinchOnHit;
        [Tooltip("걷기 클립이 제 속도로 보이는 이동 속도 (m/s). 0이면 이동 속도의 35%")]
        public float walkReferenceSpeed;
        [Tooltip("달리기 클립이 제 속도로 보이는 이동 속도 (m/s). 0이면 EnemyAI 이동 속도")]
        public float runReferenceSpeed;

        public AttackAnim Find(string attackName)
        {
            foreach (var a in attacks)
                if (a != null && a.attack == attackName) return a;
            return null;
        }

        public static string StateName(string attackName) => "Atk " + attackName;
    }
}
