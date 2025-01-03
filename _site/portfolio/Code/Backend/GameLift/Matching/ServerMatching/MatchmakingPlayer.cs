
namespace WardGames.Zooports.Lambda.UserMatchmakingResultService
{
    /// <summary>
    /// 매칭 플레이어 모델링 클래스
    /// </summary>
    public class MatchmakingPlayer
    {
        /// <summary>
        /// 매칭 플레이어의 플레이어 아이디
        /// </summary>
        public string playerId { get; private set; }

        /// <summary>
        /// 매칭 플레이어의 플레이어 세션 아이디
        /// </summary>
        public string playerSessionId { get; private set; }

        /// <summary>
        /// 매칭 플레이어의 팀
        /// </summary>
        public string team { get; private set; }

        /// <summary>
        /// 생성자
        /// </summary>
        /// <param name="playerId"></param>
        /// <param name="playerSessionId"></param>
        /// <param name="team"></param>
        public MatchmakingPlayer(string playerId, string playerSessionId, string team)
        {
            this.playerId = playerId;
            this.playerSessionId = playerSessionId;
            this.team = team;
        }
    }
}