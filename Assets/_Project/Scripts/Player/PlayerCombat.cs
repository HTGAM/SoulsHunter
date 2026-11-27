using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SoulsHunter.Combat;
using SoulsHunter.Core;

namespace SoulsHunter.Player
{
    /// <summary>
    /// 한 번의 공격 동작. 시간은 모두 초 단위.
    /// [선딜(windup)] → [판정(active)] → [후딜(recovery)]
    /// 애니메이션이 생기면 애니메이션 길이에 맞춰 숫자만 조정하면 된다.
    /// </summary>
    [Serializable]
    public class AttackStep
    {
        public string name = "Attack";
        public float damage = 20f;
        public float poiseDamage = 10f;
        public float staminaCost = 15f;

        [Header("Timing")]
        public float windup = 0.25f;
        public float active = 0.12f;
        public float recovery = 0.35f;
        [Tooltip("공격 시작부터 이 시간이 지나면 구르기/이동으로 캔슬 가능")]
        public float cancelableAfter = 0.55f;

        [Header("Movement")]
        [Tooltip("선딜~판정 동안 앞으로 전진하는 거리")]
        public float lunge = 0.6f;
        [Tooltip("선딜 중 대상 쪽으로 몸을 돌릴 수 있는 속도(도/초)")]
        public float trackingSpeed = 540f;

        [Header("Hitbox (캐릭터 로컬 좌표)")]
        public Vector3 hitboxCenter = new(0f, 1f, 1.1f);
        public Vector3 hitboxSize = new(1.6f, 1.2f, 1.6f);

        [Header("Feel")]
        public float hitStop = 0.06f;

        public float Total => windup + active + recovery;

        public AttackStep() { }

        public AttackStep(string name, float damage, float poise, float stamina,
                          float windup, float active, float recovery, float cancelableAfter,
                          float lunge, Vector3 center, Vector3 size, float hitStop)
        {
            this.name = name; this.damage = damage; poiseDamage = poise; staminaCost = stamina;
            this.windup = windup; this.active = active; this.recovery = recovery;
            this.cancelableAfter = cancelableAfter; this.lunge = lunge;
            hitboxCenter = center; hitboxSize = size; this.hitStop = hitStop;
        }
    }

    /// <summary>
    /// 약공격/강공격 콤보. 공격 중 다음 버튼을 미리 눌러두면 후딜 시작 시 이어진다.
    /// 후딜 후반에는 구르기나 이동으로 캔슬할 수 있다.
    /// </summary>
    [DefaultExecutionOrder(-50)] // PlayerLocomotion보다 먼저 ActionLocked를 정한다
    [RequireComponent(typeof(PlayerLocomotion), typeof(PlayerInputHandler), typeof(PlayerStamina))]
    public class PlayerCombat : MonoBehaviour
    {
        public enum AttackType { Light, Heavy }

        [SerializeField] List<AttackStep> lightCombo = new()
        {
            new("Light 1", 20f, 10f, 14f, 0.22f, 0.12f, 0.35f, 0.50f, 0.6f, new(0f, 1f, 1.1f), new(1.6f, 1.2f, 1.6f), 0.05f),
            new("Light 2", 22f, 10f, 14f, 0.20f, 0.12f, 0.35f, 0.48f, 0.6f, new(0f, 1f, 1.1f), new(1.8f, 1.2f, 1.6f), 0.05f),
            new("Light 3", 32f, 20f, 18f, 0.32f, 0.15f, 0.50f, 0.70f, 1.0f, new(0f, 1f, 1.2f), new(1.4f, 1.4f, 2.0f), 0.08f),
        };

        [SerializeField] List<AttackStep> heavyCombo = new()
        {
            new("Heavy 1", 45f, 35f, 25f, 0.60f, 0.15f, 0.60f, 1.00f, 0.8f, new(0f, 1f, 1.2f), new(1.8f, 1.6f, 2.0f), 0.10f),
            new("Heavy 2", 55f, 40f, 28f, 0.50f, 0.15f, 0.65f, 0.95f, 1.0f, new(0f, 1f, 1.3f), new(2.0f, 1.6f, 2.2f), 0.12f),
        };

        [Tooltip("공격이 끝난 뒤 이 시간 안에 같은 공격을 누르면 콤보가 이어진다")]
        [SerializeField] float comboResetTime = 0.4f;
        [Tooltip("공격 버튼 선입력 유지 시간")]
        [SerializeField] float inputBuffer = 0.3f;
        [SerializeField] LayerMask hitMask = ~0;

