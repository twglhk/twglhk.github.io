using UniRx;
using WardGames.Zooports.SharedModels.GamePlay;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Zooports.Controller;
using Zooports.Game;
using Zooports.Map;
using Mirage;
using Mirage.Logging;
using Mirage.Serialization;

namespace Zooports.Player
{
    /// <summary>
    /// 플레이어의 이동을 컨트롤하는 스크립트
    /// </summary>
    public sealed class CharacterMoveController : NetworkBehaviour
    {
        private static readonly ILogger LOGGER = LogFactory.GetLogger(typeof(CharacterMoveController));

        #region Serizlied

        [SerializeField] bool _isKeyBoardControlUse;

        #endregion

        #region Private.

        CharacterRoot _characterRoot; // 캐릭터 루트
        private Vector3 _moveDirection; // 이동 방향
        private JoystickController _moveJoystickController; // 이동 조이스틱 컨트롤러 참조
        private uint _currentInputNumber = 0; // 클라이언트가 서버에 전송할 입력 번호
        private Vector3 _clientRecvPos; // 클라이언트가 서버에서 받은 위치
        private Vector3 _clientSimulatedPos; // 클라이언트가 서버에서 받은 위치 정보를 토대로 re-apply를 적용한 위치
        private List<SMoveInputData> _moveInfoList = new List<SMoveInputData>(30); // 이동 정보 리스트
        private uint _lastInputNumber = 0; // 마지막 입력 번호
        private RuntimeGameData _runtimeGameData; // 런타임 게임 데이터
        private bool _isSupportedPlatform; // 지원되는 플랫폼인지 여부
        private RuntimeCharacterContainer _runtimeCharacterContainer; // 런타임 캐릭터 컨테이너
        private LineOfScrimmageManager _lineOfScrimmageManager; // 라인 오브 스크림리지 매니저 참조
        private GroundManager _groundManager; // 그라운드 매니저 참조
        private GameMode _selectedMode; // 선택된 게임 모드
        private bool _hasLosMode; // 라인 오브 스크림리지 모드인지 여부

        #endregion

        #region Struct

        /// <summary>
        /// 클라이언트에서 서버에 전송하는 이동 인풋 정보 구조체
        /// </summary>
        private struct SMoveInputData
        {
            public uint InputNumber;
            public Vector3 Direction;
        }

        #endregion

        private void Awake()
        {
            // 필요 데이터를 초기화합니다
            TryGetComponent(out _characterRoot);
            Debug.Assert(_characterRoot);

            GameManagerBase gameManager = GameManagerBase.Instance;
            _runtimeCharacterContainer = gameManager.RuntimeCharacterContainer;
            _lineOfScrimmageManager = gameManager.LineOfScrimmageManager;
            _selectedMode = gameManager.SelectedMode;
            _hasLosMode = _selectedMode == GameMode.Dogfight || _selectedMode == GameMode.FootballRun;
            gameManager.GroundManagerProperty.Subscribe(groundManager => _groundManager = groundManager);

#if SERVER
            Identity.OnStartServer.AddListener(OnStartServer);
#endif

#if CLIENT
            _isSupportedPlatform = Application.platform == RuntimePlatform.WindowsEditor ||
                                   Application.platform == RuntimePlatform.WindowsPlayer ||
                                   Application.platform == RuntimePlatform.OSXEditor ||
                                   Application.platform == RuntimePlatform.OSXPlayer;

            Identity.OnStartClient.AddListener(OnStartClient);
            Identity.OnAuthorityChanged.AddListener(OnAuthorityChanged);
#endif
        }

        private void Update()
        {
#if CLIENT
            if (IsClient && HasAuthority)
            {
                bool isAICharacter = _characterRoot.RuntimeCharacterData.IsAICharacter;
                if (isAICharacter) return;
                ClientUpdateLocalPlayer();
            }
#endif
        }

