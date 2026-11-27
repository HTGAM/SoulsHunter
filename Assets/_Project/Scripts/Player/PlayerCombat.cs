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

        [Header("Charge (버튼을 누르고 있으면 예고 끝에서 멈춰 힘을 모은다)")]
        [Tooltip("0이면 모으기 없음. 단계마다 피해·강인 피해가 늘어난다")]
        public int chargeLevels = 0;
        public float chargeTimePerLevel = 0.5f;
        [Tooltip("단계당 피해 증가 (0.5 = 단계마다 +50%)")]
        public float chargeDamagePerLevel = 0.5f;
        public float chargePoisePerLevel = 0.5f;

        [Header("Aerial")]
        [Tooltip("공중 공격이 맞으면 이 속도로 다시 튀어오른다 (0이면 없음)")]
        public float bounceOnHit = 0f;

        public float Total => windup + active + recovery;

        /// <summary>복사본 (에디터에서 프리셋을 만들 때)</summary>
        public AttackStep Clone() => (AttackStep)MemberwiseClone();

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
    /// 약공격/강공격 콤보. 동작은 장착한 무기(WeaponData)를 따른다.
    /// 공격 중 다음 버튼을 미리 눌러두면 후딜 시작 시 이어진다. 후딜 후반에는 구르기나 이동으로 캔슬할 수 있다.
    /// 모으기 공격(대검·해머)은 버튼을 누르고 있으면 예고 끝에서 멈춰 단계별로 힘을 모은다.
    /// </summary>
    [DefaultExecutionOrder(-50)] // PlayerLocomotion보다 먼저 ActionLocked를 정한다
    [RequireComponent(typeof(PlayerLocomotion), typeof(PlayerInputHandler), typeof(PlayerStamina))]
    public class PlayerCombat : MonoBehaviour
    {
        public enum AttackType { Light, Heavy, Special }

        [Tooltip("무기(WeaponData)가 없을 때 쓰는 기본 동작")]
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
        [Tooltip("후딜이 이만큼(0~1) 진행된 뒤에야 다음 콤보·이동으로 넘어간다. 1이면 동작이 완전히 끝나야 함 (구르기는 예외)")]
        [SerializeField, Range(0f, 1f)] float comboChainPoint = 0.85f;
        [Tooltip("켜면 공격마다 AttackStep의 스태미나를 쓴다 (소울류). 끄면 몬헌식: 공격은 스태미나를 쓰지 않는다")]
        [SerializeField] bool attacksUseStamina = false;
        [Tooltip("모으기 최대 단계에서 이 시간이 지나면 저절로 내려친다")]
        [SerializeField] float maxChargeHold = 0.4f;
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

        // 모으기
        bool charging, chargeDone;
        float chargeTime;
        int chargeLevel;

        AttackType? bufferedAttack;
        float bufferedUntil;

        readonly Collider[] overlap = new Collider[32];
        readonly HashSet<Health> hitThisSwing = new();
        Coroutine hitStopRoutine;

        public bool IsAttacking => current != null;
        public AttackStep CurrentAttack => current;
        public bool InActiveFrames => IsAttacking && CurrentWindow() >= 0;

        // 타격 구간 (애니메이션이 정해줌). 비어 있으면 AttackStep의 windup/active 하나만 쓴다.
        readonly List<(float start, float end, float damageScale)> hitWindows = new();
        int activeWindow = -1;

        /// <summary>
        /// 이번 공격의 타격 구간들을 정한다 (공격 타이머 기준 초). 한 동작에 여러 번 휘두르면 구간마다 따로 맞는다.
        /// PlayerAnimationDriver가 공격 시작 때 애니메이션의 타격 지점으로 채운다.
        /// </summary>
        public void SetHitWindows(IEnumerable<(float start, float end, float damageScale)> windows)
        {
            hitWindows.Clear();
            hitWindows.AddRange(windows);
            hitWindows.Sort((a, b) => a.start.CompareTo(b.start));
            activeWindow = -1;
        }

        /// <summary>첫 타격이 시작되는 시간 (선딜 끝)</summary>
        float Windup => hitWindows.Count > 0 ? hitWindows[0].start : current.windup;
        /// <summary>마지막 타격이 끝나는 시간 (후딜 시작)</summary>
        float ActiveEnd => hitWindows.Count > 0 ? hitWindows[^1].end : current.windup + current.active;

        int CurrentWindow()
        {
            if (current == null) return -1;
            if (hitWindows.Count == 0) return timer >= current.windup && timer < current.windup + current.active ? 0 : -1;
            for (int i = 0; i < hitWindows.Count; i++)
                if (timer >= hitWindows[i].start && timer < hitWindows[i].end) return i;
            return -1;
        }
        public bool IsCharging => charging;
        /// <summary>현재 공격 경과 시간(초). 모으기 중엔 멈춘다 (애니메이션 동기화용)</summary>
        public float AttackTimer => timer;
        public AttackType CurrentType => currentType;
        public int ComboIndex => comboIndex;
        public WeaponData EquippedWeapon => Weapon;
        public int ChargeLevel => chargeLevel;

        /// <summary>특수 행동(귀인화 등)이 바꾸는 피해 배율</summary>
        public float ExternalDamageMultiplier { get; set; } = 1f;
        /// <summary>특수 행동(귀인화 등)이 바꾸는 공격 속도 배율</summary>
        public float AttackSpeedMultiplier { get; set; } = 1f;

        /// <summary>
        /// 공격 애니메이션 길이에 맞춰 공격 시간 전체를 늘이거나 줄이는 배율 (PlayerAnimationDriver가 공격마다 설정).
        /// 0.5면 코드에 적힌 시간보다 두 배 천천히 진행된다.
        /// </summary>
        public float AnimationTimeScale { get; set; } = 1f;

        WeaponData Weapon => weapon ? weapon.Equipped : null;
        List<AttackStep> LightCombo => Weapon && Weapon.lightCombo.Count > 0 ? Weapon.lightCombo : lightCombo;
        List<AttackStep> HeavyCombo => Weapon && Weapon.heavyCombo.Count > 0 ? Weapon.heavyCombo : heavyCombo;

        /// <summary>공격 시작 (공격 종류, 콤보 단계 0~)</summary>
        public event Action<AttackType, int> AttackStarted;
        /// <summary>공격이 무언가에 맞았을 때</summary>
        public event Action<Hurtbox, DamageInfo> HitLanded;
        /// <summary>모으기 단계가 올라갔을 때 (단계, 최대 단계). 0이면 모으기 끝.</summary>
        public event Action<int, int> ChargeChanged;

        static readonly int LightAttackHash = Animator.StringToHash("LightAttack");
        static readonly int HeavyAttackHash = Animator.StringToHash("HeavyAttack");
        static readonly int SpecialAttackHash = Animator.StringToHash("SpecialAttack");
        static readonly int ComboIndexHash = Animator.StringToHash("ComboIndex");
        static readonly int ChargeHash = Animator.StringToHash("Charging");

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
            if (weapon) weapon.WeaponChanged += _ => { EndAttack(); comboIndex = -1; };
        }

        void OnDisable()
        {
            if (hitStopRoutine != null) { StopCoroutine(hitStopRoutine); Time.timeScale = GameState.IsPaused ? 0f : 1f; }
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

            if (IsAttacking) TickAttack(Time.deltaTime * AttackSpeedMultiplier * AnimationTimeScale);

            if (!IsAttacking && bufferedAttack.HasValue && !locomotion.IsDodging && !(flask && flask.IsDrinking))
            {
                var type = bufferedAttack.Value;
                bufferedAttack = null;

                // 조충곤: 공중에서 약공격 = 공중 베기
                if (type == AttackType.Light && !locomotion.IsGrounded && Weapon && Weapon.special == WeaponData.Special.Vault)
                    StartSpecialAttack(Weapon.specialAttack);
                else
                {
                    bool continues = type == currentType && Time.time - lastAttackEndTime <= comboResetTime;
                    StartAttack(type, continues ? comboIndex + 1 : 0);
                }
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
            var combo = type == AttackType.Light ? LightCombo : HeavyCombo;
            if (combo.Count == 0) return;
            if (index >= combo.Count) index = 0;
            if (attacksUseStamina && !stamina.TryConsume(combo[index].staminaCost)) return;
            Begin(combo[index], type, index);
        }

        /// <summary>
        /// 콤보와 상관없는 공격을 바로 시작한다 (랜스 반격, 조충곤 공중 베기 등).
        /// 스태미나는 부르는 쪽에서 처리한다.
        /// </summary>
        public void StartSpecialAttack(AttackStep step)
        {
            if (step == null || step.Total <= 0f) return;
            EndAttack();
            Begin(step, AttackType.Special, 0);
        }

        void Begin(AttackStep step, AttackType type, int index)
        {
            current = step;
            hitWindows.Clear(); // AttackStarted를 받은 애니메이션 쪽이 다시 채운다
            activeWindow = -1;
            currentType = type;
            comboIndex = index;
            timer = 0f;
            lungeProgress = 0f;
            charging = chargeDone = false;
            chargeTime = 0f;
            chargeLevel = 0;
            hitThisSwing.Clear();

            // 시작할 때 락온 대상/입력 방향으로 바로 몸을 돌린다
            locomotion.FaceTowards(locomotion.GetIntendedFacing(), 360f);

            var anim = locomotion.Animator;
            if (anim)
            {
                anim.TrySetInt(ComboIndexHash, index);
                anim.TrySetTrigger(type switch
                {
                    AttackType.Light => LightAttackHash,
                    AttackType.Heavy => HeavyAttackHash,
                    _ => SpecialAttackHash,
                });
            }
            AttackStarted?.Invoke(type, index);
        }

        void TickAttack(float dt)
        {
            var a = current;

            // 모으기: 예고 끝에서 버튼을 누르고 있으면 멈춰서 힘을 모은다
            if (a.chargeLevels > 0 && !chargeDone && timer + dt >= Windup)
            {
                bool held = currentType == AttackType.Heavy ? input.HeavyAttackHeld : input.LightAttackHeld;
                // 모으는 시간은 애니메이션 배율과 상관없이 실제 시간 기준
                if (held && TickCharge(a, dt / Mathf.Max(0.01f, AnimationTimeScale))) return;
                EndCharge();
            }

            float prev = timer;
            timer += dt;

            // 선딜 중엔 대상 쪽으로 조금씩 돌 수 있다 (소울류 '트래킹')
            if (timer < Windup) locomotion.FaceTowards(locomotion.GetIntendedFacing(), a.trackingSpeed * dt);

            // 선딜~첫 타격 동안 전진
            float lungeTime = hitWindows.Count > 0 ? hitWindows[0].end : a.windup + a.active;
            if (a.lunge > 0f && prev < lungeTime)
            {
                float p = Mathf.Clamp01(timer / lungeTime);
                float eased = 1f - (1f - p) * (1f - p); // 처음에 빠르게
                locomotion.AddDisplacement(transform.forward * ((eased - lungeProgress) * a.lunge));
                lungeProgress = eased;
            }

            // 타격 구간마다 새로 맞힐 수 있다 (한 동작 여러 번 휘두르기)
            int window = CurrentWindow();
            if (window >= 0)
            {
                if (window != activeWindow)
                {
                    activeWindow = window;
                    hitThisSwing.Clear();
                }
                DoHitDetection(hitWindows.Count > 0 ? hitWindows[window].damageScale : 1f);
            }

            // 콤보 선입력은 후딜이 거의 끝날 때(comboChainPoint)까지 기다렸다가 이어진다.
            // 애니메이션이 중간에 끊기지 않고 마무리 동작까지 보이게 하기 위함.
            float chainTime = ActiveEnd + Mathf.Max(0f, a.Total - ActiveEnd) * comboChainPoint;
            bool canChain = timer >= chainTime;
            if (canChain && bufferedAttack.HasValue && currentType != AttackType.Special)
            {
                var type = bufferedAttack.Value;
                bufferedAttack = null;
                StartAttack(type, type == currentType ? comboIndex + 1 : 0);
                return;
            }

            // 이동으로 끊는 것도 같은 지점부터 (구르기는 cancelableAfter부터 바로 가능)
            if (timer >= Mathf.Max(a.cancelableAfter, chainTime) && input.Move.sqrMagnitude > 0.04f)
            {
                EndAttack();
                return;
            }

            if (timer >= a.Total) EndAttack();
        }

        /// <returns>계속 모으는 중이면 true (이번 프레임 공격 진행을 멈춘다)</returns>
        bool TickCharge(AttackStep a, float dt)
        {
            if (!charging)
            {
                charging = true;
                locomotion.Animator.TrySetBool(ChargeHash, true);
            }
            chargeTime += dt;
            locomotion.FaceTowards(locomotion.GetIntendedFacing(), a.trackingSpeed * dt);

            int level = Mathf.Min(a.chargeLevels, Mathf.FloorToInt(chargeTime / Mathf.Max(0.05f, a.chargeTimePerLevel)));
            if (level != chargeLevel)
            {
                chargeLevel = level;
                ChargeChanged?.Invoke(chargeLevel, a.chargeLevels);
            }
            // 최대 단계에서 너무 오래 들고 있으면 저절로 내려친다
            return chargeTime < a.chargeLevels * a.chargeTimePerLevel + maxChargeHold;
        }

        void EndCharge()
        {
            chargeDone = true;
            if (charging)
            {
                charging = false;
                locomotion.Animator.TrySetBool(ChargeHash, false);
            }
        }

        void DoHitDetection(float damageScale)
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

            float chargeMult = 1f + chargeLevel * a.chargeDamagePerLevel;
            float poiseMult = 1f + chargeLevel * a.chargePoisePerLevel;
            float damage = a.damage * (weapon ? weapon.DamageMultiplier : 1f) * ExternalDamageMultiplier * chargeMult * damageScale;
            poiseMult *= damageScale;

            bool anyHit = false;
            foreach (var kv in best)
            {
                hitThisSwing.Add(kv.Key);
                var info = new DamageInfo
                {
                    Amount = damage,
                    PoiseDamage = a.poiseDamage * poiseMult,
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
            if (!anyHit) return;
            if (a.bounceOnHit > 0f) locomotion.Bounce(a.bounceOnHit);
            float stop = a.hitStop * (1f + chargeLevel * 0.3f);
            if (stop > 0f) HitStop(stop);
        }

        void EndAttack()
        {
            if (current == null) return;
            if (charging) EndCharge();
            if (chargeLevel > 0) ChargeChanged?.Invoke(0, 0);
            chargeLevel = 0;
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
            Time.timeScale = GameState.IsPaused ? 0f : 1f; // 그사이 일시정지했으면 멈춘 채로 둔다
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
