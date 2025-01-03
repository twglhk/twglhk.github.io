#if SERVER

using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using Mirage;
using Mirage.Logging;
using System.Threading;
using System.Collections.Generic;
using WardGames.Zooports.SharedModels.GamePlay;
using Zooports.DB.Script;
using Zooports.Game;
using Zooports.Master;
using Zooports.Network.AWS.GameLift;
using Zooports.SceneManagement;

namespace Zooports.Network.Server
{
    /// <summary>
    /// 게임 서버의 네트워크 관련 동작을 관리하는 기본 클래스
    /// </summary>
    public abstract class MirageNetworkServerManagerBase
    {
        #region Logger.

        private static readonly ILogger LOGGER = LogFactory.GetLogger(typeof(MirageNetworkServerManagerBase));

        #endregion
        
        public readonly GameMode GameMode; // 게임 모드
        protected int NumberOfPlayers => PlayerSessionDataContainer.ServerPlayerSessionDataList.Count; // 세션에 참여한 플레이어 수
        protected List<ServerPlayerSessionData> ServerPlayerSessionDataList => PlayerSessionDataContainer.ServerPlayerSessionDataList; // 세션에 참여한 플레이어 데이터의 리스트
        protected readonly PlayerSessionDataContainer PlayerSessionDataContainer; // 플레이어 세션 데이터 컨테이너
        protected readonly GameLiftServerManager GameLiftServerManager; // 게임리프트 서버 매니저
        protected readonly NetworkServer Server; // Mirage 네트워크 서버 참조
        private readonly NetworkSceneManager _networkSceneManager; // 네트워크 씬 매니저
        private readonly ServerSceneContainer _serverSceneContainer; // 서버 씬 컨테이너
        private readonly int _matchedPlayerCount; // 매칭된 플레이어 수
        private readonly ServerObjectManager _serverObjManager; // 서버 오브젝트 매니저
        private readonly NetworkLoadingChecker _networkLoadingChecker; // 네트워크 로딩 체커
        private NetworkGameStatus _gameStatus; // 게임 상태

        /// <summary>
        /// 게임 서버 매니저 생성자
        /// </summary>
        /// <param name="param"></param>
        protected MirageNetworkServerManagerBase(MirageServerManagerParam param)
        {
            Server = param.NetworkServer;
            _networkSceneManager = param.NetworkSceneManager;
            _serverSceneContainer = param.ServerSceneContainer;
            PlayerSessionDataContainer = param.PlayerSessionDataContainer;
            GameLiftServerManager = param.GameLiftServerManager;
            Server.MaxConnections = ModePlayerCountData.GetMaxPlayers(param.GameMode);
            GameMode = param.GameMode;
            _serverObjManager = param.ServerObjectManager;
            _networkLoadingChecker = new NetworkLoadingChecker(param.PlayerSessionDataContainer, Server);
            _matchedPlayerCount = param.MatchedPlayerCount;
        }

        /// <summary>
        /// [Server] 게임 서버를 실행
        /// </summary>
        public virtual void StartGameServer()
        {
            RegisterCallback();
            Server.StartServer();
            //FileLogger.LogToFile("Server Start");
        }

        /// <summary>
        /// 서버에서 게임 서버를 중단
        /// </summary>
        public void StopGameServer()
        {
            PlayerSessionDataContainer.Clear();
            Server.Stop();
        }

        /// <summary>
        /// 서버 이벤트와 관련된 콜백을 등록합니다.
        /// </summary>
        private void RegisterCallback()
        {
            Server.Started.AddListener(OnStartServer);
            Server.Connected.AddListener(OnServerConnected);
            Server.Authenticated.AddListener(OnServerAuthenticated);
            Server.Disconnected.AddListener(OnServerDisconnected);
            Server.Stopped.AddListener(OnStopServer);
            _networkSceneManager.OnServerStartedSceneChange.AddListener(OnServerStartedSceneChange);
            _networkSceneManager.OnServerFinishedSceneChange.AddListener(OnServerSceneChanged);
        }