        private void FixedUpdate()
        {
            bool isSyncPosition = _characterRoot.SyncPositionBehaviour.IsSync;
            if (!isSyncPosition) return;

            bool isAICharacter = _characterRoot.RuntimeCharacterData.IsAICharacter;
            bool isOutControl = _characterRoot.CharacterStateController.IsOutOfControl;

#if SERVER
            // 서버에서 이동 시뮬레이션
            if (IsServer)
            {
                ServerUpdateUserPlayerOnServer();

                if (isOutControl) return;
                if (isAICharacter) return;
                
                bool isMove = _characterRoot.CharacterStateController.IsMove;
                if (!isMove) return;
                
                AIRun();
                AITurn();
            }
#endif

#if CLIENT
            // 클라이언트에서 이동 시뮬레이션
            if (IsClient && HasAuthority)
            {
                if (isOutControl) return;
                if (isAICharacter) return;
                
                bool isMoveInput = _moveDirection.sqrMagnitude > 0.0f;
                if (!isMoveInput) return;

                ClientPlayerMove();
            }
#endif
        }

        /// <summary>
        /// 이동 정보를 리스트에 추가
        /// </summary>
        /// <param name="sMoveInfo"></param>
        private void AddMoveInputData(SMoveInputData sMoveInfo)
        {
            _moveInfoList.Add(sMoveInfo);
        }

        /// <summary>
        /// 입력 방향으로 이동을 수행합니다.
        /// </summary>
        /// <param name="moveDir"></param>
        /// <returns></returns>
        private bool Run(Vector3 moveDir)
        {
            Vector3 characterPos = _characterRoot.CharacterRigidbody.position;
            float currentMovSpeed = _characterRoot.RuntimeCharacterData.MovSpeed;
            float playerMoveSpeedRate = CharacterCommonStat.Instance.PlayerMoveSpeedRate;
            Vector3 movePos = characterPos + moveDir * currentMovSpeed * playerMoveSpeedRate;
            bool isRunPossible = RunCheck(moveDir, movePos);
            if (!isRunPossible) return false;
            
            _characterRoot.CharacterRigidbody.MovePosition(movePos);
            return true;
        }

        /// <summary>
        /// 이동이 가능한지 체크합니다.
        /// </summary>
        /// <param name="moveDir"></param>
        /// <param name="movePos"></param>
        /// <returns></returns>
        private bool RunCheck(Vector3 moveDir, Vector3 movePos)
        {
            if (_hasLosMode)
            {
                // Go for it & punt 페이즈의 경우
                var teamDir = _characterRoot.RuntimeCharacterData.TeamDirection;
                bool isLineUpState = _runtimeGameData.HasState(GameState.LineUp);
                if (isLineUpState)
                {
                    // 라인 스크리미지 너머로 이동 시 false return
                    float lineOfScrimmageXpos = _lineOfScrimmageManager.LineOfScrimmage;
                    if (movePos.x * teamDir > lineOfScrimmageXpos) return false;

                    float leftEndX = _groundManager.GroundLeftEndX;
                    float rightEndX = _groundManager.GroundRightEndX;
                    if (movePos.x < leftEndX || movePos.x > rightEndX) return false;

                    float topEndZ = _groundManager.GroundTopEndZ;
                    float bottomEndZ = _groundManager.GroundBottomEndZ;
                    if (movePos.z < bottomEndZ || movePos.z > topEndZ) return false;
                }
            }

            // 다른 플레이어가 이동 경로를 막고 있는 지 체크합니다.
            const float MOVING_DISTANCE = 0.5f;
            const float OBSTACLE_CHECK_RADIUS = 0.2f;
            Vector3 obstaclePos = _characterRoot.CharacterRigidbody.position + moveDir * MOVING_DISTANCE;
            foreach (var player in _runtimeCharacterContainer.AllCharacterList)
            {
                if (player.CharacterStateController.IsDeath) continue;
                if (Vector3.Distance(obstaclePos, player.transform.position) < OBSTACLE_CHECK_RADIUS) return false;
            }

            return true;
        }

