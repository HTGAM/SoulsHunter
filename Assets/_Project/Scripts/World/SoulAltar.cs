using UnityEngine;
using SoulsHunter.Progression;
using SoulsHunter.UI;

namespace SoulsHunter.World
{
    /// <summary>
    /// 안전구역의 혼의 제단. 상호작용하면 스킬 트리 화면이 열린다 (사냥 중에는 못 씀).
    /// 불꽃이 천천히 일렁인다.
    /// </summary>
    public class SoulAltar : Interactable
    {
        [SerializeField] Light flame;
        [SerializeField] float flickerAmount = 0.35f;

        float baseIntensity;

        public override string Prompt
        {
            get
            {
                var s = PlayerSkills.Instance;
                string souls = s ? $"  <color=#BFE3FF>(혼 {s.Souls:N0} · 다음 노드 {s.NextCost:N0})</color>" : "";
                return $"혼의 제단 — 스킬 트리{souls}";
            }
        }

        void Awake()
        {
            if (flame) baseIntensity = flame.intensity;
        }

        void Update()
        {
            if (!flame) return;
            float t = Time.time;
            flame.intensity = baseIntensity * (1f + flickerAmount * (Mathf.PerlinNoise(t * 2.3f, 0.7f) - 0.5f) * 2f);
        }

        public override bool CanInteract(GameObject player)
        {
            if (!base.CanInteract(player)) return false;
            var m = HuntManager.Instance;
            return !m || m.CurrentPhase == HuntManager.Phase.Idle;
        }

        public override void Interact(GameObject player)
        {
            if (!CanInteract(player)) return;
            if (SkillTreeScreen.Instance) SkillTreeScreen.Instance.Open();
        }

#if UNITY_EDITOR
        public void EditorSetup(Light light) => flame = light;
#endif
    }
}