        PlayerLocomotion locomotion;
        PlayerInputHandler input;
        PlayerStamina stamina;
        Health ownHealth;
        PlayerFlask flask;
        PlayerWeapon weapon;

        AttackStep current;
        AttackType currentType;
        int comboIndex = -1;
        float timer;
        float lastAttackEndTime = float.NegativeInfinity;
        float lungeProgress;

        AttackType? bufferedAttack;
        float bufferedUntil;

        readonly Collider[] overlap = new Collider[32];
        readonly HashSet<Health> hitThisSwing = new();
        Coroutine hitStopRoutine;

        public bool IsAttacking => current != null;
        public AttackStep CurrentAttack => current;
        public bool InActiveFrames => IsAttacking && timer >= current.windup && timer < current.windup + current.active;

        /// <summary>공격 시작 (공격 종류, 콤보 단계 0~)</summary>
        public event Action<AttackType, int> AttackStarted;
        /// <summary>공격이 무언가에 맞았을 때</summary>
        public event Action<Hurtbox, DamageInfo> HitLanded;

        static readonly int LightAttackHash = Animator.StringToHash("LightAttack");
        static readonly int HeavyAttackHash = Animator.StringToHash("HeavyAttack");
        static readonly int ComboIndexHash = Animator.StringToHash("ComboIndex");

        void Awake()
        {
            locomotion = GetComponent<PlayerLocomotion>();
            input = GetComponent<PlayerInputHandler>();
            stamina = GetComponent<PlayerStamina>();
            ownHealth = GetComponent<Health>();
            flask = GetComponent<PlayerFlask>();
            weapon = GetComponent<PlayerWeapon>();
            if (ownHealth) ownHealth.InvulnerabilityCheck = () => locomotion.IsInvulnerable;

            locomotion.Dodged += _ => EndAttack(); // 구르기는 공격을 캔슬한다
        }

        void OnDisable()
        {
            if (hitStopRoutine != null) { StopCoroutine(hitStopRoutine); Time.timeScale = 1f; }
            EndAttack();
        }

        void Update()
        {
            if (input.LightAttackPressed) Buffer(AttackType.Light);
            if (input.HeavyAttackPressed) Buffer(AttackType.Heavy);
            // 공격 중 선입력은 다음 공격까지 유지, 그 외엔 잠깐만 유지
            if (bufferedAttack.HasValue && !IsAttacking && Time.time > bufferedUntil) bufferedAttack = null;

            // 경직되거나 죽으면 공격 중단
            if (locomotion.IsStunned || (ownHealth && ownHealth.IsDead))
            {
                EndAttack();
                bufferedAttack = null;
                return;
            }

            if (IsAttacking) TickAttack(Time.deltaTime);

            if (!IsAttacking && bufferedAttack.HasValue && !locomotion.IsDodging && !(flask && flask.IsDrinking))
            {
                var type = bufferedAttack.Value;
                bufferedAttack = null;
                bool continues = type == currentType && Time.time - lastAttackEndTime <= comboResetTime;
                StartAttack(type, continues ? comboIndex + 1 : 0);
            }

            locomotion.ActionLocked = IsAttacking && timer < current.cancelableAfter;
        }

        void Buffer(AttackType type)
        {
            bufferedAttack = type;
            bufferedUntil = Time.time + inputBuffer;
        }

        void StartAttack(AttackType type, int index)
        {
            var combo = type == AttackType.Light ? lightCombo : heavyCombo;
            if (combo.Count == 0) return;
            if (index >= combo.Count) index = 0;
            if (!stamina.TryConsume(combo[index].staminaCost)) return;

            current = combo[index];
            currentType = type;
            comboIndex = index;
            timer = 0f;
            lungeProgress = 0f;
            hitThisSwing.Clear();

            // 시작할 때 락온 대상/입력 방향으로 바로 몸을 돌린다
            locomotion.FaceTowards(locomotion.GetIntendedFacing(), 360f);

            var anim = locomotion.Animator;
            if (anim)
            {
                anim.TrySetInt(ComboIndexHash, index);
                anim.TrySetTrigger(type == AttackType.Light ? LightAttackHash : HeavyAttackHash);
            }
            AttackStarted?.Invoke(type, index);
        }

