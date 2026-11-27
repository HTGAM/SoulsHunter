using UnityEngine;
using UnityEngine.Rendering;
using SoulsHunter.Combat;

namespace SoulsHunter.Enemy
{
    /// <summary>
    /// 바닥 장판 (낙하하는 화염 등). 깔리면 바닥에 빨간 범위가 차오르고, 다 차면 터진다.
    /// 터지는 순간 범위 안의 플레이어가 맞는다. 실행 중에 만들어지고 스스로 사라진다.
    /// </summary>
    public class GroundHazard : MonoBehaviour
    {
        const float FlashTime = 0.3f;

        float radius, delay, timer;
        DamageInfo template;
        Health target;
        AttackTelegraph telegraph;
        Transform flash;
        Material flashMaterial;
        bool exploded;
        readonly Collider[] overlap = new Collider[16];

        public static GroundHazard Spawn(Vector3 center, float radius, float delay, float damage, float poise,
                                         GameObject source, Health target, Material telegraphMaterial, Material flashMaterial)
        {
            var go = new GameObject("Ground Hazard");
            go.transform.position = center;
            var h = go.AddComponent<GroundHazard>();
            h.radius = radius;
            h.delay = Mathf.Max(0.05f, delay);
            h.target = target;
            h.flashMaterial = flashMaterial;
            h.template = new DamageInfo { Amount = damage, PoiseDamage = poise, Source = source };
            h.telegraph = AttackTelegraph.Create(go.transform, telegraphMaterial);
            h.telegraph.transform.SetParent(go.transform, true);
            return h;
        }

        void Update()
        {
            timer += Time.deltaTime;

            if (!exploded)
            {
                // 예고: 정사각형 범위가 가운데서 차오른다 (판정은 원형)
                telegraph.Show(transform.position, Quaternion.identity, Vector2.one * radius * 2f, timer / delay, false);
                if (timer >= delay) Explode();
                return;
            }

            // 폭발 섬광이 커지며 사라진다
            float t = (timer - delay) / FlashTime;
            if (t >= 1f) { Destroy(gameObject); return; }
            if (flash) flash.localScale = Vector3.one * Mathf.Lerp(radius * 1.2f, radius * 2.2f, t);
        }

        void Explode()
        {
            exploded = true;
            telegraph.Show(transform.position, Quaternion.identity, Vector2.one * radius * 2f, 1f, true);
            Destroy(telegraph.gameObject, 0.12f);

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Blast";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * 0.3f;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = flashMaterial ? flashMaterial : EnemyProjectile.DefaultMaterial();
            r.shadowCastingMode = ShadowCastingMode.Off;
            flash = go.transform;

            int count = Physics.OverlapSphereNonAlloc(transform.position + Vector3.up, radius, overlap, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var hb = overlap[i].GetComponent<Hurtbox>();
                if (!hb || hb.Health != target) continue;
                var info = template;
                info.Point = overlap[i].ClosestPoint(transform.position);
                Vector3 dir = hb.transform.position - transform.position;
                dir.y = 0f;
                info.Direction = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward;
                hb.ReceiveHit(info);
                break;
            }
        }
    }
}
