using System;
using UnityEngine;
using UnityEngine.InputSystem;
using SoulsHunter.Combat;
using SoulsHunter.Player;

namespace SoulsHunter.CameraSystem
{
    /// <summary>
    /// 소울/몬헌 스타일 3인칭 카메라. Main Camera에 붙인다.
    /// - 자유 시점 궤도 회전, 벽 충돌 시 앞으로 당겨짐
    /// - 락온: 화면 중앙에 가까운 적을 잡고, 시점을 좌우로 튕기면 대상 전환
    /// - 락온할 대상이 없으면 카메라를 캐릭터 뒤로 리셋
    /// ESC로 커서 풀기, 게임 화면 클릭으로 다시 잠금.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class ThirdPersonCamera : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] Transform target;
        [SerializeField] PlayerInputHandler input;
        [Tooltip("캐릭터 발밑 기준 카메라가 도는 중심 높이")]
        [SerializeField] float pivotHeight = 1.6f;
        [SerializeField] float followSmoothTime = 0.06f;

        [Header("Orbit")]
        [SerializeField] float distance = 4.5f;
        [SerializeField] Vector2 pitchLimits = new(-35f, 70f);
        [SerializeField] float defaultPitch = 12f;

        [Header("Collision")]
        [SerializeField] LayerMask collisionMask = ~0;
        [SerializeField] float collisionRadius = 0.25f;
        [SerializeField] float minDistance = 0.6f;
        [Tooltip("벽에서 벗어났을 때 원래 거리로 돌아가는 속도(m/s)")]
        [SerializeField] float distanceReturnSpeed = 6f;
        [Tooltip("몬스터에 가려도 카메라를 당기지 않는다 (큰 몬스터 상대 시 카메라 흔들림 방지)")]
        [SerializeField] bool ignoreEnemiesForCollision = true;

        [Header("Lock-On")]
        [SerializeField] float lockOnRange = 20f;
        [SerializeField] float lockBreakRange = 26f;
        [Tooltip("시야가 가려진 채 이 시간이 지나면 락온 해제")]
        [SerializeField] float lostSightTimeout = 2f;
        [SerializeField] float lockRotationSharpness = 10f;
        [Tooltip("대상보다 얼마나 위에서 내려다볼지(도)")]
        [SerializeField] float lockPitchOffset = 10f;
        [Tooltip("락온 중 시점을 이만큼(도) 튕기면 옆 대상으로 전환")]
        [SerializeField] float switchThreshold = 5f;
        [SerializeField] float switchCooldown = 0.35f;
        [SerializeField] LayerMask lineOfSightMask = ~0;
        [Tooltip("락온 표시용 오브젝트 (선택)")]
        [SerializeField] Transform lockMarker;

        [Header("Recenter (락온 대상이 없을 때)")]
        [SerializeField] float recenterSharpness = 12f;

        public LockOnTarget CurrentTarget { get; private set; }
        public bool IsLockedOn => CurrentTarget != null;
        public event Action<LockOnTarget> TargetChanged;

        Camera cam;
        float yaw, pitch;
        Vector3 pivot, pivotVelocity;
        float currentDistance;
        float switchAccumulator, lastSwitchTime, lostSightTime;
        bool recentering;
        readonly RaycastHit[] hits = new RaycastHit[16];

        void Awake() => cam = GetComponent<Camera>();

        void Start()
        {
            if (!target)
            {
                var player = GameObject.FindWithTag("Player");
                if (player) target = player.transform;
            }
            if (target && !input) input = target.GetComponent<PlayerInputHandler>();

            if (target)
            {
                yaw = target.eulerAngles.y;
                pitch = defaultPitch;
                pivot = target.position + Vector3.up * pivotHeight;
            }
            currentDistance = distance;
            SetCursorLocked(true);
            if (lockMarker) lockMarker.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            if (!target) return;
            float dt = Time.deltaTime;

            HandleCursor();
            Vector2 look = input ? input.LookDelta : Vector2.zero;

            if (input && input.LockOnPressed) ToggleLockOn();
            ValidateLock(dt);

            Vector3 desiredPivot = target.position + Vector3.up * pivotHeight;
            pivot = Vector3.SmoothDamp(pivot, desiredPivot, ref pivotVelocity, followSmoothTime);

            if (IsLockedOn)
            {
                HandleTargetSwitch(look.x, dt);
                RotateTowardsTarget(dt);
            }
            else
            {
                if (look.sqrMagnitude > 0.0001f) recentering = false;
                if (recentering) Recenter(dt);
                yaw += look.x;
                pitch -= look.y;
            }
            yaw = Mathf.Repeat(yaw, 360f);
            pitch = Mathf.Clamp(pitch, pitchLimits.x, pitchLimits.y);

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 back = rotation * Vector3.back;
            float allowed = ResolveCollision(pivot, back, distance);
            currentDistance = allowed < currentDistance
                ? allowed // 벽에 막히면 즉시 당김
                : Mathf.MoveTowards(currentDistance, allowed, distanceReturnSpeed * dt);

            transform.SetPositionAndRotation(pivot + back * currentDistance, rotation);
            UpdateMarker();
        }

        // ───────── 락온 ─────────

        void ToggleLockOn()
        {
            if (IsLockedOn) { SetTarget(null); return; }

            var best = FindTarget(0);
            if (best) SetTarget(best);
            else recentering = true; // 대상이 없으면 캐릭터 뒤로 시점 리셋
        }

        void SetTarget(LockOnTarget t)
        {
            if (CurrentTarget == t) return;
            CurrentTarget = t;
            lostSightTime = 0f;
            switchAccumulator = 0f;
            TargetChanged?.Invoke(t);
        }

