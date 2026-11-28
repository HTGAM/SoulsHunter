using System.Collections.Generic;
using UnityEngine;

namespace SoulsHunter.Player
{
    /// <summary>
    /// 장착한 무기를 몸에 반영한다.
    /// - 겉모습: WeaponData.visuals 대로 간단한 도형 모델을 만든다 (방패가 있으면 가드 때 들어 올림)
    /// - 이동 속도, 구르기 거리 배율
    /// 무기를 바꿀 때마다 다시 만든다.
    /// </summary>
    [RequireComponent(typeof(PlayerWeapon), typeof(PlayerLocomotion))]
    public class PlayerWeaponLoadout : MonoBehaviour
    {
        [Tooltip("무기 모델을 붙일 위치 (비워두면 캐릭터 루트)")]
        [SerializeField] Transform socket;

        [Header("Hand Sockets (캐릭터 모델이 있을 때)")]
        [Tooltip("오른손 소켓. 설정하면 오른쪽(x > 0) 부품이 손을 따라 움직인다")]
        [SerializeField] Transform rightHandSocket;
        [Tooltip("왼손 소켓. 왼쪽(x < 0) 부품(방패, 쌍검 왼쪽)이 손을 따라 움직인다")]
        [SerializeField] Transform leftHandSocket;
        [Tooltip("무기 부품 좌표(캐릭터 기준)에서 오른손이 쥐는 지점")]
        [SerializeField] Vector3 rightGrip = new(0.45f, 1.1f, 0.15f);
        [SerializeField] Vector3 leftGrip = new(-0.47f, 1.0f, 0.2f);

        PlayerWeapon weapon;
        PlayerLocomotion locomotion;
        PlayerGuard guard;
        Transform modelRoot, leftRoot;
        Shader shader;
        readonly Dictionary<Color, Material> materials = new();

        void Awake()
        {
            weapon = GetComponent<PlayerWeapon>();
            locomotion = GetComponent<PlayerLocomotion>();
            guard = GetComponent<PlayerGuard>();
            weapon.WeaponChanged += Apply;
        }

        void Start() => Apply(weapon.Equipped);

        /// <summary>
        /// 손 소켓을 새로 지정하고 무기 모델을 다시 만든다 (캐릭터 모델에 맞춰 실행 중 자동 정렬할 때).
        /// </summary>
        public void SetHandSockets(Transform right, Transform left, Vector3 rightGripPoint, Vector3 leftGripPoint)
        {
            rightHandSocket = right;
            leftHandSocket = left;
            rightGrip = rightGripPoint;
            leftGrip = leftGripPoint;
            BuildModel(weapon ? weapon.Equipped : null);
        }

        public Transform RightHandSocket => rightHandSocket;

        Transform blade;
        /// <summary>칼날 부품 (로컬 z축 -0.5 ~ 0.5가 칼날 길이). 없으면 null</summary>
        public Transform Blade => blade;
        public Transform LeftHandSocket => leftHandSocket;

        void Apply(WeaponData w)
        {
            locomotion.WeaponSpeedMultiplier = w ? w.moveSpeedMultiplier : 1f;
            locomotion.DodgeDistanceMultiplier = w ? w.dodgeDistanceMultiplier : 1f;
            BuildModel(w);
        }

        void BuildModel(WeaponData w)
        {
            if (modelRoot) Destroy(modelRoot.gameObject);
            if (leftRoot) Destroy(leftRoot.gameObject);
            bool hands = rightHandSocket;
            modelRoot = new GameObject("Weapon Model").transform;
            modelRoot.SetParent(hands ? rightHandSocket : socket ? socket : transform, false);
            if (hands)
            {
                leftRoot = new GameObject("Weapon Model (Left)").transform;
                leftRoot.SetParent(leftHandSocket ? leftHandSocket : rightHandSocket, false);
            }

            Transform shield = null;
            blade = null;
            float bladeLength = 0f;
            if (w)
            {
                foreach (var part in w.visuals)
                {
                    var go = GameObject.CreatePrimitive(part.shape);
                    go.name = part.isShield ? "Shield" : "Weapon Part";
                    Destroy(go.GetComponent<Collider>());
                    bool left = hands && part.position.x < 0f;
                    go.transform.SetParent(left ? leftRoot : modelRoot, false);
                    go.transform.localPosition = !hands ? part.position : part.position - (left ? leftGrip : rightGrip);
                    go.transform.localRotation = Quaternion.Euler(part.rotation);
                    go.transform.localScale = part.scale;
                    go.GetComponent<Renderer>().sharedMaterial = MaterialFor(part.color);
                    if (part.isShield) shield = go.transform;
                    // 칼날 = 방패가 아닌 부품 중 가장 긴 것 (궤적 이펙트용)
                    else if (!blade || part.scale.z > bladeLength) { blade = go.transform; bladeLength = part.scale.z; }
                }
            }
            // 진짜 모델이 있으면 도형은 숨기고(칼날 궤적용 위치는 유지) 모델을 그 자리에 맞춰 넣는다
            if (w && w.rightModel) ReplaceWithModel(modelRoot, w.rightModel);
            if (w && w.leftModel && leftRoot) ReplaceWithModel(leftRoot, w.leftModel);

            // 손에 붙은 방패는 가드 애니메이션이 들어 올리므로 코드로 움직이지 않는다
            if (guard) guard.SetShield(hands ? null : shield);
        }

