using System;

namespace SoulsHunter.Core
{
    /// <summary>
    /// 게임 전체에 알리는 이벤트.
    /// </summary>
    public static class GameEvents
    {
        /// <summary>화톳불 휴식 등으로 월드를 초기화할 때 (적 부활, 체력 회복).</summary>
        public static event Action WorldReset;

        /// <summary>화면에 짧은 알림을 띄울 때 (리치 텍스트 가능).</summary>
        public static event Action<string> Notified;

        /// <summary>저장할 만한 진행이 생겼을 때 (무기 강화 등). SaveSystem이 듣는다.</summary>
        public static event Action ProgressChanged;

        public static void RaiseWorldReset() => WorldReset?.Invoke();
        public static void Notify(string message) => Notified?.Invoke(message);
        public static void RaiseProgressChanged() => ProgressChanged?.Invoke();
    }
}
