using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using SoulsHunter.Combat;
using SoulsHunter.Core;
using SoulsHunter.Items;
using SoulsHunter.Player;
using SoulsHunter.World;

namespace SoulsHunter.UI
{
    /// <summary>
    /// 회복약 개수, 상호작용 안내, 아이템 획득 알림, 화톳불 메시지, 가방 창.
    /// (HP/스태미나/보스 바는 PlayerHUD)
    /// </summary>
    public class GameplayHUD : MonoBehaviour
    {
        [SerializeField] PlayerFlask flask;
        [SerializeField] PlayerInteractor interactor;
        [SerializeField] PlayerInventory inventory;
        [SerializeField] PlayerInputHandler input;
        [SerializeField] PlayerWeapon weapon;
        [SerializeField] PlayerArmor armor;

        const float ToastDuration = 3.5f;
        const int MaxToasts = 5;
        static readonly Color HuntBannerColor = new(0.95f, 0.85f, 0.55f);
        static readonly Color FailBannerColor = new(0.85f, 0.3f, 0.25f);

        Text flaskText, promptText, bannerText, inventoryText, questText;
        GameObject promptRoot, inventoryRoot;
        RectTransform toastRoot;
        Font font;
        float bannerTime = -10f;
        Color bannerColor = HuntBannerColor;

        readonly List<(Text text, float time)> toasts = new();

        void Awake()
        {
            var player = GameObject.FindWithTag("Player");
            if (player)
            {
                if (!flask) flask = player.GetComponent<PlayerFlask>();
                if (!interactor) interactor = player.GetComponent<PlayerInteractor>();
                if (!inventory) inventory = player.GetComponent<PlayerInventory>();
                if (!input) input = player.GetComponent<PlayerInputHandler>();
                if (!weapon) weapon = player.GetComponent<PlayerWeapon>();
                if (!armor) armor = player.GetComponent<PlayerArmor>();
            }
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildUI();

            if (inventory)
            {
                inventory.ItemAdded += OnItemAdded;
                inventory.Changed += RefreshInventory;
            }
            GameEvents.Notified += AddToast;
            MonsterRewards.Slain += OnMonsterSlain;
            HuntManager.QuestStarted += OnQuestStarted;
            HuntManager.WaveStarted += OnWaveStarted;
            HuntManager.QuestEnded += OnQuestEnded;
        }

        void OnDestroy()
        {
            GameEvents.Notified -= AddToast;
            MonsterRewards.Slain -= OnMonsterSlain;
            HuntManager.QuestStarted -= OnQuestStarted;
            HuntManager.WaveStarted -= OnWaveStarted;
            HuntManager.QuestEnded -= OnQuestEnded;
        }

        // ───────── 사냥 ─────────

        void OnQuestStarted(HuntQuest quest) =>
            ShowBanner($"QUEST START\n<size=36>{quest.title}  ·  {quest.arenaName}</size>", HuntBannerColor);

        void OnWaveStarted(HuntQuest quest, int index, int count)
        {
            if (count <= 1) return; // 웨이브가 하나뿐이면 시작 배너로 충분
            string label = quest.waves[index].label;
            ShowBanner($"WAVE {index + 1} / {count}" + (string.IsNullOrEmpty(label) ? "" : $"\n<size=36>{label}</size>"), HuntBannerColor);
        }

        // 자세한 내용(랭크·보상)은 결과 화면(QuestResultScreen)이 보여준다
        void OnQuestEnded(HuntResult result)
        {
            if (result.success) ShowBanner("QUEST CLEAR", HuntBannerColor);
            else ShowBanner("QUEST FAILED", FailBannerColor);
        }

        static string FormatTime(float seconds)
        {
            int s = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return $"{s / 60:00}:{s % 60:00}";
        }

