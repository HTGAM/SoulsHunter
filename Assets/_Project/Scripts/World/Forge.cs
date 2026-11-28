using UnityEngine;
using SoulsHunter.Core;
using SoulsHunter.Items;
using SoulsHunter.Player;

namespace SoulsHunter.World
{
    /// <summary>
    /// 대장간 (모루 / 방어구 작업대). 상호작용하면 몬스터 소재를 써서 장비를 한 단계 강화한다.
    /// Slot으로 무기를 다룰지 방어구를 다룰지 정한다.
    /// 안내 문구에 다음 강화 재료와 보유 개수가 표시된다.
    /// </summary>
    public class Forge : Interactable
    {
        public enum Slot { Weapon, Armor, Flask }

        [SerializeField] string forgeName = "대장간";
        [SerializeField] Slot slot = Slot.Weapon;
        [Tooltip("강화 성공 시 잠깐 밝아지는 불빛 (선택)")]
        [SerializeField] Light forgeLight;
        [SerializeField] float flashIntensity = 6f;

        GameObject cachedPlayer;
        UpgradableEquipment equipment;
        PlayerInventory inventory;
        float baseIntensity;
        float flashTime = -10f;

        public string ForgeName => forgeName;

        public override string Prompt
        {
            get
            {
                if (!equipment) return forgeName;
                if (equipment.IsMaxed) return $"{equipment.DisplayName}  <color=#AAAAAA>(최대 강화 · {equipment.StatSummary})</color>";
                return $"{equipment.SlotName} 강화  +{equipment.Level} → +{equipment.Level + 1}  <color=#AAAAAA>(지금: {equipment.StatSummary})</color>\n" +
                       $"<size=20>{equipment.DescribeNextCost(inventory)}</size>";
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
            if (cachedPlayer != player)
            {
                cachedPlayer = player;
                equipment = slot switch
                {
                    Slot.Weapon => player.GetComponent<PlayerWeapon>(),
                    Slot.Armor => player.GetComponent<PlayerArmor>(),
                    _ => player.GetComponent<PlayerFlaskUpgrade>(),
                };
                inventory = player.GetComponent<PlayerInventory>();
            }
            return equipment;
        }

        public override void Interact(GameObject player)
        {
            if (!CanInteract(player)) return;

            if (equipment.IsMaxed)
            {
                GameEvents.Notify($"{equipment.SlotName}은(는) 이미 최대로 강화했어요.");
                return;
            }
            if (!equipment.TryUpgrade(inventory))
            {
                GameEvents.Notify("<color=#FF6A5A>강화 재료가 부족해요.</color>");
                return;
            }

            flashTime = Time.time;
            GameEvents.Notify($"<color=#FFD27A>{equipment.DisplayName}</color> 강화 완료  ({equipment.StatSummary})");
            GameEvents.RaiseProgressChanged();
        }

#if UNITY_EDITOR
        public void EditorSetup(string name, Slot equipmentSlot, Light light)
        {
            forgeName = name;
            slot = equipmentSlot;
            forgeLight = light;
        }
#endif
    }
}
