
using System.Net;
using System.Text;
using Amazon.ApiGatewayManagementApi;
using Amazon.ApiGatewayManagementApi.Model;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Newtonsoft.Json;
using WardGames.Web.Dotnet.Http;
using WardGames.Web.Dotnet.WebSocket;
using WardGames.Zooports.BackendModels.User.Matching;
using WardGames.Zooports.SharedModels.Matching;
using WardGames.Zooports.SharedModels.User.Matching;

namespace WardGames.Zooports.Lambda.UserMatchmakingResultService
{
    /// <summary>
    /// 매칭 성공 핸들러
    /// </summary>
    public class MatchmakingSucceededHandler
    {
        private readonly string _webSocketApiEndpoint;
        private readonly IAmazonDynamoDB _amazonDynamoDb;
        private readonly MatchmakingSucceededEvent _matchmakingSucceededEvent;

        /// <summary>
        /// default constructor
        /// </summary>
        /// <param name="webSocketApiEndpoint"></param>
        /// <param name="amazonDynamoDb"></param>
        /// <param name="matchmakingSucceededEvent"></param>
        public MatchmakingSucceededHandler(
            string webSocketApiEndpoint,
            IAmazonDynamoDB amazonDynamoDb,
            MatchmakingSucceededEvent? matchmakingSucceededEvent)
        {
            _webSocketApiEndpoint = webSocketApiEndpoint;
            _amazonDynamoDb = amazonDynamoDb;

            if (matchmakingSucceededEvent == null)
                throw new ApiException("MatchmakingSucceededEvent is null", HttpStatusCode.InternalServerError);
            _matchmakingSucceededEvent = matchmakingSucceededEvent;
        }

        /// <summary>
        /// 매칭 성공 핸들러. 매칭 성사 여부를 유저에게 전송하고, WebSocket 연결을 제거한다.
        /// </summary>
        /// <returns></returns>
        public async Task Handle()
        {
            // 유저의 최신 매칭 데이터 쿼리
            const int MAX_PLAYER_COUNT = 6;
            List<Task<UserLatestMatchingInfoItem>> dynamoDBUserLatestMatchingDataLoadTasks = new List<Task<UserLatestMatchingInfoItem>>(MAX_PLAYER_COUNT);
            using (DynamoDBContext dynamoDbContext = new DynamoDBContext(_amazonDynamoDb))
            {
                foreach (MatchmakingTicket matchmakingTicket in _matchmakingSucceededEvent.detail.tickets)
                {
                    foreach (MatchmakingPlayer player in matchmakingTicket.players)
                    {
                        dynamoDBUserLatestMatchingDataLoadTasks.Add(dynamoDbContext.LoadAsync<UserLatestMatchingInfoItem>(player.playerId, UserLatestMatchingInfoItem.GetSK()));
                    }
                }
                await Task.WhenAll(dynamoDBUserLatestMatchingDataLoadTasks);
            }

            // 쿼리 데이터 파싱
            List<UserLatestMatchingInfoItem> userLatestMatchingInfoItems = new List<UserLatestMatchingInfoItem>(MAX_PLAYER_COUNT);
            foreach (Task<UserLatestMatchingInfoItem> loadTask in dynamoDBUserLatestMatchingDataLoadTasks)
            {
                UserLatestMatchingInfoItem userLatestMatchingInfoItem = loadTask.Result;
                if (userLatestMatchingInfoItem == null)
                    throw new ApiException("UserLatestMatchingInfoItem is null", HttpStatusCode.InternalServerError);
                if (string.IsNullOrEmpty(userLatestMatchingInfoItem.ConnectionId))
                    throw new ApiException("ConnectionId is null or empty", HttpStatusCode.InternalServerError);

                userLatestMatchingInfoItems.Add(userLatestMatchingInfoItem);
            }

            // 전송 환경 세팅
            List<Task> deleteTasks = new List<Task>(MAX_PLAYER_COUNT);
            string connectionStage = userLatestMatchingInfoItems[0].Stage;  // 매칭을 잡았던 대표 유저의 스테이지 정보를 가져옴
            IAmazonApiGatewayManagementApi amazonApiGatewayManagementApi = new AmazonApiGatewayManagementApiClient(new AmazonApiGatewayManagementApiConfig
            {
                ServiceURL = $"{_webSocketApiEndpoint}/{connectionStage}"
            });

            try
            {
                // 게임 서버 연결 정보 전송
                foreach (UserLatestMatchingInfoItem userLatestMatchingInfoItem in userLatestMatchingInfoItems)
                {
                    // 전송할 데이터 생성
                    string userNumber = userLatestMatchingInfoItem.UserNumber;
                    string? playerSessionId = _matchmakingSucceededEvent.detail.gameSessionInfo.players.Find(player => player.playerId == userNumber)?.playerSessionId;
                    if (string.IsNullOrEmpty(playerSessionId))
                        throw new ApiException("PlayerSessionId is null or empty", HttpStatusCode.InternalServerError);

                    UserMatchSuccessResponse userMatchSuccessResponse = new UserMatchSuccessResponse(
                        _matchmakingSucceededEvent.detail.gameSessionInfo.ipAddress,
                        (ushort)_matchmakingSucceededEvent.detail.gameSessionInfo.port,
                        playerSessionId);

                    WebSocketResponse<UserMatchSuccessResponse> webSocketUserMatchSuccessResponse = new WebSocketResponse<UserMatchSuccessResponse>(MatchingResponseAction.matched.ToString(), userMatchSuccessResponse);
                    webSocketUserMatchSuccessResponse.GenerateSuccessResponse();
                    string userMatchSuccessResponseJson = JsonConvert.SerializeObject(webSocketUserMatchSuccessResponse);

                    MemoryStream userMatchSuccessResponseJsonStream = new MemoryStream(Encoding.UTF8.GetBytes(userMatchSuccessResponseJson));
                    PostToConnectionRequest postToConnectionRequest = new PostToConnectionRequest
                    {
                        ConnectionId = userLatestMatchingInfoItem.ConnectionId,
                        Data = userMatchSuccessResponseJsonStream
                    };
                    await amazonApiGatewayManagementApi.PostToConnectionAsync(postToConnectionRequest);
                }

                // 연결 제거
                foreach (UserLatestMatchingInfoItem userLatestMatchingInfoItem in userLatestMatchingInfoItems)
                {
                    DeleteConnectionRequest deleteConnectionRequest = new DeleteConnectionRequest
                    {
                        ConnectionId = userLatestMatchingInfoItem.ConnectionId
                    };
                    deleteTasks.Add(amazonApiGatewayManagementApi.DeleteConnectionAsync(deleteConnectionRequest));
                }
                await Task.WhenAll(deleteTasks);

            }
            catch (GoneException)
            {
                // 유저가 WSS 연결을 끊었을 경우, 예외를 던지지 않음.
                Function.LambdaContext?.Logger.LogLine($"GoneException: User has disconnected");
            }
            catch (Exception ex)
            {
                Function.LambdaContext?.Logger.LogLine($"Exception: {ex.Message}");
                throw;
            }
            finally
            {
                amazonApiGatewayManagementApi.Dispose();
            }
        }
    }
}