using System.Net;
using Amazon.DynamoDBv2;
using Amazon.GameLift;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Newtonsoft.Json;
using WardGames.Dotnet.Util.Json;
using WardGames.Web.Dotnet.Http;
using WardGames.Web.Dotnet.WebSocket;
using WardGames.Zooports.BackendModels;
using WardGames.Zooports.SharedModels.Matching;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace WardGames.Zooports.Lambda.UserMatchingService
{
    /// <summary>
    /// Function class for the lambda
    /// </summary>
    public class Function
    {
        /// <summary>
        /// 람다 함수의 context
        /// </summary>
        public static ILambdaContext? LambdaContext { get; private set; }

        private IAmazonDynamoDB _dyanmoDBClient;

        private AmazonGameLiftClient _gameLiftClient;

        private Dictionary<string, string> _configurationNameByStage;

        /// <summary>
        /// default constructor
        /// </summary>
        public Function()
        {
            _gameLiftClient = new AmazonGameLiftClient();
            _dyanmoDBClient = new AmazonDynamoDBClient();
            _configurationNameByStage = new Dictionary<string, string>
            {
                { "dev", Environment.GetEnvironmentVariable("MATCHING_CONFIGURATION_NAME_DEV")! },
                { "prod", Environment.GetEnvironmentVariable("MATCHING_CONFIGURATION_NAME_PROD")! },
                { "live", Environment.GetEnvironmentVariable("MATCHING_CONFIGURATION_NAME_LIVE")! }
            };
        }

        /// <summary>
        /// Function handler for the lambda
        /// </summary>
        /// <param name="request">APIGatewayProxy로 들어오는 요청</param>
        /// <param name="context">Lambda관련 유용한 기능을 사용할 수 있는 context</param>
        /// <returns>APIGatewayProxy 요청에 대한 응답</returns>
        public async Task<APIGatewayProxyResponse> FunctionHandler(APIGatewayProxyRequest request, ILambdaContext context)
        {
            LambdaContext = context;

            try
            {
                string? action = JsonParsingHelper.ExtractStringValueFromJson(request.Body, "action");
                if (action == null)
                    throw new NullReferenceException("Action is null");

                string configurationName = _configurationNameByStage[request.RequestContext.Stage];
                MatchingRequestAction matchingAction = Enum.Parse<MatchingRequestAction>(action);
                switch (matchingAction)
                {
                    case MatchingRequestAction.queueing:
                        UserMatchQueueingRequestHandler handler = new UserMatchQueueingRequestHandler(_dyanmoDBClient, request, MatchingResponseAction.queued.ToString(), _gameLiftClient, configurationName);
                        return await handler.CreateResponse();
                    case MatchingRequestAction.ticketStatus:
                        MatchmakingTicketStatusQueryRequestHandlerParams ticketStatusParams = new MatchmakingTicketStatusQueryRequestHandlerParams(request, MatchingResponseAction.queried.ToString(), _gameLiftClient);
                        MatchmakingTicketStatusQueryRequestHandler ticketStatusHandler = new MatchmakingTicketStatusQueryRequestHandler(ticketStatusParams);
                        return await ticketStatusHandler.CreateResponse();
                    case MatchingRequestAction.cancel:
                        UserMatchCancelHandler cancelHandler = new UserMatchCancelHandler(_dyanmoDBClient, request, MatchingResponseAction.canceled.ToString(), _gameLiftClient);
                        return await cancelHandler.CreateResponse();
                    default:
                        throw new ApiException("Action is not valid", HttpStatusCode.BadRequest);
                }
            }
            catch (Exception ex)
            {
                context.Logger.LogLine(ex.Message);
                context.Logger.LogLine(ex.StackTrace);
                return new APIGatewayProxyResponse
                {
                    Body = null,
                    StatusCode = (int)HttpStatusCode.InternalServerError
                };
            }
        }
    }
}
