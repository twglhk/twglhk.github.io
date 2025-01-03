#if SERVER
using Aws.GameLift;
using Aws.GameLift.Server;
using Aws.GameLift.Server.Model;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System.Threading;
using WardGames.Zooports.SharedModels.Character;
using WardGames.Zooports.SharedModels.GamePlay;
using WardGames.Zooports.SharedModels.Matching;
using WardGames.Zooports.SharedModels.Position;
using Zooports.Network.GameLift;
using Zooports.Network.Server;
using Zooports.Rune;

namespace Zooports.Network.AWS.GameLift
{
    /// <summary>
    /// 게임 서버에서 GameLift 초기화를 담당하는 클래스 
    /// </summary>
    public class GameLiftServerManager
    {
        #region Property.

        /// <summary>
        /// 게임 세션 시작 여부
        /// </summary>
        public bool IsSessionStarted { get; set; }

        /// <summary>
        /// 게임 서버의 IP 주소
        /// </summary>
        public string IpAddress { get; private set; } = "localhost";

        /// <summary>
        /// 게임 서버의 포트
        /// </summary>
        private ushort Port { get; set; }

        /// <summary>
        /// GameLift 게임 세션 정보
        /// </summary>
        public GameSession GameSession => _gameSession;

        private GameSession _gameSession;

        /// <summary>
        /// GameLift 게임 세션 속성 정보
        /// </summary>
        public CNT_GameSessionProperty GameSessionProperty => _gameSessionProperty;

        /// <summary>
        /// 게임 스테이지 정보
        /// </summary>
        public string Stage => _gameSession.GameProperties["stage"];

        #endregion

        #region Private.

        private float _gameSessionEndTime;
        private readonly string _gameLiftAnywhereToken; // GameLift Anywhere 토큰
        private readonly ServerNetworkManager _serverNetworkManager;
        private readonly PlayerSessionDataContainer _playerSessionDataContainer;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly GameLiftServerLogger _gameLiftServerLogger;
        private readonly PortAllocator _portAllocator; // 포트 할당자
        private GameLiftMatchmakerData _gameLiftMatchmakerData; // 매칭 데이터
        private CNT_GameSessionProperty _gameSessionProperty; // 게임 세션 속성
        private IList<PlayerSession> _playerSessions;
        private GameMode _gameMode;
        private int _matchedPlayerCount;

        #endregion

        #region Const.

        private const float GAME_SESSION_END_LIMIT_SEC = 600f; // 게임 세션 종료 제한 시간

        #endregion

        /// <summary>
        /// GameLiftServerManager 기본 생성자
        /// </summary>
        /// <param name="serverNetworkManager"></param>
        /// <param name="playerSessionDataContainer"></param> 
        /// <param name="gameLiftAnywhereToken"></param>
        public GameLiftServerManager(ServerNetworkManager serverNetworkManager,
            PlayerSessionDataContainer playerSessionDataContainer, string gameLiftAnywhereToken = null)
        {
            _serverNetworkManager = serverNetworkManager;
            _playerSessionDataContainer = playerSessionDataContainer;
            _cancellationTokenSource = new CancellationTokenSource();
            _gameLiftServerLogger = new GameLiftServerLogger();
            _gameLiftAnywhereToken = gameLiftAnywhereToken;
            _portAllocator = new PortAllocator();
            Debug.Log("GameLiftServerManager Constructor called");
        }

