#if CLIENT

using UnityEngine;
using UnityEngine.SceneManagement;
using Mirage;
using Zooports.SceneManagement;
using Mirage.Logging;
using WardGames.Zooports.SharedModels.GamePlay;
using Zooports.DB.Addressable;
using Zooports.Loading;
using Zooports.Master;

namespace Zooports.Network.Client
{
    /// <summary>
    /// 클라이언트 매니저의 추상 클래스
    /// </summary>
    public abstract class MirageNetworkClientManagerBase
    {
        #region Logger.
        private static readonly ILogger LOGGER = LogFactory.GetLogger(typeof(MirageNetworkClientManagerBase));
        #endregion

        #region Protected.
        protected readonly NetworkClient Client; // Mirage 클라이언트 참조
        private readonly GameMode _gameMode; // 게임 모드
        private readonly NetworkSceneManager _networkSceneManager; // 네트워크 씬 매니저
        private readonly ClientSceneContainer _clientSceneContainer; // 클라이언트 씬 컨테이너
        #endregion

        /// <summary>
        /// 클라이언트 매니저 생성자. 파라미터를 받아 초기화합니다.
        /// </summary>
        /// <param name="param"></param>
        protected MirageNetworkClientManagerBase(MirageClientManagerParam param)
        {
            LOGGER.Assert(param != null, "param is null");
            Client = param.NetworkClient;
            _gameMode = param.GameMode;
            _networkSceneManager = param.NetworkSceneManager;
            _clientSceneContainer = param.ClientSceneContainer;
            
            ClientObjectManager clientObjectManager = param.ClientObjectManager;
            clientObjectManager.NetworkPrefabs = AddressableClientNetworkResourceManager.Instance.ClientNetworkPrefab;
            MirageNetworkRoot.Client = param.NetworkClient;
        }

        /// <summary>
        /// 클라이언트 이벤트 콜백을 등록
        /// </summary>
        protected virtual void RegisterCallback()
        {
            Client.Connected.AddListener(OnClientConnected);
            Client.Authenticated.AddListener(OnClientAuthenticated);
            Client.Disconnected.AddListener(OnClientDisconnected);
            _networkSceneManager.OnClientStartedSceneChange.AddListener(OnClientSceneChangeStart);
            _networkSceneManager.OnClientFinishedSceneChange.AddListener(OnClientSceneChanged);
            ClientNetworkEventContainer.Instance.OnDisconnectClient.OnEvent.AddListener(StopClient);
        }

        /// <summary>
        /// 클라이언트 이벤트 콜백을 해제
        /// </summary>
        protected virtual void UnRegisterCallback()
        {
            Client.Connected.RemoveListener(OnClientConnected);
            Client.Authenticated.RemoveListener(OnClientAuthenticated);
            Client.Disconnected.RemoveListener(OnClientDisconnected);
            _networkSceneManager.OnClientFinishedSceneChange.RemoveListener(OnClientSceneChanged);
            ClientNetworkEventContainer.Instance.OnDisconnectClient.OnEvent.RemoveListener(StopClient);
        }

        /// <summary>
        /// 서버에서 받을 메세지의 처리를 등록
        /// </summary>
        protected virtual void RegisterRecvServerMessageHandler()
        {
            Client.MessageHandler.RegisterHandler<S_LoadingGameMessage>(OnClientRecvLoadingGameMessageHandler);
        }

        /// <summary>
        /// 서버에서 받을 메시지의 처리를 해제
        /// </summary>
        protected virtual void UnRegisterRecvServerMessageHandler()
        {
            Client.MessageHandler.UnregisterHandler<S_LoadingGameMessage>();
        }

        /// <summary>
        /// [Client] Mirage 게임 서버 주소로 클라이언트 접속을 시도
        /// </summary>
        public void ConnectToServer()
        {
            if (Client.Active) return;

            RegisterCallback();
            Client.Connect();
        }

        /// <summary>
        /// [Client] 현재 접속된 서버 또는 매칭을 취소
        /// </summary>
        public void StopClient()
        {
            if (!Client.IsConnected) return;
            Client.Disconnect();
        }

        /// <summary>
        /// 클라이언트가 서버에 연결되었을 때 콜백
        /// </summary>
        /// <param name="networkPlayer"></param>
        protected virtual void OnClientConnected(INetworkPlayer networkPlayer)
        {
            LOGGER.Log($"OnClientConnected");
        }

        /// <summary>
        /// 클라이언트가 서버에서 연결이 끊어졌을 때 콜백
        /// </summary>
        protected virtual void OnClientDisconnected(ClientStoppedReason clientStoppedReason)
        {
            LOGGER.Log($"OnClientDisconnected");
            UnRegisterRecvServerMessageHandler();
            UnRegisterCallback();
        }

        /// <summary>
        /// 클라이언트가 서버에서 인증되었을 때 콜백
        /// </summary>
        /// <param name="networkPlayer"></param>
        protected virtual void OnClientAuthenticated(INetworkPlayer networkPlayer)
        {
            LOGGER.Log($"OnClientAuthenticated");
            RegisterRecvServerMessageHandler();
        }

        /// <summary>
        /// 클라이언트가 서버에서 <see cref="S_LoadingGameMessage"/> 메시지를 받았을 때 콜백
        /// </summary>
        /// <param name="networkPlayer"></param>
        /// <param name="msg"></param>
        protected virtual void OnClientRecvLoadingGameMessageHandler(INetworkPlayer networkPlayer, S_LoadingGameMessage msg)
        {
            LOGGER.Log($"OnClientRecvLoadingPopUpHandler");
            LoadingManager.Instance.SetProgress(msg.LoadingPercent);
        }

        /// <summary>
        /// 클라이언트가 씬 변경을 시작했을 때 콜백. 로딩 씬을 로드
        /// </summary>
        /// <param name="sceneName"></param>
        /// <param name="sceneOperation"></param>
        protected virtual void OnClientSceneChangeStart(string sceneName, SceneOperation sceneOperation)
        {
            ClientMaster.Instance.ClientSceneManager.LoadLoadingScene();
        }

        /// <summary>
        /// 씬이 변경되었을 때 클라이언트에서 콜백. 클라이언트가 서버에 플레이어 스폰을 요청
        /// </summary>
        /// <param name="sceneName"></param>
        /// <param name="sceneOperation"></param>
        protected virtual void OnClientSceneChanged(Scene scene, SceneOperation sceneOperation)
        {
            if (scene.path != _clientSceneContainer.GetGameScene(_gameMode)) return;
            ClientMaster.Ins.ClientSceneManager.LoadGameScene(_gameMode);
            LOGGER.Log($"OnClientSceneChanged");
            Client.Send(new C_UserGameLoadState() { UserGameLoadState = UserGameLoadState.GameSceneLoaded });
            //_client.Send(new AddCharacterMessage());
        }
    }
}
#endif
