using System.Collections.Generic;
using UnityEngine;

namespace SoulsHunter.Feedback
{
    /// <summary>
    /// 타격 이펙트 (전부 코드로 만든 파티클, 재사용 풀).
    /// - 일반 타격: 흰·주황 불꽃이 공격 방향 반대로 튐 + 짧은 섬광
    /// - 약점: 더 크고 붉은 불꽃 + 퍼지는 고리
    /// - 부위 파괴: 큰 섬광 + 파편 + 먼지 + 큰 고리
    /// - 강인도 붕괴(경직): 노란 고리
    /// - 가드: 푸른 불꽃 / 패링: 하얀 큰 섬광 + 고리 + 불꽃
    /// - 플레이어 피격: 붉은 파편
    /// 히트스톱 중엔 파티클도 같이 멈춰서 타격감이 산다.
    /// </summary>
    public static class HitEffects
    {
        enum Kind { Sparks, Flash, Ring, Debris }

        class Spec
        {
            public Kind kind;
            public Color a = Color.white, b = Color.white;
            public int count = 10;
            public Vector2 speed = new(4f, 8f), size = new(0.05f, 0.1f), life = new(0.15f, 0.3f);
            public float gravity, coneAngle = 60f, endSizeScale = 1f;
        }

        static readonly Dictionary<Spec, List<ParticleSystem>> pool = new();
        static Transform root;

        // ───────── 미리 정해둔 이펙트 ─────────

        static readonly Spec hitSparks = new() { kind = Kind.Sparks, a = new Color(1f, 0.95f, 0.75f), b = new Color(1f, 0.55f, 0.15f), count = 14, speed = new(5f, 11f), size = new(0.03f, 0.07f), life = new(0.12f, 0.3f), gravity = 1.2f, coneAngle = 55f };
        static readonly Spec hitFlash = new() { kind = Kind.Flash, a = new Color(1f, 0.9f, 0.7f, 0.9f), count = 1, size = new(0.55f, 0.7f), life = new(0.09f, 0.09f), endSizeScale = 1.4f };
        static readonly Spec weakSparks = new() { kind = Kind.Sparks, a = new Color(1f, 0.8f, 0.4f), b = new Color(1f, 0.25f, 0.1f), count = 26, speed = new(6f, 14f), size = new(0.04f, 0.09f), life = new(0.15f, 0.4f), gravity = 1.5f, coneAngle = 70f };
        static readonly Spec weakFlash = new() { kind = Kind.Flash, a = new Color(1f, 0.6f, 0.3f, 1f), count = 1, size = new(0.9f, 1.1f), life = new(0.12f, 0.12f), endSizeScale = 1.6f };
        static readonly Spec weakRing = new() { kind = Kind.Ring, a = new Color(1f, 0.5f, 0.2f, 0.9f), count = 1, size = new(0.3f, 0.3f), life = new(0.22f, 0.22f), endSizeScale = 7f };
        static readonly Spec breakFlash = new() { kind = Kind.Flash, a = new Color(1f, 0.85f, 0.6f, 1f), count = 1, size = new(1.8f, 2.2f), life = new(0.2f, 0.2f), endSizeScale = 1.8f };
        static readonly Spec breakRing = new() { kind = Kind.Ring, a = new Color(1f, 0.8f, 0.5f, 1f), count = 1, size = new(0.5f, 0.5f), life = new(0.4f, 0.4f), endSizeScale = 12f };
        static readonly Spec breakDebris = new() { kind = Kind.Debris, a = new Color(0.45f, 0.38f, 0.32f), b = new Color(0.25f, 0.2f, 0.18f), count = 30, speed = new(3f, 8f), size = new(0.08f, 0.2f), life = new(0.6f, 1.1f), gravity = 2.5f, coneAngle = 80f };
        static readonly Spec breakSparks = new() { kind = Kind.Sparks, a = new Color(1f, 0.9f, 0.6f), b = new Color(1f, 0.45f, 0.1f), count = 40, speed = new(6f, 16f), size = new(0.04f, 0.1f), life = new(0.2f, 0.5f), gravity = 1.5f, coneAngle = 90f };
        static readonly Spec staggerRing = new() { kind = Kind.Ring, a = new Color(1f, 0.9f, 0.3f, 1f), count = 1, size = new(0.6f, 0.6f), life = new(0.35f, 0.35f), endSizeScale = 8f };
        static readonly Spec blockSparks = new() { kind = Kind.Sparks, a = new Color(0.85f, 0.95f, 1f), b = new Color(0.4f, 0.7f, 1f), count = 16, speed = new(4f, 9f), size = new(0.03f, 0.06f), life = new(0.1f, 0.25f), gravity = 1f, coneAngle = 65f };
        static readonly Spec blockFlash = new() { kind = Kind.Flash, a = new Color(0.6f, 0.8f, 1f, 0.8f), count = 1, size = new(0.6f, 0.7f), life = new(0.08f, 0.08f), endSizeScale = 1.3f };
        static readonly Spec parryFlash = new() { kind = Kind.Flash, a = new Color(1f, 1f, 1f, 1f), count = 1, size = new(2f, 2.2f), life = new(0.16f, 0.16f), endSizeScale = 1.6f };
        static readonly Spec parryRing = new() { kind = Kind.Ring, a = new Color(0.85f, 0.95f, 1f, 1f), count = 1, size = new(0.4f, 0.4f), life = new(0.3f, 0.3f), endSizeScale = 9f };
        static readonly Spec parrySparks = new() { kind = Kind.Sparks, a = Color.white, b = new Color(0.6f, 0.85f, 1f), count = 32, speed = new(7f, 15f), size = new(0.04f, 0.08f), life = new(0.15f, 0.35f), gravity = 1f, coneAngle = 85f };
        static readonly Spec hurtBurst = new() { kind = Kind.Debris, a = new Color(0.75f, 0.08f, 0.06f), b = new Color(0.4f, 0.02f, 0.02f), count = 14, speed = new(2f, 5f), size = new(0.05f, 0.11f), life = new(0.3f, 0.6f), gravity = 2f, coneAngle = 60f };

