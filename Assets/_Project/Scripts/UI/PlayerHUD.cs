using UnityEngine;
using UnityEngine.UI;
using SoulsHunter.CameraSystem;
using SoulsHunter.Combat;
using SoulsHunter.Player;

namespace SoulsHunter.UI
{
    /// <summary>
    /// 왼쪽 위 HP/스태미나 바, 락온 중이면 화면 아래 대상 HP 바(보스 바).
    /// 피해를 입으면 노란 잔상 바가 잠깐 남았다가 줄어든다.
    /// UI는 실행 시 코드로 만들어지므로 프리팹이 필요 없다.
    /// </summary>
    public class PlayerHUD : MonoBehaviour
    {
        [SerializeField] Health playerHealth;
        [SerializeField] PlayerStamina playerStamina;
        [SerializeField] ThirdPersonCamera playerCamera;

        [Header("Look")]
        [SerializeField] Color healthColor = new(0.7f, 0.1f, 0.1f);
        [SerializeField] Color staminaColor = new(0.2f, 0.6f, 0.25f);
        [SerializeField] Color targetColor = new(0.6f, 0.08f, 0.08f);
        [SerializeField] Color chipColor = new(0.9f, 0.75f, 0.2f);
        [SerializeField] Color backColor = new(0f, 0f, 0f, 0.6f);
        [Tooltip("최대치 100당 바 길이(px)")]
        [SerializeField] float pixelsPer100 = 300f;

        Bar hpBar, staminaBar;
        // 화면 아래 대상 체력바: 보스(락온 안 해도 항상) + 락온한 일반 몬스터
        const int MaxTargetBars = 3;
        readonly TargetBar[] targetBars = new TargetBar[MaxTargetBars];
        readonly System.Collections.Generic.List<Health> wanted = new();
        Font font;
        Text deathText;
        Image deathBand;
        float deathAlpha;

        void Awake()
        {
            if (!playerHealth || !playerStamina)
            {
                var player = GameObject.FindWithTag("Player");
                if (player)
                {
                    if (!playerHealth) playerHealth = player.GetComponent<Health>();
                    if (!playerStamina) playerStamina = player.GetComponent<PlayerStamina>();
                }
            }
            if (!playerCamera && Camera.main) playerCamera = Camera.main.GetComponent<ThirdPersonCamera>();
            BuildUI();
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;

            if (playerHealth)
            {
                hpBar.SetWidth(playerHealth.Max / 100f * pixelsPer100);
                hpBar.Set(playerHealth.Normalized);
            }
            if (playerStamina)
            {
                staminaBar.SetWidth(playerStamina.Max / 100f * pixelsPer100);
                staminaBar.Set(playerStamina.Normalized);
            }
            hpBar.Tick(dt);
            staminaBar.Tick(dt);

            // 보여줄 대상: 플레이어를 알아챈 보스들 먼저, 그다음 락온한 대상(보스가 아니면)
            wanted.Clear();
            foreach (var boss in SoulsHunter.Enemy.EnemyAI.ActiveBosses)
            {
                var h = boss ? boss.Health : null;
                // 보스가 플레이어를 알아챈 뒤부터 (멀리서 대기 중이면 안 띄운다)
                if (h && !h.IsDead && boss.IsEngaged && !wanted.Contains(h) && wanted.Count < MaxTargetBars) wanted.Add(h);
            }
            if (playerCamera && playerCamera.IsLockedOn && playerCamera.CurrentTarget)
            {
                var locked = playerCamera.CurrentTarget.GetComponentInParent<Health>();
                if (locked && !locked.IsDead && !wanted.Contains(locked) && wanted.Count < MaxTargetBars) wanted.Add(locked);
            }

            // 이미 보여주던 대상은 같은 자리 유지, 새 대상은 빈 자리에
            foreach (var bar in targetBars)
                if (bar.Tracked && !wanted.Contains(bar.Tracked)) bar.Release();
            foreach (var h in wanted)
            {
                bool shown = false;
                foreach (var bar in targetBars) if (bar.Tracked == h) { bar.Keep(); shown = true; break; }
                if (shown) continue;
                foreach (var bar in targetBars)
                    if (bar.IsFree) { bar.Track(h); break; }
            }
            foreach (var bar in targetBars) bar.Tick(dt);

            // YOU DIED: 천천히 나타나고, 부활하면 바로 사라진다
            bool dead = playerHealth && playerHealth.IsDead;
            deathAlpha = dead ? Mathf.MoveTowards(deathAlpha, 1f, dt * 0.7f) : 0f;
            deathText.color = new Color(0.65f, 0.05f, 0.05f, deathAlpha);
            deathBand.color = new Color(0f, 0f, 0f, deathAlpha * 0.7f);
            deathText.transform.localScale = Vector3.one * Mathf.Lerp(0.95f, 1.05f, deathAlpha);
        }

