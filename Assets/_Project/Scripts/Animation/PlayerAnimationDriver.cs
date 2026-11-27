using UnityEngine;
using SoulsHunter.Combat;
using SoulsHunter.Core;
using SoulsHunter.Player;

namespace SoulsHunter.Animations
{
    /// <summary>
    /// 코드의 행동 타이밍과 애니메이션을 맞춘다.
    /// - 공격: 애니메이션 재생 위치를 코드의 공격 타이머로 직접 구동한다 (Motion Time).
    ///   클립의 '타격 지점'이 정확히 판정 시작에 오도록 앞뒤를 늘이거나 줄인다.
    ///   그래서 클립 길이가 달라도, 공격 속도 버프·히트스톱·모으기가 있어도 어긋나지 않는다.
    /// - 공격 클립은 두 슬롯(A/B)을 번갈아 갈아 끼워서 연속 공격도 자연스럽게 이어진다.
    /// - 구르기도 회피 진행도로 구동하고, 락온 중 옆구르기는 모델만 구르는 방향으로 돌린다.
    /// 애니메이터 컨트롤러는 Character Setup 창이 만들어 준다.
    /// </summary>
    [DefaultExecutionOrder(100)] // 전투·이동 처리가 끝난 뒤
    [RequireComponent(typeof(PlayerLocomotion), typeof(PlayerCombat))]
    public class PlayerAnimationDriver : MonoBehaviour
    {
        [SerializeField] Animator animator;
        [SerializeField] CharacterAnimationSet animationSet;
        [Tooltip("캐릭터 모델 (회피 방향으로 돌릴 대상)")]
        [SerializeField] Transform model;
        [SerializeField] bool rotateModelToDodgeDirection = true;

        [Header("Controller Slots (Character Setup이 채움)")]
        [SerializeField] AnimationClip attackSlotA;
        [SerializeField] AnimationClip attackSlotB;
        [Tooltip("이동 클립이 실제로 움직이는 속도(m/s): 앞, 뒤, 왼쪽, 오른쪽. 이보다 빨리 움직이면 애니메이션을 빨리 재생")]
        [SerializeField] Vector4 locomotionClipSpeeds = new(4.5f, 4.5f, 4.5f, 4.5f);
        [SerializeField] float maxLocomotionPlaybackSpeed = 2.2f;

        PlayerLocomotion locomotion;
        PlayerCombat combat;
        Health health;
        AnimatorOverrideController overrides;
        CharacterAnimationSet.AttackClip currentAttack;
        bool useSlotB;
        Quaternion modelBaseRotation = Quaternion.identity;

        static readonly int AttackAHash = Animator.StringToHash("AttackA");
        static readonly int AttackBHash = Animator.StringToHash("AttackB");
        static readonly int AttackingHash = Animator.StringToHash("Attacking");
        static readonly int AttackTimeHash = Animator.StringToHash("AttackTime");
        static readonly int DodgingHash = Animator.StringToHash("Dodging");
        static readonly int DodgeTimeHash = Animator.StringToHash("DodgeTime");
        static readonly int GroundedHash = Animator.StringToHash("Grounded");
        static readonly int LocoSpeedHash = Animator.StringToHash("LocoSpeed");

        void Awake()
        {
            locomotion = GetComponent<PlayerLocomotion>();
            combat = GetComponent<PlayerCombat>();
            health = GetComponent<Health>();
            if (!animator) animator = locomotion.Animator;
            if (model) modelBaseRotation = model.localRotation;

            if (animator && animator.runtimeAnimatorController)
            {
                overrides = new AnimatorOverrideController(animator.runtimeAnimatorController) { name = "Player Overrides" };
                animator.runtimeAnimatorController = overrides;
                animator.applyRootMotion = false; // 이동은 코드가 한다
            }

            combat.AttackStarted += OnAttackStarted;
        }

