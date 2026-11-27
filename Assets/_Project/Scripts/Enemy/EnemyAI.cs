using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using SoulsHunter.Combat;
using SoulsHunter.Core;

namespace SoulsHunter.Enemy
{
    /// <summary>
    /// 보스/몬스터 AI. 상태: 대기 → 추적 → 공격 → (경직) → 추적 …
    /// - 공격은 거리·각도 조건이 맞는 패턴 중 가중치로 고른다.
    /// - 예고(windup) 동안 눈이 주황색, 판정(active) 동안 빨간색으로 빛난다.
    /// - 강인도가 깨지면 경직된다.
    /// - 플레이어가 죽으면 체력을 회복하고 제자리로 돌아간다 (소울류 보스 리셋).
    /// NavMeshAgent는 꺼진 상태로 두면 Start에서 켜진다 (내비메시가 먼저 만들어져야 하므로).
    /// </summary>
    [RequireComponent(typeof(Health), typeof(NavMeshAgent))]
    public class EnemyAI : MonoBehaviour
    {
        public enum State { Idle, Chase, Attack, Staggered, Dead }

        [Header("Target")]
        [SerializeField] Transform target;
        [SerializeField] float aggroRange = 18f;

        [Header("Movement")]
        [SerializeField] float moveSpeed = 4f;
        [SerializeField] float turnSpeed = 120f;
        [Tooltip("이 거리 안이면 멈춰서 공격을 고른다")]
        [SerializeField] float engageDistance = 4f;

        [Header("Behaviour")]
        [Tooltip("공격 사이 쉬는 시간(초) 범위")]
        [SerializeField] Vector2 decisionDelay = new(0.4f, 1.2f);
        [SerializeField] float staggerDuration = 1.6f;
        [Tooltip("공격 중에는 몸을 돌리지 않는다 (공격 시작 순간의 방향으로 고정)")]
        [SerializeField] bool lockFacingDuringAttack = true;
        [Tooltip("공격 중에는 제자리에 멈춘다 (돌진 공격(isDash)만 예외)")]
        [SerializeField] bool stayInPlaceDuringAttack = true;
        [SerializeField] LayerMask hitMask = ~0;

        [Header("Attacks")]
        [SerializeField] List<EnemyAttack> attacks = new()
        {
            //    이름        최소/최대거리  각도  가중치 쿨  피해  강인  예고   판정   빈틈  추적  전진  전진구간          판정중심(발 기준)    판정크기
            new("Swipe",     0f, 5f,    60f, 3f,   0f, 25f, 15f, 0.70f, 0.20f, 0.9f,   0f,  0f, Vector2.zero,    new(0f, 1f, 3.5f), new(4.5f, 2.5f, 3f)),
            new("Slam",      0f, 5.5f,  40f, 2f,   4f, 45f, 40f, 1.30f, 0.15f, 1.6f,   0f,  0f, Vector2.zero,    new(0f, 1f, 4f),   new(4f, 2f, 4f)),
            new("Charge",    7f, 16f,   25f, 2f,   6f, 35f, 40f, 1.00f, 0.70f, 1.3f,   0f, 12f, new(1.0f, 1.7f), new(0f, 1f, 2.5f), new(3f, 2f, 2f), isDash: true),
            new("Tail Spin", 0f, 4.5f, 360f, 1.5f, 5f, 30f, 30f, 0.60f, 0.30f, 1.0f,   0f,  0f, Vector2.zero,    new(0f, 1f, 0f),   new(8f, 2f, 8f)),
            // 분노 전용: 주변 전체를 내려찍는 충격파. 예고가 길어서 멀리 피하거나 패링으로 대응
            new("Quake",     0f, 7f,   360f, 2.5f, 9f, 40f, 60f, 1.20f, 0.25f, 1.4f,   0f,  0f, Vector2.zero,    new(0f, 1f, 0f),   new(13f, 2.5f, 13f)) { enragedOnly = true },
        };

        [Header("Enrage (분노 페이즈)")]
        [Tooltip("체력 비율이 이 값 이하가 되면 분노한다. 0이면 사용 안 함")]
        [SerializeField, Range(0f, 1f)] float enrageThreshold = 0f;
        [Tooltip("분노 중 공격 동작 속도 배율 (예고·판정·빈틈이 모두 짧아진다)")]
        [SerializeField] float enrageAttackSpeed = 1.3f;
        [SerializeField] float enrageMoveSpeed = 1.3f;
        [Tooltip("분노 중 공격 사이 쉬는 시간 배율")]
        [SerializeField] float enrageDecisionScale = 0.5f;
        [Tooltip("분노하는 순간 잠깐 멈춰서 포효하는 시간(초)")]
        [SerializeField] float enrageRoarDuration = 1.2f;
        [SerializeField] Color enragedIdleColor = new(0.7f, 0.05f, 0.02f);

