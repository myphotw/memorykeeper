# PROJECT_CONTEXT.md

Memory Keeper 프로젝트 목적과 방향성.

---

## 프로젝트 목적

사진 기반 **추억 탐험** Windows 애플리케이션이다.

단순 파일 브라우저가 아니라, **시간 · 장소 · 여행 · 사진 · 검색**으로  
기억을 다시 발견하고 탐험하는 로컬 기억 플랫폼을 목표로 한다.

정보 구조(IA): `Docs/INFORMATION_ARCHITECTURE.md`  
표현·UX 원칙: `Docs/UX_PHILOSOPHY.md`  
디자인 시스템: `Docs/DESIGN_SYSTEM.md` (`Themes/DesignSystem.xaml`)

---

## 핵심 가치

- **사진 원본은 유지**한다. 원본 파일을 편집·덮어쓰지 않는다.
- **메타데이터와 DB**로 기억을 재구성한다. (촬영일, GPS, Place, Tag, Favorite 등)
- DB는 **관리 정보만** 보유한다. 사진 바이너리는 Storage(PhotoRoot) 쪽에 둔다.

---

## 주요 기능 흐름

```
사진등록 (Import)
  → Metadata 분석 (촬영일, GPS 등)
  → Place 연결 (자동/수동)
  → 방문지도 조회 (날짜·장소 기준, DB 테이블이 아닌 계산 뷰)
  → 태그 관리
  → Memory Search / Gallery
  → 여행기록 분석
```

보조 흐름:

- Pending Memory: 장소·날짜가 불완전한 사진 정리
- Photo Detail / Favorite: 단건 상세와 우선순위 신호
- Home: 다섯 탐색 방식의 출발점 (Dashboard가 아님). IA는 `INFORMATION_ARCHITECTURE.md`, UX는 `UX_PHILOSOPHY.md`.

---

## 기술 스택 (현재)

| 항목 | 내용 |
|------|------|
| 제품 버전 | **1.0.0** |
| UI | WinUI 3 / .NET 8 (`net8.0-windows10.0.19041.0`) |
| 아키텍처 | Domain / Application / Infrastructure / App (MVVM) |
| DB | SQLite + EF Core 8.0.29 (`%LocalAppData%\MemoryKeeper\MemoryKeeper.db`) |
| 실행 | Windows **x64** 전용, Self-contained Publish 지원 |
| 지도 | Google Maps API Key (설정, 선택) |
| 디자인 | `Themes/DesignSystem.xaml` (`Mk*` 토큰·스타일) |

솔루션 프로젝트:

- `MemoryKeeper.Domain`
- `MemoryKeeper.Application`
- `MemoryKeeper.Infrastructure`
- `MemoryKeeper.App`
- `MemoryKeeper.Tests`

---

## 장기 확장 방향

| 단계 | 방향 |
|------|------|
| **현재** | Windows Desktop Client (로컬 SQLite + Local/NAS PhotoRoot) |
| **향후** | NAS Storage 실사용 강화, Server API, `IFileAccessService` 서버 구현 교체 |
| **더 이후** | Mobile / Web (동일 다섯 탐색 축), AI 검색(검색 축 강화), 가족 공유 |

경로 추상화(`PhotoRoot` + `RelativePath` + `IFileAccessService`)는 NAS/Server 확장을 전제로 유지한다.  
화면·내비가 늘어도 **시간 · 장소 · 여행 · 사진 · 검색** 다섯 축 IA는 유지한다 (`INFORMATION_ARCHITECTURE.md`).

---

## 개발 철학

1. **사진 자체를 수정하지 않음** — Delete from library는 DB/라이브러리 경로 정리 수준이며 원본 보존을 전제로 한다.
2. **DB는 사진 관리 정보만 보유** — 바이너리 blob을 DB에 넣지 않는다.
3. **Storage 경로 추상화 유지** — UI/Service는 절대 경로를 하드코딩하지 않고 `IFileAccessService`로 resolve한다.
4. **UI와 Business Logic 분리** — View/ViewModel → Application Service → Repository Interface → Infrastructure.

---

## 관련 문서

| 문서 | 내용 |
|------|------|
| [CURRENT_STATUS.md](./CURRENT_STATUS.md) | 완료/미완료 상태 (V1.0.0) |
| [FEATURE_HISTORY.md](./FEATURE_HISTORY.md) | MK 단위 이력 |
| [ARCHITECTURE.md](./ARCHITECTURE.md) | 계층 구조 |
| [DATABASE_SCHEMA.md](./DATABASE_SCHEMA.md) | 테이블/Migration |
| [DESIGN_SYSTEM.md](./DESIGN_SYSTEM.md) | Mk* UI 토큰 |
| [UX_PHILOSOPHY.md](./UX_PHILOSOPHY.md) | 추억 탐험 UX |
| [INFORMATION_ARCHITECTURE.md](./INFORMATION_ARCHITECTURE.md) | 다섯 탐색 축 |
| [DEVELOPMENT_RULES.md](./DEVELOPMENT_RULES.md) | 개발 규칙 |
| [ENVIRONMENT_SETUP.md](./ENVIRONMENT_SETUP.md) | 빌드·Publish 환경 |
| [NEXT_STEP_GUIDE.md](./NEXT_STEP_GUIDE.md) | 다음 작업 가이드 |