        void OnAttackStarted(PlayerCombat.AttackType type, int index)
        {
            currentAttack = null;
            combat.AnimationTimeScale = 1f;
            if (!animator || !animationSet || overrides == null || !attackSlotA || !attackSlotB) return;

            var entry = animationSet.FindAttack(combat.EquippedWeapon, type, index);
            if (entry == null || !entry.clip) return;

            // 공격 시간 전체를 클립 길이에 맞춘다 → 애니메이션이 빨리감기처럼 보이지 않고 끝까지 재생됨
            var step = combat.CurrentAttack;
            if (animationSet.matchAttackDurationToClip && step != null && step.Total > 0f)
            {
                // 예전에 만든 세트 에셋은 새 필드가 0일 수 있으므로 0이면 1로 취급
                float clipSpeed = entry.speed > 0f ? entry.speed : 1f;
                float setSpeed = animationSet.attackPlaybackSpeed > 0f ? animationSet.attackPlaybackSpeed : 1f;
                float clipDuration = entry.clip.length / (setSpeed * clipSpeed);
                combat.AnimationTimeScale = Mathf.Clamp(step.Total / clipDuration, 0.2f, 3f);
            }

            // 지금 재생 중이 아닌 슬롯에 새 클립을 끼우고 그쪽으로 전환
            useSlotB = !useSlotB;
            overrides[useSlotB ? attackSlotB : attackSlotA] = entry.clip;
            currentAttack = entry;
            animator.SetFloat(AttackTimeHash, 0f);
            animator.SetTrigger(useSlotB ? AttackBHash : AttackAHash);
        }

        void Update()
        {
            if (!animator) return;

            // 공격
            bool attacking = combat.IsAttacking && currentAttack != null;
            animator.TrySetBool(AttackingHash, attacking);
            if (attacking) animator.TrySetFloat(AttackTimeHash, AttackNormalizedTime(combat.CurrentAttack, combat.AttackTimer, currentAttack.impactPoint));
            if (!combat.IsAttacking) currentAttack = null;

            // 회피
            animator.TrySetBool(DodgingHash, locomotion.IsDodging);
            animator.TrySetFloat(DodgeTimeHash, locomotion.DodgeProgress);
            animator.TrySetBool(GroundedHash, locomotion.IsGrounded);
            animator.TrySetFloat(LocoSpeedHash, LocomotionPlaybackSpeed(), 0.1f);

            UpdateModelRotation();
        }

        /// <summary>
        /// 블렌드 트리의 가장 바깥 클립보다 빨리 움직이면 그만큼 빨리 재생해서 발이 미끄러지지 않게 한다.
        /// 방향별 클립 속도로 타원을 만들어 지금 방향의 '클립 최고 속도'를 구한다.
        /// </summary>
        float LocomotionPlaybackSpeed()
        {
            Vector3 v = transform.InverseTransformDirection(locomotion.HorizontalVelocity);
            float speed = new Vector2(v.x, v.z).magnitude;
            if (speed < 0.1f) return 1f;

            float dz = v.z / speed, dx = v.x / speed;
            float f = Mathf.Max(0.1f, dz >= 0f ? locomotionClipSpeeds.x : locomotionClipSpeeds.y);
            float s = Mathf.Max(0.1f, dx >= 0f ? locomotionClipSpeeds.w : locomotionClipSpeeds.z);
            float edge = 1f / Mathf.Sqrt(dz * dz / (f * f) + dx * dx / (s * s));
            return Mathf.Clamp(speed / edge, 1f, maxLocomotionPlaybackSpeed);
        }

        /// <summary>
        /// 코드 타이머 → 클립 위치(0~1).
        /// 선딜 구간은 클립의 [0, 타격 지점], 판정+후딜 구간은 [타격 지점, 1]에 대응시킨다.
        /// </summary>
        static float AttackNormalizedTime(AttackStep a, float timer, float impact)
        {
            if (a == null) return 0f;
            if (timer < a.windup) return a.windup > 0f ? timer / a.windup * impact : impact;
            float rest = a.active + a.recovery;
            float t = rest > 0f ? (timer - a.windup) / rest : 1f;
            return Mathf.Clamp01(impact + t * (1f - impact)) * 0.999f; // 1에 닿으면 반복 재생되는 걸 막음
        }

        // 락온 중 옆/뒤로 구를 때: 구르는 애니메이션은 앞구르기 하나이므로 모델만 구르는 방향으로 돌린다
        void UpdateModelRotation()
        {
            if (!model || !rotateModelToDodgeDirection) return;

            Quaternion target = modelBaseRotation;
            if (locomotion.IsDodging && !locomotion.IsBackstep && (!health || !health.IsDead))
            {
                Vector3 dir = locomotion.DodgeDirection;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.01f)
                    target = Quaternion.Inverse(transform.rotation) * Quaternion.LookRotation(dir) * modelBaseRotation;
            }
            float speed = locomotion.IsDodging ? 40f : 14f;
            model.localRotation = Quaternion.Slerp(model.localRotation, target, 1f - Mathf.Exp(-speed * Time.deltaTime));
        }
    }
}
