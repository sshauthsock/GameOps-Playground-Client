# GameOps Playground – Client

GameOps Playground의 Unity 클라이언트 프로젝트입니다.  
UI, 게임 로직, 그래픽 처리, 서버 통신 등을 담당합니다.

## ⚠️ 중요 안내

- **Default Branch**: `develop` (GitHub에서 설정 필요)
- **직접 커밋 금지**: `main`과 `develop` 브랜치는 PR을 통해서만 변경
- **브랜치 네이밍**: `feature/`, `fix/`, `hotfix/` 접두사 필수 사용
- **작업 전 필수**: `develop` 브랜치에서 최신 코드 pull 후 브랜치 생성

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

- **main**: 프로덕션 배포용 브랜치 (보호됨)
- **develop**: 기본 개발 브랜치 (Default Branch, 보호됨)
- **feature/\***: 새로운 기능 개발 브랜치
- **fix/\***: 버그 수정 브랜치
- **hotfix/\***: 긴급 버그 수정 브랜치 (main에서 분기)

### 브랜치 작업 흐름

1. **새로운 기능 개발**

   ```bash
   git checkout develop
   git pull origin develop
   git checkout -b feature/기능명
   # 작업 진행
   git add .
   git commit -m "feat: 기능 설명"
   git push origin feature/기능명
   # GitHub에서 develop으로 PR 생성
   ```

2. **버그 수정**

   ```bash
   git checkout develop
   git pull origin develop
   git checkout -b fix/버그명
   # 작업 진행
   git add .
   git commit -m "fix: 버그 수정 내용"
   git push origin fix/버그명
   # GitHub에서 develop으로 PR 생성
   ```

3. **긴급 수정 (Hotfix)**
   ```bash
   git checkout main
   git pull origin main
   git checkout -b hotfix/긴급수정명
   # 작업 진행
   git add .
   git commit -m "hotfix: 긴급 수정 내용"
   git push origin hotfix/긴급수정명
   # GitHub에서 main으로 PR 생성 후, develop에도 merge
   ```

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

- **서버 주소**: 기본값 `127.0.0.1:7777` (NetworkManager Inspector에서 변경 가능)
- **프로토콜**: TCP 소켓 통신, 커스텀 바이너리 프로토콜
- **서버 배포**: `Archive/` 폴더의 Dockerfile을 사용하여 서버 배포 가능

## 배포 가이드

게임을 배포하여 다른 사람들과 테스트하는 방법:

### 📚 배포 옵션

1. **Amazon GameLift 배포** (프로덕션 권장)
   - 자동 스케일링, DDoS 보호, 글로벌 배포
   - 비용: $80~$150/월
   - 상세 가이드: [DEPLOYMENT_GUIDE.md](./DEPLOYMENT_GUIDE.md#옵션-1-amazon-gamelift-배포)

2. **WebGL 배포** (빠른 테스트 권장)
   - 브라우저에서 바로 플레이 가능
   - 비용: $0~$10/월 (무료 호스팅 가능)
   - 상세 가이드: [DEPLOYMENT_GUIDE.md](./DEPLOYMENT_GUIDE.md#옵션-2-webgl-배포)

### 🚀 빠른 시작

**WebGL 배포 (30분):**
1. Unity에서 WebGL 빌드 생성
2. GitHub Pages/Netlify에 업로드
3. EC2에 Docker 서버 배포
4. NetworkManager에 서버 IP 설정
5. 완료!

**GameLift 배포 (2~4시간):**
1. Docker 이미지를 ECR에 푸시
2. GameLift Fleet 생성
3. Unity에서 클라이언트 빌드
4. GitHub Releases에 클라이언트 업로드
5. NetworkManager에 Fleet 엔드포인트 설정
6. 완료!

**상세한 구현 방법**: [DEPLOYMENT_IMPLEMENTATION.md](./DEPLOYMENT_IMPLEMENTATION.md)

## Testing Guide

- Unity Play Mode 테스트 가능
- 서버 연동 테스트는 Server 프로젝트와 연결 필요
- Local Mock Server 사용 가능 (추후 문서화 예정)

## License

Private (내부용). 필요 시 라이선스 추가 또는 수정 가능.

## Contributing

### 커밋 메시지 규칙

커밋 메시지는 다음 형식을 따릅니다:

```
<type>: <subject>

<body> (선택사항)
```

**Type 종류:**

- `feat`: 새로운 기능 추가
- `fix`: 버그 수정
- `docs`: 문서 수정
- `style`: 코드 포맷팅, 세미콜론 누락 등 (코드 변경 없음)
- `refactor`: 코드 리팩토링
- `test`: 테스트 코드 추가/수정
- `chore`: 빌드 업무, 패키지 매니저 설정 등

**예시:**

```
feat: 로비 씬에 채팅 기능 추가
fix: 게임 씬 로딩 시 크래시 문제 해결
docs: README에 서버 연동 가이드 추가
```

### Pull Request 규칙

1. **모든 PR은 `develop` 브랜치를 타겟으로 생성** (hotfix 제외)
2. PR 제목은 커밋 메시지 규칙과 동일하게 작성
3. PR 설명에는 다음 내용 포함:
   - 변경 사항 요약
   - 테스트 방법
   - 스크린샷 (UI 변경 시)
   - 관련 이슈 번호 (있는 경우)
4. 최소 1명 이상의 리뷰어 승인 필요
5. 모든 CI 체크 통과 후 merge
6. Merge 후 작업 브랜치는 삭제

### 코드 리뷰 가이드

- 코드 스타일 및 컨벤션 준수 확인
- 성능 및 메모리 사용 고려
- 에러 핸들링 적절성 검토
- 테스트 커버리지 확인
