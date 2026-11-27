using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using SoulsHunter.Combat;
using SoulsHunter.Player;

namespace SoulsHunter.Feedback
{
    /// <summary>
    /// 전투 이벤트를 받아 이펙트와 효과음을 낸다 (타격감).
    /// - 휘두르기 소리 / 타격 불꽃 + 소리 / 약점 / 부위 파괴 / 적 경직
    /// - 가드·패링·가드 붕괴 / 플레이어 피격
    /// 효과음 칸이 비어 있으면 임시 합성음(ProceduralSfx)을 쓴다. 칸에 여러 개 넣으면 무작위로 골라 반복감이 줄어든다.
    /// 씬에 따로 넣지 않아도 게임 시작 때 Player에 자동으로 붙는다.
    /// </summary>
    [RequireComponent(typeof(PlayerCombat))]
    public class CombatFeedback : MonoBehaviour
    {
        [Header("Sound Clips (비워두면 임시 합성음)")]
        [SerializeField] AudioClip[] swingLight;
        [SerializeField] AudioClip[] swingHeavy;
        [SerializeField] AudioClip[] hit;
        [SerializeField] AudioClip[] weakHit;
        [SerializeField] AudioClip[] partBreak;
        [SerializeField] AudioClip[] block;
        [SerializeField] AudioClip[] parry;
        [SerializeField] AudioClip[] guardBreak;
        [SerializeField] AudioClip[] playerHurt;
        [SerializeField] AudioClip[] stagger;

        [Header("Mix")]
        [Range(0f, 1f)] [SerializeField] float masterVolume = 0.8f;
        [Tooltip("소리 높낮이를 매번 이만큼 흔들어 반복감을 줄임")]
        [Range(0f, 0.2f)] [SerializeField] float pitchVariation = 0.07f;
        [Tooltip("0 = 화면 소리, 1 = 위치에 따라 멀어지면 작아짐")]
        [Range(0f, 1f)] [SerializeField] float spatialBlend = 0.4f;

        [Header("Effects")]
        [SerializeField] bool effectsEnabled = true;

        PlayerCombat combat;
        PlayerGuard guard;
        Health health;
        readonly List<AudioSource> sources = new();
        Transform sourceRoot;

        void Awake()
        {
            combat = GetComponent<PlayerCombat>();
            guard = GetComponent<PlayerGuard>();
            health = GetComponent<Health>();
        }

        void OnEnable()
        {
            combat.SwingStarted += OnSwing;
            combat.HitLanded += OnHitLanded;
            Hurtbox.AnyBroken += OnPartBroken;
            Health.AnyStaggered += OnStaggered;
            if (health) health.Damaged += OnPlayerDamaged;
            if (guard)
            {
                guard.Blocked += OnBlocked;
                guard.Parried += OnParried;
                guard.GuardBroken += OnGuardBroken;
            }
        }

        void OnDisable()
        {
            combat.SwingStarted -= OnSwing;
            combat.HitLanded -= OnHitLanded;
            Hurtbox.AnyBroken -= OnPartBroken;
            Health.AnyStaggered -= OnStaggered;
            if (health) health.Damaged -= OnPlayerDamaged;
            if (guard)
            {
                guard.Blocked -= OnBlocked;
                guard.Parried -= OnParried;
                guard.GuardBroken -= OnGuardBroken;
            }
        }

        void OnDestroy()
        {
            if (sourceRoot) Destroy(sourceRoot.gameObject);
        }

        // ───────── 이벤트 ─────────

        void OnSwing()
        {
            bool heavy = combat.CurrentType != PlayerCombat.AttackType.Light;
            Play(heavy ? swingHeavy : swingLight, heavy ? ProceduralSfx.SwingHeavy : ProceduralSfx.SwingLight,
                 ChestPoint, heavy ? 0.75f : 0.6f);
        }

        void OnHitLanded(Hurtbox box, DamageInfo info)
        {
            bool weak = box && box.DamageMultiplier > 1f;
            var a = combat.CurrentAttack;
            float strength = Mathf.Clamp01((a != null ? a.hitStop / 0.12f : 0.5f) * 0.6f + combat.ChargeLevel * 0.2f);
            Vector3 dir = info.Direction.sqrMagnitude > 0.01f ? info.Direction : transform.forward;
            if (effectsEnabled) HitEffects.Hit(info.Point, dir, strength, weak);
            Play(weak ? weakHit : hit, weak ? ProceduralSfx.WeakHit : ProceduralSfx.Hit, info.Point, Mathf.Lerp(0.75f, 1f, strength));
        }

