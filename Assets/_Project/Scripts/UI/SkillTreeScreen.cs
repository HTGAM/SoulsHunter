using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using SoulsHunter.Core;
using SoulsHunter.Items;
using SoulsHunter.Player;
using SoulsHunter.Progression;

namespace SoulsHunter.UI
{
    /// <summary>
    /// 혼의 스킬 트리 화면 (혼의 제단에서 연다).
    /// 노드를 가리키면 설명이 나오고, 누르면 골라지고, 고른 노드를 한 번 더 누르면 해금된다.
    /// 마우스나 게임패드(방향키 + A)로 쓴다. ESC / B로 닫는다. 열려 있는 동안 게임은 멈춘다.
    /// UI는 실행 시 코드로 만든다.
    /// </summary>
    public class SkillTreeScreen : MonoBehaviour
    {
        [SerializeField] float cellSize = 58f;
        [SerializeField] Vector2 treeOffset = new(-250f, -10f);

        public static SkillTreeScreen Instance { get; private set; }
        public bool IsOpen => root && root.activeSelf;

        PlayerSkills skills;
        PlayerInventory inventory;
        Font font;
        Sprite circle;

        GameObject root;
        RectTransform treeArea;
        Text currencyText, nameText, effectText, costText, stateText, footerText;
        Button unlockButton, resetButton, closeButton;
        Text resetLabel;

        readonly Dictionary<SkillNode, NodeView> views = new();
        readonly List<(SkillNode a, SkillNode b, Image line)> lines = new();
        SkillNode shown, armed;
        bool confirmReset, closing;

        class NodeView
        {
            public RectTransform rect;
            public Image frame, inner, ring;
            public Text glyph;
            public SkillNodeButton button;
        }

        void Awake()
        {
            Instance = this;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            circle = MakeCircleSprite();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (IsOpen) Close();
        }

        // ───────── 열고 닫기 ─────────

        public void Open()
        {
            if (IsOpen) return;
            var player = GameObject.FindWithTag("Player");
            skills = player ? player.GetComponent<PlayerSkills>() : PlayerSkills.Instance;
            inventory = player ? player.GetComponent<PlayerInventory>() : null;
            if (!skills || !skills.Tree)
            {
                GameEvents.Notify("<color=#AAAAAA>스킬 트리가 준비되지 않았어요 (Build Test Arena를 다시 실행하세요)</color>");
                return;
            }
            if (!root) Build();

            EnsureEventSystem();
            root.SetActive(true);
            GameState.SetPaused(true);
            GameState.OverlayOpen = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            armed = null;
            confirmReset = false;
            skills.Changed += Refresh;
            Refresh();

            // 처음엔 가운데(시작) 노드를 고른 상태로
            SkillNode start = null;
            foreach (var n in skills.Tree.nodes) if (n.branch == SkillBranch.Root) { start = n; break; }
            if (start != null && views.TryGetValue(start, out var v)) Select(v.button);
            Show(start);
        }

        public void Close()
        {
            if (!IsOpen) return;
            if (skills) skills.Changed -= Refresh;
            root.SetActive(false);
            GameState.SetPaused(false);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            // 같은 프레임에 ESC가 일시정지 메뉴를 열지 않도록 LateUpdate에서 푼다
            closing = true;
        }