        /// <summary>
        /// 이동 방향으로 캐릭터를 회전합니다.
        /// </summary>
        /// <param name="moveDir"></param>
        private void Turn(Vector3 moveDir)
        {
            Quaternion newRotation = Quaternion.LookRotation(new Vector3(moveDir.x, 0.0f, moveDir.z));
            Quaternion slerpRotation = Quaternion.Slerp(_characterRoot.CharacterRigidbody.rotation, newRotation, _characterRoot.CharacterCommonStat.PlayerRotationSpeed * Time.deltaTime);
            _characterRoot.CharacterRigidbody.MoveRotation(slerpRotation);
        }

#if SERVER
        [Server]
        private void OnStartServer()
        {
            _runtimeGameData = RuntimeGameData.Instance;
            GameManagerBase.Instance.GameSequenceEventContainer.OnEndGameReady.OnServerEvent.AddListener(OnServerEndGameReady);
            StartCoroutine(nameof(CoSetAnimation));
        }

        [Server]
        private void OnServerEndGameReady()
        {
            _characterRoot.SyncPositionBehaviour.IsSync = false;
        }

        /// <summary>
        /// 서버에서 유저 플레이어의 이동을 업데이트합니다.
        /// </summary>
        [Server]
        private void ServerUpdateUserPlayerOnServer()
        {
            if (_moveInfoList.Count == 0)
            {
                return;
            }

            var moveInfo = _moveInfoList[0];
            _lastInputNumber = moveInfo.InputNumber;
            _moveInfoList.RemoveAt(0);
            Run(moveInfo.Direction);
            Turn(moveInfo.Direction);
            SetDirtyBit(1UL);
        }

        [Server]
        private void AIRun()
        {
            var movePos = _characterRoot.CharacterRigidbody.position + _moveDirection * (_characterRoot.RuntimeCharacterData.MovSpeed * CharacterCommonStat.Instance.PlayerMoveSpeedRate);
            if (!RunCheck(_moveDirection, movePos)) return;

            _characterRoot.CharacterRigidbody.MovePosition(movePos);
        }

        [Server]
        void AITurn()
        {
            Quaternion newRotation = Quaternion.LookRotation(new Vector3(_moveDirection.x, 0f, _moveDirection.z));
            _characterRoot.CharacterRigidbody.MoveRotation(Quaternion.Slerp(_characterRoot.CharacterRigidbody.rotation, newRotation, _characterRoot.CharacterCommonStat.PlayerRotationSpeed * Time.deltaTime));
        }

        [Server]
        public void AIMove(Vector3 moveDir)
        {
            _moveDirection = moveDir;
            SwitchToMoveState();
        }

        [Server]
        public void AIMoveStop()
        {
            _moveDirection = Vector3.zero;
            SwitchToStopState();
        }

        [Server]
        private void SwitchToMoveState()
        {
            _characterRoot.CharacterStateController.AddState(CharacterActionState.Move);
        }

        [Server]
        private void SwitchToStopState()
        {
            _characterRoot.CharacterStateController.DeleteState(CharacterActionState.Move);
        }

        /// <summary>
        /// 서버에서 이동 시뮬레이션을 진행합니다.
        /// </summary>
        /// <param name="writer"></param>
        [Server]
        private void ServerMoveSimulation(NetworkWriter writer)
        {
            if (_moveInfoList.Count == 0) return;
            writer.WriteUInt32(_lastInputNumber);
            writer.WriteVector3(_characterRoot.CharacterRigidbody.position);
        }

        /// <summary>
        /// 이동에 따른 애니메이션 상태를 설정합니다.
        /// </summary>
        /// <returns></returns>
        [Server]
        private IEnumerator CoSetAnimation()
        {
            yield return null;
            while (true)
            {
                yield return Wait.For_0_1f;
                if (_characterRoot.CharacterStateController.IsOutOfControl) continue;

                // 애니메이션 컨트롤
                if (_characterRoot.CharacterStateController.IsMove)
                    _characterRoot.AnimationController.ServerSetAnimation(PlayerAnimState.Run);
                else
                    _characterRoot.AnimationController.ServerSetAnimation(PlayerAnimState.Idle);
            }
        }
#endif

