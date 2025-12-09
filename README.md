# GameOps Playground – Client

GameOps Playground의 Unity 클라이언트 프로젝트입니다.  
UI, 게임 로직, 그래픽 처리, 서버 통신 등을 담당합니다.

## Project Overview

GameOps Playground Client는 Server 프로젝트와 분리된 Multi-repo 구조로 관리됩니다.  
클라이언트는 Unity 기반으로 개발되며, 서버와의 네트워크 통신을 통해 게임 플레이 기능을 제공합니다.

## Development Environment

- **Unity Version**: 6000.0.34f1
- **IDE**: Visual Studio / Rider
- **Platforms**: Windows / macOS / (필요 시 Mobile 추가)

## Repository Structure

```
GameOps-Playground-Client/
├── Assets/
├── Packages/
├── ProjectSettings/
├── .gitignore
├── README.md
└── ...
```

## Related Repositories

- **Server**: (추후 링크 추가)

## Branch Strategy

- **main**: 안정화 및 배포용
- **develop**: 통합 개발 브랜치
- **feature/\***: 기능 개발 브랜치
- **fix/\***: 버그 수정 브랜치

## Setup Instructions

1. **저장소 클론**

   ```bash
   git clone https://github.com/your-org/GameOps-Playground-Client.git
   ```

2. **Unity Hub에서 프로젝트 열기**

   - Unity 버전: 6000.0.34f1

3. **필요한 패키지 자동 설치 완료까지 기다리기**

4. **Unity Editor에서 Play 버튼을 눌러 실행 확인**

## Server Connection

- **서버 주소**: (추가 예정)
- **API 규격**: (추가 예정)
- **인증/세션 방식**: (추가 예정)

## Testing Guide

- Unity Play Mode 테스트 가능
- 서버 연동 테스트는 Server 프로젝트와 연결 필요
- Local Mock Server 사용 가능 (추후 문서화 예정)

## License

Private (내부용). 필요 시 라이선스 추가 또는 수정 가능.

## Contributing

- 모든 Pull Request는 `develop` 브랜치 기준으로 생성
- 코드 리뷰 후 merge 진행
