using System.Collections.Generic;
using UnityEngine;
using SoulsHunter.Player;

namespace SoulsHunter.Feedback
{
    /// <summary>
    /// 검을 휘두를 때 칼날이 지나간 자리에 남는 빛 궤적.
    /// 공격 중 칼끝이 빠르게 움직일 때만 생기고, 짧게 남았다가 사라진다.
    /// 히트스톱 중엔 궤적도 멈춘다 (Time.time 기준).
    /// </summary>
    [RequireComponent(typeof(PlayerCombat), typeof(PlayerWeaponLoadout))]
    public class SwordTrail : MonoBehaviour
    {
        [SerializeField] Color lightColor = new(1f, 0.95f, 0.85f, 0.75f);
        [SerializeField] Color heavyColor = new(1f, 0.75f, 0.45f, 0.9f);
        [Tooltip("궤적이 남는 시간(초)")]
        [SerializeField] float lifetime = 0.14f;
        [Tooltip("칼끝이 이 속도(m/s) 이상일 때만 궤적을 그린다")]
        [SerializeField] float minTipSpeed = 4f;
        [Tooltip("칼날 길이 중 궤적이 시작되는 위치 (0 = 손잡이, 1 = 칼끝)")]
        [SerializeField, Range(0f, 0.9f)] float startAlongBlade = 0.25f;
        [Tooltip("샘플 사이를 부드럽게 채우는 개수")]
        [SerializeField, Range(0, 6)] int subdivisions = 3;

        struct Sample { public Vector3 b, t; public float time; }

        PlayerCombat combat;
        PlayerWeaponLoadout loadout;
        readonly List<Sample> samples = new();
        readonly List<Vector3> verts = new();
        readonly List<Color> colors = new();
        readonly List<Vector2> uvs = new();
        readonly List<int> tris = new();
        Mesh mesh;
        GameObject trailObject;
        MeshRenderer meshRenderer;
        Vector3 lastTip;
        bool hasLastTip;
        Color currentColor;

        void Awake()
        {
            combat = GetComponent<PlayerCombat>();
            loadout = GetComponent<PlayerWeaponLoadout>();

            trailObject = new GameObject("Sword Trail");
            trailObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); // 월드 좌표로 그림
            mesh = new Mesh { name = "Sword Trail" };
            mesh.MarkDynamic();
            trailObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = trailObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = FxResources.Trail;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            combat.AttackStarted += (type, _) => currentColor = type == PlayerCombat.AttackType.Light ? lightColor : heavyColor;
            currentColor = lightColor;
        }

        void OnDestroy()
        {
            if (trailObject) Destroy(trailObject);
            if (mesh) Destroy(mesh);
        }

        void LateUpdate()
        {
            float now = Time.time;
            var blade = loadout.Blade;

            if (blade)
            {
                Vector3 tip = blade.TransformPoint(0f, 0f, 0.5f);
                Vector3 bas = blade.TransformPoint(0f, 0f, Mathf.Lerp(-0.5f, 0.5f, startAlongBlade));
                float dt = Time.deltaTime;
                float speed = hasLastTip && dt > 0f ? (tip - lastTip).magnitude / dt : 0f;
                lastTip = tip;
                hasLastTip = true;

                if (combat.IsAttacking && speed >= minTipSpeed)
                    samples.Add(new Sample { b = bas, t = tip, time = now });
            }
            else hasLastTip = false;

            samples.RemoveAll(s => now - s.time > lifetime);
            Rebuild(now);
        }

        void Rebuild(float now)
        {
            mesh.Clear();
            if (samples.Count < 2) return;

            verts.Clear(); colors.Clear(); uvs.Clear(); tris.Clear();

            // 샘플 사이를 곡선(Catmull-Rom)으로 채워 각지지 않게
            for (int i = 0; i < samples.Count - 1; i++)
            {
                var p0 = samples[Mathf.Max(0, i - 1)];
                var p1 = samples[i];
                var p2 = samples[i + 1];
                var p3 = samples[Mathf.Min(samples.Count - 1, i + 2)];
                int steps = subdivisions + 1;
                for (int s = 0; s < steps; s++)
                {
                    float u = s / (float)steps;
                    AddPair(CatmullRom(p0.b, p1.b, p2.b, p3.b, u), CatmullRom(p0.t, p1.t, p2.t, p3.t, u),
                            Mathf.Lerp(p1.time, p2.time, u), now);
                }
            }
            var last = samples[^1];
            AddPair(last.b, last.t, last.time, now);

            int pairs = verts.Count / 2;
            for (int i = 0; i < pairs - 1; i++)
            {
                int a = i * 2;
                tris.Add(a); tris.Add(a + 1); tris.Add(a + 2);
                tris.Add(a + 1); tris.Add(a + 3); tris.Add(a + 2);
            }

            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
        }

        void AddPair(Vector3 b, Vector3 t, float time, float now)
        {
            float age = Mathf.Clamp01((now - time) / lifetime);
            var c = currentColor;
            c.a *= (1f - age) * (1f - age); // 오래된 쪽일수록 흐려짐
            verts.Add(b); colors.Add(c); uvs.Add(new Vector2(age, 0f));
            verts.Add(t); colors.Add(c); uvs.Add(new Vector2(age, 1f));
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }
    }
}
