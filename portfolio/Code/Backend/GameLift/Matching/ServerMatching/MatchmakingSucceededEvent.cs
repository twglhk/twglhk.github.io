
namespace WardGames.Zooports.Lambda.UserMatchmakingResultService
{
    /// <summary>
    /// 매칭 성공 이벤트 데이터 모델링 클래스
    /// </summary>
    public class MatchmakingSucceededEvent
    {
        /// <summary>
        /// 매칭 성공 시간
        /// </summary>
        public DateTime time { get; private set; }

        /// <summary>
        /// 매칭 상세 정보
        /// </summary>
        public MatchmakingDetail detail { get; private set; }

        /// <summary>
        /// 생성자
        /// </summary>
        /// <param name="time"></param>
        /// <param name="detail"></param>
        public MatchmakingSucceededEvent(DateTime time, MatchmakingDetail detail)
        {
            this.time = time;
            this.detail = detail;
        }
    }
}