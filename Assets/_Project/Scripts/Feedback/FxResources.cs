using UnityEngine;
using UnityEngine.Rendering;

namespace SoulsHunter.Feedback
{
    /// <summary>
    /// 이펙트용 머티리얼과 텍스처를 코드로 만들어 둔다 (에셋 없이 동작).
    /// - Additive: 빛나는 불꽃·섬광 (겹칠수록 밝아짐)
    /// - Alpha: 파편·먼지 (어두운 색도 보임)
    /// </summary>
    public static class FxResources
    {
        static Material additive, alpha, trail, ringMat;
        static Texture2D softDot, ring, trailTex;

        public static Material Additive => additive ? additive : additive = Make(SoftDot, true, "FX Additive");
        public static Material Alpha => alpha ? alpha : alpha = Make(SoftDot, false, "FX Alpha");
        public static Material RingAdditive => ringMat ? ringMat : ringMat = Make(Ring, true, "FX Ring");
        public static Material Trail => trail ? trail : trail = Make(TrailTex, true, "FX Trail");

        /// <summary>가운데가 밝고 가장자리로 부드럽게 사라지는 점</summary>
        public static Texture2D SoftDot => softDot ? softDot : softDot = MakeTexture(64, (x, y) =>
        {
            float d = Mathf.Clamp01(Vector2.Distance(new Vector2(x, y), Vector2.one * 0.5f) * 2f);
            return Mathf.Pow(1f - d, 2.2f);
        });

        /// <summary>얇은 고리 (퍼지는 충격파)</summary>
        public static Texture2D Ring => ring ? ring : ring = MakeTexture(128, (x, y) =>
        {
            float d = Vector2.Distance(new Vector2(x, y), Vector2.one * 0.5f) * 2f;
            float r = 1f - Mathf.Abs(d - 0.82f) / 0.14f;
            return Mathf.Clamp01(r) * Mathf.Clamp01(r);
        });

        /// <summary>검 궤적: 칼끝 쪽(v=1)이 밝고 손잡이 쪽(v=0)으로 사라짐</summary>
        static Texture2D TrailTex => trailTex ? trailTex : trailTex = MakeTexture(64, (x, y) =>
        {
            float edge = Mathf.SmoothStep(0f, 1f, y) * (1f - Mathf.SmoothStep(0.92f, 1f, y));
            return edge;
        });

        static Texture2D MakeTexture(int size, System.Func<float, float, float> alphaAt)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "FX Tex" };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float a = alphaAt((x + 0.5f) / size, (y + 0.5f) / size);
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        static Material Make(Texture2D tex, bool additiveBlend, string name)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (!shader) shader = Shader.Find("Sprites/Default");
            var m = new Material(shader) { name = name };
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            m.mainTexture = tex;

            if (m.HasProperty("_Surface"))
            {
                m.SetFloat("_Surface", 1f);                          // 반투명
                m.SetFloat("_Blend", additiveBlend ? 2f : 0f);       // 2 = Additive, 0 = Alpha
                m.SetOverrideTag("RenderType", "Transparent");
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", additiveBlend ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.SetFloat("_Cull", (float)CullMode.Off);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                if (m.HasProperty("_ColorMode")) m.SetFloat("_ColorMode", 0f); // 정점 색 곱하기
            }
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }
    }
}
