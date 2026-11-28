using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using SoulsHunter.Core;
using SoulsHunter.Save;

namespace SoulsHunter.UI
{
    /// <summary>
    /// 타이틀 화면: 이어하기 / 새로 시작 / 종료.
    /// 저장 파일이 있으면 '이어하기'가 맨 위에 오고, '새로 시작'은 한 번 더 눌러야 저장을 지운다.
    /// UI는 실행 시 코드로 만든다. 마우스나 게임패드(방향키 + A)로 고른다.
    /// </summary>
    public class TitleScreen : MonoBehaviour
    {
        [Tooltip("시작 버튼을 누르면 불러올 게임 씬 이름 (Build Settings에 들어 있어야 함)")]
        [SerializeField] string gameScene = "test";
        [SerializeField] string gameTitle = "SOULS HUNTER";
        [SerializeField] string subtitle = "사냥은 끝나지 않는다";

        Font font;
        Button continueButton, newGameButton, quitButton;
        Text newGameLabel, hint;
        CanvasGroup group;
        bool confirmNewGame, loading;

        void Awake()
        {
            // 게임에서 돌아왔을 때 남아 있을 수 있는 상태 정리
            GameState.SetPaused(false);
            GameState.QuestActive = false;
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            font = UITheme.Font;
            EnsureEventSystem();
            BuildUI();
        }

        IEnumerator Start()
        {
            // 천천히 나타나기
            group.alpha = 0f;
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 1.2f)
            {
                group.alpha = Mathf.SmoothStep(0f, 1f, t);
                yield return null;
            }
            group.alpha = 1f;
        }

        static bool HasSave => File.Exists(SaveSystem.SavePath);

        void Continue() => StartCoroutine(LoadGame());

        void NewGame()
        {
            if (HasSave && !confirmNewGame)
            {
                confirmNewGame = true;
                newGameLabel.text = "정말 새로 시작? (저장 삭제)";
                hint.text = "한 번 더 누르면 지금까지의 진행이 지워져요";
                return;
            }
            SaveSystem.DeleteSave();
            StartCoroutine(LoadGame());
        }

        void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        IEnumerator LoadGame()
        {
            if (loading) yield break;
            if (!Application.CanStreamedLevelBeLoaded(gameScene))
            {
                hint.text = $"'{gameScene}' 씬이 Build Settings에 없어요 (SoulsHunter/Setup Build Scenes)";
                yield break;
            }
            loading = true;
            group.interactable = false;
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.5f)
            {
                group.alpha = 1f - t;
                yield return null;
            }
            SceneManager.LoadScene(gameScene);
        }

        void Update()
        {
            // 다른 버튼으로 옮기면 '새로 시작' 확인을 취소
            if (!confirmNewGame || !EventSystem.current) return;
            var selected = EventSystem.current.currentSelectedGameObject;
            if (selected && selected != newGameButton.gameObject)
            {
                confirmNewGame = false;
                newGameLabel.text = "새로 시작";
                hint.text = "";
            }
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
            var canvasGo = new GameObject("Title Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            group = canvasGo.GetComponent<CanvasGroup>();

            // 아래쪽이 어두운 배경 (3D 배경 위에 글자가 잘 보이게)
            var shade = Panel(canvasGo.transform, "Shade", new Color(0f, 0f, 0f, 0.45f));
            Stretch((RectTransform)shade.transform);

            var title = MakeText(canvasGo.transform, "Title", 120, TextAnchor.MiddleCenter);
            title.text = gameTitle;
            title.fontStyle = FontStyle.Bold;
            title.color = new Color(0.95f, 0.82f, 0.55f);
            var shadow = title.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
            shadow.effectDistance = new Vector2(4f, -4f);
            Place(title.rectTransform, new Vector2(0.5f, 0.72f), new Vector2(1400f, 160f));

            var sub = MakeText(canvasGo.transform, "Subtitle", 32, TextAnchor.MiddleCenter);
            sub.text = subtitle;
            sub.color = new Color(0.85f, 0.8f, 0.72f, 0.85f);
            Place(sub.rectTransform, new Vector2(0.5f, 0.62f), new Vector2(1000f, 50f));

            var menu = new GameObject("Menu", typeof(RectTransform), typeof(VerticalLayoutGroup));
            var mr = (RectTransform)menu.transform;
            mr.SetParent(canvasGo.transform, false);
            Place(mr, new Vector2(0.5f, 0.32f), new Vector2(460f, 260f));
            var layout = menu.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 14f;
            layout.childControlWidth = layout.childControlHeight = false;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;

            bool hasSave = HasSave;
            continueButton = MakeButton(mr, "이어하기", Continue, out _);
            newGameButton = MakeButton(mr, "새로 시작", NewGame, out newGameLabel);
            quitButton = MakeButton(mr, "종료", Quit, out _);
            continueButton.gameObject.SetActive(hasSave);
            if (hasSave) Chain(continueButton, newGameButton, quitButton);
            else Chain(newGameButton, quitButton);

            hint = MakeText(canvasGo.transform, "Hint", 24, TextAnchor.MiddleCenter);
            hint.color = new Color(1f, 0.6f, 0.45f);
            Place(hint.rectTransform, new Vector2(0.5f, 0.12f), new Vector2(1400f, 40f));

            var version = MakeText(canvasGo.transform, "Version", 20, TextAnchor.LowerRight);
            version.text = $"v{Application.version}";
            version.color = new Color(1f, 1f, 1f, 0.4f);
            var vr = version.rectTransform;
            vr.anchorMin = vr.anchorMax = vr.pivot = new Vector2(1f, 0f);
            vr.anchoredPosition = new Vector2(-20f, 16f);
            vr.sizeDelta = new Vector2(300f, 30f);

            if (EventSystem.current) EventSystem.current.SetSelectedGameObject((hasSave ? continueButton : newGameButton).gameObject);
            else StartCoroutine(SelectLater(hasSave ? continueButton : newGameButton));
        }

        static IEnumerator SelectLater(Button b)
        {
            yield return null;
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(b.gameObject);
        }

        static void Place(RectTransform rt, Vector2 anchor, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;
        }

        static void Chain(params Button[] buttons)
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnUp = buttons[(i - 1 + buttons.Length) % buttons.Length],
                    selectOnDown = buttons[(i + 1) % buttons.Length],
                };
            }
        }

        Button MakeButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick, out Text text)
        {
            var go = Panel(parent, label, new Color(0.18f, 0.15f, 0.12f, 0.92f));
            var img = go.GetComponent<Image>();
            img.raycastTarget = true;
            UITheme.Frame(img, 0.7f);
            ((RectTransform)go.transform).sizeDelta = new Vector2(420f, 66f);

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

            text = MakeText(go.transform, "Label", 30, TextAnchor.MiddleCenter);
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