        /// <summary>
        /// GameLift 초기화
        /// </summary>
        /// <returns></returns>
        public bool InitGameLift()
        {
            CheckGameSessionStarted().Forget();

            try
            {
                Debug.Log("GameLiftServerManager init ready");

#if UNITY_EDITOR
                // Local test
                //WebSocketUrl from RegisterHost call
                var webSocketUrl = "wss://ap-northeast-2.api.amazongamelift.com";

                //Unique identifier for this process
                var processId = $"Z{Guid.NewGuid().ToString().Substring(0, 8)}";

                //Unique identifier for your host that this process belongs to
                var hostId = "ZooportsServerAnywhere";

                //Unique identifier for your fleet that this host belongs to
                var fleetId = "FLEET_ID";

                ServerParameters serverParameters = new ServerParameters(
                    webSocketUrl,
                    processId,
                    hostId,
                    fleetId,
                    _gameLiftAnywhereToken);
#else
                Port = (ushort)_portAllocator.AllocatePort();
#endif
                Debug.Log($"GameLiftServerManager port : {Port}");

#if UNITY_EDITOR
                var initSDKOutcome = GameLiftServerAPI.InitSDK(serverParameters);
#else
                var initSDKOutcome = GameLiftServerAPI.InitSDK();
#endif

                if (initSDKOutcome.Success)
                {
                    Debug.Log("GameLiftServerManager init success");
                    ProcessParameters processParameters = new ProcessParameters(
                        onStartGameSession: OnStartGameSession,
                        onUpdateGameSession: (updateGameSession) =>
                        {
                            Console.Out.WriteLine("onUpdateGameSession");
                            //When a game session is updated (e.g. by FlexMatch backfill), GameLift sends a request to the game
                            //server containing the updated game session object.  The game server can then examine the provided
                            //matchmakerData and handle new incoming players appropriately.
                            //updateReason is the reason this update is being supplied.
                        },
                        onProcessTerminate: OnEndServerProcess,
                        onHealthCheck: () =>
                        {
                            // Console.Out.WriteLine("onHealthCheck");
                            //This is the HealthCheck callback.
                            //GameLift will invoke this callback every 60 seconds or so.
                            //Here, a game server might want to check the health of dependencies and such.
                            //Simply return true if healthy, false otherwise.
                            //The game server has 60 seconds to respond with its health status. GameLift will default to 'false' if the game server doesn't respond in time.
                            //In this case, we're always healthy!
                            return true;
                        },
                        Port, //This game server tells GameLift that it will listen on port 7777 for incoming player connections.
                        new LogParameters(_gameLiftServerLogger.LogPaths));

                    //Calling ProcessReady tells GameLift this game server is ready to receive incoming game sessions!
                    var processReadyOutcome = GameLiftServerAPI.ProcessReady(processParameters);
                    if (processReadyOutcome.Success)
                    {
                        // 이벤트 등록
                        Debug.Log("GameLiftServerManager process ready");
                        return true;
                    }
                    else
                    {
                        Debug.Log("ProcessReady failure : " + processReadyOutcome.Error);
                        return false;
                    }
                }
                else
                {
                    Debug.Log("InitSDK failure : " + initSDKOutcome.Error);
                    return false;
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e);
                Console.Error.WriteLine((e.StackTrace));
                throw;
            }
        }

        /// <summary>
        /// 게임 세션 시작 콜백
        /// </summary>
        /// <param name="gameSession"></param>
        private void OnStartGameSession(GameSession gameSession)
        {
            try
            {
                Console.Out.WriteLine("onStartGameSession callback received. Ready to start game.");

                _gameSessionProperty = new CNT_GameSessionProperty(gameSession: gameSession);
                _gameMode = _gameSessionProperty.GameMode;
                if (_gameMode == GameMode.Null)
                {
                    Debug.LogError("GameMode is Null");
                    return;
                }

                switch (_gameMode)
                {
                    case GameMode.Dogfight:
                    case GameMode.FootballRun:
                        ServerSetMatchmakingSession(gameSession);
                        break;
                    case GameMode.BasicTutorial:
                        ServerSetBasicTutorialGameSession();
                        break;
                }

                Console.Out.WriteLine("Start a game server successfully. Ready to activate game session.");
                var result = GameLiftServerAPI.ActivateGameSession();
                if (result.Success)
                {
                    _gameSession = gameSession;
                    ServerNetworkEventContainer.Instance.OnEndServerProcess.OnEvent.AddListener(OnEndServerProcess);
                    AsyncEndGameSession(_cancellationTokenSource.Token).Forget();
                    Console.Out.WriteLine("ActivateGameSession success");
                }
                else
                {
                    Console.Error.WriteLine("ActivateGameSession failed");
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e);
                Console.Error.WriteLine(e.StackTrace);
                throw;
            }
        }

