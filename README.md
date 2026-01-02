# LiteAPI

[![.NET 9.0](https://img.shields.io/badge/.NET-9.0-512BD4)](https://dotnet.microsoft.com/)
[![Tests](https://img.shields.io/badge/tests-48%20passed-success)](liteapi.Tests/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

ASP.NET Core 9.0 Minimal API 프로젝트로 모바일 웹 서버의 기본 기능을 제공합니다.

<br/><br/><br/>

## 주요 기능  
[주요 기능 소개](./README.features.md) 문서를 참고하세요.

<br/><br/><br/>

## 시작하기

### 필수 요구사항
- .NET 9.0 SDK (9.0.112 이상)
- MySQL 8.0 이상
- (선택) Docker (컨테이너 배포 시)

### 실행 방법

```bash
# 1. 저장소 클론
cd /mnt/c/Works/git/liteapi

# 2. NuGet 패키지 복원
export PATH="$HOME/.dotnet:$PATH"
dotnet restore

# 3. 데이터베이스 마이그레이션
cd liteapi
dotnet ef database update

# 4. 개발 서버 실행
dotnet run

# 5. 브라우저에서 확인
# http://localhost:5117/health
```

### 테스트 실행

```bash
# 전체 테스트 실행
dotnet test

# 특정 프로젝트만 테스트
dotnet test liteapi.Tests/liteapi.Tests.csproj

# 상세 출력과 함께 테스트
dotnet test -v detailed
```

<br/><br/><br/>

## 프로젝트 구조

```
liteapi/
├── liteapi/                      # 메인 API 프로젝트
│   ├── Data/                     # EF Core DbContext
│   ├── Formatters/               # MessagePack/JSON 포매터
│   ├── Middleware/               # 커스텀 미들웨어
│   ├── Models/                   # 도메인 모델
│   ├── Services/                 # 비즈니스 로직
│   ├── logs/                     # Serilog 로그 파일
│   ├── appsettings.yaml          # 기본 설정
│   ├── appsettings.Development.yaml  # 개발 환경 설정
│   └── Program.cs                # 진입점
├── liteapi.Tests/                # xUnit 테스트 프로젝트
│   ├── Models/                   # 모델 테스트
│   └── Services/                 # 서비스 테스트
├── .vscode/                      # VS Code 설정
│   ├── launch.json               # 디버그 구성
│   ├── tasks.json                # 빌드 태스크
│   └── settings.json             # 편집기 설정
├── liteapi.sln                   # 솔루션 파일
└── omnisharp.json                # OmniSharp 설정
```

<br/><br/><br/>

## 기술 스택

| 카테고리 | 기술 | 버전 |
|----------|------|------|
| **Runtime** | .NET | 9.0.112+ |
| **Framework** | ASP.NET Core Minimal API | 9.0 |
| **ORM** | Entity Framework Core | 9.0.4 |
| **Database** | MySQL (Pomelo Provider) | 9.0.0 |
| **Logging** | Serilog | 10.0.0 |
| **Metrics** | prometheus-net | 8.2.1 |
| **Serialization** | MessagePack | 3.1.4 |
| **Testing** | xUnit + Moq + FluentAssertions | 2.9.3 |
| **Configuration** | YAML | 3.1.0 |

## 테스트 커버리지

| 모듈 | 테스트 수 | 상태 |
|------|-----------|------|
| UserTests | 8 | ✅ 전체 통과 |
| RequestContextTests | 6 | ✅ 전체 통과 |
| MetricsServiceTests | 16 | ✅ 전체 통과 |
| DbLockServiceTests | 7 | ✅ 2 통과 / 5 스킵 (MySQL 필요) |
| DbLockServiceIntegrationTests | 16 | 🔶 통합 테스트 |
| **합계** | **53** | **48 통과 / 5 스킵** |

> 💡 스킵된 테스트는 실제 MySQL 데이터베이스가 필요한 통합 테스트입니다.

<br/><br/><br/>

## 향후 개선 사항

- [ ] Redis 분산 캐시 통합
- [ ] JWT 인증 구현
- [ ] Rate Limiting 미들웨어
- [ ] GraphQL 엔드포인트 추가
- [ ] Docker Compose 배포 자동화
- [ ] CI/CD 파이프라인 (GitHub Actions)
- [ ] Swagger UI 개선
- [ ] gRPC 지원

<br/><br/><br/>

## 라이센스

MIT License

<br/><br/><br/>

## 기여

이슈 및 PR은 언제든 환영합니다!
