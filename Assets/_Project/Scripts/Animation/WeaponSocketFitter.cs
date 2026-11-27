using UnityEngine;

namespace SoulsHunter.Animations
{
    /// <summary>
    /// 캐릭터 뼈를 보고 검·방패를 붙일 위치와 방향을 계산한다 (실행 중에도, 에디터에서도 사용).
    /// - 검(오른손): 주먹 가운데. 칼날은 새끼손가락 → 검지 방향 = 실제로 검을 쥐었을 때 손잡이가 지나는 축
    /// - 방패(왼팔): 팔뚝 바깥쪽. 팔뚝을 따라 붙고 앞면은 몸 바깥을 향함
    /// 지금 포즈(대기 자세여야 자연스러움) 기준으로 계산해서, 뼈 기준 로컬 값으로 돌려준다.
    /// </summary>
    public static class WeaponSocketFitter
    {
        public const string RightSocketName = "Weapon Socket R";
        public const string LeftSocketName = "Weapon Socket L";

        /// <summary>한손검 부품 좌표(캐릭터 기준)에서 손잡이(코등이 바로 뒤)</summary>
        public static readonly Vector3 SwordGrip = new(0.45f, 1.1f, 0.05f);
        /// <summary>한손검 부품 좌표에서 방패 중심</summary>
        public static readonly Vector3 ShieldGrip = new(-0.5f, 1.0f, 0.35f);

        public struct Socket
        {
            public Transform parent;
            public Vector3 localPosition;
            public Quaternion localRotation;
            public bool valid;
        }

        public static void Compute(Animator animator, Transform root, out Socket right, out Socket left)
        {
            right = default;
            left = default;
            if (!animator || !animator.isHuman) return;
            Transform B(HumanBodyBones b) => animator.GetBoneTransform(b);

            var rHand = B(HumanBodyBones.RightHand);
            if (rHand)
            {
                var index = B(HumanBodyBones.RightIndexProximal);
                var little = B(HumanBodyBones.RightLittleProximal);
                Vector3 pos; Quaternion rot;
                if (index && little)
                {
                    Vector3 knuckles = (index.position + little.position) * 0.5f;
                    Vector3 finger = (knuckles - rHand.position).normalized;                       // 손목 → 손가락
                    Vector3 blade = Vector3.ProjectOnPlane(index.position - little.position, finger).normalized; // 쥔 검의 축
                    pos = Vector3.Lerp(rHand.position, knuckles, 0.75f);                           // 주먹 가운데
                    rot = Quaternion.LookRotation(blade, Vector3.Cross(blade, finger));             // x = 손가락 방향(코등이)
                }
                else { pos = rHand.position; rot = root.rotation; }
                right = new Socket
                {
                    parent = rHand,
                    localPosition = rHand.InverseTransformPoint(pos),
                    localRotation = Quaternion.Inverse(rHand.rotation) * rot,
                    valid = true,
                };
            }

            var lLower = B(HumanBodyBones.LeftLowerArm);
            var lHand = B(HumanBodyBones.LeftHand);
            if (lLower && lHand)
            {
                var chest = B(HumanBodyBones.Chest) ? B(HumanBodyBones.Chest) : B(HumanBodyBones.Spine);
                Vector3 along = (lHand.position - lLower.position).normalized;                      // 팔꿈치 → 손목
                Vector3 up = Vector3.ProjectOnPlane(Vector3.up, along);
                if (up.sqrMagnitude < 0.01f) up = Vector3.ProjectOnPlane(root.forward, along);
                up.Normalize();
                Vector3 normal = Vector3.Cross(up, along);                                          // 방패 앞면 (x)
                Vector3 mid = Vector3.Lerp(lLower.position, lHand.position, 0.55f);
                Vector3 outward = Vector3.ProjectOnPlane(mid - (chest ? chest.position : root.position), Vector3.up).normalized
                                  + root.forward * 0.3f;
                if (Vector3.Dot(normal, outward) < 0f) { along = -along; normal = -normal; }
                Vector3 pos = mid + normal * 0.07f;                                                 // 팔뚝 바깥쪽
                Quaternion rot = Quaternion.LookRotation(along, up);                                // z = 팔뚝, y = 위, x = 앞면
                left = new Socket
                {
                    parent = lLower,
                    localPosition = lLower.InverseTransformPoint(pos),
                    localRotation = Quaternion.Inverse(lLower.rotation) * rot,
                    valid = true,
                };
            }
        }
    }
}
