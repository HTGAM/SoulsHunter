using System;
using UnityEngine;

namespace SoulsHunter.Feedback
{
    /// <summary>
    /// 효과음 파일이 없을 때 쓰는 임시 합성음. 실제 효과음을 넣으면 CombatAudio가 그쪽을 우선 사용한다.
    /// 노이즈·사인파·필터로 휘두르기, 베기, 막기, 패링 같은 소리를 흉내 낸다.
    /// </summary>
    public static class ProceduralSfx
    {
        const int Rate = 44100;
        static ClipCache cache;

        class ClipCache
        {
            public AudioClip swingLight, swingHeavy, hit, weakHit, partBreak, block, parry, guardBreak, playerHurt, stagger;
        }

        static ClipCache C => cache ??= Build();

        public static AudioClip SwingLight => C.swingLight;
        public static AudioClip SwingHeavy => C.swingHeavy;
        public static AudioClip Hit => C.hit;
        public static AudioClip WeakHit => C.weakHit;
        public static AudioClip PartBreak => C.partBreak;
        public static AudioClip Block => C.block;
        public static AudioClip Parry => C.parry;
        public static AudioClip GuardBreak => C.guardBreak;
        public static AudioClip PlayerHurt => C.playerHurt;
        public static AudioClip Stagger => C.stagger;

        static ClipCache Build()
        {
            var rng = new System.Random(1234);
            float Noise() => (float)(rng.NextDouble() * 2.0 - 1.0);

            return new ClipCache
            {
                // 휘두르기: 걸러진 바람 소리가 빠르게 커졌다 사라짐 (필터가 열렸다 닫힘 → '휙')
                swingLight = Make("Swing Light", 0.22f, (t, s) =>
                {
                    float env = Bump(t, 0.07f, 0.22f);
                    return s.Lowpass(Noise(), Mathf.Lerp(900f, 4200f, Bump(t, 0.08f, 0.2f))) * env * 0.9f;
                }),
                swingHeavy = Make("Swing Heavy", 0.36f, (t, s) =>
                {
                    float env = Bump(t, 0.12f, 0.36f);
                    return s.Lowpass(Noise(), Mathf.Lerp(400f, 2200f, Bump(t, 0.13f, 0.32f))) * env * 1.1f;
                }),
                // 베기 타격: 낮은 '퍽' + 짧은 노이즈 + 딸깍
                hit = Make("Hit", 0.22f, (t, s) =>
                {
                    float thump = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(110f, 55f, t / 0.22f) * t) * Mathf.Exp(-t * 28f);
                    float crack = s.Lowpass(Noise(), 3500f) * Mathf.Exp(-t * 60f);
                    return (thump * 0.9f + crack * 0.8f);
                }),
                weakHit = Make("Weak Hit", 0.3f, (t, s) =>
                {
                    float thump = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(130f, 50f, t / 0.3f) * t) * Mathf.Exp(-t * 20f);
                    float crack = s.Lowpass(Noise(), 6000f) * Mathf.Exp(-t * 35f);
                    float ring = Mathf.Sin(2f * Mathf.PI * 1400f * t) * Mathf.Exp(-t * 30f) * 0.25f;
                    return thump + crack * 0.9f + ring;
                }),
                // 부위 파괴: 길게 부서지는 소리 + 묵직한 쿵
                partBreak = Make("Part Break", 0.7f, (t, s) =>
                {
                    float crunch = s.Lowpass(Noise(), Mathf.Lerp(5000f, 800f, t / 0.7f)) * Mathf.Exp(-t * 6f);
                    float boom = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(80f, 40f, t / 0.7f) * t) * Mathf.Exp(-t * 8f);
                    return crunch * 0.8f + boom;
                }),
                // 막기: 쇳소리(배음 몇 개가 울리다 사라짐) + 부딪히는 소리
                block = Make("Block", 0.4f, (t, s) =>
                    Metal(t, 1150f, 1790f, 2630f, 9f) * 0.55f + s.Lowpass(Noise(), 4000f) * Mathf.Exp(-t * 70f) * 0.7f),
                // 패링: 더 높고 맑게, 더 길게
                parry = Make("Parry", 0.8f, (t, s) =>
                    Metal(t, 2100f, 3170f, 4420f, 4.5f) * 0.6f + s.Lowpass(Noise(), 7000f) * Mathf.Exp(-t * 50f) * 0.6f),
                // 가드 붕괴: 낮은 쇳소리 + 부서지는 소리
                guardBreak = Make("Guard Break", 0.6f, (t, s) =>
                    Metal(t, 520f, 830f, 1260f, 6f) * 0.6f + s.Lowpass(Noise(), 2500f) * Mathf.Exp(-t * 12f) * 0.7f),
                // 플레이어 피격: 둔탁한 소리
                playerHurt = Make("Player Hurt", 0.3f, (t, s) =>
                {
                    float thud = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(150f, 70f, t / 0.3f) * t) * Mathf.Exp(-t * 16f);
                    return thud + s.Lowpass(Noise(), 1500f) * Mathf.Exp(-t * 25f) * 0.6f;
                }),
                // 경직: 낮게 울리는 '쿵-'
                stagger = Make("Stagger", 0.6f, (t, s) =>
                    Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(70f, 45f, t / 0.6f) * t) * Mathf.Exp(-t * 5f) +
                    s.Lowpass(Noise(), 900f) * Mathf.Exp(-t * 10f) * 0.5f),
            };
        }

        /// <summary>0 → peak에서 최대 → end에서 0인 부드러운 봉우리</summary>
        static float Bump(float t, float peak, float end)
        {
            if (t <= 0f || t >= end) return 0f;
            return t < peak ? Mathf.SmoothStep(0f, 1f, t / peak) : Mathf.SmoothStep(1f, 0f, (t - peak) / (end - peak));
        }

        static float Metal(float t, float f1, float f2, float f3, float decay) =>
            (Mathf.Sin(2f * Mathf.PI * f1 * t) + 0.6f * Mathf.Sin(2f * Mathf.PI * f2 * t) + 0.35f * Mathf.Sin(2f * Mathf.PI * f3 * t))
            * Mathf.Exp(-t * decay) / 1.95f;

        /// <summary>한 극점 저역 필터 상태</summary>
        class Filter
        {
            float y;
            public float Lowpass(float x, float cutoff)
            {
                float a = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / Rate);
                y += a * (x - y);
                return y;
            }
        }

        static AudioClip Make(string name, float seconds, Func<float, Filter, float> sample)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            var filter = new Filter();
            float peak = 0.0001f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                data[i] = sample(t, filter);
                peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            }
            // 음량 맞추고 끝을 살짝 페이드 (딸깍 소리 방지)
            float gain = 0.8f / peak;
            int fade = Mathf.Min(n, Rate / 200);
            for (int i = 0; i < n; i++)
            {
                float f = i >= n - fade ? (n - i) / (float)fade : 1f;
                data[i] *= gain * f;
            }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