        // ───────── UI 생성 ─────────

        void BuildUI()
        {
            var canvasGo = new GameObject("HUD Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            var root = canvasGo.transform;

            hpBar = new Bar(root, "HP", new Vector2(0f, 1f), new Vector2(60f, -50f), pixelsPer100, 18f, healthColor, chipColor, backColor);
            staminaBar = new Bar(root, "Stamina", new Vector2(0f, 1f), new Vector2(60f, -76f), pixelsPer100, 12f, staminaColor, chipColor, backColor);

            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            // 아래에서 위로 쌓는다 (첫 번째 = 첫 보스)
            for (int i = 0; i < MaxTargetBars; i++)
                targetBars[i] = new TargetBar(root, i, 90f + i * 72f, font, targetColor, chipColor, backColor);

            // YOU DIED
            var bandGo = new GameObject("Death Band", typeof(RectTransform), typeof(Image));
            var br = (RectTransform)bandGo.transform;
            br.SetParent(root, false);
            br.anchorMin = new Vector2(0f, 0.5f);
            br.anchorMax = new Vector2(1f, 0.5f);
            br.sizeDelta = new Vector2(0f, 180f);
            deathBand = bandGo.GetComponent<Image>();
            deathBand.raycastTarget = false;
            deathBand.color = Color.clear;

            var deathGo = new GameObject("YOU DIED", typeof(RectTransform), typeof(Text));
            var dr = (RectTransform)deathGo.transform;
            dr.SetParent(br, false);
            dr.anchorMin = Vector2.zero;
            dr.anchorMax = Vector2.one;
            dr.offsetMin = dr.offsetMax = Vector2.zero;
            deathText = deathGo.GetComponent<Text>();
            deathText.font = font;
            deathText.fontSize = 110;
            deathText.alignment = TextAnchor.MiddleCenter;
            deathText.text = "YOU DIED";
            deathText.raycastTarget = false;
            deathText.color = Color.clear;
        }

        /// <summary>화면 아래 대상 체력바 한 줄 (이름 + 막대). 나타날 때와 사라질 때 서서히 바뀐다.</summary>
        class TargetBar
        {
            const float FadeSpeed = 3f;

            readonly GameObject go;
            readonly CanvasGroup group;
            readonly Bar bar;
            readonly Text label;
            float alpha;
            bool releasing;

            public Health Tracked { get; private set; }
            /// <summary>새 대상을 받을 수 있음 (비었거나 사라지는 중)</summary>
            public bool IsFree => !Tracked || releasing;

            public TargetBar(Transform parent, int index, float y, Font font, Color fillColor, Color chipColor, Color backColor)
            {
                go = new GameObject($"Target {index}", typeof(RectTransform), typeof(CanvasGroup));
                var tr = (RectTransform)go.transform;
                tr.SetParent(parent, false);
                tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 0f);
                tr.pivot = new Vector2(0.5f, 0f);
                tr.anchoredPosition = new Vector2(0f, y);
                tr.sizeDelta = new Vector2(900f, 60f);
                group = go.GetComponent<CanvasGroup>();
                group.blocksRaycasts = group.interactable = false;

                bar = new Bar(tr, "HP", new Vector2(0f, 0f), new Vector2(0f, 7f), 900f, 14f, fillColor, chipColor, backColor);

                var nameGo = new GameObject("Name", typeof(RectTransform), typeof(Text));
                var nr = (RectTransform)nameGo.transform;
                nr.SetParent(tr, false);
                nr.anchorMin = nr.anchorMax = nr.pivot = new Vector2(0f, 0f);
                nr.anchoredPosition = new Vector2(0f, 20f);
                nr.sizeDelta = new Vector2(900f, 34f);
                label = nameGo.GetComponent<Text>();
                label.font = font;
                label.fontSize = 26;
                label.color = new Color(0.95f, 0.92f, 0.85f);
                label.alignment = TextAnchor.LowerLeft;
                label.raycastTarget = false;
                var shadow = nameGo.AddComponent<Shadow>();
                shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);

                go.SetActive(false);
            }