        /// <summary>
        /// 서버 이벤트와 관련된 콜백을 해제합니다.
        /// </summary>
        private void UnRegisterCallback()
        {
            Server.Started.RemoveListener(OnStartServer);
            Server.Connected.RemoveListener(OnServerConnected);
            Server.Authenticated.RemoveListener(OnServerAuthenticated);
            Server.Disconnected.RemoveListener(OnServerDisconnected);
            Server.Stopped.RemoveListener(OnStopServer);
            _networkSceneManager.OnServerStartedSceneChange.RemoveListener(OnServerStartedSceneChange);
            _networkSceneManager.OnServerFinishedSceneChange.RemoveListener(OnServerSceneChanged);
        }

        /// <summary>
        /// 클라이언트에서 받을 메세지의 처리를 등록
        /// </summary>
        protected virtual void RegisterRecvClientMessageHandler()
        {
            Server.MessageHandler.RegisterHandler<C_UserGameLoadState>(ServerRecvUserGameLoadState);
        }

        /// <summary>
        /// 클라이언트에서 받을 메시지의 처리를 해제
        /// </summary>
        protected virtual void UnRegisterRecvClientMessageHandler()
        {
            Server.MessageHandler.UnregisterHandler<C_UserGameLoadState>();
        }

        /// <summary>
        /// [Server] Server 시작 시 호출
        /// </summary>
        protected virtual void OnStartServer()
        {
            LOGGER.Log("OnStartServer");
            RegisterRecvClientMessageHandler();
        }

        /// <summary>
        /// 서버 중단 시 호출
        /// </summary>
        protected virtual void OnStopServer()
        {
            UnRegisterRecvClientMessageHandler();
            UnRegisterCallback();
            MirageNetworkRoot.Server = null;
        }

        /// <summary>
        /// [Server] Server에 Client가 연결 되었을 때 호출
        /// </summary>
        /// <param name="networkPlayer"></param>
        protected virtual void OnServerConnected(INetworkPlayer networkPlayer)
        {
            LOGGER.Log($"OnServerConnected : {networkPlayer.Connection.EndPoint}");
        }

        /// <summary>
        /// [Server] Server에 Client 연결이 끊겼을 때 호출
        /// </summary>
        /// <param name="networkPlayer"></param>
        protected virtual void OnServerDisconnected(INetworkPlayer networkPlayer)
        {
            LOGGER.Log($"OnServerDisconnected : {networkPlayer.Connection.EndPoint}");
            if (!_gameStatus.HasFlag(NetworkGameStatus.GameSceneLoadStart))
            {
                PlayerSessionDataContainer.RemovePlayerSessionData(networkPlayer);
                return;
            }

            if (_gameStatus.HasFlag(NetworkGameStatus.GameProgressTwoThirds))
                return;
                
            // 게임 3분의 2진행 이전에 나간 플레이어는 닷지처리합니다.
            PlayerSessionDataContainer.SetPlayerAsDodge(networkPlayer);
        }

        /// <summary>
        /// [Server] Server에서 Client가 인증되었을 때 콜백
        /// </summary>
        /// <param name="networkPlayer"></param>
        protected virtual void OnServerAuthenticated(INetworkPlayer networkPlayer)
        {
            LOGGER.Log($"OnServerAuthenticated : {networkPlayer.Connection.EndPoint}");
        }

