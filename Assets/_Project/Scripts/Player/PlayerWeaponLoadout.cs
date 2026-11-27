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

        PlayerWeapon weapon;
        PlayerLocomotion locomotion;
        PlayerGuard guard;
        Transform modelRoot;
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

        void Apply(WeaponData w)
        {
            locomotion.WeaponSpeedMultiplier = w ? w.moveSpeedMultiplier : 1f;
            locomotion.DodgeDistanceMultiplier = w ? w.dodgeDistanceMultiplier : 1f;
            BuildModel(w);
        }

        void BuildModel(WeaponData w)
        {
            if (modelRoot) Destroy(modelRoot.gameObject);
            modelRoot = new GameObject("Weapon Model").transform;
            modelRoot.SetParent(socket ? socket : transform, false);

            Transform shield = null;
            if (w)
            {
                foreach (var part in w.visuals)
                {
                    var go = GameObject.CreatePrimitive(part.shape);
                    go.name = part.isShield ? "Shield" : "Weapon Part";
                    Destroy(go.GetComponent<Collider>());
                    go.transform.SetParent(modelRoot, false);
                    go.transform.localPosition = part.position;
                    go.transform.localRotation = Quaternion.Euler(part.rotation);
                    go.transform.localScale = part.scale;
                    go.GetComponent<Renderer>().sharedMaterial = MaterialFor(part.color);
                    if (part.isShield) shield = go.transform;
                }
            }
            if (guard) guard.SetShield(shield);
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