        /// <summary>
        /// 클라이언트에서 서버로 이동 인풋 데이터를 전송합니다.
        /// </summary>
        /// <param name="sMoveInputData"></param>
        [ServerRpc]
        private void ServerRpcSendMoveInputData(SMoveInputData sMoveInputData)
        {
#if SERVER
            if (_characterRoot.CharacterStateController.HasState(CharacterActionState.OutOfContol)) return;
            AddMoveInputData(sMoveInputData);
            SwitchToMoveState();
#endif
        }

        /// <summary>
        /// 클라이언트에서 서버로 캐릭터 이동을 중지 요청합니다.
        /// </summary>
        [ServerRpc]
        private void ServerRpcRequestCharactertStop()
        {
#if SERVER
            SwitchToStopState();
#endif
        }

        /// <summary>
        /// 서버가 클라이언트로 이동 결과를 전송하기 위해 대상의 위치 데이터를 Serialize합니다.
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="initialState"></param>
        /// <returns></returns>
        public override bool OnSerialize(NetworkWriter writer, bool initialState)
        {
            base.OnSerialize(writer, initialState);
            if (initialState) return false;

            LOGGER.Log("Server Send Simulated Input Result");

            writer.WriteUInt32(_lastInputNumber);
            writer.WriteVector3(_characterRoot.CharacterRigidbody.position);
            return true;
        }

#if CLIENT
        /// <summary>
        /// 클라이언트에서 로컬 플레이어의 이동을 업데이트합니다.
        /// </summary>
        [Client]
        private void ClientUpdateLocalPlayer()
        {
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
            if (Input.GetKeyDown(KeyCode.F5))
            {
                _isKeyBoardControlUse = !_isKeyBoardControlUse;
                Debug.Log($"키보드 입력 활성화/비활성화 여부: {_isKeyBoardControlUse}");
            }

            if (_isKeyBoardControlUse)
            {
                ClientProcessKeyboardInput();
                return;
            }
#endif
            ClientProcessJoystickMoveInput();
        }

        /// <summary>
        /// 조이스틱 입력 상태일 때 인풋 처리
        /// </summary>
        [Client]
        private void ClientProcessJoystickMoveInput()
        {
            _moveDirection = _moveJoystickController.Input.normalized;
        }

        /// <summary>
        /// [DEV TEST] 키보드 입력 상태일 때 인풋 처리
        /// </summary>
        [Client]
        private void ClientProcessKeyboardInput()
        {
            if (_isSupportedPlatform)
            {
                if (Input.GetKey(KeyCode.A))
                    _moveDirection.x = -1f;
                else if (Input.GetKey(KeyCode.D))
                    _moveDirection.x = 1f;
                else
                    _moveDirection.x = 0f;

                if (Input.GetKey(KeyCode.W))
                    _moveDirection.z = 1f;
                else if (Input.GetKey(KeyCode.S))
                    _moveDirection.z = -1f;
                else
                    _moveDirection.z = 0f;

                if (_moveDirection.IsEqualTo(Vector3.zero) && _characterRoot.CharacterStateController.IsMove)
                    ClientCharacterStop();
            }
        }

        [Client]
        private void OnStartClient()
        {
            _runtimeGameData = RuntimeGameData.Instance;
            GameManagerBase.Instance.GameSequenceEventContainer.OnEndGameReady.OnClientEvent.AddListener(OnClientEndGameReady);
        }

