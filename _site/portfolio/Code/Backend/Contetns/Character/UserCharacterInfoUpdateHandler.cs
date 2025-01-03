
using System.Net;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.APIGatewayEvents;
using Newtonsoft.Json;
using WardGames.Web.Dotnet.AWS.Lambda.DynamoDb;
using WardGames.Web.Dotnet.Http;
using WardGames.Zooports.BackendModels;
using WardGames.Zooports.BackendModels.CharacterSkin.Constants;
using WardGames.Zooports.BackendModels.Rune;
using WardGames.Zooports.BackendModels.Table;
using WardGames.Zooports.BackendModels.User;
using WardGames.Zooports.BackendModels.User.Character;
using WardGames.Zooports.BackendModels.User.Profile.Constants;
using WardGames.Zooports.BackendModels.User.Rune;
using WardGames.Zooports.SharedModels.CharacterSkin;
using WardGames.Zooports.SharedModels.Position;
using WardGames.Zooports.SharedModels.Rune;
using WardGames.Zooports.SharedModels.User.Character;

namespace WardGames.Zooports.Lambda.UserCharacterInfoServiceLambda
{
    /// <summary>
    /// 유저의 캐릭터 정보 업데이트를 담당하는 클래스
    /// </summary>
    public class UserCharacterInfoUpdateHandler : DbRequestHandlerBase<UserCharacterInfoUpdateRequest, UserCharacterInfoUpdateResponse>
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        /// <param name="dynamoDB"></param>
        /// <param name="request"></param>
        /// <returns></returns>
        public UserCharacterInfoUpdateHandler(IAmazonDynamoDB dynamoDB, APIGatewayProxyRequest request) : base(dynamoDB, request)
        {
            // base constructor에서 처리
        }

