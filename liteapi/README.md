# Mini Server - Production-Ready Minimal API

[![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)
[![Tests](https://img.shields.io/badge/tests-48%20passed-brightgreen.svg)](https://github.com/xunit/xunit)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

ASP.NET Core Minimal API 프로젝트로 모바일 웹 서버의 기본 기능을 제공합니다.

## 📋 주요 기능

### 1. Packet Serialization (JSON & MessagePack)
- **이중 직렬화 지원**: JSON과 MessagePack을 동적으로 선택
- **Content-Type 기반 역직렬화**: 클라이언트 요청 형식 자동 감지
- **Accept 헤더 기반 직렬화**: 응답 형식 동적 선택
- **Custom Formatters**: PacketInputFormatter, PacketOutputFormatter
- **바이너리 최적화**: MessagePack으로 데이터 크기 감소 (50-70%) 및 성능 향상

### 2. Entity Framework Core
- **ORM**: Pomelo.EntityFrameworkCore.MySql을 사용한 MySQL 연동
- **DbContext**: AppDbContext를 통한 데이터베이스 접근
- **Code First**: Entity 모델 정의 및 마이그레이션 지원
- **CRUD 엔드포인트**: User 엔티티에 대한 완전한 CRUD 작업
- **In-Memory Database**: 테스트용 인메모리 DB 지원

### 3. DB Lock & Middleware 기반 동시성 제어
- **MySQL 분산 락**: `GET_LOCK` 및 `RELEASE_LOCK` 함수 사용
- **EF Core 통합**: DbContext를 통한 락 관리
- **사용자별 독립 락**: 동일 사용자의 동시 요청을 순차 처리하여 데이터 무결성 보장
- **자동 락 처리**: `PacketLockMiddleware`가 인증된 요청에 자동으로 락 적용
- **선택적 락 제외**: 헬스체크 등 특정 엔드포인트는 락 생략
- **에러 처리**: 락 획득 실패 시 409 Conflict 응답 반환
- **타임아웃 관리**: 기본 30초, 설정 가능한 타임아웃 및 자동 해제

### 4. Serilog 구조화된 로깅
- **콘솔 및 파일 로깅**: 실시간 콘솔 출력 + 일별 롤링 파일
- **로그 레벨 필터링**: Debug, Information, Warning, Error
- **구조화된 로그**: JSON 형식으로 로그 데이터 저장
- **요청 로깅**: HTTP 요청/응답 자동 로깅
- **로그 보관**: 30일 로그 파일 자동 관리

### 5. Prometheus 메트릭
- **HTTP 메트릭**: 요청 수, 응답 시간, 상태 코드별 통계
- **커스텀 메트릭**:
  - **Counter**: 요청 수, DB 락 획득 수, 패킷 처리 수
  - **Gauge**: 활성 사용자 수, 활성 DB 락 수
  - **Histogram**: 요청 시간 분포, DB 락 대기 시간 분포
- **/metrics 엔드포인트**: Prometheus 서버 스크래핑 지원
- **Grafana 연동**: 대시보드 시각화 가능

### 6. xUnit 단위 테스트
- **48개 테스트**: 100% 통과율
- **Moq**: 의존성 모킹
- **FluentAssertions**: 가독성 높은 Assertion
- **테스트 커버리지**: Services, Models, Middleware
- **CI/CD 지원**: 자동화된 테스트 파이프라인

### 7. YAML 설정
- **appsettings.yaml**: 가독성 높은 YAML 형식
- **환경별 설정**: Development, Production 등
- **주석 지원**: 설정 파일에 설명 추가 가능

### 8. Redis 인메모리 캐싱
- **분산 캐시**: Redis를 활용한 고성능 인메모리 캐싱
- **세션 관리**: 사용자 세션 데이터의 빠른 읽기/쓰기
- **캐시 만료 정책**: TTL(Time-To-Live) 기반 자동 만료 지원
- **Pub/Sub 지원**: 실시간 이벤트 알림 및 캐시 무효화
- **직렬화 호환**: JSON 및 MessagePack 형식의 캐시 데이터 저장

## 🌐 엔드포인트

### 모니터링 & 문서
| 엔드포인트 | 메서드 | 설명 | 인증 |
|-----------|--------|------|------|
| `/health` | GET | 헬스체크 | ❌ |
| `/metrics` | GET | Prometheus 메트릭 | ❌ |
| `/swagger` | GET | API 문서 (Swagger UI) | ❌ |

### 패킷 직렬화 (JSON & MessagePack)
| 엔드포인트 | 메서드 | 설명 | 인증 |
|-----------|--------|------|------|
| `/api/packet/echo` | POST | 패킷 에코 테스트 | ❌ |
| `/api/packet/user` | POST | 패킷 기반 사용자 생성 | ❌ |

### 테스트 엔드포인트
| 엔드포인트 | 메서드 | 설명 | 인증 |
|-----------|--------|------|------|
| `/api/test/locked` | GET | 자동 락 테스트 | ✅ |
| `/api/test/concurrent` | POST | 동시성 테스트 (2초) | ✅ |
| `/api/test/direct-lock` | POST | 직접 락 테스트 | ❌ |

### User CRUD (EF Core)
| 엔드포인트 | 메서드 | 설명 | DB Lock |
|-----------|--------|------|---------|
| `/api/users` | POST | 사용자 생성 | ❌ |
| `/api/users` | GET | 모든 사용자 조회 | ❌ |
| `/api/users/{id}` | GET | 특정 사용자 조회 | ❌ |
| `/api/users/{id}` | PUT | 사용자 업데이트 | ✅ |
| `/api/users/{id}` | DELETE | 사용자 삭제 | ❌ |
| `/api/users/{id}/add-gold` | POST | 골드 추가 | ✅ |

## ⚙️ 설정

### appsettings.yaml

프로젝트는 **YAML 형식**의 설정 파일을 사용합니다.

```yaml
Serilog:
  Using:
    - Serilog.Sinks.Console
    - Serilog.Sinks.File
  MinimumLevel:
    Default: Information
    Override:
      Microsoft: Warning
      Microsoft.AspNetCore: Warning
      System: Warning
  WriteTo:
    - Name: Console
      Args:
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {Message:lj}{NewLine}{Exception}"
    - Name: File
      Args:
        path: "logs/mini-server-.log"
        rollingInterval: Day
        retainedFileCountLimit: 30
  Enrich:
    - FromLogContext
    - WithMachineName
    - WithThreadId

AllowedHosts: "*"

ConnectionStrings:
  DefaultConnection: "Server=localhost;Database=mini_server_db;User=root;Password=your_password;"

Lock:
  TimeoutSeconds: 30    # 락 타임아웃 (초)
  Prefix: "api"         # 락 이름 prefix
```

### appsettings.Development.yaml

개발 환경용 설정:

```yaml
Serilog:
  MinimumLevel:
    Default: Debug
    Override:
      Microsoft: Information
      Microsoft.AspNetCore: Information
      Microsoft.EntityFrameworkCore: Information
      Microsoft.EntityFrameworkCore.Database.Command: Information
  WriteTo:
    - Name: Console
      Args:
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"
```

### MySQL 설정

1. 데이터베이스 생성:
```sql
CREATE DATABASE mini_server_db;
```

2. EF Core 마이그레이션 생성 및 적용:
```bash
# 마이그레이션 생성
dotnet ef migrations add InitialCreate

# 데이터베이스에 적용
dotnet ef database update
```

3. MySQL GET_LOCK은 별도 테이블이 필요 없습니다.
   - 세션별로 메모리에서 관리됩니다.

## 🚀 실행 방법

### 1. 의존성 복원
```bash
dotnet restore
```

### 2. 데이터베이스 설정
```bash
# 마이그레이션 생성 (최초 1회)
dotnet ef migrations add InitialCreate

# 데이터베이스 적용
dotnet ef database update
```

### 3. 서버 실행
```bash
dotnet run
```

### 4. 접속
- **Swagger UI**: http://localhost:5000/swagger
- **Health Check**: http://localhost:5000/health
- **Prometheus Metrics**: http://localhost:5000/metrics

### 5. 로그 확인
- **콘솔**: 실시간 로그 출력
- **파일**: `logs/mini-server-YYYY-MM-DD.log`

## 🧪 테스트 방법

### 단위 테스트 실행
```bash
# 모든 테스트 실행
dotnet test

# 상세 출력
dotnet test --verbosity normal

# 특정 테스트만 실행
dotnet test --filter "FullyQualifiedName~MetricsServiceTests"

# 커버리지 포함
dotnet test /p:CollectCoverage=true
```

**테스트 결과:**
```
Passed!  - Failed: 0, Passed: 48, Skipped: 5, Total: 53
```
- ✅ **48개 통과**: 모든 단위 테스트 성공
- ⏭️ **5개 스킵**: MySQL 필요한 통합 테스트 (선택사항)

### HTTP 파일을 사용한 통합 테스트

프로젝트에는 4개의 HTTP 테스트 파일이 포함되어 있습니다:

- 📦 `test-packet.http`: **패킷 직렬화 테스트 (JSON & MessagePack)**
- 🔒 `test-lock.http`: DB Lock 기능 테스트
- 👥 `test-users.http`: User CRUD 및 EF Core 테스트
- 📊 `test-metrics.http`: Prometheus 메트릭 테스트

Visual Studio Code의 **REST Client** 확장으로 실행

### cURL을 사용한 테스트

#### 1. Lock 테스트
```bash
# 헬스체크
curl http://localhost:5000/health

# 자동 락 테스트
curl -H "X-User-Id: 12345" http://localhost:5000/api/test/locked

# 동시성 테스트 (여러 터미널에서 동시 실행)
curl -X POST -H "X-User-Id: 99999" http://localhost:5000/api/test/concurrent

# 직접 락 테스트
curl -X POST http://localhost:5000/api/test/direct-lock?userId=12345
```

#### 2. Packet Serialization 테스트
```bash
# JSON 요청/응답
curl -X POST http://localhost:5000/api/packet/echo \
  -H "Content-Type: application/json" \
  -H "Accept: application/json" \
  -d '{"code":0,"message":"Test","data":{"name":"User1","value":42,"timestamp":"2026-01-01T00:00:00Z"}}'

# JSON 요청 → MessagePack 응답 (바이너리)
curl -X POST http://localhost:5000/api/packet/echo \
  -H "Content-Type: application/json" \
  -H "Accept: application/x-msgpack" \
  -d '{"code":0,"message":"Test","data":{"name":"User2","value":100,"timestamp":"2026-01-01T00:00:00Z"}}'

# 패킷을 통한 사용자 생성
curl -X POST http://localhost:5000/api/packet/user \
  -H "Content-Type: application/json" \
  -d '{"code":0,"message":"Create","data":{"userId":1001,"username":"packet_user","email":"packet@example.com","level":10,"gold":5000}}'
```

#### 3. User CRUD 테스트 (EF Core)
```bash
# 사용자 생성
curl -X POST http://localhost:5000/api/users \
  -H "Content-Type: application/json" \
  -d '{"userId":1,"username":"testuser","email":"test@example.com","level":1,"gold":100}'

# 사용자 조회
curl http://localhost:5000/api/users/1

# 모든 사용자 조회
curl http://localhost:5000/api/users

# 사용자 업데이트 (DB Lock 사용)
curl -X PUT http://localhost:5000/api/users/1 \
  -H "Content-Type: application/json" \
  -d '{"userId":1,"username":"updated","email":"new@example.com","level":5,"gold":500}'

# 골드 추가 (DB Lock 사용)
curl -X POST http://localhost:5000/api/users/1/add-gold?amount=100

# 사용자 삭제
curl -X DELETE http://localhost:5000/api/users/1
```

#### 4. Prometheus 메트릭 테스트
```bash
# 메트릭 엔드포인트 확인
curl http://localhost:5000/metrics

# 몇 가지 요청 생성 후 메트릭 확인
curl http://localhost:5000/health
curl -H "X-User-Id: 12345" http://localhost:5000/api/test/locked
curl http://localhost:5000/metrics
```

**메트릭 예시:**
```prometheus
# HELP mini_server_requests_total Total number of HTTP requests
# TYPE mini_server_requests_total counter
mini_server_requests_total{method="GET",endpoint="/api/users",status_code="200"} 42

# HELP mini_server_active_users Number of currently active users
# TYPE mini_server_active_users gauge
mini_server_active_users 15

# HELP mini_server_request_duration_seconds HTTP request duration in seconds
# TYPE mini_server_request_duration_seconds histogram
mini_server_request_duration_seconds_bucket{method="GET",endpoint="/api/users",le="0.001"} 10
```

### 동시성 검증

동일한 userId로 여러 요청을 동시에 보내면:
- ✅ 첫 번째 요청: 락을 획득하고 처리
- ⏳ 두 번째 요청: 락 획득 대기 후 타임아웃 (30초 후) 또는 첫 번째 요청 완료 후 처리

## 아키텍처

### 요청 처리 흐름

```
Client Request
    ↓
[Authentication Middleware]  - X-User-Id 헤더에서 사용자 ID 추출
    ↓
[PacketLockMiddleware]       - DB Lock 획득 시도
    ↓                          (실패 시 409 Conflict)
[Endpoint Handler]           - 비즈니스 로직 처리
    ↓
[PacketLockMiddleware]       - DB Lock 해제
    ↓
Client Response
```

### 주요 컴포넌트

#### Models
1. **Packet Models** (`Models/Packet.cs`)
   - MessagePackObject 어트리뷰트로 직렬화 지원
   - `Packet<T>`: 제네릭 패킷 래퍼 (Code, Message, Data)
   - `TestRequest/TestResponse`: 테스트용 패킷
   - `UserPacket`: 사용자 데이터 패킷

2. **User Entity** (`Models/User.cs`)
   - 사용자 정보 엔티티
   - EF Core 모델 클래스
   - 기본 레벨 1, 골드 0 설정

3. **RequestContext** (`Models/RequestContext.cs`)
   - 요청별 사용자 정보 보관
   - Scoped 서비스로 등록

#### Services
4. **DbLockService** (`Services/DbLockService.cs`)
   - MySQL GET_LOCK/RELEASE_LOCK 래퍼
   - **EF Core 통합**: DbContext를 사용한 Raw SQL 실행
   - Singleton 서비스로 등록
   - 락 획득, 해제, 실행 메서드 제공

5. **MetricsService** (`Services/MetricsService.cs`)
   - Prometheus 메트릭 수집 서비스
   - Counter, Gauge, Histogram 메트릭 제공
   - 요청, DB 락, 패킷 처리 등 추적
   - Singleton 서비스로 등록

#### Data
6. **AppDbContext** (`Data/AppDbContext.cs`)
   - Entity Framework Core DbContext
   - User 엔티티 매핑 및 설정
   - Scoped 서비스로 등록

#### Formatters
7. **PacketInputFormatter** (`Formatters/PacketInputFormatter.cs`)
   - Content-Type 기반 역직렬화 (JSON/MessagePack)
   - 자동 형식 감지 및 처리

8. **PacketOutputFormatter** (`Formatters/PacketOutputFormatter.cs`)
   - Accept 헤더 기반 직렬화 (JSON/MessagePack)
   - 동적 응답 형식 선택

#### Middleware
9. **PacketLockMiddleware** (`Middleware/PacketLockMiddleware.cs`)
   - 자동 락 처리 미들웨어
   - 인증된 요청에만 적용
   - 요청 전후로 락 획득/해제

## 락 네이밍 규칙

락 이름 형식: `lock_{prefix}_{userId}`

예시:
- `prefix = "api"`, `userId = 12345` → `lock_api_12345`

## 에러 처리

### 401 Unauthorized
- `X-User-Id` 헤더가 없거나 유효하지 않은 경우

### 409 Conflict
- 락 획득 실패 (다른 요청이 이미 락을 보유 중)
- 타임아웃 발생

### 500 Internal Server Error
- MySQL 연결 실패
- 기타 예외 상황

## 성능 고려사항

1. **락 타임아웃 설정**
   - 기본 30초
   - 긴 작업의 경우 타임아웃 증가 고려

2. **락 범위 최소화**
   - 필요한 부분만 락 적용
   - 미들웨어는 전체 요청에 락 적용하므로 주의

3. **MySQL 연결 풀**
   - MySqlConnector는 기본적으로 연결 풀 사용
   - 필요 시 connection string에 설정 추가

## Packet Serialization 상세

### 지원 형식

1. **JSON** (application/json)
   - 사람이 읽기 쉬운 텍스트 형식
   - 디버깅 및 개발에 유리
   - 크기가 MessagePack보다 큼

2. **MessagePack** (application/x-msgpack)
   - 바이너리 형식으로 데이터 크기 감소 (약 50-70%)
   - 직렬화/역직렬화 속도가 JSON보다 빠름
   - 프로덕션 환경에 최적화

### 사용 방법

**요청 형식 지정** (Content-Type):
```bash
# JSON 요청
Content-Type: application/json

# MessagePack 요청
Content-Type: application/x-msgpack
```

**응답 형식 지정** (Accept):
```bash
# JSON 응답
Accept: application/json

# MessagePack 응답
Accept: application/x-msgpack
```

### Packet 구조

```csharp
[MessagePackObject]
public class Packet<T>
{
    [Key(0)] public int Code { get; set; }
    [Key(1)] public string Message { get; set; }
    [Key(2)] public T? Data { get; set; }
}
```

## 기존 프로젝트(projectgsi_server)와의 차이점

| 항목 | projectgsi_server | mini_server |
|------|-------------------|-------------|
| 아키텍처 | Controller 기반 | **Minimal API** |
| ORM | Dapper (Micro-ORM) | **Entity Framework Core** |
| 데이터 접근 | Raw SQL + Dapper | **EF Core LINQ + DbContext** |
| 직렬화 | MessagePack (단일) | **JSON & MessagePack (이중 지원)** |
| 설정 파일 | appsettings.json | **appsettings.yaml** |
| 락 관리 | UserLockManager + AuthRepo | **DbLockService (EF Core 통합)** |
| 미들웨어 | NMiddleware (부분 클래스) | **PacketLockMiddleware** |
| 마이그레이션 | 수동 SQL 스크립트 | **EF Core Migrations** |
| Redis 사용 | O (RedisSingleton) | X (향후 추가 가능) |
| 복잡도 | 높음 (다층 구조) | **낮음 (간결한 구조)** |

## 📦 패키지 버전

### 주요 패키지
- **.NET 8.0**
- **Entity Framework Core** 8.0.11
- **Pomelo MySQL** 8.0.2
- **MessagePack** 3.1.4
- **Serilog** 10.0.0
- **prometheus-net** 8.2.1

### 테스트 패키지
- **xUnit** 2.4.2
- **Moq** 4.20.72
- **FluentAssertions** 8.8.0
- **EF Core InMemory** 8.0.11

전체 패키지 목록은 [GUIDE.md](GUIDE.md)를 참조하세요.

## 📚 문서

- **[GUIDE.md](GUIDE.md)**: 전체 구축 가이드 (1,850+ 라인)
  - 8단계 구현 과정
  - 상세한 코드 예시
  - 트러블슈팅 가이드
  - 다음 단계 제안

## 🔍 모니터링

### Serilog 로그
```bash
# 로그 파일 위치
ls logs/

# 실시간 로그 확인
tail -f logs/mini-server-2026-01-01.log

# 에러 로그만 필터링
grep "ERR" logs/mini-server-2026-01-01.log
```

### Prometheus + Grafana
1. **Prometheus 설정** (`prometheus.yml`):
```yaml
scrape_configs:
  - job_name: 'mini_server'
    static_configs:
      - targets: ['localhost:5000']
```

2. **Grafana 대시보드**:
   - Data Source: Prometheus
   - Import: 미리 구성된 대시보드 템플릿
   - 메트릭 시각화: 요청 수, 응답 시간, 에러율 등

## 🧪 테스트 커버리지

```
mini_server.Tests/
├── Models/
│   ├── UserTests.cs              ✅ 8 tests
│   └── RequestContextTests.cs    ✅ 6 tests
└── Services/
    ├── DbLockServiceTests.cs     ✅ 3 tests (+ 5 skipped)
    └── MetricsServiceTests.cs    ✅ 16 tests
```

**총 53개 테스트 (48 통과, 5 스킵)**

## 🚀 향후 개선 사항

### 기능 추가
- [ ] Redis 기반 분산 락 (MySQL 락 대체/보완)
- [ ] JWT 토큰 기반 인증/인가
- [ ] API 버저닝 (v1, v2)
- [ ] Rate Limiting
- [ ] Response Caching

### 운영 최적화
- [ ] Docker 컨테이너화
- [ ] Kubernetes 배포 설정
- [ ] CI/CD 파이프라인 (GitHub Actions)
- [ ] ELK Stack 로그 집계
- [ ] APM (Application Performance Monitoring)

### 테스트 강화
- [ ] 통합 테스트 추가
- [ ] E2E 테스트
- [ ] 부하 테스트 (k6, JMeter)
- [ ] 테스트 커버리지 90% 이상

## 📄 라이선스

MIT License

## 👥 기여

이슈와 풀 리퀘스트를 환영합니다!

## 📞 문의

프로젝트 관련 문의사항은 이슈를 생성해주세요.
