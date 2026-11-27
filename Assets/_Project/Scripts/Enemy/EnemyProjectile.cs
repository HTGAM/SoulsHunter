using UnityEngine;
using UnityEngine.Rendering;
using SoulsHunter.Combat;

namespace SoulsHunter.Enemy
{
    /// <summary>
    /// 적이 쏘는 투사체 (화염구 등). 실행 중에 만들어지고 스스로 사라진다.
    /// - 플레이어의 Hurtbox에 닿으면 피해. 구르기 무적이면 그대로 통과한다.
    /// - 벽·기둥(Hurtbox가 없는 콜라이더)에 닿거나 사거리를 넘으면 사라진다.
    /// - 가드·패링은 일반 공격과 똑같이 적용된다 (패링하면 쏜 적의 강인도가 깎인다).
    /// </summary>
    public class EnemyProjectile : MonoBehaviour
    {
        Vector3 velocity;
        float radius, remainingRange;
        DamageInfo template;
        Health target;
        Transform owner;
        readonly Collider[] overlap = new Collider[16];

        public static EnemyProjectile Spawn(Vector3 origin, Vector3 direction, float speed, float radius, float range,
                                            float damage, float poise, GameObject source, Health target, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Enemy Projectile";
            Destroy(go.GetComponent<Collider>());
            go.transform.position = origin;
            go.transform.localScale = Vector3.one * radius * 2f;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = material ? material : DefaultMaterial();
            r.shadowCastingMode = ShadowCastingMode.Off;

            var p = go.AddComponent<EnemyProjectile>();
            p.velocity = direction.normalized * speed;
            p.radius = radius;
            p.remainingRange = range;
            p.target = target;
            p.owner = source ? source.transform : null;
            p.template = new DamageInfo { Amount = damage, PoiseDamage = poise, Source = source };
            return p;
        }

        void Update()
        {
            Vector3 step = velocity * Time.deltaTime;
            transform.position += step;
            remainingRange -= step.magnitude;
            if (remainingRange <= 0f) { Destroy(gameObject); return; }

            int count = Physics.OverlapSphereNonAlloc(transform.position, radius, overlap, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var col = overlap[i];
                if (owner && col.transform.IsChildOf(owner)) continue; // 쏜 몸에는 안 맞는다

                var hb = col.GetComponent<Hurtbox>();
                if (hb)
                {
                    if (hb.Health != target) continue;
                    var info = template;
                    info.Point = col.ClosestPoint(transform.position);
                    info.Direction = velocity.normalized;
                    if (hb.ReceiveHit(info)) { Destroy(gameObject); return; }
                    continue; // 구르기 무적: 통과
                }

                if (!col.isTrigger && !col.GetComponentInParent<Health>()) { Destroy(gameObject); return; } // 벽
            }
        }

        static Material defaultMaterial;

        /// <summary>머티리얼을 지정하지 않았을 때 쓰는 주황색 발광 머티리얼</summary>
        public static Material DefaultMaterial()
        {
            if (defaultMaterial) return defaultMaterial;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) shader = Shader.Find("Standard");
            defaultMaterial = CreateGlowMaterial(shader, new Color(1f, 0.45f, 0.1f), 4f);
            return defaultMaterial;
        }

        public static Material CreateGlowMaterial(Shader shader, Color color, float intensity)
        {
            var mat = new Material(shader) { name = "Enemy Projectile", color = color };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.SetColor("_EmissionColor", color * intensity);
            return mat;
        }
    }
}