        /// <summary>
        /// 게임을 시작하기 전 매치메이킹된 게임 세션의 데이터를 셋업합니다.
        /// </summary>
        /// <param name="gameSession"></param>
        /// <exception cref="NullReferenceException"></exception>
        private void ServerSetMatchmakingSession(GameSession gameSession)
        {
            _gameLiftMatchmakerData =
                JsonConvert.DeserializeObject<GameLiftMatchmakerData>(gameSession.MatchmakerData);

            if (_gameLiftMatchmakerData == null)
                throw new NullReferenceException("GameLiftMatchmakerData is null");

            // 매칭 플레이어 캐싱
            foreach (GameLiftMatchmakingTeam teamData in _gameLiftMatchmakerData.teams)
            {
                foreach (GameLiftMatchmakingPlayer matchingPlayerData in teamData.players)
                {
                    ServerPlayerSessionData serverPlayerSessionData = new ServerPlayerSessionData()
                    {
                        UserNumber = matchingPlayerData.playerId,
                        UserName = matchingPlayerData.attributes.userName.GetStringValue(),
                        Team = Enum.Parse<Team>(teamData.name),
                        CharacterName = Enum.Parse<CharacterName>(matchingPlayerData.attributes.characterName.GetStringValue()),
                        CharacterLevel = (int)matchingPlayerData.attributes.level.GetDoubleValue(),
                        CharacterPosition = Enum.Parse<CharacterPosition>(matchingPlayerData.attributes.position.GetStringValue()),
                        CharacterSkinId = (int)matchingPlayerData.attributes.characterSkinId.GetDoubleValue(),
                        RuneSlotEntity = StringToRuneEnumConverter.ConvertToRuneSlotEntity(matchingPlayerData.attributes.runeSlot.GetStringList()),
                        UserMatchResultStatus = UserMatchResultStatus.MatchLeftEarly
                    };
                    _playerSessionDataContainer.AddPlayerSessionData(serverPlayerSessionData);

                    _matchedPlayerCount++;
                }
            }
        }

        /// <summary>
        /// 기본 튜토리얼 게임 세션 셋업
        /// </summary>
        private void ServerSetBasicTutorialGameSession()
        {
            _matchedPlayerCount = 1;
        }

        /// <summary>
        /// 게임 세션을 시작하고, 플레이어들이 접속할 수 있도록 게임 서버를 시작합니다.
        /// </summary>
        private async UniTask CheckGameSessionStarted()
        {
            await UniTask.WaitUntil(IsGameSessionAllocated);
            Debug.Log("GameSessionAllocated");
            // TODO: EC2에서 사이드로 구동시킬 포트 매니저 추후에 만들어서 할당, 해제 중앙 처리
            _portAllocator.ReleasePort(); // 예약용 할당 포트를 해제. 게임 서버가 소켓을 릴리즈 하면 다른 프로세스에서 다시 사용 가능.

            if (_matchedPlayerCount == 0)
            {
                Debug.LogError("MatchedPlayerCount is 0");
                OnEndServerProcess();
                return;
            }

            _serverNetworkManager.StartServer(_gameSession.IpAddress, (ushort)_gameSession.Port, _gameMode, _matchedPlayerCount);
        }

        private bool IsGameSessionAllocated() => _gameSession != null;

        /// <summary>
        /// 플레이어 세션을 게임 세션에 추가
        /// </summary>
        /// <param name="playerSessionId"></param>
        /// <returns></returns>
        public bool AcceptPlayerSession(string playerSessionId)
        {
            // 이미 시작한 게임이면 참가 거절. (이 부분은 도중에 나갔다가 다시 접속하는 경우는 아님)
            if (IsSessionStarted)
            {
                Debug.Log("GAME SESSION IS ALREADY STARTED");
                return false;
            }

            var outcome = GameLiftServerAPI.AcceptPlayerSession(playerSessionId);
            if (outcome.Success)
            {
                _gameSessionEndTime = 0f;
            }

            return outcome.Success;
        }

        /// <summary>
        /// 플레이어 세션을 게임 세션에서 제거
        /// </summary>
        /// <param name="playerSessionId"></param>
        public void RemovePlayerSession(string playerSessionId)
        {
            GameLiftServerAPI.RemovePlayerSession(playerSessionId);
        }

        /// <summary>
        /// 플레이어 세션 생성을 거절함
        /// </summary>
        public void DenyCreatePlayerSession()
        {
            GameLiftServerAPI.UpdatePlayerSessionCreationPolicy(PlayerSessionCreationPolicy.DENY_ALL);
        }

