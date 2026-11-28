using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SoulsHunter.Core;
using SoulsHunter.World;

namespace SoulsHunter.UI
{
    /// <summary>
    /// 상호작용할 수 있는 곳(대장간·게시판·제단 등) 머리 위에 금색 마름모와 이름을 띄운다.
    /// 멀수록 흐려지고, 가까이 가면(상호작용 가능 거리) 밝아지며 살짝 맥동한다.
    /// 출발 관문처럼 ShowMarker가 꺼진 곳은 표시하지 않는다.
    /// </summary>
    public class InteractableMarkers : MonoBehaviour
    {
        [SerializeField] float showDistance = 30f;
        [SerializeField] float labelDistance = 14f;

        class Marker
        {
            public RectTransform root;
            public Image diamond, glow;
            public Text label;
            public CanvasGroup group;
        }

        readonly Dictionary<Interactable, Marker> markers = new();
        readonly List<Interactable> stale = new();
        RectTransform canvasRect;
        Transform player;
        Camera cam;

        void Awake()
        {
            var canvasGo = new GameObject("Interact Markers", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -5; // 다른 HUD 아래
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasRect = (RectTransform)canvasGo.transform;
        }

        void LateUpdate()
        {
            if (!cam) cam = Camera.main;
            if (!player)
            {
                var go = GameObject.FindWithTag("Player");
                if (go) player = go.transform;
            }
            if (!cam || !player) return;

            bool hidden = GameState.IsPaused;
            foreach (var it in Interactable.Active)
                if (it && it.ShowMarker && !markers.ContainsKey(it)) markers[it] = Create(it);

            stale.Clear();
            foreach (var kv in markers)
            {
                var it = kv.Key;
                var m = kv.Value;
                if (!it || !it.isActiveAndEnabled) { stale.Add(it); continue; }

                float dist = Vector3.Distance(player.position, it.transform.position);
                Vector3 sp = cam.WorldToScreenPoint(it.MarkerPosition);
                bool visible = !hidden && sp.z > 0.5f && dist <= showDistance && it.CanInteract(player.gameObject);
                if (!visible)
                {
                    m.group.alpha = Mathf.MoveTowards(m.group.alpha, 0f, Time.unscaledDeltaTime * 4f);
                    continue;
                }

                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, sp, null, out var local);
                float bob = Mathf.Sin(Time.unscaledTime * 2.2f + it.GetInstanceID()) * 4f;
                m.root.anchoredPosition = local + new Vector2(0f, bob);

                bool near = dist <= it.InteractRange + 0.5f;
                float far = Mathf.InverseLerp(showDistance, showDistance * 0.4f, dist);
                float target = near ? 1f : Mathf.Lerp(0.25f, 0.85f, far);
                m.group.alpha = Mathf.MoveTowards(m.group.alpha, target, Time.unscaledDeltaTime * 3f);

                float pulse = near ? 1f + Mathf.Sin(Time.unscaledTime * 6f) * 0.08f : 1f;
                float scale = Mathf.Lerp(0.75f, 1f, far) * pulse;
                m.root.localScale = Vector3.one * scale;
                m.glow.color = new Color(1f, 0.8f, 0.45f, near ? 0.45f : 0.18f);
                m.label.enabled = dist <= labelDistance;
            }
            foreach (var it in stale)
            {
                if (markers.TryGetValue(it, out var m) && m.root) Destroy(m.root.gameObject);
                markers.Remove(it);
            }
        }

        Marker Create(Interactable it)
        {
            var m = new Marker();
            var go = new GameObject($"Marker {it.MarkerLabel}", typeof(RectTransform), typeof(CanvasGroup));
            m.root = (RectTransform)go.transform;
            m.root.SetParent(canvasRect, false);
            m.root.sizeDelta = new Vector2(40f, 40f);
            m.group = go.GetComponent<CanvasGroup>();
            m.group.alpha = 0f;
            m.group.blocksRaycasts = false;

            m.glow = Diamond(m.root, "Glow", 34f, new Color(1f, 0.8f, 0.45f, 0.18f));
            var frame = Diamond(m.root, "Frame", 20f, new Color(0.15f, 0.11f, 0.06f, 0.9f));
            m.diamond = Diamond(m.root, "Gem", 14f, UITheme.Gold);
            UITheme.Frame(frame, 0.9f);

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            var lr = (RectTransform)labelGo.transform;
            lr.SetParent(m.root, false);
            lr.anchoredPosition = new Vector2(0f, 30f);
            lr.sizeDelta = new Vector2(320f, 34f);
            m.label = labelGo.GetComponent<Text>();
            m.label.font = UITheme.Font;
            m.label.fontSize = 22;
            m.label.alignment = TextAnchor.MiddleCenter;
            m.label.color = UITheme.Text;
            m.label.text = it.MarkerLabel;
            m.label.raycastTarget = false;
            m.label.horizontalOverflow = HorizontalWrapMode.Overflow;
            UITheme.Shadowed(m.label);
            return m;
        }

        static Image Diamond(Transform parent, string name, float size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.sizeDelta = new Vector2(size, size);
            rt.localRotation = Quaternion.Euler(0f, 0f, 45f);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }
    }
}