        void UpdateQuestTracker()
        {
            var m = HuntManager.Instance;
            string text = null;
            if (m && m.CurrentPhase == HuntManager.Phase.Idle && m.Accepted != null)
            {
                text = $"<b>{m.Accepted.title}</b>\n<size=20><color=#AAAAAA>출발 관문에서 출발하세요</color></size>";
            }
            else if (m && m.Active != null)
            {
                var q = m.Active;
                string faintColor = m.Faints >= q.maxFaints - 1 && m.Faints > 0 ? "FF6A5A" : "FFFFFF";
                var sb = new StringBuilder($"<b>{q.title}</b>\n<size=20>");
                if (m.CurrentPhase == HuntManager.Phase.Hunting)
                {
                    int wave = Mathf.Max(0, m.WaveIndex) + 1;
                    sb.Append(q.waves.Count > 1 ? $"웨이브  {wave} / {q.waves.Count}   ·   " : "");
                    sb.Append(m.WaveIndex < 0 ? "곧 시작" : $"남은 적  {m.AliveInWave}");
                    sb.Append('\n');
                }
                sb.Append($"쓰러진 횟수  <color=#{faintColor}>{m.Faints} / {q.maxFaints}</color>\n");
                sb.Append($"경과  {FormatTime(m.Elapsed)}");
                if (m.CurrentPhase == HuntManager.Phase.Returning)
                    sb.Append($"\n<color=#FFD27A>안전구역 귀환까지 {Mathf.CeilToInt(m.ReturnIn)}초</color>");
                sb.Append("</size>");
                text = sb.ToString();
            }

            questText.gameObject.SetActive(text != null);
            if (text != null) questText.text = text;
        }

        void Update()
        {
            float now = Time.unscaledTime;

            string status = flask ? $"회복약  {flask.Charges} / {flask.MaxCharges}" : "";
            if (armor) status = $"<size=20><color=#CCCCCC>{armor.DisplayName}</color></size>\n{status}";
            if (weapon) status = $"<size=20><color=#CCCCCC>{weapon.DisplayName}</color></size>\n{status}";
            flaskText.text = status;
            UpdateQuestTracker();

            var target = interactor ? interactor.Current : null;
            promptRoot.SetActive(target);
            if (target)
            {
                promptText.text = $"<b>[E]</b>  {target.Prompt}";
                // 문구 길이에 맞춰 상자 크기 조절
                var pr = (RectTransform)promptRoot.transform;
                pr.sizeDelta = new Vector2(Mathf.Max(420f, promptText.preferredWidth + 60f),
                                           Mathf.Max(54f, promptText.preferredHeight + 20f));
            }

            // 배너: 잠깐 나타났다가 서서히 사라짐
            float b = now - bannerTime;
            float alpha = b < 0.4f ? b / 0.4f : Mathf.Clamp01(1f - (b - 1.8f) / 0.8f);
            bannerText.color = new Color(bannerColor.r, bannerColor.g, bannerColor.b, alpha);

            // 획득 알림
            for (int i = toasts.Count - 1; i >= 0; i--)
            {
                var (text, time) = toasts[i];
                float age = now - time;
                if (age > ToastDuration)
                {
                    Destroy(text.gameObject);
                    toasts.RemoveAt(i);
                    continue;
                }
                var c = text.color;
                c.a = age < 0.2f ? age / 0.2f : Mathf.Clamp01((ToastDuration - age) / 0.6f);
                text.color = c;
            }

            if (input && input.InventoryPressed)
            {
                inventoryRoot.SetActive(!inventoryRoot.activeSelf);
                RefreshInventory();
            }
        }

        void OnItemAdded(ItemData item, int amount) =>
            AddToast($"<color=#{ColorUtility.ToHtmlStringRGB(item.RarityColor)}>{item.DisplayName}</color>  x{amount}  획득");

        void AddToast(string message)
        {
            if (toasts.Count >= MaxToasts)
            {
                Destroy(toasts[0].text.gameObject);
                toasts.RemoveAt(0);
            }
            var t = MakeText(toastRoot, "Toast", 24, TextAnchor.MiddleRight);
            t.text = message;
            ((RectTransform)t.transform).sizeDelta = new Vector2(720f, 34f);
            toasts.Add((t, Time.unscaledTime));
        }

        void OnMonsterSlain(Health monster) =>
            AddToast($"<color=#FFD27A>{monster.DisplayName}</color> 토벌!");

        void ShowBanner(string text, Color color)
        {
            bannerText.text = text;
            bannerColor = color;
            bannerTime = Time.unscaledTime;
        }

