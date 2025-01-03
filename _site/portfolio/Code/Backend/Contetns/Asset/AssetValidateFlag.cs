
namespace WardGames.Zooports.Lambda.UserAssetServiceLambda
{
    /// <summary>
    /// 유저 에셋 DB에 필수로 있어야 하는 항목을 체크합니다. <br/>
    /// 새로 업데이트한 항목을 중심으로 추가합니다. 
    /// </summary>
    [Flags]
    public enum AssetValidateFlag
    {
        /// <summary>
        /// 아무것도 없음
        /// </summary>
        Null = 0,

        /// <summary>
        /// 경험치 토큰
        /// </summary>
        ExpToken = 1 << 0,

        /// <summary>
        /// 모든 항목
        /// </summary>
        All = ExpToken
    }
}