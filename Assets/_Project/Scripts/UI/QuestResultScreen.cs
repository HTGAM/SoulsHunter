using System.Text;
using UnityEngine;
using UnityEngine.UI;
using SoulsHunter.World;

namespace SoulsHunter.UI
{
    /// <summary>
    /// 퀘스트 결과 화면. 클리어·실패 배너가 뜬 뒤 잠시 후 화면 왼쪽에 나타나고, 안전구역으로 돌아오면 사라진다.
    /// 클리어: 랭크(S/A/B), 걸린 시간, 쓰러진 횟수, 신기록 여부, 얻은 아이템 목록.
    /// 실패: 퀘스트 이름, 버틴 시간, 얻은 아이템(갈무리한 것은 남는다).
    /// 게임 진행을 막지 않는다 (귀환 대기 중에도 움직일 수 있음).
    /// </summary>
    public class QuestResultScreen : MonoBehaviour
    {
        [SerializeField] float showDelay = 1.8f;
        [SerializeField] float fadeTime = 0.35f;

        CanvasGroup group;
        Text rankText, bodyText;
        float showAt = float.PositiveInfinity;
        bool visible;

        static readonly Color PanelColor = new(0.05f, 0.04f, 0.03f, 0.86f);

        void Awake() => BuildUI();

        void OnEnable()
        {
            HuntManager.QuestEnded += OnQuestEnded;
            HuntManager.Returned += Hide;
        }

        void OnDisable()
        {
            HuntManager.QuestEnded -= OnQuestEnded;
            HuntManager.Returned -= Hide;
        }

        void Update()
        {
            if (!visible && Time.unscaledTime >= showAt) visible = true;
            float target = visible ? 1f : 0f;
            group.alpha = Mathf.MoveTowards(group.alpha, target, Time.unscaledDeltaTime / fadeTime);
        }

        void OnQuestEnded(HuntResult r)
        {
            rankText.text = r.success ? r.rank : "";
            rankText.color = RankColor(r.rank);

            var sb = new StringBuilder();
            sb.Append(r.success ? "<b>QUEST CLEAR</b>\n" : "<b><color=#E0604F>QUEST FAILED</color></b>\n");
            sb.Append($"<size=26>{r.quest.title}</size>\n\n");
            sb.Append($"시간          {Format(r.time)}");
            if (r.newRecord) sb.Append("   <color=#FFD27A>NEW RECORD!</color>");
            sb.Append('\n');
            sb.Append($"쓰러진 횟수   {r.faints} / {HuntManager.FaintLimit(r.quest)}\n");
            if (r.success)
            {
                var rec = HuntManager.Instance ? HuntManager.Instance.GetRecord(r.quest) : null;
                if (rec != null && rec.bestTime >= 0f) sb.Append($"<color=#AAAAAA>최고 기록     {Format(rec.bestTime)}  ({rec.bestRank})</color>\n");
                sb.Append($"<color=#888888><size=18>S 랭크 기준: {Format(r.quest.parTime)} 안에, 한 번도 쓰러지지 않고</size></color>\n");
            }

            sb.Append(r.firstClear ? "\n<b>얻은 아이템</b>  <color=#FFD27A>(첫 클리어 보너스 포함)</color>\n" : "\n<b>얻은 아이템</b>\n");
            if (r.items.Count == 0) sb.Append("<color=#888888>없음</color>");
            foreach (var (item, amount) in r.items)
                sb.Append($"<color=#{ColorUtility.ToHtmlStringRGB(item.RarityColor)}>{item.DisplayName}</color>  x{amount}\n");
            if (r.souls > 0) sb.Append($"<color=#BFE3FF>혼  +{r.souls:N0}</color>\n");
            if (r.unlocked.Count > 0)
            {
                sb.Append("\n<b><color=#FFD27A>새 퀘스트 해금</color></b>\n");
                foreach (var q in r.unlocked) sb.Append($"{q.title}\n");
            }

            bodyText.text = sb.ToString();
            showAt = Time.unscaledTime + showDelay;
            visible = false;
        }

        void Hide()
        {
            visible = false;
            showAt = float.PositiveInfinity;
        }

        static string Format(float seconds)
        {
            int s = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return $"{s / 60:00}:{s % 60:00}";
        }

        static Color RankColor(string rank) => rank switch
        {
            "S" => new Color(1f, 0.82f, 0.3f),
            "A" => new Color(0.75f, 0.85f, 1f),
            _ => new Color(0.8f, 0.7f, 0.6f),
        };

        // ───────── UI 생성 ─────────

        void BuildUI()
        {
            var canvasGo = new GameObject("Result Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 12;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            group = canvasGo.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            var pr = (RectTransform)panel.transform;
            pr.SetParent(canvasGo.transform, false);
            pr.anchorMin = pr.anchorMax = pr.pivot = new Vector2(0f, 0.5f);
            pr.anchoredPosition = new Vector2(60f, 0f);
            pr.sizeDelta = new Vector2(560f, 600f);
            var img = panel.GetComponent<Image>();
            img.color = PanelColor;
            img.raycastTarget = false;

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            rankText = MakeText(pr, font, 150, TextAnchor.UpperRight);
            rankText.fontStyle = FontStyle.Bold;
            var rr = rankText.rectTransform;
            rr.anchorMin = rr.anchorMax = rr.pivot = new Vector2(1f, 1f);
            rr.anchoredPosition = new Vector2(-28f, -10f);
            rr.sizeDelta = new Vector2(200f, 170f);

            bodyText = MakeText(pr, font, 24, TextAnchor.UpperLeft);
            var br = bodyText.rectTransform;
            br.anchorMin = Vector2.zero;
            br.anchorMax = Vector2.one;
            br.offsetMin = new Vector2(32f, 24f);
            br.offsetMax = new Vector2(-32f, -28f);
        }

        static Text MakeText(Transform parent, Font font, int size, TextAnchor align)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(Outline));
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
    }
}
