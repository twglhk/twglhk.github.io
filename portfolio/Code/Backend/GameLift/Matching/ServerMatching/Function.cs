using System.Net;
using Amazon.ApiGatewayManagementApi;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WardGames.Web.Dotnet.Http;
using WardGames.Zooports.BackendModels.User.Matching;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

// change LAMBDA_NAME to the name of your lambda
namespace WardGames.Zooports.Lambda.UserMatchmakingResultService
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

        /// <summary>
        /// DynamoDb 사용을 위한 client
        /// </summary>
        public IAmazonDynamoDB DyanmoDBClient
        {
            get
            {
                if (_dyanmoDBClient == null)
                    _dyanmoDBClient = new AmazonDynamoDBClient();
                return _dyanmoDBClient;
            }
        }
        private static IAmazonDynamoDB? _dyanmoDBClient;

        /// <summary>
        /// WebSocket API 엔드포인트
        /// </summary>
        public string? WebSocketApiEndpoint { get; set; } = Environment.GetEnvironmentVariable("WEB_SOCKET_API_URL");

        /// <summary>
        /// default constructor
        /// </summary>
        public Function() { }

        /// <summary>
        /// Function handler for the lambda
        /// </summary>
        /// <param name="sQsEvent">SQS 이벤트. FlexMatch 이벤트가 들어옴</param>
        /// <param name="context">Lambda관련 유용한 기능을 사용할 수 있는 context</param>
        /// <returns>APIGatewayProxy 요청에 대한 응답</returns>
        public async Task FunctionHandler(SQSEvent sQsEvent, ILambdaContext context)
        {
            LambdaContext = context; 
            if (string.IsNullOrEmpty(WebSocketApiEndpoint))
                throw new ApiException("WebSocketApiEndpoint is null or empty", HttpStatusCode.InternalServerError);

            foreach (var message in sQsEvent.Records)
            {
                // Body의 데이터를 Json으로 파싱합니다.
                string sqsEventMessageContent = ExtractStringValueFromJson(message.Body, "Message", "Message is null");
                string matchmakingEventDetailContent = ExtractStringValueFromJson(sqsEventMessageContent, "detail", "detail is null");
                string matchmakingTypeContent = ExtractStringValueFromJson(matchmakingEventDetailContent, "type", "type is null");

                context.Logger.LogLine($"Matchmaking Type: {matchmakingTypeContent} / {sqsEventMessageContent}");

                // MatchmakingEventType에 따라 처리합니다.
                MatchmakingEventType matchmakingEventType = Enum.Parse<MatchmakingEventType>(matchmakingTypeContent);
                switch (matchmakingEventType)
                {
                    case MatchmakingEventType.MatchmakingSucceeded:
                        MatchmakingSucceededHandler matchmakingSucceededHandler = new MatchmakingSucceededHandler(webSocketApiEndpoint: WebSocketApiEndpoint, DyanmoDBClient, JsonConvert.DeserializeObject<MatchmakingSucceededEvent>(sqsEventMessageContent));
                        await matchmakingSucceededHandler.Handle();
                        break;
                    default:
                        break;
                }
            }
        }

        private string ExtractStringValueFromJson(string jsonContent, string key, string errorMessage)
        {
            if (string.IsNullOrEmpty(jsonContent))
                throw new ApiException($"{errorMessage}: JSON content is null or empty", HttpStatusCode.InternalServerError);

            try
            {
                JObject jsonObject = JObject.Parse(jsonContent);
                string? value = jsonObject[key]?.ToString();
                if (string.IsNullOrEmpty(value))
                    throw new ApiException($"{errorMessage}: {key} is null or empty", HttpStatusCode.InternalServerError);

                return value;
            }
            catch (JsonException ex)
            {
                throw new ApiException($"{errorMessage}: Error parsing JSON - {ex.Message}", HttpStatusCode.InternalServerError);
            }
        }
    }
}