        /// <summary>
        /// 유저의 캐릭터 정보 1개를 업데이트 합니다.
        /// </summary>
        /// <returns></returns>
        protected override async Task<UserCharacterInfoUpdateResponse> DoDynamoDbTask(DynamoDBContext dBContext)
        {
            string userTableName = TableNameService.GetTableName<UserItemBase>();

            try
            {
                string firstNormalRuneString = _requestData.RuneSlotEntity.FirstNormalRune.ToString();
                string secondNormalRuneString = _requestData.RuneSlotEntity.SecondNormalRune.ToString();
                string positionRuneString = _requestData.RuneSlotEntity.PositionRune.ToString();
                string specialRuneString = _requestData.RuneSlotEntity.SpecialRune.ToString();

                /* 유저 캐릭터 & 보유 룬 쿼리 */
                List<Task> userAssetQueryTasks = new List<Task>(4);
                Task<QueryResponse> userRuneItemQueryResponse = _dynamoDB.QueryAsync(new QueryRequest
                {
                    TableName = userTableName,
                    KeyConditionExpression = $"{UserItemConstants.USER_NUMBER} = :userNumber AND begins_with({UserItemConstants.USER_ITEM_KEY}, :userItemKey)",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        { ":userNumber", new AttributeValue { S = _requestData.UserNumber } },
                        { ":userItemKey", new AttributeValue { S = UserRuneItem.GetUserRuneItemSK() } }
                    }
                });
                Task<UserCharacterInfoItem> userCharacterInfoItemQueryTask = dBContext.LoadAsync<UserCharacterInfoItem>(_requestData.UserNumber, UserCharacterInfoItem.GetUserCharacterItemSK(_requestData.CharacterName));
                // Function.LambdaContext?.Logger.LogLine($"UserNumber: {_requestData.UserNumber}");
                // Function.LambdaContext?.Logger.LogLine($"UserCharacterInfoItem.GetUserCharacterItemSK: {UserCharacterInfoItem.GetUserCharacterItemSK(_requestData.CharacterName)}");

                const int NO_SKIN = 0;
                int currentSkinId = _requestData.CharacterSkinData.CurrentSkinId;
                Task<UserCharacterSkinItem>? userCharacterSkinItemQueryTask = currentSkinId == NO_SKIN ? null : dBContext.LoadAsync<UserCharacterSkinItem>(_requestData.UserNumber, UserCharacterSkinItem.GetSK(_requestData.CharacterName, currentSkinId));  
                Task<UserLastSettingItem> userLastSettingItemQueryTask = dBContext.LoadAsync<UserLastSettingItem>(_requestData.UserNumber, UserLastSettingItem.GetUserLastSettingItemSK());
                
                userAssetQueryTasks.Add(userCharacterInfoItemQueryTask);
                userAssetQueryTasks.Add(userRuneItemQueryResponse);
                userAssetQueryTasks.Add(userLastSettingItemQueryTask);
                if (userCharacterSkinItemQueryTask != null)
                    userAssetQueryTasks.Add(userCharacterSkinItemQueryTask);

                await Task.WhenAll(userAssetQueryTasks);

                UserCharacterInfoItem userCharacterInfoItem = userCharacterInfoItemQueryTask.Result;
                UserLastSettingItem userLastSettingItem = userLastSettingItemQueryTask.Result;
                UserCharacterSkinItem? userCharacterSkinItem = userCharacterSkinItemQueryTask?.Result;

                /* 쿼리 유효성 검사 */
                {
                    if (userCharacterInfoItem == null)
                        throw new ApiException("유저 캐릭터 정보가 없습니다.", HttpStatusCode.BadRequest);
                    if (userLastSettingItem == null)
                        throw new ApiException("유저 마지막 설정 정보가 없습니다.", HttpStatusCode.BadRequest);
                }

                /* 스킨 유효성 검사 */
                {
                    if (currentSkinId != NO_SKIN)
                    {
                        if (userCharacterSkinItem == null)
                            throw new InvalidDataException("유저가 보유한 스킨이 없습니다.");
                        if (userCharacterSkinItem.CharacterSkinId != currentSkinId)
                            throw new InvalidDataException("유저가 보유한 스킨과 설정하려는 스킨이 다릅니다.");
                    }
                }

                /* 룬 유효성 검사 */
                {
                    const int MAX_RUNE_SLOT_COUNT = 4;
                    List<UserRuneItem> userRuneItems = new List<UserRuneItem>(MAX_RUNE_SLOT_COUNT);
                    foreach (var item in userRuneItemQueryResponse.Result.Items)
                    {
                        userRuneItems.Add(new UserRuneItem(item));
                    }

                    // 유저 룬 소유 검사
                    if (!CheckUserRuneOwnership(userRuneItems,
                        _requestData.RuneSlotEntity.FirstNormalRune,
                        _requestData.RuneSlotEntity.SecondNormalRune,
                        _requestData.RuneSlotEntity.PositionRune,
                        _requestData.RuneSlotEntity.SpecialRune
                    ))
                        throw new InvalidDataException("유저가 보유한 룬이 아닙니다.");

                    // 유저 룬 레벨 검사
                    uint userLevel = userCharacterInfoItem.Level;
                    bool normalRuneLevelOK = userLevel >= SharedModels.Rune.Constants.RuneConstants.GetRuneEquipLevelLimit(RuneType.Normal);
                    bool positionRuneLevelOK = userLevel >= SharedModels.Rune.Constants.RuneConstants.GetRuneEquipLevelLimit(RuneType.Position);
                    bool specialRuneLevelOK = userLevel >= SharedModels.Rune.Constants.RuneConstants.GetRuneEquipLevelLimit(RuneType.Special);
                    if ((!normalRuneLevelOK && (_requestData.RuneSlotEntity.FirstNormalRune != NormalRune.NULL || _requestData.RuneSlotEntity.SecondNormalRune != NormalRune.NULL))
                        || (!positionRuneLevelOK && _requestData.RuneSlotEntity.PositionRune != PositionRune.NULL)
                        || (!specialRuneLevelOK && _requestData.RuneSlotEntity.SpecialRune != SpecialRune.NULL))
                    {
                        throw new InvalidDataException("유저 레벨에 맞지 않는 룬 착용 중입니다.");
                    }

                    // 룬 갯수 검사
                    const uint EQUIPABLE_MIN_RUNE_QUANTITY = 1u;
                    uint normalRuneCountMin = _requestData.RuneSlotEntity.FirstNormalRune == _requestData.RuneSlotEntity.SecondNormalRune ? 2u : EQUIPABLE_MIN_RUNE_QUANTITY;
                    bool firstNormalRuneEQuantityOK = _requestData.RuneSlotEntity.FirstNormalRune == NormalRune.NULL || userRuneItems.Any(item => item.RuneId == firstNormalRuneString && item.Quantity >= normalRuneCountMin);
                    bool secondNormalRuneEQuantityOK = _requestData.RuneSlotEntity.SecondNormalRune == NormalRune.NULL || userRuneItems.Any(item => item.RuneId == secondNormalRuneString && item.Quantity >= normalRuneCountMin);
                    bool positionRuneQuantityOK = _requestData.RuneSlotEntity.PositionRune == PositionRune.NULL || userRuneItems.Any(item => item.RuneId == positionRuneString && item.Quantity >= EQUIPABLE_MIN_RUNE_QUANTITY);
                    bool specialRuneQuantityOK = _requestData.RuneSlotEntity.SpecialRune == SpecialRune.NULL || userRuneItems.Any(item => item.RuneId == specialRuneString && item.Quantity >= EQUIPABLE_MIN_RUNE_QUANTITY);

                    // Function.LambdaContext?.Logger.LogLine($"FirstNormalRune: {firstNormalRuneEQuantityOK}, SecondNormalRune: {secondNormalRuneEQuantityOK}, PositionRune: {positionRuneQuantityOK}, SpecialRune: {specialRuneQuantityOK}");
                    if (!firstNormalRuneEQuantityOK || !secondNormalRuneEQuantityOK || !positionRuneQuantityOK || !specialRuneQuantityOK)
                        throw new InvalidDataException("유저가 보유한 룬의 갯수가 부족합니다.");

                    // 룬 타입별 포지션 검사 
                    if (_requestData.RuneSlotEntity.PositionRune != PositionRune.NULL)
                    {
                        string[] parsedPositionRuneName = _requestData.RuneSlotEntity.PositionRune.ToString().Split('_');
                        CharacterPosition characterPosition = Enum.Parse<CharacterPosition>(parsedPositionRuneName[2]);
                        if (characterPosition != userCharacterInfoItem.CharacterPosition)
                            throw new InvalidDataException("유저의 캐릭터 포지션과 룬의 포지션이 일치하지 않습니다.");
                    }

                    // 룬 타입별 캐릭터 검사
                    if (_requestData.RuneSlotEntity.SpecialRune != SpecialRune.NULL)
                    {
                        string[] parsedSpecialRuneName = _requestData.RuneSlotEntity.SpecialRune.ToString().Split('_');
                        if (parsedSpecialRuneName[2] != userCharacterInfoItem.CharacterName.ToString().ToUpper())
                            throw new InvalidDataException("유저의 캐릭터 이름과 룬의 캐릭터 이름이 일치하지 않습니다.");
                    }
                }

                /* 데이터 저장 */
                userCharacterInfoItem.CurrentSkinId = currentSkinId;
                RuneSlotItem userRuneSlotItem = new RuneSlotItem()
                {
                    FirstNormalRune = _requestData.RuneSlotEntity.FirstNormalRune,
                    SecondNormalRune = _requestData.RuneSlotEntity.SecondNormalRune,
                    PositionRune = _requestData.RuneSlotEntity.PositionRune,
                    SpecialRune = _requestData.RuneSlotEntity.SpecialRune
                };

                OptimisticLockingHelper.VersionUp(userCharacterInfoItem);
                OptimisticLockingHelper.VersionUp(userLastSettingItem);
                Task<TransactWriteItemsResponse> updateUserItemTask = _dynamoDB.TransactWriteItemsAsync(new TransactWriteItemsRequest
                {
                    TransactItems = new List<TransactWriteItem>
                    {
                        new TransactWriteItem
                        {
                            Update = new Update
                            {
                                TableName = userTableName,
                                Key = new Dictionary<string, AttributeValue>
                                {
                                    { UserItemConstants.USER_NUMBER, new AttributeValue { S = _requestData.UserNumber } },
                                    { UserItemConstants.USER_ITEM_KEY, new AttributeValue { S = UserCharacterInfoItem.GetUserCharacterItemSK(_requestData.CharacterName) } }
                                },
                                ConditionExpression = $"{CommonConstants.VERSION} < :version",
                                // TODO: 이후에 CharacterSkinConstants.CurrentSkinId로 변경
                                UpdateExpression = $"SET {"CurrentSkinId"} = :currentSkinId, {BackendModels.Rune.Constants.RuneConstants.RUNE_SLOT} = :runeSlot, {CommonConstants.VERSION} = :version",
                                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                                {
                                    { ":currentSkinId", new AttributeValue { N = currentSkinId.ToString() } },
                                    { ":runeSlot", new AttributeValue { M = userRuneSlotItem.ToAttributeValueDictionary()  } },
                                    { ":version", new AttributeValue { N = userCharacterInfoItem.Version.ToString() } }
                                }
                            }
                        },
                        new TransactWriteItem
                        {
                            Update = new Update
                            {
                                TableName = userTableName,
                                Key = new Dictionary<string, AttributeValue>
                                {
                                    { UserItemConstants.USER_NUMBER, new AttributeValue { S = _requestData.UserNumber } },
                                    { UserItemConstants.USER_ITEM_KEY, new AttributeValue { S = UserLastSettingItem.GetUserLastSettingItemSK() } }
                                },
                                ConditionExpression = $"{CommonConstants.VERSION} < :version",
                                UpdateExpression = $"SET {UserProfileConstants.USER_LAST_SELECTED_CHARACTER_NAME} = :characterName, {CommonConstants.VERSION} = :version",
                                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                                {
                                    { ":characterName", new AttributeValue { S = _requestData.CharacterName.ToString() } },
                                    { ":version", new AttributeValue { N = userLastSettingItem.Version.ToString() } }
                                },
                            }
                        }
                    }
                });
                await updateUserItemTask;

                /* 응답 반환 */
                return new UserCharacterInfoUpdateResponse();
            }
            catch (Exception e)
            {
                Function.LambdaContext?.Logger.LogLine(e.Message);
                Function.LambdaContext?.Logger.LogLine(e.StackTrace);
                throw;
            }
        }

        private bool CheckUserRuneOwnership(IEnumerable<UserRuneItem> userRuneItems, params Enum[] runes)
        {
            foreach (var rune in runes)
            {
                string runeId = rune.ToString();
                if (runeId == "NULL") 
                    continue;
                
                // Function.LambdaContext?.Logger.LogLine(runeId);
                if (!userRuneItems.Any(rune => rune.RuneId == runeId))
                    return false;
            }
            return true;
        }
    }
}