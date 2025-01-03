using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Zooports.Network
{
    /// <summary>
    /// 포트 번호를 동적으로 할당하는 클래스입니다.
    /// </summary>
    public class PortAllocator
    {
        // TODO: GameLift에서 얻어오도록 변경
        private readonly int _minPort; // GameLift에서 지정한 최소 포트 번호
        private readonly int _maxPort; // GameLift에서 지정한 최대 포트 번호
        private Socket _reserveSocket;
        
        public PortAllocator(int minPort = 7000, int maxPort = 60000)
        {
            _minPort = minPort;
            _maxPort = maxPort;
        }
        
        /// <summary>
        /// 소켓 바인딩을 수행하여 특정 포트를 예약하고 포트 번호를 반환합니다.
        /// </summary>
        /// <returns></returns>
        public int AllocatePort()
        {
            int allocatedPort = 0;
            bool isBound = false;

            while (!isBound)
            {
                allocatedPort = GetRandomPortInRange(_minPort, _maxPort);
                Socket reserveSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                try
                {
                    reserveSocket.Bind(new IPEndPoint(IPAddress.Any, allocatedPort));
                    isBound = true; // 성공적으로 바인드 되면 반복을 종료합니다.
                    _reserveSocket = reserveSocket;
                }
                catch (SocketException)
                {
                    // 바인드 실패 (포트가 이미 사용 중일 경우), 다른 포트 시도
                    reserveSocket.Dispose();
                    isBound = false;
                }
            }
            return allocatedPort;
        }
        
        /// <summary>
        /// 다른 프로세스에서 사용할 수 있또록 포트 번호를 릴리즈합니다.
        /// </summary>
        public void ReleasePort()
        {
            _reserveSocket?.Dispose();
        }

        /// <summary>
        /// 지정한 범위 내에서 랜덤한 포트 번호를 반환합니다.
        /// </summary>
        /// <param name="minPort"></param>
        /// <param name="maxPort"></param>
        /// <returns></returns>
        private static int GetRandomPortInRange(int minPort, int maxPort)
        {
            using (var rng = new RNGCryptoServiceProvider())
            {
                byte[] buffer = new byte[4];
                rng.GetBytes(buffer);
                int result = BitConverter.ToInt32(buffer, 0) & int.MaxValue; // 음수를 제거합니다.
                return minPort + (result % (maxPort - minPort));
            }
        }
    }
}