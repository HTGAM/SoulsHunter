using System.Collections.Generic;
using UnityEngine;

namespace SoulsHunter.World
{
    /// <summary>
    /// 지역 분위기 (하늘·안개·햇빛). 플레이어가 이 지역 반경 안에 들어오면 이 지역 설정으로 바뀐다.
    /// 안전구역·사냥터·투기장처럼 서로 멀리 떨어진 지역마다 하나씩 둔다.
    /// </summary>
    public class AreaAtmosphere : MonoBehaviour
    {
        [SerializeField] string areaName = "Area";
        [Tooltip("지역 중심(이 오브젝트 위치)에서 이 거리 안이면 이 지역")]
        [SerializeField] float radius = 150f;
        [SerializeField] Material skybox;
        [SerializeField] Color fogColor = new(0.6f, 0.65f, 0.7f);
        [SerializeField] float fogStart = 40f;
        [SerializeField] float fogEnd = 220f;
        [SerializeField] Color ambientColor = new(0.55f, 0.58f, 0.62f);
        [Tooltip("해 방향 (x: 높이각, y: 방위각)")]
        [SerializeField] Vector2 sunAngles = new(45f, -30f);
        [SerializeField] Color sunColor = new(1f, 0.95f, 0.85f);
        [SerializeField] float sunIntensity = 1.2f;

        static readonly List<AreaAtmosphere> areas = new();
        static AreaAtmosphere current;
        static Transform player;
        static Light sun;
        static float nextCheck;

        public string AreaName => areaName;

        void OnEnable() => areas.Add(this);
        void OnDisable()
        {
            areas.Remove(this);
            if (current == this) current = null;
        }

        void Update()
        {
            // 지역 판정은 첫 번째 지역 오브젝트 하나만 한다
            if (areas.Count == 0 || areas[0] != this || Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.25f;
            if (!player)
            {
                var go = GameObject.FindWithTag("Player");
                if (!go) return;
                player = go.transform;
            }

            AreaAtmosphere best = null;
            float bestDist = float.MaxValue;
            foreach (var a in areas)
            {
                float d = Vector3.Distance(Flat(a.transform.position), Flat(player.position));
                if (d <= a.radius && d < bestDist) { best = a; bestDist = d; }
            }
            if (best && best != current) best.Apply();
        }

        static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);

        public void Apply()
        {
            current = this;
            if (skybox) RenderSettings.skybox = skybox;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;
            // 하늘(위)·지평선·땅(아래)으로 나눈 주변광: 아래쪽이 어두워서 입체감이 산다
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = ambientColor * 1.1f;
            RenderSettings.ambientEquatorColor = Color.Lerp(ambientColor, fogColor, 0.4f) * 0.85f;
            RenderSettings.ambientGroundColor = ambientColor * 0.4f;

            if (!sun) sun = RenderSettings.sun;
            if (!sun)
                foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional) { sun = l; break; }
            if (sun)
            {
                sun.transform.rotation = Quaternion.Euler(sunAngles.x, sunAngles.y, 0f);
                sun.color = sunColor;
                sun.intensity = sunIntensity;
            }
            DynamicGI.UpdateEnvironment();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            areas.Clear();
            current = null;
            player = null;
            sun = null;
            nextCheck = 0f;
        }

#if UNITY_EDITOR
        public void EditorSetup(string name, float range, Material sky, Color fog, float fogNear, float fogFar,
                                Color ambient, Vector2 sunDir, Color sunTint, float sunPower)
        {
            areaName = name;
            radius = range;
            skybox = sky;
            fogColor = fog;
            fogStart = fogNear;
            fogEnd = fogFar;
            ambientColor = ambient;
            sunAngles = sunDir;
            sunColor = sunTint;
            sunIntensity = sunPower;
        }
#endif
    }
}
