using System.Collections;
using UnityEngine;

namespace SoulsHunter.Combat
{
    /// <summary>
    /// 테스트용 허수아비. 맞으면 번쩍이고, 경직되면 노랗게, 죽으면 사라졌다가 다시 나타난다.
    /// 부위가 파괴되면 해당 부위가 회색으로 바뀐다.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class TrainingDummy : MonoBehaviour
    {
        [SerializeField] float respawnTime = 3f;
        [SerializeField] Color hitColor = Color.white;
        [SerializeField] Color staggerColor = new(1f, 0.85f, 0.2f);
        [SerializeField] Color brokenPartColor = new(0.3f, 0.3f, 0.3f);

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        Health health;
        Renderer[] renderers;
        Collider[] colliders;
        Hurtbox[] hurtboxes;
        LockOnTarget lockOn;
        MaterialPropertyBlock block;
        Coroutine flash;

        void Awake()
        {
            health = GetComponent<Health>();
            renderers = GetComponentsInChildren<Renderer>();
            colliders = GetComponentsInChildren<Collider>();
            hurtboxes = GetComponentsInChildren<Hurtbox>();
            lockOn = GetComponent<LockOnTarget>();
            block = new MaterialPropertyBlock();

            health.Damaged += info =>
            {
                Debug.Log($"[{health.DisplayName}] {info.Part} 피격 {info.Amount:0.#} → 남은 HP {health.Current:0}");
                Flash(hitColor, 0.08f);
            };
            health.Staggered += _ => Flash(staggerColor, 0.4f);
            health.Died += () => StartCoroutine(DieAndRespawn());
            foreach (var hb in hurtboxes)
                hb.Broken += part =>
                {
                    Debug.Log($"[{health.DisplayName}] 부위 파괴: {part.PartName}");
                    var r = part.GetComponent<Renderer>();
                    if (r) SetColor(r, brokenPartColor);
                };
        }

        void Flash(Color color, float duration)
        {
            if (!isActiveAndEnabled) return;
            if (flash != null) StopCoroutine(flash);
            flash = StartCoroutine(FlashRoutine(color, duration));
        }

        IEnumerator FlashRoutine(Color color, float duration)
        {
            foreach (var r in renderers) if (!IsBroken(r)) SetColor(r, color);
            yield return new WaitForSeconds(duration);
            foreach (var r in renderers) if (!IsBroken(r)) r.SetPropertyBlock(null);
            flash = null;
        }

        bool IsBroken(Renderer r)
        {
            var hb = r.GetComponent<Hurtbox>();
            return hb && hb.IsBroken;
        }

        void SetColor(Renderer r, Color c)
        {
            r.GetPropertyBlock(block);
            block.SetColor(BaseColorId, c);
            block.SetColor(ColorId, c);
            r.SetPropertyBlock(block);
        }

        IEnumerator DieAndRespawn()
        {
            Debug.Log($"[{health.DisplayName}] 쓰러짐");
            if (lockOn) lockOn.CanBeTargeted = false;
            SetVisible(false);
            yield return new WaitForSeconds(respawnTime);
            ResetAll();
        }

        /// <summary>체력, 부위, 색, 표시 상태를 처음으로 되돌린다.</summary>
        public void ResetAll()
        {
            StopAllCoroutines();
            flash = null;
            foreach (var hb in hurtboxes) hb.ResetPart();
            foreach (var r in renderers) r.SetPropertyBlock(null);
            health.ResetHealth();
            SetVisible(true);
            if (lockOn) lockOn.CanBeTargeted = true;
        }

        void SetVisible(bool visible)
        {
            foreach (var r in renderers) r.enabled = visible;
            foreach (var c in colliders) c.enabled = visible;
        }
    }
}
