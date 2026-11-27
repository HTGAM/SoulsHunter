using System;
using UnityEngine;
using SoulsHunter.Combat;
using SoulsHunter.Core;
using SoulsHunter.Player;

namespace SoulsHunter.World
{
    /// <summary>
    /// 화톳불. 처음엔 꺼져 있고 상호작용하면 불이 붙는다.
    /// 휴식하면: 체력·스태미나·회복약 회복, 부활 지점 갱신, 모든 적 부활(월드 리셋).
    /// </summary>
    public class Bonfire : Interactable
    {
        [SerializeField] string bonfireName = "Bonfire";
        [SerializeField] bool litAtStart = false;
        [Tooltip("휴식 후 잠깐 움직일 수 없는 시간(초)")]
        [SerializeField] float restDuration = 1.2f;

        [Header("Visual")]
        [SerializeField] Light fireLight;
        [SerializeField] GameObject flame;
        [SerializeField] float baseIntensity = 3f;

        public string BonfireName => bonfireName;
        public bool IsLit { get; private set; }

        /// <summary>(화톳불, 처음 불을 붙였는지)</summary>
        public static event Action<Bonfire, bool> Rested;

        public override string Prompt => IsLit ? "휴식한다" : "화톳불을 밝힌다";

        // 사냥 퀘스트 중에는 쉴 수 없다 (쉬면 몬스터가 회복되므로)
        public override bool CanInteract(GameObject player) => base.CanInteract(player) && !GameState.QuestActive;

        void Awake() => SetLit(litAtStart);

        void Update()
        {
            if (IsLit && fireLight)
                fireLight.intensity = baseIntensity * (0.85f + 0.15f * Mathf.PerlinNoise(Time.time * 6f, 0f) * 2f);
        }

        public override void Interact(GameObject player)
        {
            bool firstTime = !IsLit;
            SetLit(true);

            if (player.TryGetComponent(out Health health)) health.ResetHealth();
            if (player.TryGetComponent(out PlayerStamina stamina)) stamina.Refill();
            if (player.TryGetComponent(out PlayerFlask flask)) flask.Refill();
            if (player.TryGetComponent(out PlayerLocomotion locomotion)) locomotion.Stun(restDuration);
            if (player.TryGetComponent(out PlayerDamageHandler damage))
            {
                GetRespawnPose(out var spawn, out var rot);
                damage.SetRespawnPoint(spawn, rot);
            }

            GameEvents.RaiseWorldReset();
            Rested?.Invoke(this, firstTime);
        }

        /// <summary>이 화톳불에서 부활할 위치와 방향 (화톳불 앞, 화톳불을 바라봄)</summary>
        public void GetRespawnPose(out Vector3 position, out Quaternion rotation)
        {
            position = transform.position + transform.forward * 1.5f;
            rotation = Quaternion.LookRotation(-transform.forward);
        }

        /// <summary>저장 데이터를 불러올 때 이벤트 없이 불만 붙인다.</summary>
        public void RestoreLit() => SetLit(true);

        void SetLit(bool lit)
        {
            IsLit = lit;
            if (fireLight) fireLight.enabled = lit;
            if (flame) flame.SetActive(lit);
        }
    }
}
