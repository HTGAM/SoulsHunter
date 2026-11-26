using System;
using System.Collections.Generic;
using UnityEngine;

namespace SoulsHunter.Items
{
    /// <summary>
    /// 플레이어 가방. 아이템별 개수를 들고 있다. (저장/불러오기는 나중 단계)
    /// </summary>
    public class PlayerInventory : MonoBehaviour
    {
        readonly Dictionary<ItemData, int> items = new();
        readonly List<ItemData> order = new(); // 얻은 순서대로 표시

        /// <summary>(아이템, 이번에 얻은 개수)</summary>
        public event Action<ItemData, int> ItemAdded;
        public event Action Changed;

        public IReadOnlyList<ItemData> Items => order;

        public int Count(ItemData item) => item && items.TryGetValue(item, out int c) ? c : 0;

        public void Add(ItemData item, int amount = 1)
        {
            if (!item || amount <= 0) return;
            int current = Count(item);
            int added = Mathf.Min(amount, item.MaxStack - current);
            if (added <= 0) return;

            if (current == 0) order.Add(item);
            items[item] = current + added;
            ItemAdded?.Invoke(item, added);
            Changed?.Invoke();
        }

        /// <returns>충분히 있어서 뺐으면 true</returns>
        public bool Remove(ItemData item, int amount = 1)
        {
            int current = Count(item);
            if (current < amount) return false;
            if (current == amount)
            {
                items.Remove(item);
                order.Remove(item);
            }
            else items[item] = current - amount;
            Changed?.Invoke();
            return true;
        }
    }
}
