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

        public static void RaiseWorldReset() => WorldReset?.Invoke();
    }
}