        // ───────── 재생 ─────────

        /// <param name="direction">공격이 들어온 방향 (공격자 → 맞은 쪽). 불꽃은 반대로 튄다</param>
        /// <param name="strength">0~1. 클수록 많이·크게 (강공격, 모으기 등)</param>
        public static void Hit(Vector3 point, Vector3 direction, float strength, bool weakPoint)
        {
            var rot = SprayRotation(direction);
            float s = Mathf.Lerp(0.85f, 1.5f, Mathf.Clamp01(strength));
            if (weakPoint)
            {
                Play(weakSparks, point, rot, s);
                Play(weakFlash, point, rot, s);
                Play(weakRing, point, rot, s);
            }
            else
            {
                Play(hitSparks, point, rot, s);
                Play(hitFlash, point, rot, s);
            }
        }

        public static void PartBreak(Vector3 point)
        {
            Play(breakFlash, point, Quaternion.identity, 1f);
            Play(breakRing, point, Quaternion.identity, 1f);
            Play(breakSparks, point, Quaternion.LookRotation(Vector3.up), 1f);
            Play(breakDebris, point, Quaternion.LookRotation(Vector3.up), 1f);
        }

        public static void Stagger(Vector3 point) => Play(staggerRing, point, Quaternion.identity, 1f);

        public static void Block(Vector3 point, Vector3 direction)
        {
            var rot = SprayRotation(direction);
            Play(blockSparks, point, rot, 1f);
            Play(blockFlash, point, rot, 1f);
        }

        public static void Parry(Vector3 point, Vector3 direction)
        {
            var rot = SprayRotation(direction);
            Play(parryFlash, point, rot, 1f);
            Play(parryRing, point, rot, 1f);
            Play(parrySparks, point, rot, 1f);
        }

        public static void PlayerHurt(Vector3 point, Vector3 direction) =>
            Play(hurtBurst, point, SprayRotation(-direction), 1f); // 피해 방향(뒤쪽)으로 튐

