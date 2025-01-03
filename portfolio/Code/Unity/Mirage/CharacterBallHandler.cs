using UnityEngine;
using UnityEngine.Events;
using Zooports.Ball;
using Zooports.Game;
using Mirage;
using Mirage.Logging;
using WardGames.Zooports.SharedModels.GamePlay;
using Zooports.Player.BasicTutorial;
using Zooports.Player.Dogfight;
using Zooports.Player.FootballRun;

namespace Zooports.Player
{
    /// <summary>
    /// 캐릭터가 볼을 컨트롤할 때 사용하는 스크립트입니다.
    /// <para> Authority = Client </para>
    /// </summary>  
    [DisallowMultipleComponent]
    public sealed class CharacterBallHandler : NetworkBehaviour
    {
        private static readonly ILogger LOGGER = LogFactory.GetLogger(typeof(CharacterBallHandler));

        [SerializeField] Transform grabRightHandTr; // 공을 잡는 오른손 위치
        [SerializeField] Transform grabLeftHandTr;

        private CharacterRoot _characterRoot; // 캐릭터 루트
        private CharacterStateController _characterStateController; // 캐릭터 상태 컨트롤러
        private RuntimeCharacterContainer _runtimeCharacterContainer; // 런타임 캐릭터 컨테이너
        private PlayerEventContainer _playerEventContainer; // 플레이어 이벤트 컨테이너 캐시

#if SERVER
        private CharacterBallHandlerModuleBase _characterBallHandlerModuleBase; // 게임 모드 마다 컴포지션 형태로 구현하는 캐릭터 볼 핸들러 모듈
#endif

        #region Property.

        private GameBallBase _currentGameBall; // 현재 캐릭터가 가지고 있는 공
        public GameBallBase CurrentGameBall => _currentGameBall;

        #endregion

        #region Client and Server

        private void Awake()
        {
            // 필요한 변수들을 초기화 합니다.
            GameManagerBase gameManager = GameManagerBase.Instance;
            _runtimeCharacterContainer = gameManager.RuntimeCharacterContainer;
            _playerEventContainer = gameManager.PlayerEventContainer;

            TryGetComponent(out _characterRoot);
            Debug.Assert(_characterRoot);

            _characterStateController = _characterRoot.CharacterStateController;

#if SERVER
            Identity.OnStartServer.AddListener(OnStartServer);
#endif
        }

        /// <summary>
        /// 공이 캐릭터의 손을 따라가도록 지속적으로 업데이트 합니다.
        /// </summary>
        private void FixedUpdate()
        {
            CharacterActionState characterState = _characterStateController.CharacterState;
            bool isGrabBall = characterState.HasFlag(CharacterActionState.GrabBall);
            bool isBallAttack = characterState.HasFlag(CharacterActionState.BallAttack);
            if (!isGrabBall && !isBallAttack) return;
            
#if SERVER
            if (IsServer)
            {
                _currentGameBall.Rigidbody.MovePosition(grabRightHandTr.position);
            }
#endif

#if CLIENT
            if (IsClient)
            {
                _currentGameBall.Rigidbody.MovePosition(grabRightHandTr.position);
                LOGGER.Log($"{gameObject.name} GrabBall");
            }
#endif
        }

        /// <summary>
        /// 클라이언트 상에서 캐릭터의 공 참조를 업데이트 해줍니다.
        /// </summary>
        /// <param name="gameBall"></param>
        [ClientRpc]
        public void ClientRpcSetCurrentGrabBall(GameBallBase gameBall)
        {
#if CLIENT
            _currentGameBall = gameBall;
#endif
        }

        /// <summary>
        /// 클라이언트 상에서 캐릭터의 공 참조를 해제합니다.
        /// </summary>
        [ClientRpc]
        private void ClientRpcClearCurrentGrabBall()
        {
#if CLIENT
            _currentGameBall = null;
#endif
        }

        #endregion

        #region Server

#if SERVER
        /// <summary>
        /// [Server] 서버에 필요한 데이터를 초기화합니다.
        /// </summary>
        [Server]
        private void OnStartServer()
        {
            GameManagerBase gameManager = GameManagerBase.Instance;
            GameMode gameMode = gameManager.SelectedMode;
            BallHandlerAddonParams param = new BallHandlerAddonParams()
            {
                CharacterRoot = _characterRoot,
                RuntimeCharacterContainer = _runtimeCharacterContainer,
                BallData = GameBallData.Instance,
                RuntimeGameData = gameManager.RuntimeGameData,
                BallManager = gameManager.SequenceManager.GameBallManager,
                PlayerEventContainer = gameManager.PlayerEventContainer,
                BallEventContainer = gameManager.BallEventContainer
            };

            // 게임 모드에 따라 공에 대한 처리가 다르게 동작하도록 모듈을 생성합니다.
            switch (gameMode)
            {
                case GameMode.Dogfight:
                    _characterBallHandlerModuleBase = new DogfightCharacterBallHandlerModule(param);
                    break;

                case GameMode.BasicTutorial:
                    _characterBallHandlerModuleBase = new BasicTutorialCharacterBallHandlerModule(param);
                    break;

                case GameMode.FootballRun:
                    _characterBallHandlerModuleBase = new FootballRunBallHandlerModule(param);
                    break;
            }

            // 볼과 관련된 이벤트를 등록합니다.
            BallEventContainer ballEventContainer = gameManager.BallEventContainer;
            ballEventContainer.OnBallTouchDown.OnServerTargetEvent += _characterBallHandlerModuleBase.ServerOnTouchdown;
            ballEventContainer.OnBallDown.OnServerTargetEvent += ServerOnBallDown;
        }