        void RefreshInventory()
        {
            if (!inventoryRoot || !inventoryRoot.activeSelf) return;
            var sb = new StringBuilder("<b>가방</b>   <size=18>(Tab / I 닫기)</size>\n\n");
            if (!inventory || inventory.Items.Count == 0) sb.Append("<color=#888888>비어 있음</color>");
            else
                foreach (var item in inventory.Items)
                    sb.Append($"<color=#{ColorUtility.ToHtmlStringRGB(item.RarityColor)}>{item.DisplayName}</color>  x{inventory.Count(item)}\n" +
                              $"<size=16><color=#AAAAAA>  {item.Description}</color></size>\n");
            inventoryText.text = sb.ToString();
        }

        // ───────── UI 생성 ─────────

        void BuildUI()
        {
            var canvasGo = new GameObject("Gameplay HUD Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 11;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            var root = (RectTransform)canvasGo.transform;

            // 회복약 (왼쪽 아래)
            flaskText = MakeText(root, "Flask", 26, TextAnchor.LowerLeft);
            Place(flaskText.rectTransform, new Vector2(0f, 0f), new Vector2(60f, 50f), new Vector2(400f, 40f));
            flaskText.color = new Color(1f, 0.75f, 0.35f);

            // 퀘스트 진행 (오른쪽 위)
            questText = MakeText(root, "Quest", 26, TextAnchor.UpperRight);
            Place(questText.rectTransform, new Vector2(1f, 1f), new Vector2(-60f, -50f), new Vector2(520f, 160f));
            questText.color = new Color(0.95f, 0.9f, 0.78f);
            questText.gameObject.SetActive(false);

            // 상호작용 안내 (가운데 아래)
            promptRoot = MakePanel(root, "Prompt", new Color(0f, 0f, 0f, 0.65f));
            var pr = (RectTransform)promptRoot.transform;
            pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0f);
            pr.pivot = new Vector2(0.5f, 0f);
            pr.anchoredPosition = new Vector2(0f, 230f);
            pr.sizeDelta = new Vector2(420f, 54f);
            promptText = MakeText(pr, "Text", 26, TextAnchor.MiddleCenter);
            Stretch(promptText.rectTransform);
            promptRoot.SetActive(false);

            // 화톳불 배너 (가운데)
            bannerText = MakeText(root, "Banner", 72, TextAnchor.MiddleCenter);
            Place(bannerText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), new Vector2(1400f, 120f));
            bannerText.color = Color.clear;

            // 획득 알림 (오른쪽, 아래에서 위로 쌓임)
            var toastGo = new GameObject("Toasts", typeof(RectTransform), typeof(VerticalLayoutGroup));
            toastRoot = (RectTransform)toastGo.transform;
            toastRoot.SetParent(root, false);
            toastRoot.anchorMin = toastRoot.anchorMax = toastRoot.pivot = new Vector2(1f, 0f);
            toastRoot.anchoredPosition = new Vector2(-60f, 160f);
            toastRoot.sizeDelta = new Vector2(520f, 220f);
            var layout = toastGo.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.LowerRight;
            layout.childControlHeight = false;
            layout.childControlWidth = false;
            layout.childForceExpandHeight = false;
            layout.spacing = 4f;

            // 가방 창 (오른쪽)
            inventoryRoot = MakePanel(root, "Inventory", new Color(0.05f, 0.04f, 0.03f, 0.88f));
            var ir = (RectTransform)inventoryRoot.transform;
            ir.anchorMin = ir.anchorMax = ir.pivot = new Vector2(1f, 0.5f);
            ir.anchoredPosition = new Vector2(-60f, 60f);
            ir.sizeDelta = new Vector2(560f, 620f);
            inventoryText = MakeText(ir, "List", 24, TextAnchor.UpperLeft);
            Stretch(inventoryText.rectTransform, 24f);
            inventoryRoot.SetActive(false);
        }

        Text MakeText(Transform parent, string name, int size, TextAnchor align)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Outline));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.alignment = align;
            t.color = Color.white;
            t.supportRichText = true;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            go.GetComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);
            return t;
        }

        static GameObject MakePanel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return go;
        }

        static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        static void Stretch(RectTransform rt, float padding = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(padding, padding);
            rt.offsetMax = new Vector2(-padding, -padding);
        }
    }
}
