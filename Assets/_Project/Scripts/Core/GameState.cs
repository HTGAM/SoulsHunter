using System;
using UnityEngine;

namespace SoulsHunter.Core
{
    /// <summary>
    /// 일시정지 상태. 멈추면 Time.timeScale = 0, 입력과 카메라 회전이 막힌다.
    /// </summary>
    public static class GameState
    {
        public static bool IsPaused { get; private set; }

        /// <summary>일시정지 메뉴가 씬에 있으면 true (없으면 카메라가 ESC로 커서만 푼다)</summary>
        public static bool PauseMenuAvailable { get; set; }

        /// <summary>(멈췄는지)</summary>
        public static event Action<bool> PauseChanged;

        public static void SetPaused(bool paused)
        {
            if (IsPaused == paused) return;
            IsPaused = paused;
            Time.timeScale = paused ? 0f : 1f;
            PauseChanged?.Invoke(paused);
        }

        // 'Enter Play Mode' 옵션으로 도메인 리로드를 끈 경우에도 값이 남지 않게
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            IsPaused = false;
            PauseMenuAvailable = false;
            PauseChanged = null;
        }
    }
}
