# LiteAPI 주요 기능

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

<br/><br/><br/>

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

<br/><br/><br/>

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
