using System.Collections.Generic;
using UnityEngine;
using Mirage;
using Zooports.Network.AWS.GameLift;
using Mirage.Logging;
using Aws.GameLift.Server.Model;
using Mirage.Authentication;
using System;
using System.Linq;

namespace Zooports.Network
{
    /// <summary>
    /// 클라이언트가 데디케이티드 서버에 접속하기 위해 인증 과정을 담당하는 클래스 <br/>
    /// GameLift에 있는 PlayerSession만 인증 성공.
    /// </summary>
    public sealed class MirageGameLiftAuthenticator : NetworkAuthenticator<MirageAuthRequest>
    {
        #region Logger.

        private static readonly ILogger LOGGER = LogFactory.GetLogger(typeof(MirageGameLiftAuthenticator));

        #endregion


#if SERVER
        private PlayerSessionDataContainer _playerSessionDataContainer;
        private GameLiftServerManager _gameLiftServerManager;

        /// <summary>
        /// GameLift 인증 과정을 위한 초기화 메서드
        /// </summary>
        /// <param name="playerSessionDataContainer"></param>
        /// <param name="gameLiftServerManager"></param>
        public void InitMirageGameLiftAuthenticator(PlayerSessionDataContainer playerSessionDataContainer,
            GameLiftServerManager gameLiftServerManager)
        {
            _playerSessionDataContainer = playerSessionDataContainer;
            _gameLiftServerManager = gameLiftServerManager;
        }

        /// <summary>
        /// [Server] GameLift에 있는 PlayerSession을 인증하는 메서드
        /// </summary>
        /// <param name="player"></param>
        /// <param name="message"></param>
        /// <returns></returns>
        protected override AuthenticationResult Authenticate(INetworkPlayer player, MirageAuthRequest message)
        {
            // GameLift에서 Player 정보를 찾지 못했다면 실패 처리.
            string receivedPlayerSessionId = message.PlayerSessionId;
            IList<PlayerSession> playerSessions = _gameLiftServerManager.GetPlayerSessions();
            
            try
            {
                // GameLift에서 찾은 Player 정보 중, 요청한 PlayerSessionId와 일치하는 정보를 찾는다.
                PlayerSession targetPlayerSession = playerSessions?.FirstOrDefault(playerSession =>
                    playerSession.PlayerSessionId == receivedPlayerSessionId);
                
                if (targetPlayerSession == null)
                {
                    Debug.Log($"PlayerSessionId is null : {message.PlayerSessionId}");
                    return AuthenticationResult.CreateFail("Player Session is null.");
                }
                
                string targetPlayerSessionId = targetPlayerSession.PlayerSessionId;
                Debug.Log($"Target PlayerSessionId : {targetPlayerSessionId}");

                // Target Player를 GameLift에서 accept 시도.
                if (!_gameLiftServerManager.AcceptPlayerSession(targetPlayerSessionId))
                {
                    Debug.Log($"AcceptPlayerSession Failed : {targetPlayerSessionId}");
                    return AuthenticationResult.CreateFail("AcceptPlayerSession Failed.");
                }
                
                // 인증 성공 처리
                _playerSessionDataContainer.UpdateGameLiftPlayerSessionData(targetPlayerSession, player);
                Debug.Log($"GAMELIFT_PLAYER_SESSION ACCEPT SUCCESS");
                return AuthenticationResult.CreateSuccess(this, null);
            }
            catch (Exception exception)
            {
                return AuthenticationResult.CreateFail($"Player Session is null. {exception.Message}");
            }
        }
#endif

#if CLIENT
        /// <summary>
        /// [Client] GameLift에 생성된 PlayerSession ID를 서버에 전송하는 메서드
        /// </summary>
        /// <param name="client"></param>
        /// <param name="userNumber"></param>
        /// <param name="playerSessionId"></param>
        public void SendCode(NetworkClient client, string userNumber, string playerSessionId)
        {
            var message = new MirageAuthRequest
            {
                UserNumber = userNumber,
                PlayerSessionId = playerSessionId
            };

            SendAuthentication(client, message);
        }
#endif
    }
}