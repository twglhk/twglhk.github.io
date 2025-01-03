
namespace WardGames.Zooports.Lambda.UserMatchmakingResultService
{
    /// <summary>
    /// 매칭 성공 시 게임 세션 정보
    /// </summary>
    public class MatchmakingGameSessionInfo
    {
        /// <summary>
        /// 게임 세션의 ip 주소 
        /// </summary>
        public string ipAddress { get; private set; }

        /// <summary>
        /// 게임 세션의 포트 번호
        /// </summary>
        public int port { get; private set; }

        /// <summary>
        /// 게임 세션 플레이어 리스트
        /// </summary>
        public List<MatchmakingPlayer> players { get; private set; }

        /// <summary>
        /// 생성자
        /// </summary>
        /// <param name="ipAddress"></param>
        /// <param name="port"></param>
        /// <param name="players"></param>
        public MatchmakingGameSessionInfo(string ipAddress, int port, List<MatchmakingPlayer> players)
        {
            this.ipAddress = ipAddress;
            this.port = port;
            this.players = players;
        }
    }
}