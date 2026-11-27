using System.Collections.Generic;
using UnityEngine;
using SoulsHunter.Combat;
using SoulsHunter.Enemy;

namespace SoulsHunter.Animations
{
    /// <summary>
    /// EnemyAI 상태를 몬스터 모델의 Animator로 옮긴다.
    /// - 이동: 실제로 움직인 속도로 대기 ↔ 걷기 ↔ 달리기 블렌드 (돌진 중에도 달리는 모습)
    /// - 공격: 공격 이름과 같은 상태를 재생. 때리는 순간이 판정 시작과 맞도록 재생 속도와 예고 시간을 맞춘다
    /// - 경직 / 분노 포효 / 죽음 / (작은 몬스터) 움찔
    /// 컨트롤러는 SoulsHunter/Apply Monster Models 메뉴가 만든다.
    /// </summary>
    [RequireComponent(typeof(EnemyAI))]
    public class EnemyAnimationDriver : MonoBehaviour
    {
        [SerializeField] MonsterAnimationSet set;
        [SerializeField] Animator animator;
        [SerializeField] float crossFade = 0.15f;
        [Tooltip("공격 애니메이션의 때리는 순간에 맞춰 공격 예고 시간을 조정하고, 동작이 끝나기 전에 다음 행동을 하지 않게 한다")]
        [SerializeField] bool matchAttackTiming = true;
        [Tooltip("동작이 끝날 때까지 기다리느라 빈틈이 늘어나는 최대 시간(초)")]
        [SerializeField] float maxExtraRecovery = 0.8f;

        static readonly int MoveId = Animator.StringToHash("Move");
        static readonly int LocoPlaybackId = Animator.StringToHash("LocoPlayback");
        static readonly int AttackSpeedId = Animator.StringToHash("AttackSpeed");
        static readonly int LocomotionState = Animator.StringToHash("Locomotion");
        static readonly int LocomotionStateFull = Animator.StringToHash("Base Layer.Locomotion");
        static readonly int HitState = Animator.StringToHash("Hit");
        static readonly int HitStateFull = Animator.StringToHash("Base Layer.Hit");
        static readonly int StaggerState = Animator.StringToHash("Stagger");
        static readonly int StaggerStateFull = Animator.StringToHash("Base Layer.Stagger");
        static readonly int DeathState = Animator.StringToHash("Death");
        static readonly int DeathStateFull = Animator.StringToHash("Base Layer.Death");
        static readonly int RoarState = Animator.StringToHash("Roar");
        static readonly int RoarStateFull = Animator.StringToHash("Base Layer.Roar");

        EnemyAI ai;
        Health health;
        EnemyAI.State lastState;
        readonly Dictionary<EnemyAttack, float> playbackSpeeds = new();
        float attackSpeed = 1f;
        float roarUntil;
        Vector3 lastPosition;
        float smoothedSpeed;

        public MonsterAnimationSet Set => set;
        public Animator Animator => animator;

        void Awake()
        {
            ai = GetComponent<EnemyAI>();
            health = GetComponent<Health>();
            if (!animator) animator = GetComponentInChildren<Animator>();
            if (animator)
            {
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; // 화면 밖에서도 부위 판정이 따라 움직이게
            }

            ai.AttackStarted += OnAttackStarted;
            ai.Enraged += OnEnraged;
            if (health) health.Damaged += OnDamaged;

            if (set && matchAttackTiming) FitAttackTimings();
            lastPosition = transform.position;
            lastState = ai.CurrentState;
        }

        void OnDestroy()
        {
            if (ai)
            {
                ai.AttackStarted -= OnAttackStarted;
                ai.Enraged -= OnEnraged;
            }
            if (health) health.Damaged -= OnDamaged;
        }

        /// <summary>
        /// 클립의 때리는 순간이 공격 판정 시작(돌진 공격은 판정 한가운데)에 오도록
        /// 재생 속도를 정하고, 속도 범위로 다 못 맞춘 차이는 예고 시간을 늘리거나 줄여서 맞춘다.
        /// 실행 중에만 바뀌고 저장된 값은 그대로다.
        /// </summary>
        void FitAttackTimings()
        {
            foreach (var a in ai.Attacks)
            {
                var anim = set.Find(a.name);
                if (anim == null || !anim.clip) continue;

                float hitSec = anim.hitPoint * anim.clip.length;
                float strike = a.isDash ? a.windup + a.active * 0.5f : a.windup;
                float speed = Mathf.Clamp(hitSec / Mathf.Max(0.05f, strike), anim.minSpeed, anim.maxSpeed);
                float shift = hitSec / speed - strike;
                a.windup = Mathf.Max(0.1f, a.windup + shift);
                if (a.isDash) a.lungeWindow += new Vector2(shift, shift);

                // 동작이 거의 끝날 때까지는 다음 행동을 하지 않는다 (끊기는 느낌 방지)
                float clipEnd = anim.clip.length / speed * 0.9f;
                float needed = clipEnd - (a.windup + a.active);
                if (needed > a.recovery) a.recovery = Mathf.Min(needed, a.recovery + maxExtraRecovery);

                playbackSpeeds[a] = speed;
            }
        }

