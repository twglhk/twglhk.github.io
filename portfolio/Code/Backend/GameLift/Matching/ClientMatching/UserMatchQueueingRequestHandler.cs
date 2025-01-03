
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.GameLift;
using Amazon.GameLift.Model;
using Amazon.Lambda.APIGatewayEvents;
using WardGames.Web.Dotnet.AWS.Lambda.APIGatewayEvents;
using WardGames.Zooports.BackendModels.User;
using WardGames.Zooports.BackendModels.User.Character;
using WardGames.Zooports.BackendModels.User.Matching;
using WardGames.Zooports.SharedModels.User.Matching;

namespace WardGames.Zooports.Lambda.UserMatchingService
{
    /// <summary>
    /// UserMatchingQueue를 처리하는 클래스
    /// </summary>
    public class UserMatchQueueingRequestHandler : WssRequestHandlerBase<UserMatchQueueingRequest, UserMatchQueueingResponse>
    {
        private readonly IAmazonGameLift _gameLiftClient;
        private readonly string _connectionId;
        private readonly string _connectionStage;
        private readonly string _configurationName;

        /// <summary>
        /// default constructor
        /// </summary>
        /// <param name="dynamoDB"></param>
        /// <param name="request"></param>
        /// <param name="action"></param>
        /// <param name="gameLiftClient"></param>
        /// <param name="configurationName"></param>
        /// <returns></returns>
        public UserMatchQueueingRequestHandler(
            IAmazonDynamoDB dynamoDB, 
            APIGatewayProxyRequest request, 
            string action, 
            IAmazonGameLift gameLiftClient,
            string configurationName) 
            : base(dynamoDB, request, action)
        {
            _gameLiftClient = gameLiftClient;
            _connectionId = request.RequestContext.ConnectionId;
            _connectionStage = request.RequestContext.Stage;
            _configurationName = configurationName;
        }

        /// <summary>
        /// 매칭을 요청하는 메서드
        /// </summary>
        /// <returns></returns>
        protected override async Task<UserMatchQueueingResponse> GenerateResponseTask()
        {
            try
            {
                using DynamoDBContext dBContext = new DynamoDBContext(_dynamoDB);

                // 가장 최근 매칭 정보 쿼리
                Task<UserLastSettingItem> userLastSettingItemLoadTask = dBContext.LoadAsync<UserLastSettingItem>(_requestData.UserNumber, UserLastSettingItem.GetUserLastSettingItemSK());
                Task<UserProfileItem> userNicknameItemLoadTask = dBContext.LoadAsync<UserProfileItem>(_requestData.UserNumber, UserProfileItem.GetUserProfileItemSK() );
                await Task.WhenAll(userLastSettingItemLoadTask, userNicknameItemLoadTask);

                /* 신규 매칭 요청 */
                UserLastSettingItem userLastSettingItem = userLastSettingItemLoadTask.Result;
                UserProfileItem userProfileItem = userNicknameItemLoadTask.Result;
                UserCharacterInfoItem userCharacterInfoItem = await dBContext.LoadAsync<UserCharacterInfoItem>(_requestData.UserNumber, UserCharacterInfoItem.GetUserCharacterInfoItemSK(userLastSettingItem.LastSelectedCharacterName));
                List<string> userCharacterRuneList = new List<string>
                { 
                    userCharacterInfoItem.RuneSlot.FirstNormalRune.ToString(),
                    userCharacterInfoItem.RuneSlot.SecondNormalRune.ToString(),
                    userCharacterInfoItem.RuneSlot.PositionRune.ToString(),
                    userCharacterInfoItem.RuneSlot.SpecialRune.ToString() 
                };
                
                StartMatchmakingRequest startMatchmakingRequest = new StartMatchmakingRequest
                {
                    ConfigurationName = _configurationName,
                    Players = new List<Player> 
                    { 
                        new Player 
                        { 
                            PlayerId = _requestData.UserNumber,
                            PlayerAttributes = new Dictionary<string, AttributeValue>()
                            {
                                { "userName", new AttributeValue { S = userProfileItem.UserNickname } },
                                { "characterName", new AttributeValue { S = userCharacterInfoItem.CharacterName.ToString() } },
                                { "characterSkinId", new AttributeValue { N = userCharacterInfoItem.CurrentSkinId } },
                                { "level", new AttributeValue { N = userCharacterInfoItem.Level } },
                                { "position", new AttributeValue { S = userCharacterInfoItem.CharacterPosition.ToString() } },
                                { "runeSlot", new AttributeValue { SL = userCharacterRuneList } }
                            }
                        } 
                    }
                };
                StartMatchmakingResponse response = await _gameLiftClient.StartMatchmakingAsync(startMatchmakingRequest);
                await dBContext.SaveAsync(new UserLatestMatchingInfoItem(_requestData.UserNumber, _connectionId, response.MatchmakingTicket.TicketId, _connectionStage));
                
                return new UserMatchQueueingResponse(response.MatchmakingTicket.TicketId);
            }
            catch (Exception ex)
            {
                Function.LambdaContext?.Logger.LogLine(ex.Message);
                Function.LambdaContext?.Logger.LogLine(ex.StackTrace);
                throw;
            }
        }
    }
}