        [Header("Telegraph (예고 표시)")]
        [SerializeField] Renderer[] telegraphRenderers;
        [SerializeField] Color idleColor = new(0.15f, 0.15f, 0.15f);
        [SerializeField] Color windupColor = new(1f, 0.55f, 0.05f);
        [SerializeField] Color activeColor = new(1f, 0.05f, 0.05f);

        [Header("Ground Telegraph (바닥 공격 범위)")]
        [SerializeField] bool showGroundTelegraph = true;
        [Tooltip("비워두면 실행 중에 만든다. 빌드에 넣으려면 에셋으로 지정")]
        [SerializeField] Material telegraphMaterial;
        [SerializeField] Color telegraphAreaColor = new(1f, 0.05f, 0.02f, 0.22f);
        [SerializeField] Color telegraphFillColor = new(1f, 0.1f, 0.02f, 0.45f);
        [SerializeField] Color telegraphEdgeColor = new(1f, 0.08f, 0.04f, 0.95f);
        [SerializeField] Color telegraphActiveColor = new(1f, 0.2f, 0.1f, 0.8f);

        AttackTelegraph groundTelegraph;

        NavMeshAgent agent;
        Health health;
        Health targetHealth;
        TrainingDummy dummy;

        State state = State.Idle;
        EnemyAttack current;
        float timer, lungePrev, nextDecisionTime, staggerEnd;
        bool hitLanded;
        bool enraged;
        Vector3 spawnPosition;
        Quaternion spawnRotation;

        readonly Collider[] overlap = new Collider[32];
        MaterialPropertyBlock block;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        public State CurrentState => state;
        public EnemyAttack CurrentAttack => current;
        public bool IsEnraged => enraged;

        /// <summary>분노 페이즈에 들어갈 때</summary>
        public event System.Action Enraged;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            health = GetComponent<Health>();
            dummy = GetComponent<TrainingDummy>();
            block = new MaterialPropertyBlock();

            agent.updateRotation = false;
            spawnPosition = transform.position;
            spawnRotation = transform.rotation;

            health.Staggered += _ => OnStaggered();
            health.Died += OnDied;
            health.Damaged += _ => CheckEnrage();
        }

        void Start()
        {
            if (!target)
            {
                var p = GameObject.FindWithTag("Player");
                if (p) target = p.transform;
            }
            if (target) targetHealth = target.GetComponent<Health>();

            if (showGroundTelegraph)
            {
                groundTelegraph = AttackTelegraph.Create(transform, telegraphMaterial);
                groundTelegraph.SetColors(telegraphAreaColor, telegraphFillColor, telegraphEdgeColor, telegraphActiveColor);
            }

            agent.speed = moveSpeed;
            agent.stoppingDistance = engageDistance * 0.8f;
            agent.enabled = true;
            if (!agent.isOnNavMesh) Debug.LogWarning($"[{name}] 내비메시 위에 있지 않아요. 아레나에 NavMesh가 있는지 확인하세요.", this);
        }

        void Update()
        {
            float dt = Time.deltaTime;

            if (state == State.Dead)
            {
                if (!health.IsDead) Revive(); // TrainingDummy가 체력을 되돌리면 부활
                UpdateTelegraph();
                return;
            }
            if (!target || !agent.isOnNavMesh) return;

            // 플레이어가 죽으면 보스 리셋
            if (targetHealth && targetHealth.IsDead)
            {
                if (state != State.Idle) ResetToSpawn();
                UpdateTelegraph();
                return;
            }

            Vector3 toTarget = target.position - Feet;
            toTarget.y = 0f;
            float dist = toTarget.magnitude;

            switch (state)
            {
                case State.Idle:
                    if (dist <= aggroRange) EnterChase(0.5f);
                    break;
                case State.Chase:
                    TickChase(toTarget, dist, dt);
                    break;
                case State.Attack:
                    TickAttack(toTarget, dt);
                    break;
                case State.Staggered:
                    if (Time.time >= staggerEnd) EnterChase(Random.Range(0.1f, 0.4f));
                    break;
            }
            UpdateTelegraph();
        }

        /// <summary>발 위치 (피벗이 몸 가운데 있어도 바닥 기준으로 계산)</summary>
        Vector3 Feet => transform.position - Vector3.up * agent.baseOffset;

        // ───────── 추적 ─────────

        void EnterChase(float delay)
        {
            state = State.Chase;
            current = null;
            nextDecisionTime = Time.time + delay;
        }

