using Amazon.DynamoDBv2;
using Amazon.Lambda.APIGatewayEvents;

using Amazon.Lambda.Core;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

// change LAMBDA_NAME to the name of your lambda
namespace WardGames.Zooports.Lambda.UserAssetServiceLambda
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
        private readonly IAmazonDynamoDB _dynamoDB;

        /// <summary>
        /// default constructor
        /// </summary>
        public Function()
        {
            _dynamoDB = new AmazonDynamoDBClient();
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
            
            UserAssetQueryRequestHandler userAssetQueryRequestHandler = new UserAssetQueryRequestHandler(_dynamoDB, request);
            return await userAssetQueryRequestHandler.CreateResponse();
        }
    }
}
