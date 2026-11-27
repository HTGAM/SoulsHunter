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
        [Tooltip("분노 페이즈에서만 쓰는 공격")]
        public bool enragedOnly = false;

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

        public enum Kind
        {
            /// <summary>근접: 판정 시간 동안 Hitbox로 때린다</summary>
            Melee,
            /// <summary>투사체: 예고가 끝나는 순간 플레이어 쪽으로 발사 (여러 발이면 부채꼴)</summary>
            Projectile,
            /// <summary>장판(낙하): 예고가 끝나는 순간 플레이어 주변 바닥에 지연 폭발을 깐다</summary>
            Meteor,
        }

        [Header("원거리 (Kind가 Melee가 아닐 때)")]
        public Kind kind = Kind.Melee;
        [Tooltip("투사체 / 장판 개수")]
        public int projectileCount = 1;
        [Tooltip("투사체 부채꼴 전체 각도(도)")]
        public float spreadAngle = 30f;
        public float projectileSpeed = 18f;
        public float projectileRadius = 0.5f;
        public float projectileRange = 30f;
        [Tooltip("발사 위치 (적 로컬 좌표, 발 기준)")]
        public Vector3 muzzleOffset = new(0f, 2f, 2f);
        [Tooltip("장판 하나의 반지름")]
        public float meteorRadius = 2.5f;
        [Tooltip("장판이 깔린 뒤 터지기까지 시간(초)")]
        public float meteorDelay = 1.3f;
        [Tooltip("첫 장판은 플레이어 위치, 나머지는 이 반경 안에 흩어진다")]
        public float meteorScatter = 6f;

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