        void TickChase(Vector3 toTarget, float dist, float dt)
        {
            Turn(toTarget, turnSpeed * dt);

            if (Time.time >= nextDecisionTime)
            {
                var attack = ChooseAttack(dist, Vector3.Angle(transform.forward, toTarget));
                if (attack != null) { StartAttack(attack); return; }
            }

            if (dist > engageDistance)
            {
                agent.isStopped = false;
                agent.SetDestination(target.position);
            }
            else
            {
                agent.isStopped = true;
            }
        }

        EnemyAttack ChooseAttack(float dist, float angle)
        {
            float total = 0f;
            foreach (var a in attacks)
                if (IsUsable(a, dist, angle)) total += a.weight;
            if (total <= 0f) return null;

            float pick = Random.value * total;
            foreach (var a in attacks)
            {
                if (!IsUsable(a, dist, angle)) continue;
                pick -= a.weight;
                if (pick <= 0f) return a;
            }
            return null;
        }

        bool IsUsable(EnemyAttack a, float dist, float angle) =>
            a.weight > 0f && Time.time >= a.readyTime && (!a.enragedOnly || enraged) &&
            dist >= a.minRange && dist <= a.maxRange && angle <= a.maxAngle * 0.5f + 0.01f;

        // ───────── 공격 ─────────

        void StartAttack(EnemyAttack attack)
        {
            state = State.Attack;
            current = attack;
            timer = 0f;
            lungePrev = 0f;
            hitLanded = false;
            attack.readyTime = Time.time + attack.cooldown;
            agent.isStopped = true;
            agent.ResetPath();
            agent.velocity = Vector3.zero; // 미끄러지지 않고 그 자리에서 바로 멈춤
        }

        /// <summary>이 공격에서 실제로 전진할 거리</summary>
        float EffectiveLunge(EnemyAttack a)
        {
            if (stayInPlaceDuringAttack && !a.isDash) return 0f;
            return a.lungeWindow.y > a.lungeWindow.x ? a.lunge : 0f;
        }

        void TickAttack(Vector3 toTarget, float dt)
        {
            var a = current;
            timer += dt * (enraged ? enrageAttackSpeed : 1f);

            if (!lockFacingDuringAttack && timer < a.windup && a.trackingSpeed > 0f) Turn(toTarget, a.trackingSpeed * dt);

            // 전진 (돌진 공격은 공격 시작 방향으로 일직선)
            float lunge = EffectiveLunge(a);
            if (lunge > 0f)
            {
                float p = Mathf.Clamp01(Mathf.InverseLerp(a.lungeWindow.x, a.lungeWindow.y, timer));
                if (p > lungePrev)
                {
                    agent.Move(transform.forward * ((p - lungePrev) * lunge));
                    lungePrev = p;
                }
            }

            if (!hitLanded && timer >= a.windup && timer < a.windup + a.active) CheckHit(a);

            if (timer >= a.Total)
                EnterChase(Random.Range(decisionDelay.x, decisionDelay.y) * (enraged ? enrageDecisionScale : 1f));
        }

        void CheckHit(EnemyAttack a)
        {
            Vector3 center = HitboxCenter(a);
            int count = Physics.OverlapBoxNonAlloc(center, a.hitboxSize * 0.5f, overlap, transform.rotation, hitMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var hb = overlap[i].GetComponent<Hurtbox>();
                if (!hb || hb.Health != targetHealth) continue;

                var info = new DamageInfo
                {
                    Amount = a.damage,
                    PoiseDamage = a.poiseDamage,
                    Point = overlap[i].ClosestPoint(center),
                    Direction = (target.position - transform.position).normalized,
                    Source = gameObject,
                };
                // 구르기 무적으로 피하면 false → 판정 시간 안에선 계속 검사한다
                if (hb.ReceiveHit(info)) hitLanded = true;
                return;
            }
        }

        Vector3 HitboxCenter(EnemyAttack a) => Feet + transform.rotation * a.hitboxCenter;

        // ───────── 경직 / 죽음 / 리셋 ─────────

        void OnStaggered()
        {
            if (state == State.Dead) return;
            state = State.Staggered;
            current = null;
            staggerEnd = Time.time + staggerDuration;
            if (agent.isOnNavMesh) { agent.isStopped = true; agent.ResetPath(); }
        }

        void CheckEnrage()
        {
            if (enraged || enrageThreshold <= 0f || health.IsDead || state == State.Dead) return;
            if (health.Normalized > enrageThreshold) return;

            enraged = true;
            agent.speed = moveSpeed * enrageMoveSpeed;
            // 포효: 하던 공격을 끊고 잠깐 멈춘다 (플레이어가 숨 돌릴 틈)
            if (enrageRoarDuration > 0f)
            {
                state = State.Staggered;
                current = null;
                staggerEnd = Time.time + enrageRoarDuration;
                if (agent.isOnNavMesh) { agent.isStopped = true; agent.ResetPath(); }
            }
            GameEvents.Notify($"<color=#FF5544>{health.DisplayName}이(가) 분노했다!</color>");
            Enraged?.Invoke();
        }

