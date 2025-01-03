using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using WardGames.Zooports.SharedModels.User;
using WardGames.Zooports.SharedModels.User.Character;
using WardGames.Web.Dotnet.AWS.Lambda.APIGatewayEvents;
using Newtonsoft.Json;
using System.Net;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

// change LAMBDA_NAME to the name of your lambda
namespace WardGames.Zooports.Lambda.UserCharacterInfoServiceLambda
{
    /// <summary>
    /// 유저 정보 요청을 처리하는 람다 함수 클래스
    /// </summary>
    public class Function
    {
        private readonly IAmazonDynamoDB _dynamoDB;

        /// <summary>
        /// 람다 함수의 context
        /// </summary>
        public static ILambdaContext? LambdaContext { get; private set; }

        /// <summary>
        /// default constructor
        /// </summary>
        public Function()
        {
            _dynamoDB = new AmazonDynamoDBClient();
        }

        /// <summary>
        /// dynamoDb 전용 constructor
        /// </summary>
        /// <param name="dynamoDB"></param>
        public Function(IAmazonDynamoDB dynamoDB)
        {
            _dynamoDB = dynamoDB;
        }

        /// <summary>
        /// 유저 정보 요청을 처리하는 함수. GET, PUT 처리가 가능하며, 각 요청마다 응답을 다르게 처리함.
        /// </summary>
        /// <param name="request">요청 파라미터</param>
        /// <param name="context">람다 컨텍스트</param>
        /// <returns>HttpMethod에 따라 다른 반환 데이터를 가짐</returns>
        public async Task<APIGatewayProxyResponse> FunctionHandler(APIGatewayProxyRequest request, ILambdaContext context)
        {
            LambdaContext = context;
            APIGatewayProxyResponse response;

            try
            {
                switch (request.HttpMethod)
                {
                    case "POST":
                        response = await new UserCharacterInfoUpdateHandler(_dynamoDB, request).CreateResponse();
                        break;
                    default:
                        response = APIGatewayEventHelper.GenerateResponse("Invalid HTTP Method", (int)HttpStatusCode.BadRequest);
                        break;
                }
            }
            catch (Exception e)
            {
                response = APIGatewayEventHelper.GenerateResponse(e.Message, (int)HttpStatusCode.InternalServerError);
            }

            return response;
        }
    }
}
