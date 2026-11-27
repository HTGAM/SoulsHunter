using UnityEngine;
using SoulsHunter.Combat;
using SoulsHunter.Player;

namespace SoulsHunter.CameraSystem
{
    /// <summary>
    /// 전투 상황에 맞춰 카메라를 흔든다 (ThirdPersonCamera와 같은 오브젝트에 붙인다).
    /// - 플레이어가 맞으면: 피해량에 비례
    /// - 플레이어가 때리면: 약하게 (강공격·약점은 조금 더)
    /// - 가드 / 패링 / 가드 브레이크 / 강인도 붕괴
    /// </summary>
    [RequireComponent(typeof(ThirdPersonCamera))]
    public class CameraFeedback : MonoBehaviour
    {
        [SerializeField] float hitTakenPerDamage = 0.006f;
        [SerializeField] float hitTakenMax = 0.35f;
        [SerializeField] float hitDealt = 0.05f;
        [SerializeField] float blockShake = 0.08f;
        [SerializeField] float parryShake = 0.18f;
        [SerializeField] float guardBreakShake = 0.3f;
        [SerializeField] float enemyStaggerShake = 0.15f;

        ThirdPersonCamera cam;
        Health playerHealth;
        PlayerCombat combat;
        PlayerGuard guard;

        void Awake() => cam = GetComponent<ThirdPersonCamera>();

        void Start()
        {
            var player = GameObject.FindWithTag("Player");
            if (!player) return;

            playerHealth = player.GetComponent<Health>();
            combat = player.GetComponent<PlayerCombat>();
            guard = player.GetComponent<PlayerGuard>();

            if (playerHealth) playerHealth.Damaged += OnPlayerDamaged;
            if (combat) combat.HitLanded += OnHitLanded;
            if (guard)
            {
                guard.Blocked += _ => cam.Shake(blockShake, 0.15f);
                guard.Parried += _ => cam.Shake(parryShake, 0.2f);
                guard.GuardBroken += () => cam.Shake(guardBreakShake, 0.35f);
            }
        }

        void OnDestroy()
        {
            if (playerHealth) playerHealth.Damaged -= OnPlayerDamaged;
            if (combat) combat.HitLanded -= OnHitLanded;
        }

        void OnPlayerDamaged(DamageInfo info) =>
            cam.Shake(Mathf.Min(hitTakenMax, info.Amount * hitTakenPerDamage + 0.05f), 0.3f);

        void OnHitLanded(Hurtbox box, DamageInfo info)
        {
            float s = hitDealt;
            if (combat.CurrentAttack != null && combat.CurrentAttack.hitStop >= 0.1f) s *= 2f; // 강공격
            if (box.DamageMultiplier > 1f) s *= 1.4f; // 약점
            cam.Shake(s, 0.12f);
        }

        // 적이 플레이어 공격(패링 포함)으로 경직되면 (강인도 붕괴) 크게
        void OnEnable()
        {
            foreach (var h in FindObjectsByType<Health>(FindObjectsSortMode.None))
                h.Staggered += OnSomethingStaggered;
        }

        void OnDisable()
        {
            foreach (var h in FindObjectsByType<Health>(FindObjectsSortMode.None))
                if (h) h.Staggered -= OnSomethingStaggered;
        }

        void OnSomethingStaggered(DamageInfo info)
        {
            var player = playerHealth ? playerHealth.gameObject : null;
            if (player && info.Source == player) cam.Shake(enemyStaggerShake, 0.25f);
        }
    }
}