        void ClearEnrage()
        {
            enraged = false;
            agent.speed = moveSpeed;
        }

        void OnDied()
        {
            state = State.Dead;
            current = null;
            if (agent.isOnNavMesh) { agent.isStopped = true; agent.ResetPath(); }
        }

        void Revive()
        {
            if (agent.isOnNavMesh) agent.Warp(spawnPosition);
            transform.rotation = spawnRotation;
            state = State.Idle;
            ClearEnrage();
        }

        void ResetToSpawn()
        {
            ClearEnrage();
            if (dummy) dummy.ResetAll();
            else
            {
                health.ResetHealth();
                foreach (var hb in GetComponentsInChildren<Hurtbox>()) hb.ResetPart();
            }
            foreach (var a in attacks) a.readyTime = 0f;
            agent.Warp(spawnPosition);
            transform.rotation = spawnRotation;
            state = State.Idle;
            current = null;
        }

        // ───────── 보조 ─────────

        void Turn(Vector3 direction, float maxDegrees)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), maxDegrees);
        }

        void OnEnable() => GameEvents.WorldReset += OnWorldReset;
        void OnDisable() => GameEvents.WorldReset -= OnWorldReset;

        void OnWorldReset()
        {
            if (agent && agent.enabled && agent.isOnNavMesh) ResetToSpawn();
        }

        void OnDestroy()
        {
            if (groundTelegraph) Destroy(groundTelegraph.gameObject);
        }

        void UpdateGroundTelegraph()
        {
            if (!groundTelegraph) return;
            var a = current;
            if (state != State.Attack || a == null || timer >= a.windup + a.active)
            {
                groundTelegraph.Hide();
                return;
            }

            // 남은 전진 거리만큼 영역을 앞으로 늘려서 돌진 경로까지 보여준다
            float remainingLunge = EffectiveLunge(a) * (1f - lungePrev);
            float depth = a.hitboxSize.z + remainingLunge;
            Vector3 localCenter = new(a.hitboxCenter.x, 0f, a.hitboxCenter.z + remainingLunge * 0.5f);
            Vector3 center = Feet + transform.rotation * localCenter;

            bool active = timer >= a.windup;
            float progress = a.windup > 0f ? timer / a.windup : 1f;
            groundTelegraph.Show(center, transform.rotation, new Vector2(a.hitboxSize.x, depth), progress, active);
        }

        void UpdateTelegraph()
        {
            UpdateGroundTelegraph();
            if (telegraphRenderers == null || telegraphRenderers.Length == 0) return;

            Color baseColor = enraged ? enragedIdleColor : idleColor;
            Color c = baseColor;
            if (state == State.Attack && current != null)
            {
                if (timer < current.windup)
                {
                    // 판정 직전으로 갈수록 빠르게 깜빡인다
                    float t = timer / current.windup;
                    float pulse = 0.5f + 0.5f * Mathf.Sin(timer * Mathf.Lerp(8f, 30f, t));
                    c = Color.Lerp(baseColor, windupColor, Mathf.Lerp(0.4f, 1f, pulse));
                }
                else if (timer < current.windup + current.active) c = activeColor;
            }

            foreach (var r in telegraphRenderers)
            {
                if (!r) continue;
                r.GetPropertyBlock(block);
                block.SetColor(BaseColorId, c);
                block.SetColor(EmissionId, c * 2f);
                r.SetPropertyBlock(block);
            }
        }

#if UNITY_EDITOR
        /// <summary>에디터 코드(테스트 아레나)에서 몬스터 종류별로 설정할 때 사용</summary>
        public void EditorSetup(List<EnemyAttack> attackList, float speed, float engage, float aggro,
                                float staggerTime, float enrageAt)
        {
            attacks = attackList;
            moveSpeed = speed;
            engageDistance = engage;
            aggroRange = aggro;
            staggerDuration = staggerTime;
            enrageThreshold = enrageAt;
        }
#endif

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, aggroRange);

            if (current == null || !agent) return;
            bool active = timer >= current.windup && timer < current.windup + current.active;
            Gizmos.color = active ? new Color(1f, 0f, 0f, 0.5f) : new Color(1f, 0.5f, 0f, 0.25f);
            Gizmos.matrix = Matrix4x4.TRS(HitboxCenter(current), transform.rotation, Vector3.one);
            Gizmos.DrawCube(Vector3.zero, current.hitboxSize);
        }
    }
}
