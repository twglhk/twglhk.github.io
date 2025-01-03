# .NET 백엔드 코드 샘플 포트폴리오

[Wild Bowl : Zooports] 프로젝트를 제작하며 구현했던 백엔드 코드 샘플 포트폴리오

## Contents
- Contents
  -  Asset
     - 유저의 에셋 데이터를 쿼리할 떄 사용한 람다 백엔드 코드 모음
   - Character
     - 유저 캐릭터와 관련된 처리를 수앵하는 람다 백엔드 코드 모음

- GameLift
  - Matcing
    - ClientMatching
      - 클라이언트에서 보낸 매칭 요청을 처리하는 람다 백엔드 코드 모음

    - ServerMatching
      - GameLift -> AWS SQS를 통해 들어온 데이터를 가지고 매칭을 처리한 뒤, 클라이언트의 WebSocket으로 매칭 결과를 반환하는 람다 백엔드 코드 모음 