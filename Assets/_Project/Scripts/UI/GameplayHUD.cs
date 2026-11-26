using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
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

        const float ToastDuration = 3.5f;
        const int MaxToasts = 5;

        Text flaskText, promptText, bannerText, inventoryText;
        GameObject promptRoot, inventoryRoot;
        RectTransform toastRoot;
        Font font;
        float bannerTime = -10f;

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
            }
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildUI();

            if (inventory)
            {
                inventory.ItemAdded += OnItemAdded;
                inventory.Changed += RefreshInventory;
            }
            Bonfire.Rested += OnRested;
        }

        void OnDestroy() => Bonfire.Rested -= OnRested;

        void Update()
        {
            float now = Time.unscaledTime;

            if (flask) flaskText.text = $"회복약  {flask.Charges} / {flask.MaxCharges}";

            var target = interactor ? interactor.Current : null;
            promptRoot.SetActive(target);
            if (target) promptText.text = $"<b>[E]</b>  {target.Prompt}";

            // 화톳불 배너: 1초 동안 나타났다가 서서히 사라짐
            float b = now - bannerTime;
            float alpha = b < 0.4f ? b / 0.4f : Mathf.Clamp01(1f - (b - 1.8f) / 0.8f);
            bannerText.color = new Color(1f, 0.82f, 0.45f, alpha);

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

        void OnItemAdded(ItemData item, int amount)
        {
            if (toasts.Count >= MaxToasts)
            {
                Destroy(toasts[0].text.gameObject);
                toasts.RemoveAt(0);
            }
            var t = MakeText(toastRoot, "Toast", 24, TextAnchor.MiddleRight);
            t.text = $"<color=#{ColorUtility.ToHtmlStringRGB(item.RarityColor)}>{item.DisplayName}</color>  x{amount}  획득";
            ((RectTransform)t.transform).sizeDelta = new Vector2(520f, 34f);
            toasts.Add((t, Time.unscaledTime));
        }

        void OnRested(Bonfire bonfire, bool firstTime)
        {
            bannerText.text = firstTime ? "BONFIRE LIT" : bonfire.BonfireName;
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
