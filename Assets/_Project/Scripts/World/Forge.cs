using UnityEngine;
using SoulsHunter.Core;
using SoulsHunter.Items;
using SoulsHunter.Player;

namespace SoulsHunter.World
{
    /// <summary>
    /// 대장간 모루. 상호작용하면 몬스터 소재를 써서 무기를 한 단계 강화한다.
    /// 안내 문구에 다음 강화 재료와 보유 개수가 표시된다.
    /// </summary>
    public class Forge : Interactable
    {
        [SerializeField] string forgeName = "대장간";
        [Tooltip("강화 성공 시 잠깐 밝아지는 불빛 (선택)")]
        [SerializeField] Light forgeLight;
        [SerializeField] float flashIntensity = 6f;

        PlayerWeapon weapon;
        PlayerInventory inventory;
        float baseIntensity;
        float flashTime = -10f;

        public string ForgeName => forgeName;

        public override string Prompt
        {
            get
            {
                if (!weapon) return forgeName;
                if (weapon.IsMaxed) return $"{weapon.DisplayName}  <color=#AAAAAA>(최대 강화)</color>";
                return $"무기 강화  +{weapon.Level} → +{weapon.Level + 1}\n<size=20>{weapon.DescribeNextCost(inventory)}</size>";
            }
        }

        void Awake()
        {
            if (forgeLight) baseIntensity = forgeLight.intensity;
        }

        void Update()
        {
            if (!forgeLight) return;
            float t = Time.time - flashTime;
            forgeLight.intensity = t < 0.6f ? Mathf.Lerp(flashIntensity, baseIntensity, t / 0.6f) : baseIntensity;
        }

        public override bool CanInteract(GameObject player)
        {
            if (!base.CanInteract(player)) return false;
            // 안내 문구를 만들 때 쓰도록 플레이어 컴포넌트를 기억해 둔다
            if (!weapon || weapon.gameObject != player)
            {
                weapon = player.GetComponent<PlayerWeapon>();
                inventory = player.GetComponent<PlayerInventory>();
            }
            return weapon;
        }

        public override void Interact(GameObject player)
        {
            if (!CanInteract(player)) return;

            if (weapon.IsMaxed)
            {
                GameEvents.Notify("이미 최대로 강화한 무기예요.");
                return;
            }
            if (!weapon.TryUpgrade(inventory))
            {
                GameEvents.Notify("<color=#FF6A5A>강화 재료가 부족해요.</color>");
                return;
            }

            flashTime = Time.time;
            GameEvents.Notify($"<color=#FFD27A>{weapon.DisplayName}</color> 강화 완료  (공격력 x{weapon.DamageMultiplier:0.00})");
            GameEvents.RaiseProgressChanged();
        }
    }
}
