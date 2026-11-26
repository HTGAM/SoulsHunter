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

        Bar hpBar, staminaBar, targetBar;
        GameObject targetRoot;
        Text targetName;
        Health trackedTarget;

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

            Health target = null;
            if (playerCamera && playerCamera.IsLockedOn) target = playerCamera.CurrentTarget.GetComponentInParent<Health>();
            if (target != trackedTarget)
            {
                trackedTarget = target;
                if (target) { targetName.text = target.DisplayName; targetBar.Snap(target.Normalized); }
            }
            targetRoot.SetActive(target);
            if (target)
            {
                targetBar.Set(target.Normalized);
                targetBar.Tick(dt);
            }
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

            targetRoot = new GameObject("Target", typeof(RectTransform));
            var tr = (RectTransform)targetRoot.transform;
            tr.SetParent(root, false);
            tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 0f);
            tr.pivot = new Vector2(0.5f, 0f);
            tr.anchoredPosition = new Vector2(0f, 90f);
            tr.sizeDelta = new Vector2(900f, 60f);

            targetBar = new Bar(tr, "Target HP", new Vector2(0f, 0f), new Vector2(0f, 7f), 900f, 14f, targetColor, chipColor, backColor);

            var nameGo = new GameObject("Name", typeof(RectTransform), typeof(Text));
            var nr = (RectTransform)nameGo.transform;
            nr.SetParent(tr, false);
            nr.anchorMin = nr.anchorMax = nr.pivot = new Vector2(0f, 0f);
            nr.anchoredPosition = new Vector2(0f, 20f);
            nr.sizeDelta = new Vector2(900f, 34f);
            targetName = nameGo.GetComponent<Text>();
            targetName.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            targetName.fontSize = 26;
            targetName.color = new Color(0.95f, 0.92f, 0.85f);
            targetName.alignment = TextAnchor.LowerLeft;
            targetRoot.SetActive(false);
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