        void ValidateLock(float dt)
        {
            if (!IsLockedOn) return;
            Vector3 p = CurrentTarget.LockPosition;
            if (!CurrentTarget.CanBeTargeted || Vector3.Distance(target.position, p) > lockBreakRange)
            {
                SetTarget(null);
                return;
            }
            lostSightTime = HasLineOfSight(p, CurrentTarget) ? 0f : lostSightTime + dt;
            if (lostSightTime > lostSightTimeout) SetTarget(null);
        }

        void HandleTargetSwitch(float lookX, float dt)
        {
            // 입력을 모았다가 일정량 넘으면 전환. 가만히 두면 서서히 0으로.
            switchAccumulator = Mathf.MoveTowards(switchAccumulator + lookX, 0f, switchThreshold * 2f * dt);
            if (Time.time - lastSwitchTime < switchCooldown || Mathf.Abs(switchAccumulator) < switchThreshold) return;

            var next = FindTarget(switchAccumulator > 0f ? 1 : -1);
            switchAccumulator = 0f;
            if (!next) return;
            SetTarget(next);
            lastSwitchTime = Time.time;
        }

        /// <param name="side">0 = 화면 중앙에 가장 가까운 대상, ±1 = 현재 대상의 오른쪽/왼쪽</param>
        LockOnTarget FindTarget(int side)
        {
            LockOnTarget best = null;
            float bestScore = float.MaxValue;
            float currentX = side != 0 && IsLockedOn ? cam.WorldToViewportPoint(CurrentTarget.LockPosition).x : 0.5f;

            foreach (var t in LockOnTarget.Active)
            {
                if (!t.CanBeTargeted || (side != 0 && t == CurrentTarget)) continue;
                Vector3 p = t.LockPosition;
                float dist = Vector3.Distance(target.position, p);
                if (dist > lockOnRange) continue;

                Vector3 vp = cam.WorldToViewportPoint(p);
                if (vp.z <= 0f || vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f) continue;
                if (!HasLineOfSight(p, t)) continue;

                float score;
                if (side == 0)
                {
                    float screenOffset = Mathf.Abs(vp.x - 0.5f) + Mathf.Abs(vp.y - 0.5f) * 0.5f;
                    score = screenOffset * lockOnRange + dist * 0.5f;
                }
                else
                {
                    float dx = vp.x - currentX;
                    if (dx * side <= 0.01f) continue; // 반대쪽은 제외
                    score = Mathf.Abs(dx) * lockOnRange + dist * 0.25f;
                }

                if (score < bestScore) { bestScore = score; best = t; }
            }
            return best;
        }

        void RotateTowardsTarget(float dt)
        {
            Vector3 toTarget = CurrentTarget.LockPosition - pivot;
            float horizontal = new Vector2(toTarget.x, toTarget.z).magnitude;
            float k = 1f - Mathf.Exp(-lockRotationSharpness * dt);

            if (horizontal > 0.5f) // 너무 가까우면 yaw가 튀므로 유지
            {
                float desiredYaw = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
                yaw = Mathf.LerpAngle(yaw, desiredYaw, k);
            }
            float desiredPitch = -Mathf.Atan2(toTarget.y, Mathf.Max(horizontal, 0.01f)) * Mathf.Rad2Deg + lockPitchOffset;
            pitch = Mathf.LerpAngle(pitch, desiredPitch, k);
        }

        void Recenter(float dt)
        {
            float k = 1f - Mathf.Exp(-recenterSharpness * dt);
            yaw = Mathf.LerpAngle(yaw, target.eulerAngles.y, k);
            pitch = Mathf.LerpAngle(pitch, defaultPitch, k);
            if (Mathf.Abs(Mathf.DeltaAngle(yaw, target.eulerAngles.y)) < 0.5f &&
                Mathf.Abs(pitch - defaultPitch) < 0.5f)
                recentering = false;
        }

        bool HasLineOfSight(Vector3 point, LockOnTarget t)
        {
            Vector3 from = target.position + Vector3.up * pivotHeight;
            Vector3 dir = point - from;
            int count = Physics.RaycastNonAlloc(from, dir.normalized, hits, dir.magnitude, lineOfSightMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Transform h = hits[i].transform;
                if (h.IsChildOf(target) || h.IsChildOf(t.transform)) continue;
                return false;
            }
            return true;
        }

        void UpdateMarker()
        {
            if (!lockMarker) return;
            lockMarker.gameObject.SetActive(IsLockedOn);
            if (IsLockedOn) lockMarker.position = CurrentTarget.LockPosition;
        }

        // ───────── 충돌 ─────────

        float ResolveCollision(Vector3 origin, Vector3 direction, float maxDistance)
        {
            int count = Physics.SphereCastNonAlloc(origin, collisionRadius, direction, hits, maxDistance, collisionMask, QueryTriggerInteraction.Ignore);
            float closest = maxDistance;
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = hits[i];
                if (h.distance <= 0f) continue; // 시작 지점에서 이미 겹친 콜라이더
                if (h.transform.IsChildOf(target)) continue;
                if (ignoreEnemiesForCollision && h.collider.GetComponentInParent<LockOnTarget>()) continue;
                closest = Mathf.Min(closest, h.distance);
            }
            return Mathf.Max(minDistance, closest);
        }

        // ───────── 커서 ─────────

        void HandleCursor()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) SetCursorLocked(false);
            else if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
                SetCursorLocked(true);
        }

        static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        /// <summary>코드에서 대상 설정 (컷신, 보스 등장 등)</summary>
        public void Configure(Transform followTarget, PlayerInputHandler inputHandler)
        {
            target = followTarget;
            input = inputHandler;
        }
    }
}