        void OnPartBroken(Hurtbox box)
        {
            Vector3 p = PartCenter(box);
            if (effectsEnabled) HitEffects.PartBreak(p);
            Play(partBreak, ProceduralSfx.PartBreak, p, 1f);
        }

        void OnStaggered(Health target, DamageInfo info)
        {
            if (target == health) return; // 플레이어 경직은 피격 연출로 충분
            Vector3 p = info.Point != Vector3.zero ? info.Point : target.transform.position + Vector3.up * 1.5f;
            if (effectsEnabled) HitEffects.Stagger(p);
            Play(stagger, ProceduralSfx.Stagger, p, 0.9f);
        }

        void OnPlayerDamaged(DamageInfo info)
        {
            if (guard && guard.IsGuarding) return; // 막은 공격은 가드 연출로 대신

            Vector3 p = info.Point != Vector3.zero ? info.Point : ChestPoint;
            if (effectsEnabled) HitEffects.PlayerHurt(p, info.Direction);
            Play(playerHurt, ProceduralSfx.PlayerHurt, p, 0.9f);
        }

        void OnBlocked(DamageInfo info)
        {
            Vector3 p = ShieldPoint;
            if (effectsEnabled) HitEffects.Block(p, DirectionOrForward(info.Direction));
            Play(block, ProceduralSfx.Block, p, 0.85f);
        }

        void OnParried(DamageInfo info)
        {
            Vector3 p = ShieldPoint;
            if (effectsEnabled) HitEffects.Parry(p, DirectionOrForward(info.Direction));
            Play(parry, ProceduralSfx.Parry, p, 1f);
        }

        void OnGuardBroken()
        {
            Vector3 p = ShieldPoint;
            if (effectsEnabled) { HitEffects.Block(p, -transform.forward); HitEffects.Stagger(p); }
            Play(guardBreak, ProceduralSfx.GuardBreak, p, 1f);
        }

        // ───────── 보조 ─────────

        Vector3 ChestPoint => transform.position + Vector3.up * 1.3f + transform.forward * 0.3f;
        Vector3 ShieldPoint => transform.position + Vector3.up * 1.25f + transform.forward * 0.55f - transform.right * 0.15f;
        Vector3 DirectionOrForward(Vector3 d) => d.sqrMagnitude > 0.01f ? d : -transform.forward;

        static Vector3 PartCenter(Hurtbox box)
        {
            if (!box) return Vector3.zero;
            var col = box.GetComponent<Collider>();
            return col ? col.bounds.center : box.transform.position;
        }

        void Play(AudioClip[] options, AudioClip fallback, Vector3 position, float volume)
        {
            AudioClip clip = fallback;
            if (options != null && options.Length > 0)
            {
                var pick = options[Random.Range(0, options.Length)];
                if (pick) clip = pick;
            }
            if (!clip) return;

            var src = GetSource();
            src.transform.position = position;
            src.clip = clip;
            src.volume = volume * masterVolume;
            src.pitch = 1f + Random.Range(-pitchVariation, pitchVariation);
            src.Play();
        }

        AudioSource GetSource()
        {
            if (!sourceRoot)
            {
                sourceRoot = new GameObject("SFX Pool").transform;
                sources.Clear();
            }
            foreach (var s in sources)
                if (s && !s.isPlaying) return s;

            var go = new GameObject("SFX");
            go.transform.SetParent(sourceRoot, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = spatialBlend;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 5f;
            src.maxDistance = 40f;
            src.dopplerLevel = 0f;
            sources.Add(src);
            return src;
        }

        // ───────── 자동 부착 ─────────

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            AttachToPlayer();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => AttachToPlayer();

        static void AttachToPlayer()
        {
            var player = GameObject.FindWithTag("Player");
            if (!player || !player.GetComponent<PlayerCombat>()) return;
            if (!player.GetComponent<CombatFeedback>()) player.AddComponent<CombatFeedback>();
            if (player.GetComponent<PlayerWeaponLoadout>() && !player.GetComponent<SwordTrail>()) player.AddComponent<SwordTrail>();
        }
    }
}
