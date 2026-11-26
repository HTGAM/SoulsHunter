using UnityEngine;
using SoulsHunter.Combat;
using SoulsHunter.World;

namespace SoulsHunter.Player
{
    /// <summary>
    /// 가까운 상호작용 대상을 찾고, 상호작용 키를 누르면 실행한다.
    /// 공격·회피·경직·회복약 중에는 상호작용할 수 없다.
    /// </summary>
    [RequireComponent(typeof(PlayerInputHandler), typeof(PlayerLocomotion))]
    public class PlayerInteractor : MonoBehaviour
    {
        PlayerInputHandler input;
        PlayerLocomotion locomotion;
        PlayerCombat combat;
        PlayerFlask flask;
        Health health;

        /// <summary>지금 상호작용할 수 있는 대상 (없으면 null)</summary>
        public Interactable Current { get; private set; }

        void Awake()
        {
            input = GetComponent<PlayerInputHandler>();
            locomotion = GetComponent<PlayerLocomotion>();
            combat = GetComponent<PlayerCombat>();
            flask = GetComponent<PlayerFlask>();
            health = GetComponent<Health>();
        }

        void Update()
        {
            Current = Busy ? null : FindNearest();
            if (Current && input.InteractPressed) Current.Interact(gameObject);
        }

        bool Busy =>
            locomotion.IsDodging || locomotion.IsStunned ||
            (combat && combat.IsAttacking) || (flask && flask.IsDrinking) || (health && health.IsDead);

        Interactable FindNearest()
        {
            Interactable best = null;
            float bestDist = float.MaxValue;
            foreach (var it in Interactable.Active)
            {
                if (!it.CanInteract(gameObject)) continue;
                float d = Vector3.Distance(transform.position, it.transform.position);
                if (d <= it.InteractRange && d < bestDist) { bestDist = d; best = it; }
            }
            return best;
        }
    }
}
