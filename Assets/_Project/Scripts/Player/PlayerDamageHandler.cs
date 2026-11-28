using System;
using System.Collections;
using UnityEngine;
using SoulsHunter.Combat;
using SoulsHunter.Core;

namespace SoulsHunter.Player
{
    /// <summary>
    /// 플레이어 피격 반응: 넉백, 강인도가 깨지면 경직, 죽으면 잠시 후 시작 지점에서 부활.
    /// </summary>
    [RequireComponent(typeof(Health), typeof(PlayerLocomotion))]
    public class PlayerDamageHandler : MonoBehaviour
    {
        [Header("Hit")]
        [SerializeField] float knockbackDistance = 0.4f;
        [SerializeField] float knockbackDuration = 0.15f;
        [Tooltip("맞은 뒤 잠깐 무적 (연속 판정·겹친 공격에 한 번에 녹지 않게)")]
        [SerializeField] float hitInvulnerability = 0.45f;
        [Tooltip("강인도가 깨져 경직됐을 때의 무적 (경직이 끝나고 조금 더)")]
        [SerializeField] float staggerInvulnerability = 0.9f;
        [Tooltip("부활 직후 무적")]
        [SerializeField] float respawnInvulnerability = 1.5f;

        [Header("Stagger (강인도 붕괴)")]
        [SerializeField] float staggerDuration = 0.6f;
        [SerializeField] float staggerKnockback = 1.5f;

        [Header("Death")]
        [SerializeField] float respawnDelay = 4f;
        [Tooltip("비워두면 게임 시작 위치")]
        [SerializeField] Transform respawnPoint;

        Health health;
        PlayerLocomotion locomotion;
        PlayerStamina stamina;
        Vector3 startPosition;
        Quaternion startRotation;
        Coroutine knockback;
        float invulnerableUntil = float.NegativeInfinity;

        /// <summary>피격·경직·부활 직후의 짧은 무적 중인가</summary>
        public bool IsHitInvulnerable => Time.time < invulnerableUntil;

        void GrantInvulnerability(float seconds) =>
            invulnerableUntil = Mathf.Max(invulnerableUntil, Time.time + seconds);

        public event Action Respawned;

        static readonly int HitHash = Animator.StringToHash("Hit");
        static readonly int StaggerHash = Animator.StringToHash("Stagger");
        static readonly int DeadHash = Animator.StringToHash("Dead");

        void Awake()
        {
            health = GetComponent<Health>();
            locomotion = GetComponent<PlayerLocomotion>();
            stamina = GetComponent<PlayerStamina>();
            startPosition = transform.position;
            startRotation = transform.rotation;

            health.Damaged += OnDamaged;
            health.Staggered += OnStaggered;
            health.Died += OnDied;
        }

        void OnDamaged(DamageInfo info)
        {
            if (health.IsDead) return;
            GrantInvulnerability(hitInvulnerability);
            Push(info.Direction, knockbackDistance, knockbackDuration);
            locomotion.Animator.TrySetTrigger(HitHash);
        }

        void OnStaggered(DamageInfo info)
        {
            if (health.IsDead) return;
            GrantInvulnerability(staggerInvulnerability);
            locomotion.Stun(staggerDuration);
            Push(info.Direction, staggerKnockback, staggerDuration * 0.5f);
            locomotion.Animator.TrySetTrigger(StaggerHash);
        }

        void OnDied()
        {
            locomotion.Stun(float.MaxValue);
            locomotion.Animator.TrySetBool(DeadHash, true);
            StartCoroutine(RespawnRoutine());
        }

        IEnumerator RespawnRoutine()
        {
            yield return new WaitForSeconds(respawnDelay);
            Vector3 pos = respawnPoint ? respawnPoint.position : startPosition;
            Quaternion rot = respawnPoint ? respawnPoint.rotation : startRotation;
            if (hasCustomRespawn) { pos = customRespawnPos; rot = customRespawnRot; }
            locomotion.Teleport(pos, rot);
            locomotion.ClearStun();
            health.ResetHealth();
            invulnerableUntil = float.NegativeInfinity;
            GrantInvulnerability(respawnInvulnerability);
            if (stamina) stamina.Refill();
            locomotion.Animator.TrySetBool(DeadHash, false);
            // 소울류: 죽으면 적들도 부활. 단, 사냥 퀘스트 중에는 몬스터가 그대로 남는다 (몬헌식)
            if (!GameState.QuestActive) GameEvents.RaiseWorldReset();
            Respawned?.Invoke();
        }

        bool hasCustomRespawn;
        Vector3 customRespawnPos;
        Quaternion customRespawnRot;

        /// <summary>마지막으로 휴식한 화톳불 등에서 부활하도록 설정</summary>
        public void SetRespawnPoint(Vector3 position, Quaternion rotation)
        {
            hasCustomRespawn = true;
            customRespawnPos = position;
            customRespawnRot = rotation;
        }

        void Push(Vector3 direction, float distance, float duration)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f || distance <= 0f) return;
            if (knockback != null) StopCoroutine(knockback);
            knockback = StartCoroutine(PushRoutine(direction.normalized, distance, duration));
        }

        IEnumerator PushRoutine(Vector3 dir, float distance, float duration)
        {
            float t = 0f, prev = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / duration);
                float eased = 1f - (1f - p) * (1f - p);
                locomotion.AddDisplacement(dir * ((eased - prev) * distance));
                prev = eased;
                yield return null;
            }
            knockback = null;
        }
    }
}
