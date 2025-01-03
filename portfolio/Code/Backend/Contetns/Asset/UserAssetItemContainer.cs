
using WardGames.Zooports.BackendModels.User.ExpToken;

namespace WardGames.Zooports.Lambda.UserAssetServiceLambda
{
    /// <summary>
    /// UserAssetItem의 목록들을 저장하는 컨테이너 클래스
    /// </summary>
    public class UserAssetItemContainer
    {
        /// <summary>
        /// 유저가 보유한 경험치 토큰 아이템 리스트
        /// </summary>
        public List<UserExpTokenItem> UserExpTokenItems { get; set; }

        /// <summary>
        /// default constructor
        /// </summary>
        public UserAssetItemContainer()
        {
            UserExpTokenItems = new List<UserExpTokenItem>();
        }
    }
}