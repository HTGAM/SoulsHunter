using UnityEngine;
using UnityEngine.Rendering;

namespace SoulsHunter.Enemy
{
    /// <summary>
    /// 바닥에 그리는 공격 범위 표시 (빨간 영역).
    /// - 예고 중: 반투명 빨간 바탕 + 깜빡이는 진한 테두리 + 가운데서 바깥으로 차오르는 채움
    ///   → 채움이 테두리에 닿는 순간 공격이 들어온다.
    /// - 판정 중: 영역 전체가 진하게 번쩍인다.
    /// 쿼드 몇 장으로 만들어서 별도 텍스처/셰이더 에셋이 필요 없다.
    /// </summary>
    public class AttackTelegraph : MonoBehaviour
    {
        const float GroundLift = 0.04f;   // 바닥과 겹쳐 깜빡이지 않게 살짝 띄움
        const float EdgeWidth = 0.18f;    // 테두리 두께(m)

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        Color areaColor = new(1f, 0.05f, 0.02f, 0.22f);
        Color fillColor = new(1f, 0.1f, 0.02f, 0.45f);
        Color edgeColor = new(1f, 0.08f, 0.04f, 0.95f);
        Color activeColor = new(1f, 0.2f, 0.1f, 0.8f);

        Renderer area, fill;
        readonly Renderer[] edges = new Renderer[4];
        MaterialPropertyBlock block;

        /// <param name="material">투명 Unlit 머티리얼. null이면 실행 중에 만든다.</param>
        public static AttackTelegraph Create(Transform owner, Material material)
        {
            var go = new GameObject($"{owner.name} Telegraph");
            var t = go.AddComponent<AttackTelegraph>();
            t.Build(material ? material : CreateMaterial());
            go.SetActive(false);
            return t;
        }

        public void SetColors(Color area, Color fill, Color edge, Color active)
        {
            areaColor = area; fillColor = fill; edgeColor = edge; activeColor = active;
        }

        void Build(Material mat)
        {
            block = new MaterialPropertyBlock();
            area = MakeQuad("Area", mat, 0f);
            fill = MakeQuad("Fill", mat, 0.005f);
            for (int i = 0; i < 4; i++) edges[i] = MakeQuad("Edge", mat, 0.01f);
        }

        Renderer MakeQuad(string name, Material mat, float lift)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name;
            Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(transform, false);
            q.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // 바닥에 눕힘
            q.transform.localPosition = new Vector3(0f, lift, 0f);
            var r = q.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        /// <param name="center">영역 중심 (바닥 높이)</param>
        /// <param name="size">가로(x), 세로(z) 크기(m)</param>
        /// <param name="windupProgress">예고 진행도 0~1</param>
        /// <param name="isActive">판정 중이면 true</param>
        public void Show(Vector3 center, Quaternion rotation, Vector2 size, float windupProgress, bool isActive)
        {
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            transform.SetPositionAndRotation(center + Vector3.up * GroundLift, rotation);

            float w = size.x, d = size.y;
            float p = Mathf.Clamp01(windupProgress);

            SetQuad(area, Vector3.zero, new Vector2(w, d), isActive ? activeColor : areaColor);

            // 채움: 가운데서 바깥으로. 끝에 가까울수록 진해진다.
            var fc = fillColor;
            fc.a = Mathf.Lerp(fillColor.a * 0.5f, fillColor.a, p);
            fill.enabled = !isActive;
            SetQuad(fill, Vector3.zero, new Vector2(w * p, d * p), fc);

            // 테두리: 예고 중 깜빡임 (판정이 가까울수록 빠르게)
            var ec = edgeColor;
            if (isActive) ec = Color.Lerp(edgeColor, Color.white, 0.35f);
            else ec.a *= Mathf.Lerp(0.55f, 1f, 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.Lerp(6f, 24f, p)));

            float e = Mathf.Min(EdgeWidth, w * 0.5f, d * 0.5f);
            SetQuad(edges[0], new Vector3(0f, 0f, d * 0.5f - e * 0.5f), new Vector2(w, e), ec);   // 앞
            SetQuad(edges[1], new Vector3(0f, 0f, -d * 0.5f + e * 0.5f), new Vector2(w, e), ec);  // 뒤
            SetQuad(edges[2], new Vector3(-w * 0.5f + e * 0.5f, 0f, 0f), new Vector2(e, d), ec);  // 왼
            SetQuad(edges[3], new Vector3(w * 0.5f - e * 0.5f, 0f, 0f), new Vector2(e, d), ec);   // 오
        }

        public void Hide()
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        void SetQuad(Renderer r, Vector3 localPos, Vector2 size, Color color)
        {
            var t = r.transform;
            t.localPosition = new Vector3(localPos.x, t.localPosition.y, localPos.z);
            t.localScale = new Vector3(Mathf.Max(size.x, 0.001f), Mathf.Max(size.y, 0.001f), 1f);
            r.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            block.SetColor(ColorId, color);
            r.SetPropertyBlock(block);
        }

        /// <summary>URP Unlit 반투명 머티리얼을 만든다 (에디터에서 에셋으로 저장해두면 빌드에도 포함됨).</summary>
        public static Material CreateMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (!shader) shader = Shader.Find("Sprites/Default");
            var mat = new Material(shader) { name = "Attack Telegraph" };

            if (mat.HasProperty("_Surface"))
            {
                mat.SetFloat("_Surface", 1f); // Transparent
                mat.SetFloat("_Blend", 0f);   // Alpha
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.SetFloat("_Cull", (float)CullMode.Off);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }
            mat.renderQueue = (int)RenderQueue.Transparent;
            return mat;
        }
    }
}