        /// <summary>
        /// 플레이어 세션의 리스트를 가져옴
        /// </summary>
        /// <returns></returns>
        public IList<PlayerSession> GetPlayerSessions()
        {
            if (_playerSessions != null)
                return _playerSessions;

            // 게임 세션에 할당된 플레이어 세션 쿼리
            DescribePlayerSessionsRequest describePlayerSessionsRequest = new DescribePlayerSessionsRequest()
            {
                GameSessionId = _gameSession.GameSessionId,
                Limit = 6,
                PlayerSessionStatusFilter = PlayerSessionStatusMapper.GetNameForPlayerSessionStatus(PlayerSessionStatus.RESERVED)
            };
            DescribePlayerSessionsOutcome playerSessionsOutcome = GameLiftServerAPI.DescribePlayerSessions(describePlayerSessionsRequest);

            if (!playerSessionsOutcome.Success)
                throw new Exception("DescribePlayerSessions failed");

            _playerSessions = playerSessionsOutcome.Result.PlayerSessions;
            Console.Out.WriteLine($"PlayerSession list count : {playerSessionsOutcome.Result.PlayerSessions.Count}");
            foreach (var playerSession in _playerSessions)
            {
                Console.Out.WriteLine($"reserved player session id : {playerSession.PlayerSessionId}");
            }

            return _playerSessions;
        }

        /// <summary>
        /// 플레이어 세션을 게임 세션에서 확인 후 제거
        /// </summary>
        /// <param name="playerSessionId"></param>
        public void ReleasePlayerSession(string playerSessionId)
        {
            DescribePlayerSessionsRequest describePlayerSessionsRequest = new DescribePlayerSessionsRequest();
            describePlayerSessionsRequest.GameSessionId = _gameSession.GameSessionId;
            var result = GameLiftServerAPI.DescribePlayerSessions(describePlayerSessionsRequest);
            if (result.Success)
            {
                var targetPlayerSession = result.Result.PlayerSessions.FirstOrDefault(playerSession => playerSession.PlayerSessionId == playerSessionId);
                if (targetPlayerSession == null) return;
                GameLiftServerAPI.RemovePlayerSession(targetPlayerSession.PlayerSessionId);
            }
        }

        /// <summary>
        /// 게임 세션의 MatchmakerData를 가져옴
        /// </summary>
        /// <returns></returns>
        public GameLiftMatchmakerData GetGameLiftMatchmakerData()
        {
            return _gameLiftMatchmakerData;
        }

        /// <summary>
        /// GameLift Fleet Role Credentials를 가져오는 함수
        /// </summary>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public GetFleetRoleCredentialsResult GetFleetRoleCredentials()
        {
            try
            {
                GetFleetRoleCredentialsRequest getFleetRoleCredentialsRequest = new GetFleetRoleCredentialsRequest("ROLE_ARN");
                var outcome = GameLiftServerAPI.GetFleetRoleCredentials(getFleetRoleCredentialsRequest);
                if (outcome.Success)
                {
                    return outcome.Result;
                }

                throw new Exception("GetFleetRoleCredentials failed");
            }
            catch (Exception e)
            {
                Debug.LogError($"{e.Message} \n {e.StackTrace}");
                return null;
            }
        }

        #region EndGameSession

        /// <summary>
        /// 게임 세션을 일정 시간 뒤에 강제로 종료하는 코루틴
        /// </summary>
        /// <returns></returns>
        private async UniTask AsyncEndGameSession(CancellationToken cancellationToken = default)
        {
            while (_gameSessionEndTime < GAME_SESSION_END_LIMIT_SEC)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                await UniTask.Delay(1000);
                _gameSessionEndTime += 1f;
            }

            ServerNetworkEventContainer.Instance.OnReadyServerProcessEnd.OnEventRaise();
            OnEndServerProcess();
        }

        /// <summary>
        /// 게임 서버 프로세스를 종료하는 함수
        /// </summary>
        private void OnEndServerProcess()
        {
            Debug.Log("GameLiftServerManager process Ending ready");

            _cancellationTokenSource.Cancel();
            ServerNetworkEventContainer.Instance.OnEndServerProcess.OnEvent.RemoveListener(OnEndServerProcess);

            // Operations to end game sessions and the server process
            GenericOutcome processEndingOutcome = GameLiftServerAPI.ProcessEnding();

            // Shut down and destroy the instance of the GameLift Game Server SDK
            GenericOutcome destroyOutcome = GameLiftServerAPI.Destroy();

            // Exit the process with success or failure
            if (processEndingOutcome.Success)
            {
                Debug.Log("GameLiftServerManager process Ending success");
                Environment.Exit(0);
            }
            else
            {
                Debug.LogError("ProcessEnding() failed. Error: " + processEndingOutcome.Error);
                Environment.Exit(-1);
            }
        }

        #endregion
    }
}
#endif