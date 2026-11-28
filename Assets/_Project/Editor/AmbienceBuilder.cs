using UnityEditor;
using UnityEngine;

namespace SoulsHunter.EditorTools
{
    /// <summary>
    /// 공기 효과(파티클)와 반사 프로브를 만든다: 낮게 깔린 안개, 햇빛에 떠다니는 먼지, 올라오는 불씨, 굴뚝 연기.
    /// 부드러운 점 텍스처와 파티클 재질은 에셋으로 저장해 빌드에도 들어간다.
    /// </summary>
    public static class AmbienceBuilder
    {
        const string Folder = "Assets/_Project/Data/Environment/Ambience";

        static Texture2D softDot;
        static Material mistMat, moteMat, emberMat, smokeMat;

        static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Data/Environment")) AssetDatabase.CreateFolder("Assets/_Project/Data", "Environment");
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/_Project/Data/Environment", "Ambience");
        }

        /// <summary>가운데가 밝고 가장자리로 부드럽게 사라지는 원 (파티클용)</summary>
        static Texture2D SoftDot()
        {
            if (softDot) return softDot;
            EnsureFolder();
            string path = Folder + "/SoftDot.png";
            softDot = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (softDot) return softDot;

            const int s = 128;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(s * 0.5f, s * 0.5f)) / (s * 0.5f);
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a * (3f - 2f * a);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter ti)
            {
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = true;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.SaveAndReimport();
            }
            softDot = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            return softDot;
        }

        static Material ParticleMat(ref Material cache, string name, Color tint, bool additive)
        {
            if (cache) return cache;
            EnsureFolder();
            string path = $"{Folder}/{name}.mat";
            cache = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!cache)
            {
                cache = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                AssetDatabase.CreateAsset(cache, path);
            }
            cache.SetTexture("_BaseMap", SoftDot());
            cache.SetColor("_BaseColor", tint);
            cache.SetFloat("_Surface", 1f);
            cache.SetFloat("_Blend", additive ? 2f : 0f);
            cache.SetFloat("_ZWrite", 0f);
            cache.SetFloat("_SrcBlend", additive ? (float)UnityEngine.Rendering.BlendMode.SrcAlpha : (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            cache.SetFloat("_DstBlend", additive ? (float)UnityEngine.Rendering.BlendMode.One : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            cache.SetFloat("_SoftParticlesEnabled", 1f);
            cache.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            cache.EnableKeyword("_SOFTPARTICLES_ON");
            cache.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            cache.SetOverrideTag("RenderType", "Transparent");
            EditorUtility.SetDirty(cache);
            return cache;
        }

        public static void ResetCache()
        {
            softDot = null;
            mistMat = moteMat = emberMat = smokeMat = null;
        }

        static ParticleSystem MakeSystem(string name, Transform parent, Vector3 pos, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, true);
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = true;
            return ps;
        }

        static void FadeInOut(ParticleSystem ps, float peakAlpha)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(peakAlpha, 0.3f), new GradientAlphaKey(peakAlpha, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
        }

        /// <summary>바닥에 낮게 깔려 천천히 흐르는 안개 덩어리</summary>
        public static void Mist(Transform parent, Vector3 center, float radius, Color color, int count = 40)
        {
            var ps = MakeSystem("Ground Mist", parent, center + Vector3.up * 1f, ParticleMat(ref mistMat, "Mist", Color.white, false));
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(14f, 22f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(6f, 12f);
            main.startColor = color;
            main.maxParticles = count;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var em = ps.emission;
            em.rateOverTime = count / 18f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(radius * 2f, 0.8f, radius * 2f);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
            vel.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
            FadeInOut(ps, color.a);
        }

        /// <summary>햇빛에 반짝이며 떠다니는 먼지·꽃가루</summary>
        public static void Motes(Transform parent, Vector3 center, float radius, Color color, int count = 120)
        {
            var ps = MakeSystem("Floating Motes", parent, center + Vector3.up * 2f, ParticleMat(ref moteMat, "Motes", Color.white, true));
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
            main.startColor = color;
            main.maxParticles = count;
            var em = ps.emission;
            em.rateOverTime = count / 8f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(radius * 2f, 3.5f, radius * 2f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.25f;
            noise.frequency = 0.3f;
            noise.scrollSpeed = 0.2f;
            FadeInOut(ps, color.a);
        }

        /// <summary>땅에서 천천히 올라오는 불씨 (잿불 분지)</summary>
        public static void RisingEmbers(Transform parent, Vector3 center, float radius, int count = 150)
        {
            var ps = MakeSystem("Rising Embers", parent, center, ParticleMat(ref emberMat, "Embers", Color.white, true));
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.14f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.55f, 0.15f, 1f), new Color(1f, 0.3f, 0.05f, 1f));
            main.maxParticles = count;
            main.gravityModifier = -0.05f;
            var em = ps.emission;
            em.rateOverTime = count / 4.5f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.5f;
            noise.frequency = 0.6f;
            FadeInOut(ps, 1f);
        }

        /// <summary>굴뚝·모닥불 연기</summary>
        public static void Smoke(Transform parent, Vector3 pos, float scale = 1f)
        {
            var ps = MakeSystem("Smoke", parent, pos, ParticleMat(ref smokeMat, "Smoke", Color.white, false));
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.8f * scale, 1.6f * scale);
            main.startColor = new Color(0.55f, 0.55f, 0.55f, 0.35f);
            main.maxParticles = 40;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var em = ps.emission;
            em.rateOverTime = 5f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 8f;
            shape.radius = 0.2f * scale;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.2f));
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
            vel.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0.1f);
            FadeInOut(ps, 0.35f);
        }

        /// <summary>주변을 비추는 반사 프로브 (시작할 때 한 번 찍는다): 금속·물에 주변이 비친다</summary>
        public static void ReflectionProbe(Transform parent, Vector3 center, Vector3 size)
        {
            var go = new GameObject("Reflection Probe");
            go.transform.SetParent(parent, true);
            go.transform.position = center;
            var p = go.AddComponent<ReflectionProbe>();
            p.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
            p.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.OnAwake;
            p.timeSlicingMode = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
            p.resolution = 128;
            p.size = size;
            p.boxProjection = false;
            p.intensity = 1f;
        }
    }
}
