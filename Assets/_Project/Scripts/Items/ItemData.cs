using UnityEngine;

namespace SoulsHunter.Items
{
    /// <summary>
    /// 아이템 정의 (소재, 소모품 등). Project 창에서 우클릭 → Create → SoulsHunter → Item 으로 만든다.
    /// </summary>
    [CreateAssetMenu(menuName = "SoulsHunter/Item", fileName = "New Item")]
    public class ItemData : ScriptableObject
    {
        [SerializeField] string displayName = "Item";
        [SerializeField, TextArea] string description = "";
        [Tooltip("희귀도 1~8 (몬헌식). 높을수록 귀함")]
        [SerializeField, Range(1, 8)] int rarity = 1;
        [SerializeField] int maxStack = 99;

        public string DisplayName => displayName;
        public string Description => description;
        public int Rarity => rarity;
        public int MaxStack => maxStack;

        /// <summary>희귀도별 표시 색</summary>
        public Color RarityColor => rarity switch
        {
            1 => new Color(0.85f, 0.85f, 0.85f),
            2 => new Color(0.75f, 0.6f, 0.95f),
            3 => new Color(0.6f, 0.85f, 0.4f),
            4 => new Color(0.4f, 0.7f, 1f),
            5 => new Color(0.9f, 0.45f, 0.95f),
            6 => new Color(1f, 0.6f, 0.2f),
            7 => new Color(1f, 0.35f, 0.35f),
            _ => new Color(1f, 0.85f, 0.3f),
        };

#if UNITY_EDITOR
        /// <summary>에디터 코드에서 에셋을 만들 때 사용</summary>
        public void EditorSetup(string name, string desc, int rarityLevel)
        {
            displayName = name;
            description = desc;
            rarity = Mathf.Clamp(rarityLevel, 1, 8);
        }
#endif
    }
}