            public void Track(Health h)
            {
                Tracked = h;
                releasing = false;
                label.text = h.DisplayName;
                bar.Snap(h.Normalized);
                if (!go.activeSelf) { alpha = 0f; go.SetActive(true); }
            }

            /// <summary>대상이 죽거나 사라짐: 막대를 비우고 서서히 숨긴다</summary>
            public void Release() => releasing = true;

            /// <summary>사라지던 중에 다시 보여줄 대상이 되면 그대로 유지</summary>
            public void Keep() => releasing = false;

            public void Tick(float dt)
            {
                if (!Tracked && !go.activeSelf) return;
                if (Tracked && !releasing) bar.Set(Tracked.Normalized);
                else if (releasing && Tracked && Tracked.IsDead) bar.Set(0f);
                bar.Tick(dt);

                alpha = Mathf.MoveTowards(alpha, releasing ? 0f : 1f, FadeSpeed * dt);
                group.alpha = alpha;
                if (releasing && alpha <= 0f)
                {
                    Tracked = null;
                    releasing = false;
                    go.SetActive(false);
                }
            }
        }

        /// <summary>배경 + 잔상(chip) + 채움으로 된 막대</summary>
        class Bar
        {
            const float ChipDelay = 0.6f;
            const float ChipSpeed = 0.6f; // 초당 비율

            readonly RectTransform root, fill, chip;
            float value = 1f, chipValue = 1f, lastDropTime;

            public Bar(Transform parent, string name, Vector2 anchor, Vector2 pos, float width, float height,
                       Color fillColor, Color chipColor, Color backColor)
            {
                root = MakeImage(name, parent, backColor);
                root.anchorMin = root.anchorMax = anchor;
                root.pivot = new Vector2(0f, 0.5f);
                root.anchoredPosition = pos;
                root.sizeDelta = new Vector2(width, height);

                chip = MakeImage("Chip", root, chipColor);
                fill = MakeImage("Fill", root, fillColor);
                foreach (var r in new[] { chip, fill })
                {
                    r.anchorMin = Vector2.zero;
                    r.anchorMax = Vector2.one;
                    r.offsetMin = r.offsetMax = Vector2.zero;
                }
            }

            static RectTransform MakeImage(string name, Transform parent, Color color)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(Image));
                go.GetComponent<Image>().color = color;
                go.GetComponent<Image>().raycastTarget = false;
                var rt = (RectTransform)go.transform;
                rt.SetParent(parent, false);
                return rt;
            }

            public void SetWidth(float width) => root.sizeDelta = new Vector2(width, root.sizeDelta.y);

            public void Set(float normalized)
            {
                normalized = Mathf.Clamp01(normalized);
                if (normalized < value) lastDropTime = Time.unscaledTime;
                value = normalized;
                if (chipValue < value) chipValue = value; // 회복은 즉시
            }

            public void Snap(float normalized) { value = chipValue = Mathf.Clamp01(normalized); Apply(); }

            public void Tick(float dt)
            {
                if (chipValue > value && Time.unscaledTime - lastDropTime > ChipDelay)
                    chipValue = Mathf.MoveTowards(chipValue, value, ChipSpeed * dt);
                Apply();
            }

            void Apply()
            {
                fill.anchorMax = new Vector2(value, 1f);
                chip.anchorMax = new Vector2(chipValue, 1f);
            }
        }
    }
}
