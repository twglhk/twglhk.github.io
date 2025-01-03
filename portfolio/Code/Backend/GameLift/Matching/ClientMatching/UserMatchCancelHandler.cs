
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.GameLift;
using Amazon.GameLift.Model;
using Amazon.Lambda.APIGatewayEvents;
using WardGames.Web.Dotnet.AWS.Lambda.APIGatewayEvents;
using WardGames.Zooports.BackendModels.User.Matching;
using WardGames.Zooports.SharedModels.Matching;
using WardGames.Zooports.SharedModels.User.Matching;

namespace WardGames.Zooports.Lambda.UserMatchingService
{
    /// <summary>
    /// 유저 매칭 취소 요청을 처리하는 클래스
    /// </summary>
    public class UserMatchCancelHandler : WssRequestHandlerBase<UserMatchCancelRequest, UserMatchCancelResponse>
    {
        private readonly IAmazonGameLift _gameLiftClient;

        /// <summary>
        /// default constructor
        /// </summary>
        /// <param name="dynamoDB"></param>
        /// <param name="request"></param>
        /// <param name="action"></param>
        /// <param name="gameLiftClient"></param>
        /// <returns></returns>
        public UserMatchCancelHandler(IAmazonDynamoDB dynamoDB, APIGatewayProxyRequest request, string action, IAmazonGameLift gameLiftClient) : base(dynamoDB, request, action)
        {
            _gameLiftClient = gameLiftClient;
        }

        /// <summary>
        /// 매칭을 취소하는 메서드
        /// </summary>
        /// <returns></returns>
        protected override async Task<UserMatchCancelResponse> GenerateResponseTask()
        {
            try
            {
                using (DynamoDBContext dBContext = new DynamoDBContext(_dynamoDB))
                {
                    // 가장 최근 매칭 정보 쿼리
                    // TODO: 캐시 DB 사용하도록 변경
                    UserLatestMatchingInfoItem userLatestMatchingInfoItem = await dBContext.LoadAsync<UserLatestMatchingInfoItem>(_requestData.UserNumber, UserLatestMatchingInfoItem.GetSK());
                    DescribeMatchmakingRequest describeMatchmakingRequest = new DescribeMatchmakingRequest
                    {
                        TicketIds = new List<string> { userLatestMatchingInfoItem.TicketId }
                    };
                    DescribeMatchmakingResponse describeMatchmakingResponse = await _gameLiftClient.DescribeMatchmakingAsync(describeMatchmakingRequest);
                    MatchmakingConfigurationStatus status = describeMatchmakingResponse.TicketList[0].Status;

                    bool cancelPossible = status == MatchmakingConfigurationStatus.QUEUED || status == MatchmakingConfigurationStatus.SEARCHING || status == MatchmakingConfigurationStatus.REQUIRES_ACCEPTANCE;
                    Function.LambdaContext?.Logger.LogLine($"matching ticket status: {status} - cancelPossible: {cancelPossible}");

                    MatchMakingCancelResult matchMakingCancelResult = MatchMakingCancelResult.Null;
                    if (cancelPossible)
                    {
                        StopMatchmakingRequest stopMatchmakingRequest = new StopMatchmakingRequest
                        {
                            TicketId = userLatestMatchingInfoItem.TicketId
                        };
                        StopMatchmakingResponse stopMatchmakingResponse = await _gameLiftClient.StopMatchmakingAsync(stopMatchmakingRequest);
                        bool isCancelSuccess = stopMatchmakingResponse.HttpStatusCode == System.Net.HttpStatusCode.OK;
                        matchMakingCancelResult = isCancelSuccess ? MatchMakingCancelResult.Cancelled : MatchMakingCancelResult.NotCancelled;
                    }
                    else
                    {
                        matchMakingCancelResult = MatchMakingCancelResult.NotCancelled;
                    }

                    return new UserMatchCancelResponse(matchMakingCancelResult);
                }
            }
            catch (InvalidRequestException ex)
            {
                Function.LambdaContext?.Logger.LogLine($"InvalidRequestException: {ex.Message} {ex.StackTrace}");
                return new UserMatchCancelResponse(MatchMakingCancelResult.NotCancelled);
            }
            catch (InternalServiceException ex)
            {
                Function.LambdaContext?.Logger.LogLine($"InternalServiceException: {ex.Message} {ex.StackTrace}");
                throw;
            }
            catch (Exception ex)
            {
                Function.LambdaContext?.Logger.LogLine($"Exception: {ex.Message} {ex.StackTrace}");
                throw;
            }
        }
    }
}