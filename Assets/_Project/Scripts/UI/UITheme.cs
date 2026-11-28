using UnityEngine;
using UnityEngine.UI;

namespace SoulsHunter.UI
{
    /// <summary>
    /// 게임 UI 공통 모양: 폰트, 색, 테두리.
    /// 폰트는 윈도우의 '맑은 고딕'(한글이 깔끔함)을 쓰고, 없으면 유니티 기본 폰트를 쓴다.
    /// </summary>
    public static class UITheme
    {
        public static readonly Color Gold = new(0.92f, 0.78f, 0.5f);
        public static readonly Color GoldDim = new(0.62f, 0.52f, 0.34f, 0.85f);
        public static readonly Color Panel = new(0.05f, 0.05f, 0.07f, 0.78f);
        public static readonly Color Text = new(0.94f, 0.92f, 0.88f);

        static Font font;

        public static Font Font
        {
            get
            {
                if (font) return font;
                try
                {
                    font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Segoe UI", "Arial" }, 32);
                }
                catch { font = null; }
                if (!font) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return font;
            }
        }

        /// <summary>얇은 금색 테두리 (Outline 효과)</summary>
        public static void Frame(Graphic g, float alpha = 0.55f)
        {
            if (!g) return;
            var o = g.gameObject.GetComponent<Outline>() ?? g.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(GoldDim.r, GoldDim.g, GoldDim.b, alpha);
            o.effectDistance = new Vector2(1.5f, -1.5f);
            o.useGraphicAlpha = true;
        }

        /// <summary>글자 그림자 (밝은 배경에서도 읽히게)</summary>
        public static void Shadowed(Text t, float alpha = 0.85f)
        {
            if (!t) return;
            var s = t.gameObject.GetComponent<Shadow>() ?? t.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(0f, 0f, 0f, alpha);
            s.effectDistance = new Vector2(1.5f, -1.5f);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => font = null;
    }
}
