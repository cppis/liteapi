# LiteAPI

[![.NET 9.0](https://img.shields.io/badge/.NET-9.0-512BD4)](https://dotnet.microsoft.com/)
[![Tests](https://img.shields.io/badge/tests-48%20passed-success)](liteapi.Tests/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

ASP.NET Core 9.0 Minimal API 프로젝트로 모바일 웹 서버의 기본 기능을 제공합니다.

## 주요 기능

### 1. JSON & MessagePack 직렬화 지원
- **JSON**: 기본 텍스트 기반 직렬화, 디버깅 및 개발 환경에 적합
- **MessagePack**: 바이너리 직렬화, 프로덕션 환경에서 고성능 통신
- **Content-Type 자동 감지**: 요청 헤더에 따라 자동 선택
  - `application/json` → JSON 처리
  - `application/x-msgpack` → MessagePack 처리

### 2. Entity Framework Core 통합
- **Code-First 접근**: C# 모델 클래스 기반 데이터베이스 스키마 생성
- **MySQL 지원**: Pomelo.EntityFrameworkCore.MySql 프로바이더 사용
- **마이그레이션**: EF Core Migration 기능으로 스키마 버전 관리
- **비동기 처리**: 모든 DB 작업은 async/await 패턴 사용

### 3. DB Lock & Middleware 기반 동시성 제어
- **MySQL 분산 락**: `GET_LOCK` 및 `RELEASE_LOCK` 함수 사용
- **EF Core 통합**: DbContext를 통한 락 관리
- **사용자별 독립 락**: 동일 사용자의 동시 요청을 순차 처리하여 데이터 무결성 보장
- **자동 락 처리**: `PacketLockMiddleware`가 인증된 요청에 자동으로 락 적용
- **선택적 락 제외**: 헬스체크 등 특정 엔드포인트는 락 생략
- **에러 처리**: 락 획득 실패 시 409 Conflict 응답 반환
- **타임아웃 관리**: 기본 30초, 설정 가능한 타임아웃 및 자동 해제

### 4. Serilog 구조화 로깅
- **멀티 싱크**: Console + File 동시 출력
- **구조화된 로그**: JSON 형식으로 파싱 가능한 로그 저장
- **로그 레벨**: Information, Warning, Error 등 단계별 필터링
- **파일 롤링**: 일별 로그 파일 자동 생성 (`logs/log-yyyyMMdd.txt`)
- **요청 추적**: HTTP 요청/응답 자동 로깅

### 5. Prometheus 메트릭 수집
- **HTTP 메트릭**: 요청 수, 응답 시간, 상태 코드별 통계
- **커스텀 메트릭**: 사용자 액션, 비즈니스 로직 측정
- **Grafana 연동**: `/metrics` 엔드포인트로 실시간 모니터링
- **타입 지원**: Counter, Gauge, Histogram, Summary

### 6. xUnit 단위 테스트
- **높은 커버리지**: 48/53 테스트 통과 (90.6%)
- **Moq 프레임워크**: 의존성 모킹으로 격리된 테스트
- **FluentAssertions**: 읽기 쉬운 assertion 문법
- **InMemory DB**: 실제 DB 없이 빠른 테스트 실행

### 7. YAML 기반 설정 파일
- **계층적 구조**: YAML의 들여쓰기로 설정 그룹화
- **환경별 분리**: `appsettings.yaml` + `appsettings.Development.yaml`
- **타입 안전성**: C# 클래스로 강타입 바인딩
- **핫 리로드**: 설정 변경 시 자동 반영 (일부 설정)

## API 엔드포인트

### 사용자 관리
| Method | Path | Description | Request | Response |
|--------|------|-------------|---------|----------|
| POST | `/api/user/create` | 사용자 생성 | `{ "username": "string", "email": "string" }` | `UserPacket` |
| GET | `/api/user/{userId}` | 사용자 조회 | - | `UserPacket` |
| PUT | `/api/user/update` | 사용자 정보 수정 | `UserPacket` | `UserPacket` |
| DELETE | `/api/user/{userId}` | 사용자 삭제 | - | `204 No Content` |

### 테스트 및 헬스체크
| Method | Path | Description | Auth Required |
|--------|------|-------------|---------------|
| POST | `/test` | MessagePack 테스트 | ❌ |
| GET | `/test/json` | JSON 직렬화 테스트 | ❌ |
| GET | `/health` | 서버 상태 확인 | ❌ |
| GET | `/metrics` | Prometheus 메트릭 | ❌ |

## 모니터링

### Serilog
```bash
# 로그 파일 위치
liteapi/logs/log-20260101.txt

# 실시간 로그 모니터링
tail -f liteapi/logs/log-$(date +%Y%m%d).txt
```

### Prometheus + Grafana
```yaml
# prometheus.yml
scrape_configs:
  - job_name: 'liteapi'
    scrape_interval: 5s
    static_configs:
      - targets: ['localhost:5117']
```

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

## 마이그레이션 내역

**이전 버전**: mini_server (.NET 8.0)
**현재 버전**: liteapi (.NET 9.0)

### 주요 변경사항
- ✅ .NET 9.0 SDK로 업그레이드 (9.0.112 사용)
- ✅ 모든 NuGet 패키지 .NET 9 호환 버전으로 업데이트
- ✅ 네임스페이스 `mini_server` → `liteapi` 변경
- ✅ 프로젝트 파일명 및 경로 업데이트
- ✅ VS Code 디버그 설정 최신화 (.NET 9 대응)
- ✅ global.json으로 SDK 버전 고정

## 향후 개선 사항

- [ ] Redis 분산 캐시 통합
- [ ] JWT 인증 구현
- [ ] Rate Limiting 미들웨어
- [ ] GraphQL 엔드포인트 추가
- [ ] Docker Compose 배포 자동화
- [ ] CI/CD 파이프라인 (GitHub Actions)
- [ ] Swagger UI 개선
- [ ] gRPC 지원

## 라이센스

MIT License

## 기여

이슈 및 PR은 언제든 환영합니다!
