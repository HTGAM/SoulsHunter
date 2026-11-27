using System;
using UnityEngine;
using SoulsHunter.CameraSystem;
using SoulsHunter.Core;

namespace SoulsHunter.Player
{
    /// <summary>
    /// 3인칭 이동, 달리기, 구르기/백스텝(무적 프레임 포함).
    /// 락온 중에는 대상을 바라본 채 옆걸음질하고, 달리기 중에는 이동 방향을 바라본다.
    /// </summary>
    [RequireComponent(typeof(CharacterController), typeof(PlayerInputHandler), typeof(PlayerStamina))]
    public class PlayerLocomotion : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] ThirdPersonCamera playerCamera;
        [Tooltip("모델이 생기면 연결. 없어도 동작한다.")]
        [SerializeField] Animator animator;

        [Header("Movement")]
        [SerializeField] float runSpeed = 4.5f;
        [SerializeField] float sprintSpeed = 7f;
        [Tooltip("속도 변화량(m/s²). 클수록 반응이 빠르다.")]
        [SerializeField] float acceleration = 30f;
        [Tooltip("초당 회전 각도")]
        [SerializeField] float rotationSpeed = 720f;
        [SerializeField] float gravity = -25f;

        [Header("Sprint")]
        [SerializeField] float sprintStaminaPerSecond = 15f;

        [Header("Roll (방향 입력 + 회피)")]
        [SerializeField] float rollDistance = 4.5f;
        [SerializeField] float rollDuration = 0.6f;
        [Tooltip("무적 구간 (시작초, 끝초)")]
        [SerializeField] Vector2 rollInvulnerability = new(0.05f, 0.4f);
        [SerializeField] float rollStaminaCost = 20f;

        [Header("Backstep (방향 입력 없이 회피)")]
        [SerializeField] float backstepDistance = 2f;
        [SerializeField] float backstepDuration = 0.4f;
        [SerializeField] Vector2 backstepInvulnerability = new(0f, 0.2f);
        [SerializeField] float backstepStaminaCost = 12f;

        [Header("Dodge Feel")]
        [Tooltip("시간(0~1)에 따른 이동 비율(0~1). 앞쪽이 가파를수록 초반에 빨리 튀어나간다.")]
        [SerializeField] AnimationCurve dodgeCurve = new(
            new Keyframe(0f, 0f, 0f, 2.2f),
            new Keyframe(1f, 1f, 0f, 0f));
        [Tooltip("회피 버튼을 미리 눌러둬도 이 시간 안에는 다음 회피가 나간다")]
        [SerializeField] float dodgeInputBuffer = 0.2f;

        CharacterController controller;
        PlayerInputHandler input;
        PlayerStamina stamina;

        Vector3 horizontalVelocity;
        float verticalVelocity;
        bool sprintLockedOut;
        float dodgeBufferedUntil = float.NegativeInfinity;

        // 회피 상태
        bool isDodging, isBackstep;
        Vector3 dodgeDirection;
        float dodgeTimer, dodgeDuration, dodgeDistance, dodgeCurvePrev;
        Vector2 dodgeIFrames;

        public bool IsDodging => isDodging;
        public bool IsSprinting { get; private set; }
        public bool IsInvulnerable => isDodging && dodgeTimer >= dodgeIFrames.x && dodgeTimer <= dodgeIFrames.y;
        public Vector3 HorizontalVelocity => horizontalVelocity;
        public ThirdPersonCamera PlayerCamera => playerCamera;
        public Animator Animator => animator;

        /// <summary>
        /// 공격 등 다른 행동 중일 때 true. 이동 입력과 회피를 막는다.
        /// (PlayerCombat처럼 이 컴포넌트보다 먼저 Update되는 쪽에서 매 프레임 설정)
        /// </summary>
        public bool ActionLocked { get; set; }

        /// <summary>피격 경직 중이면 true. 이동/회피/공격 모두 불가.</summary>
        public bool IsStunned => Time.time < stunnedUntil;

        /// <summary>이동 속도 배율 (회복약 마시는 중 느리게 등). 1보다 작으면 달리기 불가.</summary>
        public float SpeedMultiplier { get; set; } = 1f;

        /// <summary>true면 회피 불가 (회복약 마시는 중 등)</summary>
        public bool DodgeBlocked { get; set; }

        Vector3 externalDisplacement;
        float stunnedUntil;
        float frameSpeedCap = 1f;

        /// <summary>
        /// 이번 프레임만 이동 속도를 제한한다 (가드 중 느리게 등). 1보다 작으면 달리기 불가.
        /// 이 컴포넌트보다 먼저 Update되는 쪽에서 매 프레임 호출한다. 여러 곳에서 부르면 가장 작은 값.
        /// </summary>
        public void LimitSpeedThisFrame(float multiplier) => frameSpeedCap = Mathf.Min(frameSpeedCap, multiplier);

        /// <summary>회피 시작. 인자는 백스텝 여부.</summary>
        public event Action<bool> Dodged;

        // 애니메이터 파라미터 (모델 붙일 때 같은 이름으로 만들면 자동 연결)
        static readonly int SpeedHash = Animator.StringToHash("Speed");
        static readonly int MoveXHash = Animator.StringToHash("MoveX");
        static readonly int MoveYHash = Animator.StringToHash("MoveY");
        static readonly int LockedOnHash = Animator.StringToHash("LockedOn");
        static readonly int RollHash = Animator.StringToHash("Roll");
        static readonly int BackstepHash = Animator.StringToHash("Backstep");

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            input = GetComponent<PlayerInputHandler>();
            stamina = GetComponent<PlayerStamina>();
            if (!playerCamera && Camera.main) playerCamera = Camera.main.GetComponent<ThirdPersonCamera>();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (input.DodgePressed) dodgeBufferedUntil = Time.time + dodgeInputBuffer;

            Vector3 moveDir = GetCameraRelativeInput();

            bool locked = ActionLocked || IsStunned;
            if (!isDodging && !locked && !DodgeBlocked && Time.time <= dodgeBufferedUntil && controller.isGrounded && stamina.CanUse)
            {
                dodgeBufferedUntil = float.NegativeInfinity;
                StartDodge(moveDir);
            }

            Vector3 displacement = isDodging ? UpdateDodge(dt)
                : locked ? UpdateLocked(dt)
                : UpdateMovement(moveDir, dt);
            displacement += externalDisplacement;
            externalDisplacement = Vector3.zero;

            if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f; // 바닥에 붙여두기
            verticalVelocity += gravity * dt;
            displacement.y = verticalVelocity * dt;

            controller.Move(displacement);
            UpdateAnimator();
            frameSpeedCap = 1f;
        }

        Vector3 GetCameraRelativeInput()
        {
            Vector2 raw = input.Move;
            Transform cam = playerCamera ? playerCamera.transform : Camera.main ? Camera.main.transform : null;
            if (!cam) return new Vector3(raw.x, 0f, raw.y);

            Vector3 forward = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized;
            return forward * raw.y + right * raw.x; // 길이 0~1 (스틱 기울기 유지)
        }

        Vector3 UpdateMovement(Vector3 moveDir, float dt)
        {
            bool hasInput = moveDir.sqrMagnitude > 0.0001f;

            // 달리기: 스태미나가 바닥나면 버튼을 뗄 때까지 다시 못 달림
            if (!input.SprintHeld) sprintLockedOut = false;
            float speedMultiplier = SpeedMultiplier * frameSpeedCap;
            IsSprinting = input.SprintHeld && hasInput && !sprintLockedOut && stamina.CanUse && speedMultiplier >= 1f;
            if (IsSprinting)
            {
                stamina.Drain(sprintStaminaPerSecond * dt);
                if (!stamina.CanUse) sprintLockedOut = true;
            }

            float speed = (IsSprinting ? sprintSpeed : runSpeed * moveDir.magnitude) * speedMultiplier;
            Vector3 targetVelocity = hasInput ? moveDir.normalized * speed : Vector3.zero;
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, targetVelocity, acceleration * dt);

            // 회전: 락온 중(달리기 제외)엔 대상을, 아니면 이동 방향을 바라본다
            Vector3 face = Vector3.zero;
            if (TryGetLockPosition(out Vector3 lockPos) && !IsSprinting) face = lockPos - transform.position;
            else if (hasInput) face = moveDir;
            RotateTowards(face, rotationSpeed * dt);

            return horizontalVelocity * dt;
        }

        Vector3 UpdateLocked(float dt)
        {
            IsSprinting = false;
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, Vector3.zero, acceleration * dt);
            return horizontalVelocity * dt;
        }

        /// <summary>seconds 동안 경직시킨다. 진행 중인 회피는 끊긴다.</summary>
        public void Stun(float seconds)
        {
            stunnedUntil = Mathf.Max(stunnedUntil, Time.time + seconds);
            isDodging = false;
            IsSprinting = false;
            dodgeBufferedUntil = float.NegativeInfinity;
        }

        public void ClearStun() => stunnedUntil = 0f;

        /// <summary>순간이동 (리스폰 등). CharacterController는 잠깐 꺼야 위치가 바뀐다.</summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            controller.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            controller.enabled = true;
            horizontalVelocity = Vector3.zero;
            verticalVelocity = 0f;
            isDodging = false;
            Teleported?.Invoke();
        }

        /// <summary>순간이동했을 때 (카메라가 바로 따라붙는다)</summary>
        public event Action Teleported;

        /// <summary>이번 프레임에 추가로 이동시킬 양 (공격 전진 등). 충돌은 CharacterController가 처리한다.</summary>
        public void AddDisplacement(Vector3 delta) => externalDisplacement += delta;

        /// <summary>최대 maxDegrees만큼 해당 방향으로 몸을 돌린다.</summary>
        public void FaceTowards(Vector3 direction, float maxDegrees) => RotateTowards(direction, maxDegrees);

        /// <summary>
        /// 행동을 시작할 때 바라볼 방향: 락온 대상 → 이동 입력 방향 → 현재 정면 순.
        /// </summary>
        public Vector3 GetIntendedFacing()
        {
            if (TryGetLockPosition(out Vector3 lockPos))
            {
                Vector3 d = lockPos - transform.position;
                d.y = 0f;
                if (d.sqrMagnitude > 0.01f) return d.normalized;
            }
            Vector3 move = GetCameraRelativeInput();
            return move.sqrMagnitude > 0.01f ? move.normalized : transform.forward;
        }

        void StartDodge(Vector3 moveDir)
        {
            isBackstep = moveDir.sqrMagnitude < 0.01f;
            if (!stamina.TryConsume(isBackstep ? backstepStaminaCost : rollStaminaCost)) return;

            isDodging = true;
            IsSprinting = false;
            dodgeTimer = 0f;
            dodgeCurvePrev = 0f;
            horizontalVelocity = Vector3.zero;

            if (isBackstep)
            {
                dodgeDirection = -transform.forward;
                dodgeDistance = backstepDistance;
                dodgeDuration = backstepDuration;
                dodgeIFrames = backstepInvulnerability;
            }
            else
            {
                dodgeDirection = moveDir.normalized;
                dodgeDistance = rollDistance;
                dodgeDuration = rollDuration;
                dodgeIFrames = rollInvulnerability;
                // 락온이 아닐 땐 구르는 방향으로 즉시 몸을 돌린다
                if (!TryGetLockPosition(out _)) transform.rotation = Quaternion.LookRotation(dodgeDirection);
            }

            if (animator)
            {
                Vector3 local = transform.InverseTransformDirection(dodgeDirection);
                SetFloat(MoveXHash, local.x);
                SetFloat(MoveYHash, local.z);
                SetTrigger(isBackstep ? BackstepHash : RollHash);
            }
            Dodged?.Invoke(isBackstep);
        }

        Vector3 UpdateDodge(float dt)
        {
            dodgeTimer += dt;
            float t = Mathf.Clamp01(dodgeTimer / dodgeDuration);
            float c = dodgeCurve.Evaluate(t);
            Vector3 displacement = dodgeDirection * ((c - dodgeCurvePrev) * dodgeDistance);
            dodgeCurvePrev = c;

            // 락온 중 구르기: 몸은 계속 대상 쪽으로 천천히 돌린다
            if (TryGetLockPosition(out Vector3 lockPos)) RotateTowards(lockPos - transform.position, rotationSpeed * 0.5f * dt);

            if (t >= 1f) isDodging = false;
            return displacement;
        }

        void RotateTowards(Vector3 direction, float maxDegrees)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), maxDegrees);
        }

        bool TryGetLockPosition(out Vector3 position)
        {
            if (playerCamera && playerCamera.IsLockedOn)
            {
                position = playerCamera.CurrentTarget.LockPosition;
                return true;
            }
            position = default;
            return false;
        }

        void UpdateAnimator()
        {
            if (!animator) return;
            Vector3 local = transform.InverseTransformDirection(horizontalVelocity) / runSpeed;
            SetFloat(SpeedHash, horizontalVelocity.magnitude);
            if (!isDodging)
            {
                SetFloat(MoveXHash, local.x, 0.1f);
                SetFloat(MoveYHash, local.z, 0.1f);
            }
            SetBool(LockedOnHash, playerCamera && playerCamera.IsLockedOn);
        }

        void SetFloat(int hash, float v, float damp = 0f) => animator.TrySetFloat(hash, v, damp);
        void SetBool(int hash, bool v) => animator.TrySetBool(hash, v);
        void SetTrigger(int hash) => animator.TrySetTrigger(hash);
    }
}
