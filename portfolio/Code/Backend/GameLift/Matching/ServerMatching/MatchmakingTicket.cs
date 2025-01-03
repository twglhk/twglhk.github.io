
namespace WardGames.Zooports.Lambda.UserMatchmakingResultService
{
    /// <summary>
    /// 매칭 티켓 모델링 클래스
    /// </summary>
    public class MatchmakingTicket
    {
        /// <summary>
        /// 플레이어(또는 그룹)의 매칭 티켓 아이디
        /// </summary>
        public string ticketId { get; set; }
    
        /// <summary>
        /// 플레이어(또는 그룹)의 매칭 시작 시간
        /// </summary>
        public DateTime startTime { get; set; }

        /// <summary>
        /// 티켓 내의 플레이어 리스트
        /// </summary>
        public List<MatchmakingPlayer> players { get; set; }

        /// <summary>
        /// 생성자
        /// </summary>
        /// <param name="ticketId"></param>
        /// <param name="startTime"></param>
        /// <param name="players"></param>
        public MatchmakingTicket(string ticketId, DateTime startTime, List<MatchmakingPlayer> players)
        {
            this.ticketId = ticketId;
            this.startTime = startTime;
            this.players = players;
        }
    }
}