        void Update()
        {
            if (!animator || !set) return;
            float dt = Time.deltaTime;

            // 실제로 움직인 속도 (내비 이동이든 돌진이든 똑같이)
            Vector3 delta = transform.position - lastPosition;
            delta.y = 0f;
            lastPosition = transform.position;
            float speed = dt > 0f ? Mathf.Min(delta.magnitude / dt, 40f) : 0f;
            smoothedSpeed = Mathf.Lerp(smoothedSpeed, speed, 1f - Mathf.Exp(-12f * dt));

            float runRef = set.runReferenceSpeed > 0f ? set.runReferenceSpeed : Mathf.Max(0.5f, ai.MoveSpeed);
            float walkRef = set.walkReferenceSpeed > 0f ? set.walkReferenceSpeed : runRef * 0.35f;
            float move = smoothedSpeed < walkRef
                ? smoothedSpeed / walkRef
                : 1f + Mathf.Clamp01((smoothedSpeed - walkRef) / Mathf.Max(0.01f, runRef - walkRef));
            animator.SetFloat(MoveId, move, 0.08f, dt);
            animator.SetFloat(LocoPlaybackId, smoothedSpeed > runRef ? Mathf.Clamp(smoothedSpeed / runRef, 1f, 2.2f) : 1f);
            animator.SetFloat(AttackSpeedId, attackSpeed * ai.AttackTimeScale);

            var state = ai.CurrentState;
            if (state != lastState)
            {
                OnStateChanged(lastState, state);
                lastState = state;
            }
        }

        void OnStateChanged(EnemyAI.State from, EnemyAI.State to)
        {
            switch (to)
            {
                case EnemyAI.State.Dead:
                    Play(DeathStateFull, 0.12f);
                    break;
                case EnemyAI.State.Staggered:
                    if (Time.time >= roarUntil) Play(StaggerStateFull, 0.08f);
                    break;
                case EnemyAI.State.Chase:
                case EnemyAI.State.Idle:
                    if (from == EnemyAI.State.Dead) animator.Play(LocomotionStateFull, 0, 0f); // 부활
                    else if (!IsIn(HitState)) Play(LocomotionStateFull, 0.3f);
                    break;
                // Attack은 AttackStarted에서 처리
            }
        }

        void OnAttackStarted(EnemyAttack a)
        {
            if (!animator || !set) return;
            var anim = set.Find(a.name);
            int stateHash = Animator.StringToHash("Base Layer." + MonsterAnimationSet.StateName(a.name));
            if (anim != null && anim.clip && animator.HasState(0, stateHash))
            {
                attackSpeed = playbackSpeeds.TryGetValue(a, out var s) ? s : 1f;
                animator.SetFloat(AttackSpeedId, attackSpeed * ai.AttackTimeScale);
                animator.CrossFadeInFixedTime(stateHash, crossFade, 0, 0f);
            }
            else if (!IsIn(LocomotionState))
            {
                Play(LocomotionStateFull, crossFade); // 클립이 없는 공격(돌진)은 이동 모습으로
            }
        }

        void OnEnraged()
        {
            if (!set || !set.roar || !animator) return;
            roarUntil = Time.time + set.roar.length;
            Play(RoarStateFull, 0.1f);
        }

        void OnDamaged(DamageInfo info)
        {
            if (!set || !set.flinchOnHit || !set.hit || !animator || health.IsDead) return;
            var s = ai.CurrentState;
            if (s == EnemyAI.State.Chase || s == EnemyAI.State.Idle)
                animator.CrossFadeInFixedTime(HitStateFull, 0.06f, 0, 0f);
        }

        void Play(int stateHash, float fade)
        {
            if (animator && animator.HasState(0, stateHash))
                animator.CrossFadeInFixedTime(stateHash, fade, 0);
        }

        bool IsIn(int stateHash)
        {
            var info = animator.GetCurrentAnimatorStateInfo(0);
            if (info.shortNameHash == stateHash) return true;
            return animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).shortNameHash == stateHash;
        }

#if UNITY_EDITOR
        public void EditorSetup(MonsterAnimationSet animationSet, Animator modelAnimator)
        {
            set = animationSet;
            animator = modelAnimator;
        }
#endif
    }
}