        /// <summary>
        /// root 아래 도형 부품들이 차지하는 상자에 모델을 맞춘다.
        /// 모델의 가장 긴 축 → 상자의 가장 긴 축, 가장 얇은 축 → 가장 얇은 축이 되도록 돌리고, 가장 긴 길이가 같아지게 키운다.
        /// </summary>
        void ReplaceWithModel(Transform root, GameObject prefab)
        {
            var parts = new List<Renderer>(root.GetComponentsInChildren<Renderer>());
            if (parts.Count == 0) return;
            var box = LocalBounds(root, parts);
            foreach (var r in parts) r.enabled = false;

            var model = Instantiate(prefab, root);
            model.name = "Weapon Mesh";
            foreach (var c in model.GetComponentsInChildren<Collider>()) Destroy(c);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            var mb = LocalBounds(model.transform, model.GetComponentsInChildren<Renderer>());
            if (mb.size.sqrMagnitude < 1e-6f) return;

            int[] mAxes = SortAxes(mb.size), bAxes = SortAxes(box.size);
            // 모델 축 mAxes[i] → 상자 축 bAxes[i] 로 보내는 회전 (거울상이면 가장 얇은 축을 뒤집는다)
            var cols = new Vector3[3];
            for (int i = 0; i < 3; i++) cols[mAxes[i]] = Axis(bAxes[i]);
            if (Vector3.Dot(Vector3.Cross(cols[0], cols[1]), cols[2]) < 0f) cols[mAxes[2]] = -cols[mAxes[2]];
            var rot = Quaternion.LookRotation(cols[2], cols[1]);

            float scale = box.size[bAxes[0]] / Mathf.Max(0.0001f, mb.size[mAxes[0]]);
            model.transform.localRotation = rot;
            model.transform.localScale = Vector3.one * scale;
            model.transform.localPosition = box.center - rot * (mb.center * scale);
        }

        static Vector3 Axis(int i) => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;

        /// <summary>크기가 큰 축부터 (0=x, 1=y, 2=z)</summary>
        static int[] SortAxes(Vector3 size)
        {
            var axes = new List<int> { 0, 1, 2 };
            axes.Sort((a, b) => size[b].CompareTo(size[a]));
            return axes.ToArray();
        }

        /// <summary>root 로컬 좌표에서 렌더러 메시들을 감싸는 상자</summary>
        static Bounds LocalBounds(Transform root, IEnumerable<Renderer> renderers)
        {
            var toRoot = root.worldToLocalMatrix;
            bool any = false;
            var b = new Bounds();
            foreach (var r in renderers)
            {
                Mesh mesh = r is SkinnedMeshRenderer sk ? sk.sharedMesh : r.TryGetComponent(out MeshFilter mf) ? mf.sharedMesh : null;
                if (!mesh) continue;
                var m = toRoot * r.transform.localToWorldMatrix;
                var mbounds = mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mbounds.center + Vector3.Scale(mbounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(corner);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            return b;
        }

        Material MaterialFor(Color c)
        {
            if (materials.TryGetValue(c, out var m)) return m;
            if (!shader)
            {
                shader = Shader.Find("Universal Render Pipeline/Lit");
                if (!shader) shader = Shader.Find("Standard");
            }
            m = new Material(shader) { color = c, name = "Weapon" };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            materials[c] = m;
            return m;
        }

        void OnDestroy()
        {
            foreach (var m in materials.Values) if (m) Destroy(m);
        }
    }
}
