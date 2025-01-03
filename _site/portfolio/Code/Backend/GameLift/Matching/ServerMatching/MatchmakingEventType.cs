
namespace WardGames.Zooports.Lambda.UserMatchmakingResultService
{
    /// <summary>
    /// Matchmaking 이벤트 타입
    /// </summary>
    public enum MatchmakingEventType
    {
        /// <summary>
        /// Matchmaking 시작
        /// </summary>
        MatchmakingSearching,

        /// <summary>
        /// 잠재적인 매칭이 생성됨
        /// </summary>
        PotentialMatchCreated,

        /// <summary>
        /// 매칭을 수락함
        /// </summary>
        AcceptMatch,

        /// <summary>
        /// 매칭 수락이 완료됨
        /// </summary>
        AcceptMatchCompleted,

        /// <summary>
        /// 매칭이 성공함
        /// </summary>
        MatchmakingSucceeded,

        /// <summary>
        /// 매칭이 실패함 (타임아웃)
        /// </summary>
        MatchmakingTimedOut,

        /// <summary>
        /// 매칭이 취소됨
        /// </summary>
        MatchmakingCancelled,

        /// <summary>
        /// 매칭이 실패함
        /// </summary>
        MatchmakingFailed
    }
}