using System.Collections.Generic;
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
        [Tooltip("방향을 바꿀 때 모델이 몸통보다 살짝 늦게 따라 돌아 부드럽게 보이게 한다 (클수록 빠름, 0이면 끔)")]
        [SerializeField] float visualTurnSharpness = 14f;
        [Tooltip("게임 시작 때 대기 자세 기준으로 검·방패 위치를 손·팔 뼈에 자동으로 맞춘다")]
        [SerializeField] bool autoFitWeaponSockets = true;

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
        bool linearTimeline;
        float clipStart; // 공격 클립을 어디서부터 재생하는지 (0~1, 준비 동작 잘라내기)
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
                bool light = type == PlayerCombat.AttackType.Light;
                var hits = entry.HitPoints;
                float length = entry.clip.length;

                // 앞부분 준비 동작 잘라내기: 첫 휘두르기 전 leadIn초만 남기고 그 앞은 건너뛴다 → 버튼 누르면 바로 휘두름
                float leadIn = light ? animationSet.lightLeadIn : animationSet.heavyLeadIn;
                clipStart = 0f;
                if (animationSet.trimAttackLeadIn && leadIn > 0f && hits.Count > 0 && length > 0f)
                    clipStart = Mathf.Clamp(hits[0] - leadIn / length, 0f, hits[0]);
                float used = 1f - clipStart; // 실제로 재생하는 비율

                // 예전에 만든 세트 에셋은 새 필드가 0일 수 있으므로 0이면 1로 취급
                float clipSpeed = entry.speed > 0f ? entry.speed : 1f;
                float setSpeed = animationSet.attackPlaybackSpeed > 0f ? animationSet.attackPlaybackSpeed : 1f;
                float typeSpeed = light && animationSet.lightAttackSpeed > 0f ? animationSet.lightAttackSpeed : 1f;
                float clipDuration = length * used / (setSpeed * clipSpeed * typeSpeed);
                combat.AnimationTimeScale = Mathf.Clamp(step.Total / clipDuration, 0.2f, 4f);

                // 클립의 타격 지점마다 판정 구간을 만든다 → 애니메이션에서 휘두르는 순간에 맞고, 여러 번 휘두르면 여러 번 맞는다
                float total = step.Total;
                float width = Mathf.Clamp(animationSet.hitWindow, 0.03f, 0.3f) / used;
                float scale = hits.Count > 1 ? Mathf.Max(0.1f, animationSet.multiHitTotalDamage) / hits.Count : 1f;
                var windows = new List<(float, float, float)>();
                foreach (float hit in hits)
                {
                    float n = (hit - clipStart) / used; // 잘라낸 구간 기준 위치
                    float start = Mathf.Clamp01(n - width * 0.4f) * total; // 휘두르는 순간(손이 가장 빠를 때) 조금 앞부터
                    windows.Add((start, start + width * total, scale));
                }
                combat.SetHitWindows(windows);
                linearTimeline = true;
            }
            else { linearTimeline = false; clipStart = 0f; }

            // 지금 재생 중이 아닌 슬롯에 새 클립을 끼우고 그쪽으로 전환
            useSlotB = !useSlotB;
            overrides[useSlotB ? attackSlotB : attackSlotA] = entry.clip;
            currentAttack = entry;
            animator.SetFloat(AttackTimeHash, clipStart);
            animator.SetTrigger(useSlotB ? AttackBHash : AttackAHash);
        }

        void Update()
        {
            if (!animator) return;

            // 공격
            bool attacking = combat.IsAttacking && currentAttack != null;
            animator.TrySetBool(AttackingHash, attacking);
            if (attacking)
            {
                // 클립 기준 타이밍이면 그대로 비례, 아니면 타격 지점을 판정 시작에 맞춰 늘이고 줄인다
                var step = combat.CurrentAttack;
                float n = linearTimeline && step != null && step.Total > 0f
                    ? (clipStart + Mathf.Clamp01(combat.AttackTimer / step.Total) * (1f - clipStart)) * 0.999f
                    : AttackNormalizedTime(step, combat.AttackTimer, currentAttack.impactPoint);
                animator.TrySetFloat(AttackTimeHash, n);
            }
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
            if (!model) return;

            Quaternion targetLocal = modelBaseRotation;
            if (rotateModelToDodgeDirection && locomotion.IsDodging && !locomotion.IsBackstep && (!health || !health.IsDead))
            {
                Vector3 dir = locomotion.DodgeDirection;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.01f)
                    targetLocal = Quaternion.Inverse(transform.rotation) * Quaternion.LookRotation(dir) * modelBaseRotation;
            }

            // 월드 기준으로 부드럽게 따라 돌게 한다 → 방향을 휙 바꿔도 몸이 자연스럽게 돈다 (판정·이동은 즉시)
            Quaternion desired = transform.rotation * targetLocal;
            if (!hasSmoothedRotation) { smoothedRotation = desired; hasSmoothedRotation = true; }
            float k = locomotion.IsDodging ? 40f : combat.IsAttacking ? 28f : visualTurnSharpness;
            smoothedRotation = k <= 0f ? desired : Quaternion.Slerp(smoothedRotation, desired, 1f - Mathf.Exp(-k * Time.deltaTime));
            smoothedRotation = Quaternion.RotateTowards(desired, smoothedRotation, 70f); // 너무 늦게 따라오진 않게
            model.rotation = smoothedRotation;
        }

        Quaternion smoothedRotation;
        bool hasSmoothedRotation;

        // ───────── 무기 소켓 자동 정렬 ─────────

        bool socketsFitted;
        int fitFrame;

        void LateUpdate()
        {
            // 애니메이터가 대기 자세를 한두 번 그린 뒤에 계산해야 손 모양이 제대로 잡힌다
            if (!autoFitWeaponSockets || socketsFitted || !animator || !animator.isHuman) return;
            if (++fitFrame < 3) return;
            socketsFitted = true;

            var loadout = GetComponent<PlayerWeaponLoadout>();
            if (!loadout) return;
            WeaponSocketFitter.Compute(animator, transform, out var right, out var left);
            if (!right.valid) return;

            Transform Make(WeaponSocketFitter.Socket s, string name)
            {
                if (!s.valid) return null;
                var t = new GameObject(name).transform;
                t.SetParent(s.parent, false);
                t.localPosition = s.localPosition;
                t.localRotation = s.localRotation;
                return t;
            }

            var oldRight = loadout.RightHandSocket;
            var oldLeft = loadout.LeftHandSocket;
            loadout.SetHandSockets(Make(right, WeaponSocketFitter.RightSocketName), Make(left, WeaponSocketFitter.LeftSocketName),
                                   WeaponSocketFitter.SwordGrip, WeaponSocketFitter.ShieldGrip);
            if (oldRight) Destroy(oldRight.gameObject);
            if (oldLeft) Destroy(oldLeft.gameObject);
        }
    }
}