        /// <summary>
        /// 클라이언트에서 권한이 변경되었을 때 처리
        /// </summary>
        /// <param name="hasAuthority"></param>
        [Client]
        private void OnAuthorityChanged(bool hasAuthority)
        {
            if (!hasAuthority)
            {
                return;
            }

            Vector3 pos = _characterRoot.CharacterRigidbody.position;
            _clientRecvPos = pos;
            _clientSimulatedPos = pos;

            // MoveController 초기화
            var moveController = ControllerContainer.ControllerDic[ControllerType.MoveController];
            moveController.SetUpController(SkillType.NONE, GrabType.NoMatter, ControlType.Both);
            moveController.JoystickController.OnDragEvent.AddListener(ClientProcessJoystickMoveInput);
            moveController.JoystickController.OffDragEvent.AddListener(ClientCharacterStop);
            moveController.ControllerLock = false;
            _moveJoystickController = moveController.JoystickController;
        }

        [Client]
        private void OnClientEndGameReady()
        {
            _moveInfoList.Clear();
            _characterRoot.AnimationController.ClientSetAnimation(PlayerAnimState.Idle);
        }

        /// <summary>
        /// 클라이언트에서 이동 Input의 결과 값을 계산하고 이동에 선반영
        /// </summary>
        [Client]
        private void ClientReSimulation()
        {
            Vector3 goal = _clientRecvPos;
            var moveSpeed = _characterRoot.RuntimeCharacterData.MovSpeed;
            var playerMoveSpeedRate = CharacterCommonStat.Instance.PlayerMoveSpeedRate;
            foreach (var moveInfo in _moveInfoList)
            {
                goal += moveInfo.Direction * moveSpeed * playerMoveSpeedRate;
            }

            _clientSimulatedPos = goal;

            // Need Teleport
            if (Vector3.Distance(_clientSimulatedPos, transform.position) > 1f)
            {
                Debug.Log("Teleport!");
                transform.position = _clientSimulatedPos;
            }
        }

        /// <summary>
        /// 클라이언트에서 로컬 플레이어의 이동을 처리하고 이동한 데이터를 서버에 전송합니다.
        /// </summary>
        [Client]
        private void ClientPlayerMove()
        {
            if (!_characterRoot.CharacterStateController.IsTurnLock)
                Turn(_moveDirection);
            if (!_characterRoot.CharacterStateController.IsMoveLock)
                if (!Run(_moveDirection))
                    return;

            var moveInputData = new SMoveInputData { Direction = _moveDirection, InputNumber = ++_currentInputNumber };

            AddMoveInputData(moveInputData);
            ServerRpcSendMoveInputData(moveInputData);
        }

        /// <summary>
        /// 클라이언트가 서버로부터 이동 결과를 Deserialize합니다.
        /// </summary>
        /// <param name="reader"></param>
        /// <param name="initialState"></param>
        [Client]
        public override void OnDeserialize(NetworkReader reader, bool initialState)
        {
            base.OnDeserialize(reader, initialState);
            if (initialState) return;

            if (!reader.CanRead()) return;
            if (!HasAuthority)
            {
                reader.ReadUInt32();
                reader.ReadVector3();
                return;
            }

            LocalPlayerProcessRecvMoveData(reader);
        }

        /// <summary>
        /// 클라이언트에서 서버로부터 받은 이동 결과를 처리합니다.
        /// </summary>
        /// <param name="reader"></param>
        [Client]
        private void LocalPlayerProcessRecvMoveData(NetworkReader reader)
        {
            var lastInputNumber = reader.ReadUInt32();
            var clientRecvPos = reader.ReadVector3();

            if (!HasAuthority) return;
            if (_moveInfoList.Count == 0) return;

            int inputIndex = _moveInfoList.FindIndex((x) => lastInputNumber == x.InputNumber);
            if (inputIndex != -1)
                _moveInfoList.RemoveRange(0, inputIndex + 1);
            _clientRecvPos = clientRecvPos;
            ClientReSimulation();
        }

        [Client]
        private void ClientCharacterStop()
        {
            ServerRpcRequestCharactertStop();
        }
#endif
    }
}