        /// <summary>
        /// 일정 시간 유저의 게임 서버 접속을 대기합니다. 이후 접속한 모든 플레이어에게 게임씬 로드 시작 메시지를 전송
        /// </summary> 
        /// <param name="maxWaitTime"></param>
        /// <param name="cancellationToken"></param>
        protected async UniTask AsyncWaitMatching(float maxWaitTime = 10f, CancellationToken cancellationToken = default)
        {
            float waitTime = 0f;
            while (true)
            {
                await UniTask.Delay(1000, cancellationToken: cancellationToken);
                waitTime += 1f;

                if (cancellationToken.IsCancellationRequested)
                    break;

                int authenticatedPlayersCount = PlayerSessionDataContainer.AuthenticatedPlayerCount;
                if (authenticatedPlayersCount != _matchedPlayerCount && waitTime < maxWaitTime)
                {
                    LOGGER.Log($"Wait Matching : {authenticatedPlayersCount}/{_matchedPlayerCount}");
                    continue;
                }

                await UniTask.Delay(1000); // 인증 안정성을 위한 1초 대기
                _gameStatus |= NetworkGameStatus.GameSceneLoadStart;
                
                // GameMode에 따른 씬 로드 분기
                string gameScenePath = _serverSceneContainer.GetGameScene(GameMode);
                _networkSceneManager.ServerLoadSceneAdditively(gameScenePath, Server.Players);
                break;
            }
        }
        
        /// <summary>
        /// [Server] 서버에서 씬 변경이 시작되었을 때 호출. 서버 대기 씬 => 게임 씬
        /// </summary>
        /// <param name="scene"></param>
        /// <param name="sceneOperation"></param>
        protected virtual void OnServerStartedSceneChange(string scene, SceneOperation sceneOperation)
        {
            LOGGER.Log($"MirageServer - OnServerStartedSceneChange : {scene}");
        }

        /// <summary>
        /// [Server] 서버 씬 변경 완료시 호출
        /// </summary>
        /// <param name="scene"></param>
        /// <param name="sceneOperation"></param>
        protected virtual void OnServerSceneChanged(Scene scene, SceneOperation sceneOperation)
        {
            LOGGER.Log($"MirageServer - OnServerSceneChanged : {scene}");
            if (scene.path != _serverSceneContainer.GetGameScene(GameMode)) return;
            
            _gameStatus |= NetworkGameStatus.GameSceneLoadComplete;
            ServerMaster.Instance.ServerSceneManager.LoadGameScene(GameMode);
            TriggerGameStartAsync().Forget();
        }
        
        /// <summary>
        /// [Server] 게임 시작 트리거
        /// </summary>
        private async UniTask TriggerGameStartAsync()
        {
            // 모든 클라이언트 씬 로딩 대기
            await _networkLoadingChecker.AsyncWaitUserGameLoadState(UserGameLoadState.GameSceneLoaded);
            
            // 인게임 매니저 초기화 트리거
            GameManagerBase.Instance.ServerInitManager(_serverObjManager, _networkLoadingChecker, GameMode, PlayerSessionDataContainer);
        }

        /// <summary>
        /// [Server] 게임 상태 플래그를 추가합니다.
        /// </summary>
        /// <param name="gameStatus"></param>
        public void AddGameStatus(NetworkGameStatus gameStatus)
        {
            _gameStatus |= gameStatus;
        }
        
        /// <summary>
        /// [Server] 클라이언트에서 게임의 특정 단계 로드 완료 플래그 메시지를 받았을 때 호출
        /// </summary>
        /// <param name="player"></param>
        /// <param name="msg"></param>
        private void ServerRecvUserGameLoadState(INetworkPlayer player, C_UserGameLoadState msg)
        {
            ServerPlayerSessionData serverPlayerSessionData = PlayerSessionDataContainer.GetPlayerSessionData(player);
            if (serverPlayerSessionData == null)
            {
                LOGGER.LogWarning($"ServerRecvUserGameLoadState - Not Found Player : {player.Connection.EndPoint}");
                return;
            }
            LOGGER.Log($"ServerRecvUserGameLoadState : {player.Connection.EndPoint} / {msg.UserGameLoadState}");
            serverPlayerSessionData.UserGameLoadState |= msg.UserGameLoadState;
        }
    }
}
#endif