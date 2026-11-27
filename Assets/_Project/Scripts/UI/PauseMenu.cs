using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using SoulsHunter.Core;
using SoulsHunter.Player;
using SoulsHunter.Save;
using SoulsHunter.World;

namespace SoulsHunter.UI
{
    /// <summary>
    /// ESC / Start 일시정지 메뉴: 계속하기, 조작법, 퀘스트 포기(사냥 중), 저장하고 종료.
    /// 마우스나 게임패드(방향키 + A)로 고른다. UI는 실행 시 코드로 만든다.
    /// </summary>
    public class PauseMenu : MonoBehaviour
    {
        [SerializeField] PlayerInputHandler input;

        GameObject root, mainPanel, controlsPanel;
        Button resumeButton, controlsButton, abandonButton, quitButton, controlsBackButton;
        Font font;

        const string ControlsText =
            "<b>조작법</b>\n\n" +
            "<b>키보드 / 마우스</b>\n" +
            "WASD 이동   ·   마우스 시점   ·   Space 짧게 = 구르기 / 꾹 = 달리기\n" +
            "좌클릭 약공격   ·   우클릭 강공격 (대검·해머는 누르고 있으면 모으기)\n" +
            "Ctrl 가드 (누르는 순간 패링)   ·   F 무기 특수 행동 (귀인화 / 도약 / 카운터)\n" +
            "Q / 휠클릭 락온   ·   R 회복약   ·   E 상호작용   ·   Tab / I 가방   ·   ESC 메뉴\n\n" +
            "<b>게임패드</b>\n" +
            "왼스틱 이동   ·   오른스틱 시점   ·   B 짧게 구르기 / 길게 달리기\n" +
            "R1 약공격   ·   R2 강공격   ·   L1 가드   ·   L2 무기 특수 행동   ·   R3 락온\n" +
            "X 회복약   ·   Y 상호작용   ·   Select 가방   ·   Start 메뉴";

        void Awake()
        {
            if (!input)
            {
                var player = GameObject.FindWithTag("Player");
                if (player) input = player.GetComponent<PlayerInputHandler>();
            }
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            EnsureEventSystem();
            BuildUI();
            root.SetActive(false);
        }

        void OnEnable() => GameState.PauseMenuAvailable = true;

        void OnDisable()
        {
            GameState.PauseMenuAvailable = false;
            if (GameState.IsPaused) Resume();
        }

        void Update()
        {
            if (!input || !input.PausePressed) return;
            if (!GameState.IsPaused) Pause();
            else if (controlsPanel.activeSelf) ShowControls(false);
            else Resume();
        }

        public void Pause()
        {
            GameState.SetPaused(true);
            root.SetActive(true);
            bool hunting = HuntManager.Instance && HuntManager.Instance.CurrentPhase == HuntManager.Phase.Hunting;
            abandonButton.gameObject.SetActive(hunting);
            if (hunting) Chain(resumeButton, controlsButton, abandonButton, quitButton);
            else Chain(resumeButton, controlsButton, quitButton);
            ShowControls(false);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public void Resume()
        {
            GameState.SetPaused(false);
            if (root) root.SetActive(false);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        void ShowControls(bool show)
        {
            mainPanel.SetActive(!show);
            controlsPanel.SetActive(show);
            Select(show ? controlsBackButton : resumeButton);
        }

        void AbandonQuest()
        {
            if (HuntManager.Instance) HuntManager.Instance.Abandon();
            Resume();
        }

        void SaveAndQuit()
        {
            var player = GameObject.FindWithTag("Player");
            if (player && player.TryGetComponent(out SaveSystem save)) save.Save();
            GameState.SetPaused(false);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        static void Select(Button b)
        {
            if (EventSystem.current && b) EventSystem.current.SetSelectedGameObject(b.gameObject);
        }

        static void EnsureEventSystem()
        {
            if (EventSystem.current || FindAnyObjectByType<EventSystem>()) return;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            go.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        // ───────── UI 생성 ─────────

        void BuildUI()
        {
            var canvasGo = new GameObject("Pause Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // 화면 전체를 어둡게 (뒤쪽 클릭 막기)
            root = Panel(canvasGo.transform, "Overlay", new Color(0f, 0f, 0f, 0.72f));
            Stretch((RectTransform)root.transform);
            root.GetComponent<Image>().raycastTarget = true;

            // 메인
            mainPanel = new GameObject("Main", typeof(RectTransform), typeof(VerticalLayoutGroup));
            var mr = (RectTransform)mainPanel.transform;
            mr.SetParent(root.transform, false);
            mr.sizeDelta = new Vector2(420f, 520f);
            var layout = mainPanel.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 14f;
            layout.childControlWidth = layout.childControlHeight = false;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;

            var title = MakeText(mr, "Title", 64, TextAnchor.MiddleCenter);
            title.text = "PAUSED";
            title.color = new Color(0.95f, 0.85f, 0.6f);
            ((RectTransform)title.transform).sizeDelta = new Vector2(420f, 110f);

            resumeButton = MakeButton(mr, "계속하기", Resume);
            controlsButton = MakeButton(mr, "조작법", () => ShowControls(true));
            abandonButton = MakeButton(mr, "퀘스트 포기", AbandonQuest);
            quitButton = MakeButton(mr, "저장하고 종료", SaveAndQuit);
            Chain(resumeButton, controlsButton, quitButton);

            // 조작법
            controlsPanel = Panel(root.transform, "Controls", new Color(0.06f, 0.05f, 0.04f, 0.95f));
            var cr = (RectTransform)controlsPanel.transform;
            cr.sizeDelta = new Vector2(1200f, 560f);
            var text = MakeText(cr, "Text", 26, TextAnchor.UpperCenter);
            text.text = ControlsText;
            text.lineSpacing = 1.2f;
            var tr = text.rectTransform;
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(40f, 100f);
            tr.offsetMax = new Vector2(-40f, -40f);
            controlsBackButton = MakeButton(cr, "돌아가기", () => ShowControls(false));
            var br = (RectTransform)controlsBackButton.transform;
            br.anchorMin = br.anchorMax = br.pivot = new Vector2(0.5f, 0f);
            br.anchoredPosition = new Vector2(0f, 30f);
            controlsPanel.SetActive(false);
        }

        /// <summary>게임패드 위/아래 이동 순서를 명시</summary>
        static void Chain(params Button[] buttons)
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                var nav = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnUp = buttons[(i - 1 + buttons.Length) % buttons.Length],
                    selectOnDown = buttons[(i + 1) % buttons.Length],
                };
                buttons[i].navigation = nav;
            }
        }

        Button MakeButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = Panel(parent, label, new Color(0.18f, 0.15f, 0.12f, 0.95f));
            var img = go.GetComponent<Image>();
            img.raycastTarget = true;
            ((RectTransform)go.transform).sizeDelta = new Vector2(360f, 64f);

            var button = go.AddComponent<Button>();
            button.targetGraphic = img;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.6f, 1.4f, 1.0f);
            colors.selectedColor = new Color(1.6f, 1.4f, 1.0f);
            colors.pressedColor = new Color(0.8f, 0.7f, 0.5f);
            colors.colorMultiplier = 1.5f;
            button.colors = colors;
            button.onClick.AddListener(onClick);

            var text = MakeText(go.transform, "Label", 30, TextAnchor.MiddleCenter);
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
    }
}
