using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SoulsHunter.Combat;

namespace SoulsHunter.UI
{
    /// <summary>
    /// 플레이어가 준 피해를 맞은 자리에 숫자로 띄운다 (몬헌식).
    /// 약점(배율 > 1)은 주황색, 단단한 부위(배율 < 1)는 회색, 그 외는 흰색.
    /// </summary>
    [DefaultExecutionOrder(100)] // 카메라가 움직인 뒤 화면 위치를 계산한다
    public class DamageNumbers : MonoBehaviour
    {
        [SerializeField] float lifetime = 0.9f;
        [SerializeField] float riseHeight = 1.2f;
        [SerializeField] int fontSize = 30;
        [SerializeField] Color normalColor = Color.white;
        [SerializeField] Color weakColor = new(1f, 0.6f, 0.15f);
        [SerializeField] Color hardColor = new(0.65f, 0.65f, 0.65f);

        class Entry
        {
            public Text text;
            public Vector3 worldPos;
            public Vector2 jitter;
            public float born;
            public Color color;
        }

        readonly List<Entry> active = new();
        readonly Stack<Entry> pool = new();
        RectTransform root;
        Font font;
        GameObject player;

        void Awake()
        {
            font = UITheme.Font;
            player = GameObject.FindWithTag("Player");

            var canvasGo = new GameObject("Damage Numbers Canvas", typeof(Canvas));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5; // 다른 HUD보다 아래
            root = (RectTransform)canvasGo.transform;
        }

        void OnEnable() => Health.AnyDamaged += OnAnyDamaged;
        void OnDisable() => Health.AnyDamaged -= OnAnyDamaged;

        void OnAnyDamaged(Health target, DamageInfo info)
        {
            if (info.Amount <= 0.05f || !player) return;
            if (target.gameObject == player || info.Source != player) return; // 플레이어가 준 피해만

            var e = pool.Count > 0 ? pool.Pop() : Create();
            e.text.gameObject.SetActive(true);
            e.text.text = Mathf.RoundToInt(info.Amount).ToString();
            e.color = info.PartMultiplier > 1.01f ? weakColor
                    : info.PartMultiplier > 0f && info.PartMultiplier < 0.99f ? hardColor
                    : normalColor;
            // 약점은 조금 크게
            e.text.fontSize = info.PartMultiplier > 1.01f ? Mathf.RoundToInt(fontSize * 1.25f) : fontSize;
            e.worldPos = info.Point;
            e.jitter = Random.insideUnitCircle * 18f;
            e.born = Time.unscaledTime;
            active.Add(e);
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            float now = Time.unscaledTime;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var e = active[i];
                float t = (now - e.born) / lifetime;
                if (t >= 1f || !cam)
                {
                    e.text.gameObject.SetActive(false);
                    active.RemoveAt(i);
                    pool.Push(e);
                    continue;
                }

                float rise = 1f - (1f - t) * (1f - t); // 처음에 빠르게 떠오름
                Vector3 screen = cam.WorldToScreenPoint(e.worldPos + Vector3.up * (rise * riseHeight));
                bool visible = screen.z > 0f;
                e.text.enabled = visible;
                if (!visible) continue;

                e.text.rectTransform.position = new Vector3(screen.x + e.jitter.x, screen.y + e.jitter.y, 0f);
                var c = e.color;
                c.a = t < 0.7f ? 1f : 1f - (t - 0.7f) / 0.3f;
                e.text.color = c;
                float pop = t < 0.1f ? Mathf.Lerp(1.4f, 1f, t / 0.1f) : 1f; // 처음에 살짝 커졌다 작아짐
                e.text.rectTransform.localScale = Vector3.one * pop;
            }
        }

        Entry Create()
        {
            var go = new GameObject("Damage", typeof(RectTransform), typeof(Text), typeof(Outline));
            go.transform.SetParent(root, false);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.alignment = TextAnchor.MiddleCenter;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.fontStyle = FontStyle.Bold;
            go.GetComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.85f);
            ((RectTransform)go.transform).sizeDelta = new Vector2(160f, 50f);
            return new Entry { text = t };
        }
    }
}
