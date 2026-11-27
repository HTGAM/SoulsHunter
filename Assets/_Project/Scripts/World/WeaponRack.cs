using UnityEngine;
using SoulsHunter.Core;
using SoulsHunter.Player;

namespace SoulsHunter.World
{
    /// <summary>
    /// 안전구역의 무기 거치대. 상호작용할 때마다 다음 무기 종류로 바꿔 든다.
    /// 강화 단계는 무기 종류와 상관없이 유지된다.
    /// </summary>
    public class WeaponRack : Interactable
    {
        GameObject cachedPlayer;
        PlayerWeapon weapon;

        public override string Prompt
        {
            get
            {
                if (!weapon || weapon.Available.Count == 0) return "무기 거치대";
                var next = weapon.Next;
                string current = weapon.Equipped ? weapon.Equipped.DisplayName : "-";
                return $"무기 바꾸기 → <b>{next.DisplayName}</b>  <color=#AAAAAA>(지금: {current})</color>\n" +
                       $"<size=20>{next.Description}</size>";
            }
        }

        public override bool CanInteract(GameObject player)
        {
            if (!base.CanInteract(player)) return false;
            var m = HuntManager.Instance;
            if (m && m.CurrentPhase != HuntManager.Phase.Idle) return false;
            if (cachedPlayer != player)
            {
                cachedPlayer = player;
                weapon = player.GetComponent<PlayerWeapon>();
            }
            return weapon && weapon.Available.Count > 1;
        }

        public override void Interact(GameObject player)
        {
            if (!CanInteract(player)) return;
            var next = weapon.Next;
            weapon.Equip(next);
            GameEvents.Notify($"<color=#FFD27A>{next.DisplayName}</color>을(를) 들었어요.  {next.SpecialHint}");
            GameEvents.RaiseProgressChanged();
        }
    }
}
