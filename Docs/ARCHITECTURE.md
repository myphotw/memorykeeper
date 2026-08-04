# ARCHITECTURE.md

현재 솔루션 아키텍처 (코드 기준).

제품 탐색 구조(IA)는 `Docs/INFORMATION_ARCHITECTURE.md`를 본다.  
이 문서는 **코드·레이어·확장 포트**만 다룬다.

---

## 프로젝트 구조

```
MemoryKeeper.sln
├── MemoryKeeper.Domain          # Entity, Enum, Domain interface
├── MemoryKeeper.Application     # DTO, Service, Interface, UseCase
├── MemoryKeeper.Infrastructure  # EF Core, Repository, File, External API
├── MemoryKeeper.App             # WinUI MVVM (View / ViewModel)
└── MemoryKeeper.Tests           # Unit tests
```

### MemoryKeeper.Domain

- Entity: `Media`, `Storage`, `Place`, `Setting`, `Tag`, `MediaTag`, `BaseEntity`
- Enum: `MediaType`, `MediaStatus`, `StorageType`, `TagSource`
- Domain interface 예: `IStorageProvider`

비즈니스 규칙의 핵심 모델은 Domain에 두고, UI/인프라에 의존하지 않는다.

### MemoryKeeper.Application

- DTO, Application Service, Repository/외부 포트 Interface
- UseCase (예: `GetLibraryUseCase`)
- DI: `AddApplicationServices()`

주요 서비스 예:

- Import / Storage / Place / Tag / PhotoDetail / Pending
- MemorySearch / VisitRecordQuery / HomeDashboard / TravelRecords
- SetupWizard, HomeLocation

### MemoryKeeper.Infrastructure

- EF Core `MemoryKeeperDbContext`, Migrations, Repositories
- File: Scanner, Hasher, Storage, `LocalFileAccessService`
- Metadata extractor, Google `ILocationResolver`
- `PrototypeMaintenanceService` (Backup/Restore/Reset)
- DI: `AddInfrastructureServices()`, `AddMemoryKeeperDatabase(path)`

### MemoryKeeper.App

- WinUI 3 Views / ViewModels (CommunityToolkit.Mvvm)
- Shell: `MainWindow`, NavigationView
- **Themes:** `Themes/DesignSystem.xaml` + `Tokens/` + `Styles/` (`Mk*` 디자인 시스템)
- App services: Thumbnail, Folder/File picker, Navigation state
- Maps: `Maps/Google/` (WebView2 지도 호스트)
- Host DI in `App.xaml.cs`
- Publish: `Properties/PublishProfiles/FolderProfile.pubxml` → `MemoryKeeper.Release\`

---

## 호출 흐름

```
UI (Page / XAML)
  ↓
ViewModel
  ↓
Application Service
  ↓
Repository Interface / Port Interface
  ↓
Infrastructure (EF, File IO, HTTP)
```

### 금지

| 금지 | 이유 |
|------|------|
| UI에서 DbContext / SQL 직접 접근 | 계층 붕괴, 테스트·교체 불가 |
| UI에서 파일 경로를 임의로 조합해 IO | PhotoRoot 변경·NAS/Server 대비 깨짐 → **`IFileAccessService` 사용** |
| Application이 WinUI/WinRT 참조 | 플랫폼 종속 |

### 권장

- 화면 상태·네비게이션 힌트: App의 `IPlaceFocusState`, `IPhotoNavigationState` 등
- 절대 경로 resolve: `IFileAccessService.ResolveAbsolutePath(photoRoot, relativePath)`
- 검색 Analyzer 교체: `IMemorySearchAnalyzer`만 교체

---

## 데이터 / 파일 위치

| 리소스 | 위치 |
|--------|------|
| SQLite DB | `%LocalAppData%\MemoryKeeper\MemoryKeeper.db` |
| Thumbnail cache | `%LocalAppData%\MemoryKeeper\ThumbnailCache` |
| Startup log | `%LocalAppData%\MemoryKeeper\Logs\startup.log` |
| Map HTML cache | `%LocalAppData%\MemoryKeeper\map-host` |
| Photo library | `TB_STORAGE.PhotoRoot` + `TB_MEDIA.RelativePath` |
| Publish 산출물 | 리포지토리 `MemoryKeeper.Release\` |

---

## 확장 포인트

| 포트 | 현재 구현 | 향후 |
|------|-----------|------|
| `IFileAccessService` | `LocalFileAccessService` | Server/NAS API 구현으로 DI 교체 |
| `IMemorySearchAnalyzer` | `RuleBasedMemorySearchAnalyzer` | AI Analyzer |
| `ILocationResolver` | `GoogleLocationResolver` | 다른 Geocoder |
| `IStorageProvider` | `LocalStorageProvider` | 추가 Provider |

---

## Visit Record 모델링 주의

- **엔티티/테이블 `VisitRecord` / `TB_VISIT_RECORD` 없음**
- Application의 Visit Record = Place별 Media 날짜 집계 + DTO
- 향후 실체화 시 Domain Entity + Migration 추가가 필요 (현 시점 미구현)
