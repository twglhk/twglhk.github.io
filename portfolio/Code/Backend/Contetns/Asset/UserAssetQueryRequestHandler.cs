
using System.Net;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.APIGatewayEvents;
using WardGames.Web.Dotnet.AWS.Lambda.APIGatewayEvents;
using WardGames.Web.Dotnet.Http;
using WardGames.Zooports.BackendModels.Table;
using WardGames.Zooports.BackendModels.User;
using WardGames.Zooports.BackendModels.User.Box;
using WardGames.Zooports.BackendModels.User.Character;
using WardGames.Zooports.BackendModels.User.Currency;
using WardGames.Zooports.BackendModels.User.ExpToken;
using WardGames.Zooports.BackendModels.User.Profile.Constants;
using WardGames.Zooports.BackendModels.User.Rune;
using WardGames.Zooports.SharedModels.Character;
using WardGames.Zooports.SharedModels.User;

namespace WardGames.Zooports.Lambda.UserAssetServiceLambda
{
    /// <summary>
    /// UserAssetQueryRequest를 처리하는 handler
    /// </summary>
    public class UserAssetQueryRequestHandler : ApiRequestHandlerBase<UserAssetQueryRequest, UserAssetQueryResponse>
    {
        /// <summary>
        /// default constructor
        /// </summary>
        /// <param name="dynamoDB"></param>
        /// <param name="request"></param> 
        public UserAssetQueryRequestHandler(IAmazonDynamoDB dynamoDB, APIGatewayProxyRequest request) : base(dynamoDB, request) { /* base class init */ }

        /// <summary>
        /// User의 Asset을 쿼리하여 반환하는 메서드
        /// </summary>
        /// <returns></returns>
        protected override async Task<UserAssetQueryResponse> GenerateResponseTask()
        {
            try
            {
                /* 유저 데이터 쿼리 */
                string userTableName = TableNameService.GetTableName<UserItemBase>();
                Task<QueryResponse> queryResponseTask = _dynamoDB!.QueryAsync(new QueryRequest
                {
                    TableName = userTableName,
                    KeyConditionExpression = $"{UserItemConstants.USER_NUMBER} = :userNumber AND begins_with({UserItemConstants.USER_ITEM_KEY}, :assetPrefix)",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        { ":userNumber", new AttributeValue { S = _requestData.UserNumber } },
                        { ":assetPrefix", new AttributeValue { S = UserItemConstants.USER_ITEM_CATEGORY_ASSET } },
                    }
                });
                await queryResponseTask;

                /* 데이터 매핑 */
                UserAssetItemContainer userAssetItemContainer = new UserAssetItemContainer();

                // TODO: 리펙토링 - 모든 리스트를 UserAssetItemContainer로 옮긴 뒤 저장하고, DTO로 변환
                List<UserCharacterInfoItem> userCharacterInfoItems = new List<UserCharacterInfoItem>();
                List<UserCharacterSkinItem> userCharacterSkinItems = new List<UserCharacterSkinItem>();
                List<UserRuneItem> userRuneItems = new List<UserRuneItem>();
                List<UserBoxItem> userBoxItems = new List<UserBoxItem>();
                UserCurrencyItem userCurrencyItem = new UserCurrencyItem();
                CharacterName userLastSelectedCharacterName = CharacterName.NULL;
                AssetValidateFlag assetValidateFlag = AssetValidateFlag.Null;

                QueryResponse queryResponse = queryResponseTask.Result;
                foreach (Dictionary<string, AttributeValue> item in queryResponse.Items)
                {
                    string itemSortKey = item[UserItemConstants.USER_ITEM_KEY].S;
                    string subSortKey = ExtractAssetItemSubSortKey(itemSortKey);

                    if (subSortKey.Equals(UserItemConstants.USER_ITEM_KEY_CHARACTER))
                    {
                        UserCharacterInfoItem userCharacterInfoItem = new UserCharacterInfoItem(item);
                        userCharacterInfoItems.Add(userCharacterInfoItem);
                    }
                    else if (subSortKey.Equals(UserItemConstants.USER_ITEM_KEY_CHARACTER_SKIN))
                    {
                        UserCharacterSkinItem userCharacterSkinItem = new UserCharacterSkinItem(item);
                        userCharacterSkinItems.Add(userCharacterSkinItem);
                    }
                    else if (subSortKey.Equals(UserItemConstants.USER_ITEM_KEY_RUNE))
                    {
                        UserRuneItem userRuneItem = new UserRuneItem(item);
                        userRuneItems.Add(userRuneItem);
                    }
                    else if (subSortKey.Equals(UserItemConstants.USER_ITEM_KEY_CURRENCY))
                    {
                        userCurrencyItem = new UserCurrencyItem(item);
                    }
                    else if (subSortKey.Equals(UserItemConstants.USER_ITEM_KEY_LAST_SETTING))
                    {
                        userLastSelectedCharacterName = (CharacterName)Enum.Parse(typeof(CharacterName), item[UserProfileConstants.USER_LAST_SELECTED_CHARACTER_NAME].S);
                    }
                    else if (subSortKey.Equals(UserItemConstants.USER_ITEM_KEY_BOX))
                    {
                        userBoxItems.Add(new UserBoxItem(item));
                    }
                    else if (subSortKey.Equals(UserItemConstants.USER_ITEM_KEY_EXP_TOKEN))
                    {
                        userAssetItemContainer.UserExpTokenItems.Add(new UserExpTokenItem(item));
                        assetValidateFlag |= AssetValidateFlag.ExpToken;
                    }
                    else
                    {
                        throw new System.Exception($"Unknown user asset item: {subSortKey}");
                    }
                }

                /* 유효성 검사 */
                AssetDBValidator assetDBValidator = new AssetDBValidator(_dynamoDB, _requestData.UserNumber, assetValidateFlag, userAssetItemContainer);
                await assetDBValidator.Validate();

                /* DTO 매핑 처리 후 반환 */
                // TODO: 리펙토링, UserAssetItemContainer를 백엔드 모델로 옮긴 뒤, 파라미터로 받아서 처리
                return UserAssetDTOMapper.ToDTO(userCharacterInfoItems, userCharacterSkinItems, userRuneItems, userBoxItems, userAssetItemContainer.UserExpTokenItems, userLastSelectedCharacterName, userCurrencyItem);
            }
            catch (Exception e)
            {
                Function.LambdaContext?.Logger.LogLine(e.Message);
                Function.LambdaContext?.Logger.LogLine(e.StackTrace);
                throw;
            }
        }

        /// <summary>
        /// Asset Item 레코드의 sort key에서 서브 아이템 키를 추출하는 메서드
        /// </summary>
        /// <param name="fullSortKey"></param>
        /// <returns></returns>
        public static string ExtractAssetItemSubSortKey(string fullSortKey)
        {
            string[]? segments = fullSortKey.Split('#');
            string subSortKey = segments.Length > 1 ? segments[1] : string.Empty;
            if (string.IsNullOrEmpty(subSortKey))
            {
                throw new ApiException($"The provided key is not in the expected format: {fullSortKey}", HttpStatusCode.InternalServerError);
            }
            return subSortKey;
        }
    }
}