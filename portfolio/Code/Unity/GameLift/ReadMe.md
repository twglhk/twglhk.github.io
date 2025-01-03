# GameLift SDK 적용 코드 샘플 포트폴리오

[Wild Bowl : Zooports] 프로젝트를 제작하며 구현했던 Unity + GameLift SDK 코드 샘플 포트폴리오

## Contents
- GameLiftServerMAnager
  - GameLift의 초기화 및 세션 시작을 담당하는 매니저 클래스

- MirageGameLiftAuthenticator
  - 게임 서버에 접속한 유저들을 GameLift와 연계하여 인증 처리하던 클래스

- PortAllocator
  - GameLift에 의해 생성된 EC2 인스턴스 내에서 게임 서버 세션을 할당할 때, Port를 예약하는 클래스