        void Update()
        {
            if (!IsOpen) return;
            bool back = (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                        || (Gamepad.current != null && (Gamepad.current.buttonEast.wasPressedThisFrame || Gamepad.current.startButton.wasPressedThisFrame));
            if (back) Close();
        }

        void LateUpdate()
        {
            if (!closing) return;
            closing = false;
            GameState.OverlayOpen = false;
        }

        // ───────── 노드 조작 ─────────

        internal void OnNodeHover(SkillNode n) => Show(n);

        internal void OnNodeClick(SkillNode n)
        {
            if (n == null) return;
            Show(n);
            if (armed == n) TryUnlock(n);
            else
            {
                armed = n;
                UpdateInfo();
            }
        }

        void TryUnlock(SkillNode n)
        {
            if (skills.TryUnlock(n))
            {
                armed = null;
                GameEvents.Notify($"<color=#FFD27A>{n.title}</color> 해금  ({n.DescribeEffects().Replace("\n", ", ")})");
                GameEvents.RaiseProgressChanged();
            }
            else UpdateInfo();
        }

        void OnUnlockButton()
        {
            if (shown != null) TryUnlock(shown);
        }

        void OnResetButton()
        {
            if (skills.UnlockedCount == 0) return;
            if (!confirmReset)
            {
                confirmReset = true;
                resetLabel.text = "정말 초기화? (한 번 더)";
                return;
            }
            skills.ResetAll();
            confirmReset = false;
            resetLabel.text = "초기화 (전부 환급)";
            GameEvents.Notify("스킬을 초기화했어요. 쓴 혼과 희귀 혼을 모두 돌려받았어요.");
            GameEvents.RaiseProgressChanged();
        }

        void Show(SkillNode n)
        {
            shown = n;
            UpdateInfo();
            foreach (var kv in views) kv.Value.ring.enabled = kv.Key == n;
        }

        // ───────── 그리기 ─────────

        void Refresh()
        {
            if (!root) return;
            foreach (var kv in views) Paint(kv.Key, kv.Value);
            foreach (var (a, b, line) in lines)
            {
                bool lit = skills.IsUnlocked(a) && skills.IsUnlocked(b);
                bool half = skills.IsUnlocked(a) || skills.IsUnlocked(b);
                var c = BranchColor(a.branch == SkillBranch.Root ? b.branch : a.branch);
                line.color = lit ? c : half ? c * new Color(0.55f, 0.55f, 0.55f, 0.8f) : new Color(0.22f, 0.24f, 0.28f, 0.8f);
            }

            var sb = new StringBuilder();
            sb.Append($"<color=#BFE3FF>혼  {skills.Souls:N0}</color>");
            foreach (var rare in RareItems())
            {
                int have = inventory ? inventory.Count(rare) : 0;
                sb.Append($"     <color=#E8B8FF>{rare.DisplayName}  {have}</color>");
            }
            currencyText.text = sb.ToString();
            footerText.text = $"다음 노드 비용  <color=#BFE3FF>혼 {skills.NextCost:N0}</color>   ·   찍은 노드 {skills.UnlockedCount}개   ·   노드를 누르고 한 번 더 누르면 해금";
            resetButton.interactable = skills.UnlockedCount > 0;
            UpdateInfo();
        }

        void Paint(SkillNode n, NodeView v)
        {
            var c = BranchColor(n.branch);
            bool unlocked = skills.IsUnlocked(n);
            bool reachable = skills.IsReachable(n);
            bool affordable = reachable && skills.CanUnlock(n);

            if (unlocked)
            {
                v.frame.color = c;
                v.inner.color = Color.Lerp(new Color(0.06f, 0.06f, 0.08f), c, 0.55f);
                v.glyph.color = Color.white;
            }
            else if (reachable)
            {
                v.frame.color = affordable ? c : c * new Color(0.6f, 0.6f, 0.6f, 1f);
                v.inner.color = new Color(0.07f, 0.08f, 0.1f);
                v.glyph.color = affordable ? c : c * new Color(0.7f, 0.7f, 0.7f, 1f);
            }
            else
            {
                v.frame.color = new Color(0.25f, 0.27f, 0.3f);
                v.inner.color = new Color(0.06f, 0.07f, 0.08f);
                v.glyph.color = new Color(0.4f, 0.42f, 0.45f);
            }
        }

        void UpdateInfo()
        {
            if (!root) return;
            var n = shown;
            if (n == null)
            {
                nameText.text = effectText.text = costText.text = stateText.text = "";
                unlockButton.gameObject.SetActive(false);
                return;
            }

            var c = BranchColor(n.branch);
            nameText.text = $"<color=#{ColorUtility.ToHtmlStringRGB(c)}>{n.title}</color>";
            effectText.text = n.branch == SkillBranch.Root
                ? "모든 갈래가 시작되는 곳.\n연결된 노드부터 차례로 해금할 수 있어요."
                : $"<size=18><color=#999999>{BranchName(n.branch)}{(n.keystone ? " · 핵심 노드" : "")}</color></size>\n{n.DescribeEffects()}";

            bool unlocked = skills.IsUnlocked(n);
            var cost = new StringBuilder();
            if (!unlocked && n.branch != SkillBranch.Root)
            {
                string soulColor = skills.Souls >= skills.NextCost ? "BFE3FF" : "FF6A5A";
                cost.Append($"비용  <color=#{soulColor}>혼 {skills.NextCost:N0}</color> <color=#888888>(보유 {skills.Souls:N0})</color>");
                if (n.rareCost && n.rareAmount > 0)
                {
                    int have = inventory ? inventory.Count(n.rareCost) : 0;
                    string rc = have >= n.rareAmount ? "E8B8FF" : "FF6A5A";
                    cost.Append($"\n        <color=#{rc}>{n.rareCost.DisplayName} {n.rareAmount}</color> <color=#888888>(보유 {have})</color>");
                }
            }
            costText.text = cost.ToString();

            string state;
            bool canUnlock = skills.CanUnlock(n);
            if (n.branch == SkillBranch.Root || unlocked) state = "<color=#9FD89F>해금됨</color>";
            else if (!skills.IsReachable(n)) state = "<color=#888888>연결된 노드를 먼저 해금하세요</color>";
            else if (!canUnlock) state = skills.HasRareCost(n) ? "<color=#FF6A5A>혼이 부족해요</color>" : "<color=#FF6A5A>희귀 혼이 부족해요 (보스를 사냥하세요)</color>";
            else state = armed == n ? "<color=#FFD27A>한 번 더 누르면 해금!</color>" : "<color=#FFD27A>해금할 수 있어요</color>";
            stateText.text = state;

            unlockButton.gameObject.SetActive(canUnlock);
        }

        IEnumerable<ItemData> RareItems()
        {
            var seen = new HashSet<ItemData>();
            foreach (var n in skills.Tree.nodes)
                if (n.rareCost && seen.Add(n.rareCost)) yield return n.rareCost;
        }

        static string BranchName(SkillBranch b) => b switch
        {
            SkillBranch.Offense => "공격의 갈래",
            SkillBranch.Defense => "방어의 갈래",
            SkillBranch.Survival => "생존의 갈래",
            SkillBranch.Hunt => "사냥의 갈래",
            _ => "",
        };

        public static Color BranchColor(SkillBranch b) => b switch
        {
            SkillBranch.Offense => new Color(0.9f, 0.35f, 0.3f),
            SkillBranch.Defense => new Color(0.35f, 0.65f, 0.9f),
            SkillBranch.Survival => new Color(0.5f, 0.8f, 0.42f),
            SkillBranch.Hunt => new Color(0.92f, 0.72f, 0.32f),
            _ => new Color(1f, 0.62f, 0.2f),
        };

        // ───────── UI 생성 ─────────

        void Build()
        {
            root = new GameObject("Skill Tree", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 60;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var bg = Panel(root.transform, "Background", new Color(0.03f, 0.04f, 0.06f, 0.96f));
            Stretch((RectTransform)bg.transform);
            bg.GetComponent<Image>().raycastTarget = true;

            var title = MakeText(root.transform, "Title", 44, TextAnchor.UpperLeft);
            title.text = "<b>혼의 제단</b>   <size=22><color=#888888>사냥한 몬스터의 혼으로 힘을 일깨운다</color></size>";
            title.color = new Color(0.95f, 0.85f, 0.65f);
            Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(40f, -30f), new Vector2(1200f, 60f));

            currencyText = MakeText(root.transform, "Currency", 28, TextAnchor.UpperRight);
            Anchor(currencyText.rectTransform, new Vector2(1f, 1f), new Vector2(-40f, -38f), new Vector2(1000f, 40f));

            footerText = MakeText(root.transform, "Footer", 22, TextAnchor.LowerLeft);
            footerText.color = new Color(0.8f, 0.8f, 0.8f);
            Anchor(footerText.rectTransform, new Vector2(0f, 0f), new Vector2(40f, 30f), new Vector2(1300f, 40f));

            // 트리
            var treeGo = new GameObject("Tree", typeof(RectTransform));
            treeArea = (RectTransform)treeGo.transform;
            treeArea.SetParent(root.transform, false);
            treeArea.anchorMin = treeArea.anchorMax = new Vector2(0.5f, 0.5f);
            treeArea.anchoredPosition = treeOffset;
            treeArea.sizeDelta = Vector2.zero;
            BuildTree();

            // 정보 패널
            var info = Panel(root.transform, "Info", new Color(0.08f, 0.09f, 0.12f, 0.95f));
            var ir = (RectTransform)info.transform;
            ir.anchorMin = ir.anchorMax = ir.pivot = new Vector2(1f, 0.5f);
            ir.anchoredPosition = new Vector2(-40f, 0f);
            ir.sizeDelta = new Vector2(520f, 600f);

            nameText = MakeText(ir, "Name", 34, TextAnchor.UpperLeft);
            nameText.fontStyle = FontStyle.Bold;
            Anchor(nameText.rectTransform, new Vector2(0f, 1f), new Vector2(28f, -24f), new Vector2(470f, 50f));
            effectText = MakeText(ir, "Effects", 24, TextAnchor.UpperLeft);
            effectText.horizontalOverflow = HorizontalWrapMode.Wrap;
            effectText.lineSpacing = 1.2f;
            Anchor(effectText.rectTransform, new Vector2(0f, 1f), new Vector2(28f, -84f), new Vector2(470f, 220f));
            costText = MakeText(ir, "Cost", 24, TextAnchor.UpperLeft);
            Anchor(costText.rectTransform, new Vector2(0f, 1f), new Vector2(28f, -320f), new Vector2(470f, 80f));
            stateText = MakeText(ir, "State", 24, TextAnchor.UpperLeft);
            Anchor(stateText.rectTransform, new Vector2(0f, 1f), new Vector2(28f, -410f), new Vector2(470f, 40f));

            unlockButton = MakeButton(ir, "해금", OnUnlockButton, out _, new Vector2(470f, 60f));
            var ur = (RectTransform)unlockButton.transform;
            ur.anchorMin = ur.anchorMax = ur.pivot = new Vector2(0.5f, 0f);
            ur.anchoredPosition = new Vector2(0f, 28f);

            // 아래쪽 버튼
            resetButton = MakeButton(root.transform, "초기화 (전부 환급)", OnResetButton, out resetLabel, new Vector2(300f, 56f));
            var rr = (RectTransform)resetButton.transform;
            rr.anchorMin = rr.anchorMax = rr.pivot = new Vector2(1f, 0f);
            rr.anchoredPosition = new Vector2(-360f, 24f);
            closeButton = MakeButton(root.transform, "닫기 (ESC)", Close, out _, new Vector2(300f, 56f));
            var cr = (RectTransform)closeButton.transform;
            cr.anchorMin = cr.anchorMax = cr.pivot = new Vector2(1f, 0f);
            cr.anchoredPosition = new Vector2(-40f, 24f);

            root.SetActive(false);
        }

        void BuildTree()
        {
            views.Clear();
            lines.Clear();
            var tree = skills.Tree;

            // 선을 먼저 (노드 아래에 깔리도록)
            var drawn = new HashSet<(string, string)>();
            foreach (var n in tree.nodes)
                foreach (var id in n.links)
                {
                    var other = tree.Find(id);
                    if (other == null) continue;
                    var key = string.CompareOrdinal(n.id, other.id) < 0 ? (n.id, other.id) : (other.id, n.id);
                    if (!drawn.Add(key)) continue;
                    lines.Add((n, other, Line(n.position * cellSize, other.position * cellSize)));
                }

            foreach (var n in tree.nodes) views[n] = MakeNode(n);
        }

        Image Line(Vector2 a, Vector2 b)
        {
            var go = new GameObject("Link", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(treeArea, false);
            var d = b - a;
            rt.anchoredPosition = (a + b) * 0.5f;
            rt.sizeDelta = new Vector2(d.magnitude, 4f);
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            return img;
        }

        NodeView MakeNode(SkillNode n)
        {
            bool big = n.keystone || n.branch == SkillBranch.Root;
            float size = big ? 58f : 40f;
            var v = new NodeView();

            var go = new GameObject(n.id, typeof(RectTransform), typeof(Image));
            v.rect = (RectTransform)go.transform;
            v.rect.SetParent(treeArea, false);
            v.rect.anchoredPosition = n.position * cellSize;
            v.rect.sizeDelta = new Vector2(size, size);

            // 고른 노드 표시 (뒤쪽 흰 테두리)
            var ring = new GameObject("Ring", typeof(RectTransform), typeof(Image));
            var rr = (RectTransform)ring.transform;
            rr.SetParent(v.rect, false);
            Stretch(rr);
            rr.offsetMin = new Vector2(-7f, -7f);
            rr.offsetMax = new Vector2(7f, 7f);
            v.ring = ring.GetComponent<Image>();
            v.ring.color = new Color(1f, 0.95f, 0.8f, 0.9f);
            v.ring.raycastTarget = false;
            v.ring.enabled = false;
            ring.transform.SetAsFirstSibling();

            v.frame = go.GetComponent<Image>();
            var inner = new GameObject("Inner", typeof(RectTransform), typeof(Image));
            var ir = (RectTransform)inner.transform;
            ir.SetParent(v.rect, false);
            Stretch(ir);
            ir.offsetMin = new Vector2(3f, 3f);
            ir.offsetMax = new Vector2(-3f, -3f);
            v.inner = inner.GetComponent<Image>();
            v.inner.raycastTarget = false;
            if (big)
            {
                v.frame.sprite = v.inner.sprite = v.ring.sprite = circle;
            }
            else
            {
                // 작은 노드는 45도 돌린 마름모 대신 네모 (이미지처럼)
            }

            v.glyph = MakeText(v.rect, "Glyph", big ? 26 : 20, TextAnchor.MiddleCenter);
            v.glyph.text = n.branch == SkillBranch.Root ? "魂" : n.glyph;
            v.glyph.fontStyle = FontStyle.Bold;
            Stretch(v.glyph.rectTransform);

            v.button = go.AddComponent<SkillNodeButton>();
            v.button.Setup(this, n);
            v.button.targetGraphic = v.frame;
            var colors = v.button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.3f, 1.3f, 1.3f);
            colors.selectedColor = new Color(1.3f, 1.3f, 1.3f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
            colors.colorMultiplier = 1.4f;
            v.button.colors = colors;
            return v;
        }

        static void EnsureEventSystem()
        {
            if (EventSystem.current || FindAnyObjectByType<EventSystem>()) return;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            go.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        static void Select(Selectable s)
        {
            if (EventSystem.current && s) EventSystem.current.SetSelectedGameObject(s.gameObject);
        }

        Button MakeButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick, out Text text, Vector2 size)
        {
            var go = Panel(parent, label, new Color(0.18f, 0.15f, 0.12f, 0.95f));
            var img = go.GetComponent<Image>();
            img.raycastTarget = true;
            ((RectTransform)go.transform).sizeDelta = size;
            var button = go.AddComponent<Button>();
            button.targetGraphic = img;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.6f, 1.4f, 1.0f);
            colors.selectedColor = new Color(1.6f, 1.4f, 1.0f);
            colors.colorMultiplier = 1.5f;
            button.colors = colors;
            button.onClick.AddListener(onClick);
            text = MakeText(go.transform, "Label", 26, TextAnchor.MiddleCenter);
            text.text = label;
            Stretch(text.rectTransform);
            return button;
        }

        Text MakeText(Transform parent, string name, int size, TextAnchor align)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.alignment = align;
            t.color = Color.white;
            t.supportRichText = true;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        static GameObject Panel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return go;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        static void Anchor(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        /// <summary>핵심 노드용 동그라미 (가장자리 부드럽게)</summary>
        static Sprite MakeCircleSprite()
        {
            const int s = 128;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            float r = s * 0.5f - 1f;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(s * 0.5f, s * 0.5f));
                    byte a = (byte)(Mathf.Clamp01(r - d + 0.5f) * 255f);
                    px[y * s + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
        }
    }

    /// <summary>스킬 노드 버튼: 가리키거나(마우스) 고르면(패드) 설명을 보여주고, 누르면 화면에 알린다.</summary>
    public class SkillNodeButton : Button
    {
        SkillTreeScreen screen;
        SkillNode node;

        public void Setup(SkillTreeScreen owner, SkillNode n)
        {
            screen = owner;
            node = n;
            onClick.AddListener(() => screen.OnNodeClick(node));
        }

        public override void OnSelect(BaseEventData eventData)
        {
            base.OnSelect(eventData);
            if (screen) screen.OnNodeHover(node);
        }

        public override void OnPointerEnter(PointerEventData eventData)
        {
            base.OnPointerEnter(eventData);
            if (screen) screen.OnNodeHover(node);
        }
    }
}
