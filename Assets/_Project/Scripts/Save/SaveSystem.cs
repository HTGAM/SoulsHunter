using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using SoulsHunter.Core;
using SoulsHunter.Items;
using SoulsHunter.Player;
using SoulsHunter.World;

namespace SoulsHunter.Save
{
    /// <summary>
    /// 진행 상황 저장/불러오기 (JSON, Application.persistentDataPath/save.json).
    /// 저장하는 것: 가방, 무기·방어구 강화 단계, 클리어한 퀘스트, 불 붙인 화톳불, 마지막으로 쉰 화톳불.
    /// - 시작할 때 불러오고, 마지막 화톳불 앞에서 시작한다.
    /// - 화톳불 휴식, 무기 강화 때 바로 저장. 아이템을 얻으면 잠시 뒤 자동 저장. 게임을 끌 때도 저장.
    /// 플레이어 오브젝트에 붙인다.
    /// </summary>
    [DefaultExecutionOrder(100)] // 다른 컴포넌트가 준비된 뒤 불러온다
    public class SaveSystem : MonoBehaviour
    {
        public const string FileName = "save.json";
        public static string SavePath => Path.Combine(Application.persistentDataPath, FileName);

        [Tooltip("저장 파일의 아이템 ID(에셋 이름)를 실제 아이템으로 바꿀 때 쓰는 목록")]
        [SerializeField] List<ItemData> itemDatabase = new();
        [SerializeField] bool loadOnStart = true;
        [Tooltip("가방이 바뀐 뒤 이 시간(초)이 지나면 자동 저장")]
        [SerializeField] float autosaveDelay = 2f;

        [Serializable]
        class SaveData
        {
            public int version = 1;
            public List<ItemEntry> items = new();
            public int weaponLevel;
            public int armorLevel;
            public List<string> litBonfires = new();
            public string lastBonfire = "";
            public List<string> clearedQuests = new();
        }

        [Serializable]
        class ItemEntry
        {
            public string id;
            public int count;
        }

        PlayerInventory inventory;
        PlayerWeapon weapon;
        PlayerArmor armor;
        PlayerDamageHandler damageHandler;
        PlayerLocomotion locomotion;

        string lastBonfire = "";
        bool loading;
        float autosaveAt = float.PositiveInfinity;

        void Awake()
        {
            inventory = GetComponent<PlayerInventory>();
            weapon = GetComponent<PlayerWeapon>();
            armor = GetComponent<PlayerArmor>();
            damageHandler = GetComponent<PlayerDamageHandler>();
            locomotion = GetComponent<PlayerLocomotion>();
        }

        void OnEnable()
        {
            Bonfire.Rested += OnRested;
            GameEvents.ProgressChanged += Save;
            if (inventory) inventory.Changed += OnInventoryChanged;
        }

        void OnDisable()
        {
            Bonfire.Rested -= OnRested;
            GameEvents.ProgressChanged -= Save;
            if (inventory) inventory.Changed -= OnInventoryChanged;
        }

        void Start()
        {
            if (loadOnStart) Load();
        }

        void Update()
        {
            if (Time.unscaledTime >= autosaveAt) Save();
        }

        void OnApplicationQuit()
        {
            if (autosaveAt < float.PositiveInfinity) Save();
        }

        void OnRested(Bonfire bonfire, bool firstTime)
        {
            lastBonfire = bonfire.BonfireName;
            Save();
        }

        void OnInventoryChanged()
        {
            if (!loading) autosaveAt = Time.unscaledTime + autosaveDelay;
        }

        // ───────── 저장 ─────────

        public void Save()
        {
            autosaveAt = float.PositiveInfinity;
            var data = new SaveData { lastBonfire = lastBonfire };

            if (inventory)
                foreach (var item in inventory.Items)
                    data.items.Add(new ItemEntry { id = item.name, count = inventory.Count(item) });
            if (weapon) data.weaponLevel = weapon.Level;
            if (armor) data.armorLevel = armor.Level;
            foreach (var b in FindObjectsByType<Bonfire>(FindObjectsSortMode.None))
                if (b.IsLit) data.litBonfires.Add(b.BonfireName);
            foreach (var board in FindObjectsByType<QuestBoard>(FindObjectsSortMode.None))
                foreach (var id in board.ClearedIds)
                    if (!data.clearedQuests.Contains(id)) data.clearedQuests.Add(id);

            try
            {
                File.WriteAllText(SavePath, JsonUtility.ToJson(data, true));
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveSystem] 저장 실패: {e.Message}");
            }
        }

        // ───────── 불러오기 ─────────

        public bool Load()
        {
            if (!File.Exists(SavePath)) return false;

            SaveData data;
            try
            {
                data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveSystem] 저장 파일을 읽지 못했어요: {e.Message}");
                return false;
            }
            if (data == null) return false;

            loading = true;
            try
            {
                if (inventory)
                {
                    var lookup = BuildItemLookup();
                    var contents = new List<(ItemData, int)>();
                    foreach (var e in data.items)
                    {
                        if (e == null || string.IsNullOrEmpty(e.id)) continue;
                        if (lookup.TryGetValue(e.id, out var item)) contents.Add((item, e.count));
                        else Debug.LogWarning($"[SaveSystem] 알 수 없는 아이템 '{e.id}' — Item Database에 추가하세요.");
                    }
                    inventory.LoadContents(contents);
                }

                if (weapon) weapon.SetLevel(data.weaponLevel);
                if (armor) armor.SetLevel(data.armorLevel);

                if (data.clearedQuests != null)
                    foreach (var board in FindObjectsByType<QuestBoard>(FindObjectsSortMode.None))
                        board.RestoreCleared(data.clearedQuests);

                lastBonfire = data.lastBonfire ?? "";
                foreach (var b in FindObjectsByType<Bonfire>(FindObjectsSortMode.None))
                {
                    if (data.litBonfires.Contains(b.BonfireName)) b.RestoreLit();
                    if (b.BonfireName == lastBonfire && b.IsLit)
                    {
                        b.GetRespawnPose(out var pos, out var rot);
                        if (damageHandler) damageHandler.SetRespawnPoint(pos, rot);
                        if (locomotion) locomotion.Teleport(pos, rot);
                    }
                }
            }
            finally
            {
                loading = false;
            }

            GameEvents.Notify("<color=#AAAAAA>저장된 진행 상황을 불러왔어요</color>");
            return true;
        }

        Dictionary<string, ItemData> BuildItemLookup()
        {
            var lookup = new Dictionary<string, ItemData>();
            foreach (var item in itemDatabase)
                if (item) lookup[item.name] = item;
            // 목록에 없더라도 씬에서 참조 중인(로드된) 아이템은 찾을 수 있다
            foreach (var item in Resources.FindObjectsOfTypeAll<ItemData>())
                lookup.TryAdd(item.name, item);
            return lookup;
        }

        public static void DeleteSave()
        {
            if (File.Exists(SavePath)) File.Delete(SavePath);
        }

#if UNITY_EDITOR
        public void EditorSetup(List<ItemData> items) => itemDatabase = items;
#endif
    }
}