        /// <summary>
        /// [Server] 볼이 다운되었을 때 발동하는 이벤트
        /// </summary>
        /// <param name="ballId"></param>
        private void ServerOnBallDown(NetworkIdentity ballId)
        {
            if (_currentGameBall == null) return;
            if (Identity.NetId != ballId.NetId) return;
            ServerDropBall();
        }

        /// <summary>
        /// [Server] 볼 그랩 물리 연산과 조건 체크
        /// </summary>
        /// <param name="other"></param>
        private void OnTriggerEnter(Collider other)
        {
            if (!IsServer)
                return;

            _characterBallHandlerModuleBase.ServerOnCharacterBallTriggerEnter(other);
        }

        /// <summary>
        /// [Server] 볼 그랩 수행
        /// </summary>
        /// <param name="ball"></param>
        [Server]
        private void ServerSetCurrentGrabBall(GameBallBase ball)
        {
            _currentGameBall = ball;
        }

        /// <summary>
        /// [Server] 현재 가지고 있는 공의 참조를 해제합니다.
        /// </summary>
        [Server]
        private void ServerClearCurrentGrabBall()
        {
            _currentGameBall = null;
        }

        /// <summary>
        /// [Server] 볼 그랩 수행
        /// </summary>
        [Server]
        public void ServerGrabBall(GameBallBase ball)
        {
            LOGGER.Assert(ball);
            if (_characterStateController.IsGrabBall)
                return;

            ClientRpcSetCurrentGrabBall(ball);
            ServerSetCurrentGrabBall(ball);
            ball.GameBallGrabController.ServerOnPlayerGrabBall(Identity);

            _playerEventContainer.OnPlayerBallDataSetUpEvent.OnServerRaise(Identity);
            _playerEventContainer.OnPlayerGrabBallEvent.OnServerRaise(Identity);
            _playerEventContainer.OnPlayerGrabBallDualIdEvent.OnServerRaise(ball.Identity, Identity);
        }

        /// <summary>
        /// [Server] 볼 드랍 트리거 발동
        /// </summary>
        [Server]
        public void ServerDropBall()
        {
            if (!_characterStateController.IsGrabBall) return;

            _playerEventContainer.OnPlayerDropBallEvent.OnServerRaise(Identity);
            _playerEventContainer.OnPlayerDropBallDualIdEvent.OnServerRaise(_currentGameBall.Identity, Identity);
            ClientRpcClearCurrentGrabBall();
            ServerClearCurrentGrabBall();
        }

        /// <summary>
        /// [Server] 숏 패스 발동
        /// </summary>
        [Server]
        public void ServerShortPass(CharacterRoot targetPlayer, float duration, UnityAction onComplete = null)
        {
            GameBallBase gameBall = _currentGameBall;

            ServerDropBall();
            gameBall.GameBallThrowingController.OnServerStartShortPass(Identity, targetPlayer.Identity, duration,
                onComplete);
        }

        /// <summary>
        /// [Server] 패스 발동 (직선)
        /// </summary>
        [Server]
        public void PassBall(Vector3 targetPos, float duration, bool isSpecialPass, UnityAction onComplete = null)
        {
            GameBallBase currentGameBall = _currentGameBall;

            ServerDropBall();
            currentGameBall.GameBallThrowingController.StraightPassBall(Identity, grabRightHandTr.position, targetPos,
                duration, isSpecialPass, onComplete);
        }

        /// <summary>
        /// [Server] 공 던지기 (곡선, 베지어 커브 기반)
        /// </summary>
        [Server]
        public void PassBall(Vector3 targetPos, float height, float moveTime, bool isSpecialPass,
            UnityAction onComplete = null)
        {
            GameBallBase currentGameBall = _currentGameBall;

            ServerDropBall();
            currentGameBall.GameBallThrowingController.CurvedlyPassBall(
                passerId: Identity,
                startPos: grabRightHandTr.position,
                endPos: targetPos,
                height: height,
                moveTime: moveTime,
                isSpecialPass: isSpecialPass,
                onComplete: onComplete);
        }

        /// <summary>
        /// [Server] 공 던지기 (BallAttack), Isaac 전용
        /// </summary>
        [Server]
        public void ThrowBallAttack(Vector3 targetPos, float duration, UnityAction onCompleteThrowing,
            UnityAction onCompleteReturn)
        {
            if (!_characterStateController.IsGrabBall) return;

            GameBallBase currentGameBall = _currentGameBall;
            currentGameBall.GameBallThrowingController.SetBallAttackThrowingValues(targetPos, duration, _characterRoot,
                onCompleteThrowing, onCompleteReturn);
        }
#endif

        #endregion
    }
}