
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.Model;
using WardGames.Zooports.BackendModels.Table;
using WardGames.Zooports.BackendModels.User;
using WardGames.Zooports.BackendModels.User.ExpToken;
using WardGames.Zooports.SharedModels.ExpToken;

namespace WardGames.Zooports.Lambda.UserAssetServiceLambda
{
    /// <summary>
    /// 새로운 UserAsset 데이터가 추가되었을 때, DB에 대한 유효성 검사를 수행하는 클래스 <br/>
    /// 데이터가 없다면 여기서 추가.
    /// </summary>
    public class AssetDBValidator
    {
        private readonly IAmazonDynamoDB _dynamoDB;
        private readonly string _userNumber;
        private readonly AssetValidateFlag _flag;
        private readonly UserAssetItemContainer _userAssetItemContainer;

        /// <summary>
        /// default constructor
        /// </summary>
        /// <param name="dynamoDB"></param>
        /// <param name="userNumber"></param>
        /// <param name="flag"></param>
        /// <param name="userAssetItemContainer"></param>
        public AssetDBValidator(IAmazonDynamoDB dynamoDB, string userNumber, AssetValidateFlag flag, UserAssetItemContainer userAssetItemContainer)
        {
            _dynamoDB = dynamoDB;
            _userNumber = userNumber;
            _flag = flag;
            _userAssetItemContainer = userAssetItemContainer;
        }

        /// <summary>
        /// 유저 Asset DB에 대한 유효성 검사를 수행합니다. <br/>
        /// </summary>
        public async Task Validate()
        {
            if (_flag.HasFlag(AssetValidateFlag.All))
                return;    

            // TODO: TASK 리스트로 변경
            bool isExpTokenMarked = _flag.HasFlag(AssetValidateFlag.ExpToken);
            if (!isExpTokenMarked)
                await GenerateUserExpTokenItem();
        }

        /// <summary>
        /// 유저의 경험치 토큰 아이템을 검사합니다.
        /// </summary>
        /// <returns></returns>
        private async Task GenerateUserExpTokenItem()
        {
            UserExpTokenItem userSilverExpTokenItem = new UserExpTokenItem(_userNumber, ExpTokenType.SilverToken, 0);
            UserExpTokenItem userGoldExpTokenItem = new UserExpTokenItem(_userNumber, ExpTokenType.GoldToken, 0);
            UserExpTokenItem userPlatinumExpTokenItem = new UserExpTokenItem(_userNumber, ExpTokenType.PlatinumToken, 0);
            
            _userAssetItemContainer.UserExpTokenItems.Add(userSilverExpTokenItem);
            _userAssetItemContainer.UserExpTokenItems.Add(userGoldExpTokenItem);
            _userAssetItemContainer.UserExpTokenItems.Add(userPlatinumExpTokenItem);

            // TODO: 트랜잭션 리스트로 변경
            string tableName = TableNameService.GetTableName<UserItemBase>();
            TransactWriteItemsRequest transactWriteRequest = new TransactWriteItemsRequest
            {
                TransactItems = new List<TransactWriteItem>
                {
                    new TransactWriteItem
                    {
                        Put = new Put
                        {
                            TableName = tableName,
                            Item = userSilverExpTokenItem.ConvertToAttributeValues()
                        }
                    },
                    new TransactWriteItem
                    {
                        Put = new Put
                        {
                            TableName = tableName,
                            Item = userGoldExpTokenItem.ConvertToAttributeValues()
                        }
                    },
                    new TransactWriteItem
                    {
                        Put = new Put
                        {
                            TableName = tableName,
                            Item = userPlatinumExpTokenItem.ConvertToAttributeValues()
                        }
                    }
                }
            };
            await _dynamoDB.TransactWriteItemsAsync(transactWriteRequest);
        }
    }
}