        static Quaternion SprayRotation(Vector3 direction)
        {
            Vector3 back = -direction;
            back.y = Mathf.Max(back.y, 0.25f); // 살짝 위로
            return back.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(back.normalized) : Quaternion.identity;
        }

        static void Play(Spec spec, Vector3 position, Quaternion rotation, float scale)
        {
            var ps = Get(spec);
            ps.transform.SetPositionAndRotation(position, rotation);
            ps.transform.localScale = Vector3.one * scale;
            var emission = ps.emission;
            emission.SetBurst(0, new ParticleSystem.Burst(0f, (short)Mathf.RoundToInt(spec.count * (spec.count > 1 ? scale : 1f))));
            ps.Play(true);
        }

        static ParticleSystem Get(Spec spec)
        {
            if (!root)
            {
                root = new GameObject("FX Pool").transform;
                Object.DontDestroyOnLoad(root.gameObject);
                pool.Clear();
            }
            if (!pool.TryGetValue(spec, out var list)) pool[spec] = list = new List<ParticleSystem>();
            foreach (var p in list)
                if (p && !p.IsAlive(true)) return p;
            var created = Create(spec);
            list.Add(created);
            return created;
        }

        static ParticleSystem Create(Spec spec)
        {
            var go = new GameObject($"FX {spec.kind}");
            go.transform.SetParent(root, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.3f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 128;
            main.startLifetime = new ParticleSystem.MinMaxCurve(spec.life.x, spec.life.y);
            main.startSize = new ParticleSystem.MinMaxCurve(spec.size.x, spec.size.y);
            main.startColor = new ParticleSystem.MinMaxGradient(spec.a, spec.b);
            main.gravityModifier = spec.gravity;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)spec.count) });

            var shape = ps.shape;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
            col.color = fade;

            var sol = ps.sizeOverLifetime;
            switch (spec.kind)
            {
                case Kind.Sparks:
                    main.startSpeed = new ParticleSystem.MinMaxCurve(spec.speed.x, spec.speed.y);
                    shape.shapeType = ParticleSystemShapeType.Cone;
                    shape.angle = spec.coneAngle * 0.5f;
                    shape.radius = 0.05f;
                    renderer.renderMode = ParticleSystemRenderMode.Stretch; // 길쭉한 불꽃 줄기
                    renderer.velocityScale = 0.04f;
                    renderer.lengthScale = 1.5f;
                    renderer.sharedMaterial = FxResources.Additive;
                    sol.enabled = true;
                    sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));
                    var drag = ps.limitVelocityOverLifetime;
                    drag.enabled = true;
                    drag.drag = 3f;
                    break;

                case Kind.Flash:
                case Kind.Ring:
                    main.startSpeed = 0f;
                    shape.enabled = false;
                    renderer.renderMode = ParticleSystemRenderMode.Billboard;
                    renderer.sharedMaterial = spec.kind == Kind.Ring ? FxResources.RingAdditive : FxResources.Additive;
                    sol.enabled = true;
                    // 곡선은 0~1 범위로 두고 배율로 키운다: 시작 = 1배, 끝 = endSizeScale배
                    float end = Mathf.Max(1f, spec.endSizeScale);
                    sol.size = new ParticleSystem.MinMaxCurve(end, AnimationCurve.EaseInOut(0f, 1f / end, 1f, 1f));
                    break;

                case Kind.Debris:
                    main.startSpeed = new ParticleSystem.MinMaxCurve(spec.speed.x, spec.speed.y);
                    main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                    shape.shapeType = ParticleSystemShapeType.Cone;
                    shape.angle = spec.coneAngle * 0.5f;
                    shape.radius = 0.15f;
                    renderer.renderMode = ParticleSystemRenderMode.Billboard;
                    renderer.sharedMaterial = FxResources.Alpha;
                    sol.enabled = true;
                    sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.5f));
                    break;
            }
            return ps;
        }
    }
}
