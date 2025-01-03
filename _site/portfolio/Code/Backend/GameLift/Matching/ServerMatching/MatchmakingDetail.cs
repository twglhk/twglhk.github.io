
namespace WardGames.Zooports.Lambda.UserMatchmakingResultService
{
    /// <summary>
    /// 매칭 상세 정보 모델링 클래스
    /// </summary>
    public class MatchmakingDetail
    {
        /// <summary>
        /// 매칭 티켓 리스트
        /// </summary>
        public List<MatchmakingTicket> tickets { get; private set; }

        /// <summary>
        /// 매칭 성공 시 게임 세션 정보
        /// </summary>
        public MatchmakingGameSessionInfo gameSessionInfo { get; private set; }

        /// <summary>
        /// 매칭 Id
        /// </summary>
        public string matchId { get; private set; }

        /// <summary>
        /// 생성자
        /// </summary>
        /// <param name="tickets"></param>
        /// <param name="gameSessionInfo"></param>
        /// <param name="matchId"></param>
        public MatchmakingDetail(List<MatchmakingTicket> tickets, MatchmakingGameSessionInfo gameSessionInfo, string matchId)
        {
            this.tickets = tickets;
            this.gameSessionInfo = gameSessionInfo;
            this.matchId = matchId;
        }
    }
}