        void TickAttack(float dt)
        {
            float prev = timer;
            timer += dt;
            var a = current;

            // 선딜 중엔 대상 쪽으로 조금씩 돌 수 있다 (소울류 '트래킹')
            if (timer < a.windup) locomotion.FaceTowards(locomotion.GetIntendedFacing(), a.trackingSpeed * dt);

            // 선딜~판정 동안 전진
            float lungeTime = a.windup + a.active;
            if (a.lunge > 0f && prev < lungeTime)
            {
                float p = Mathf.Clamp01(timer / lungeTime);
                float eased = 1f - (1f - p) * (1f - p); // 처음에 빠르게
                locomotion.AddDisplacement(transform.forward * ((eased - lungeProgress) * a.lunge));
                lungeProgress = eased;
            }

            if (InActiveFrames) DoHitDetection();

            // 콤보 선입력이 있으면 후딜 시작 지점부터 바로 다음 공격으로
            bool inRecovery = timer >= a.windup + a.active;
            if (inRecovery && bufferedAttack.HasValue)
            {
                var type = bufferedAttack.Value;
                bufferedAttack = null;
                StartAttack(type, type == currentType ? comboIndex + 1 : 0);
                return;
            }

            // 캔슬 가능 구간에서 이동하면 공격 종료
            if (timer >= a.cancelableAfter && input.Move.sqrMagnitude > 0.04f)
            {
                EndAttack();
                return;
            }

            if (timer >= a.Total) EndAttack();
        }

        void DoHitDetection()
        {
            var a = current;
            Vector3 center = transform.TransformPoint(a.hitboxCenter);
            int count = Physics.OverlapBoxNonAlloc(center, a.hitboxSize * 0.5f, overlap, transform.rotation, hitMask, QueryTriggerInteraction.Collide);

            // 같은 대상의 여러 부위가 겹치면 판정 중심에 가장 가까운 부위 하나만 맞는다
            Dictionary<Health, (Hurtbox box, float dist, Vector3 point)> best = null;
            for (int i = 0; i < count; i++)
            {
                var hb = overlap[i].GetComponent<Hurtbox>();
                if (!hb || !hb.Health || hb.Health == ownHealth || hitThisSwing.Contains(hb.Health)) continue;

                Vector3 point = overlap[i].ClosestPoint(center);
                float dist = (point - center).sqrMagnitude;
                best ??= new Dictionary<Health, (Hurtbox, float, Vector3)>();
                if (!best.TryGetValue(hb.Health, out var b) || dist < b.dist) best[hb.Health] = (hb, dist, point);
            }
            if (best == null) return;

            bool anyHit = false;
            foreach (var kv in best)
            {
                hitThisSwing.Add(kv.Key);
                var info = new DamageInfo
                {
                    Amount = a.damage * (weapon ? weapon.DamageMultiplier : 1f),
                    PoiseDamage = a.poiseDamage,
                    Point = kv.Value.point,
                    Direction = transform.forward,
                    Source = gameObject,
                };
                if (kv.Value.box.ReceiveHit(info))
                {
                    anyHit = true;
                    info.Amount *= kv.Value.box.DamageMultiplier;
                    info.Part = kv.Value.box.PartName;
                    HitLanded?.Invoke(kv.Value.box, info);
                }
            }
            if (anyHit && a.hitStop > 0f) HitStop(a.hitStop);
        }

        void EndAttack()
        {
            if (current == null) return;
            current = null;
            lastAttackEndTime = Time.time;
            locomotion.ActionLocked = false;
        }

        // 타격감: 아주 짧게 시간을 거의 멈춘다
        void HitStop(float duration)
        {
            if (hitStopRoutine != null) StopCoroutine(hitStopRoutine);
            hitStopRoutine = StartCoroutine(HitStopRoutine(duration));
        }

        IEnumerator HitStopRoutine(float duration)
        {
            Time.timeScale = 0.05f;
            yield return new WaitForSecondsRealtime(duration);
            Time.timeScale = 1f;
            hitStopRoutine = null;
        }

        void OnDrawGizmos()
        {
            if (!IsAttacking) return;
            Gizmos.color = InActiveFrames ? new Color(1f, 0f, 0f, 0.5f) : new Color(1f, 1f, 0f, 0.2f);
            Gizmos.matrix = Matrix4x4.TRS(transform.TransformPoint(current.hitboxCenter), transform.rotation, Vector3.one);
            Gizmos.DrawCube(Vector3.zero, current.hitboxSize);
        }
    }
}
