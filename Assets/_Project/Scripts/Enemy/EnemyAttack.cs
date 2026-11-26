using System;
using UnityEngine;

namespace SoulsHunter.Enemy
{
    /// <summary>
    /// 적의 공격 패턴 하나. 거리/각도 조건을 만족하는 패턴 중 가중치로 무작위 선택된다.
    /// [예고(windup)] → [판정(active)] → [빈틈(recovery)]
    /// 빈틈(recovery)이 플레이어가 때릴 수 있는 타이밍이다.
    /// </summary>
    [Serializable]
    public class EnemyAttack
    {
        public string name = "Attack";

        [Header("선택 조건")]
        public float minRange = 0f;
        public float maxRange = 4f;
        [Tooltip("플레이어가 정면 기준 이 각도 안에 있어야 사용 (360 = 전방위)")]
        public float maxAngle = 45f;
        public float weight = 1f;
        [Tooltip("한 번 쓰면 이 시간 동안 다시 안 씀")]
        public float cooldown = 0f;

        [Header("피해")]
        public float damage = 25f;
        public float poiseDamage = 15f;

        [Header("타이밍 (초)")]
        public float windup = 0.7f;
        public float active = 0.2f;
        public float recovery = 0.8f;
        [Tooltip("예고 동안 플레이어 쪽으로 도는 속도(도/초). 크면 피하기 어렵다.")]
        public float trackingSpeed = 90f;

        [Header("이동")]
        [Tooltip("돌진 공격이면 체크. 체크 안 된 공격은 제자리에서 나간다 (EnemyAI의 Stay In Place 설정 시)")]
        public bool isDash = false;
        [Tooltip("공격 중 앞으로 이동하는 거리")]
        public float lunge = 1f;
        [Tooltip("전진 시작/끝 시간(초, 공격 시작 기준)")]
        public Vector2 lungeWindow = new(0.5f, 0.9f);

        [Header("Hitbox (적 로컬 좌표)")]
        public Vector3 hitboxCenter = new(0f, 0f, 2f);
        public Vector3 hitboxSize = new(3f, 2f, 3f);

        [NonSerialized] public float readyTime;

        public float Total => windup + active + recovery;

        public EnemyAttack() { }

        public EnemyAttack(string name, float minRange, float maxRange, float maxAngle, float weight, float cooldown,
                           float damage, float poise, float windup, float active, float recovery, float tracking,
                           float lunge, Vector2 lungeWindow, Vector3 center, Vector3 size, bool isDash = false)
        {
            this.isDash = isDash;
            this.name = name; this.minRange = minRange; this.maxRange = maxRange; this.maxAngle = maxAngle;
            this.weight = weight; this.cooldown = cooldown; this.damage = damage; poiseDamage = poise;
            this.windup = windup; this.active = active; this.recovery = recovery; trackingSpeed = tracking;
            this.lunge = lunge; this.lungeWindow = lungeWindow; hitboxCenter = center; hitboxSize = size;
        }
    }
}
