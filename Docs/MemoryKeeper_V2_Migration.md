# MemoryKeeper V2 Migration Design

| 항목 | 내용 |
|------|------|
| 문서 버전 | 0.1 (설계 전용) |
| 작성일 | 2026-08-04 |
| 대상 | MemoryKeeper **Version 2** |
| Backend | **TC-Backend Version 1.0.0 Freeze** (`D:\999. etc\tc-backend`) |
| 이번 단계 | 분석 · 설계 · Repository 교체 **준비** (구현 없음) |
| 비고 | 기존 UI / MVVM / 기능 **유지**. Repository 교체·API 연결·기능 삭제는 **후속 단계** |

---

## 0. 목표와 범위

### 목표

MemoryKeeper를 로컬 SQLite 중심 구조에서 **TC-Backend V1.0 Common API** 기반 클라이언트로 전환한다.

### 이번 단계 (본 문서)

- 현재 MVVM / SQLite / 사진 처리 구조 분석
- SQLite → TC-Backend API 대응표
- 신규 ApiRepository / ApiClient / Config / Upload 흐름 **설계**
- 삭제 예정 · 유지 기능 목록

### 이번 단계에서 하지 않음

- Repository 실제 교체 / DI 전환
- UI 수정
- HTTP API 연결·호출 코드
- 기존 기능·코드 삭제
- Metadata / Thumbnail / Preview / Worker 이관 구현

---

## 1. 현재 구조 분석

### 1.1 솔루션 레이어

```
View (WinUI Page/Window)
  → ViewModel (CommunityToolkit.Mvvm)
      → Application Service
          → I*Repository / IMetadataExtractor / IFile* / ILocationResolver
              → Infrastructure (EF Core → SQLite MemoryKeeper.db)
              → Local filesystem (PhotoRoot library + ThumbnailCache)
              → (선택) Google Geocoding HTTP
```

| 프로젝트 | 역할 |
|----------|------|
| `MemoryKeeper.Domain` | Entity, Enum, `IStorageProvider` |
| `MemoryKeeper.Application` | DTO, Service, `I*Repository` 등 Port |
| `MemoryKeeper.Infrastructure` | EF/SQLite, Repository 구현, Import/Metadata/File |
| `MemoryKeeper.App` | Views, ViewModels, Thumbnail, Maps UI |
| `MemoryKeeper.Tests` | Unit (InMemory / SQLite `:memory:`) |

### 1.2 View (`MemoryKeeper.App/Views`)

| View | 역할 | 주요 ViewModel |
|------|------|----------------|
| `MainWindow` | Shell + NavigationView | `MainViewModel` |
| `HomePage` | 홈 / Hero / 통계 요약 | `HomeViewModel` |
| `GalleryPage` | 연도·국가·도시·장소 계층 갤러리 | `GalleryViewModel` |
| `FavoritesPage` | 즐겨찾기 | `FavoritesViewModel` |
| `PhotoDetailPage` | 상세·태그·메모·장소 | `PhotoDetailViewModel` |
| `PhotoViewerPage` | 원본 뷰어 | `PhotoViewerViewModel` |
| `ImportPage` | 폴더 Import | `ImportViewModel` |
| `PendingMemoryPage` | 미배정 추억 | `PendingMemoryViewModel` |
| `PlaceMapPage` / `VisitRecordPage` | 방문 지도·기록 | `PlaceMapViewModel` / `VisitRecordViewModel` |
| `TimelinePage` | 타임라인 검색 UI | `TimelineViewModel` |
| `TravelRecordsPage` (+ Detail) | 여행 기록 | `TravelRecordsViewModel` |
| `TagManagementPage` | 태그 관리 | `TagManagementViewModel` |
| `StorageManagementPage` | 스토리지 루트 | `StorageManagementViewModel` |
| `SettingsPage` / `SetupWizardPage` | 설정·초기 설정 | `SettingsViewModel` / `SetupWizardViewModel` |
| `PlaceManagementPage` | 장소 CRUD | `PlaceManagementViewModel` |

관련: `Maps/Google/*`, `Dialogs/*`, `Diagnostics/ErrorDialogWindow`.

### 1.3 ViewModel → Application 의존 (요약)

| ViewModel | Application / App 의존 |
|-----------|-------------------------|
| `HomeViewModel` | `HomeDashboardService`, `IThumbnailService` |
| `GalleryViewModel` | `GalleryHierarchyService`, `MediaService`, `IThumbnailService` |
| `FavoritesViewModel` | `MediaService`, `IThumbnailService` |
| `PhotoDetailViewModel` | `PhotoDetailService`, Place/Tag services, `IMetadataExtractor`, `ISettingRepository` |
| `ImportViewModel` | `MediaImportService` (scoped) |
| `VisitRecordViewModel` | `VisitRecordQueryService`, `MemorySearchService` |
| `TimelineViewModel` | `MemorySearchService` |
| `TravelRecordsViewModel` | `TravelRecordsService` |
| `SettingsViewModel` | `ISettingRepository`, maintenance / integrity |

### 1.4 Repository (SQLite / EF)

**인터페이스:** `MemoryKeeper.Application.Interfaces`  
**구현:** `MemoryKeeper.Infrastructure.Repositories`

| Interface | Concrete | 주요 테이블 |
|-----------|----------|-------------|
| `IMediaRepository` | `MediaRepository` | `TB_MEDIA` |
| `IPlaceRepository` | `PlaceRepository` | `TB_PLACE` |
| `IStorageRepository` | `StorageRepository` | `TB_STORAGE` |
| `ISettingRepository` | `SettingRepository` | `TB_SETTING` |
| `ITagRepository` | `TagRepository` | `TB_TAG` |
| `IMediaTagRepository` | `MediaTagRepository` | `TB_MEDIA_TAG` |
| `IDashboardRepository` | `DashboardRepository` | Media/Place/Tag aggregate |
| `ITravelRecordsRepository` | `TravelRecordsRepository` | Place + Media aggregate |

DB: `%LocalAppData%\MemoryKeeper\MemoryKeeper.db`  
`MemoryKeeperDbContext` + `DatabaseInitializer.MigrateAsync`.

### 1.5 Service (Application)

Import · Gallery · PhotoDetail · Place* · Tag · MemorySearch · VisitRecord* · HomeDashboard · TravelRecords · SetupWizard · Library path sync · Integrity 등.  
DI: `AddApplicationServices()` / `AddMemoryKeeperDatabase()`.

### 1.6 Model

| 계층 | 위치 | 예 |
|------|------|-----|
| Domain Entity | `Domain/Entities` | `Media`, `Place`, `Storage`, `Setting`, `Tag`, `MediaTag` |
| Application DTO | `Application/DTOs` | `MediaDto`, `GalleryMediaDto`, `PhotoDetailDto`, Search/Travel/Visit DTOs |
| App UI Model | `App/Models` | `GalleryItem`, `VisitRecordModels`, `TravelRecordsModels` |

### 1.7 사진 처리 · Metadata · EXIF · Thumbnail · Preview · Watcher

| 관심사 | 위치 | 역할 | 사용처 |
|--------|------|------|--------|
| **Import** | `MediaImportService` | Scan → Metadata → Hash → Copy → Place → DB | `ImportViewModel` |
| **File scan** | `FileScanner` | 폴더 이미지 수집 | Import |
| **Hash** | `FileHasher` | SHA-256 중복 검사 | Import |
| **Metadata** | `MetadataExtractorService` | EXIF → `MediaMetadataDto` | Import, PhotoDetail(디버그) |
| **EXIF** | `ExifReader` (+ MetadataExtractor NuGet) | 카메라·날짜·GPS 태그 | Metadata |
| **GPS** | `GpsParser`, `CoordinateConverter` | DMS/Decimal | EXIF |
| **Date** | `DateResolver` | 촬영일 우선순위 | EXIF |
| **Storage 복사** | `FileStorageService` / `LocalStorageProvider` | PhotoRoot 하위 복사·이동 | Import, path sync |
| **경로 해석** | `LocalFileAccessService` | Absolute = Root + Relative | 전역 |
| **Thumbnail** | `App/Services/ThumbnailService` | ImageSharp → `%LocalAppData%\MemoryKeeper\ThumbnailCache` | Gallery/Home/Favorites/… |
| **Preview (원본)** | 별도 Preview 서비스 **없음** | `BitmapImage` ← AbsoluteLibraryPath | PhotoViewer / Detail |
| **Place 자동 배정** | `PlaceAssignmentService` + `GoogleLocationResolver` | GPS → Place | Import |
| **Watcher** | — | **미구현** (FileSystemWatcher 없음) | — |
| **AI Tag** | `TagSource.Ai` enum만 | 생성 파이프라인 **없음** | 예약 |

**현재 Import 파이프라인**

```
Import UI
  → MediaImportService
      → FileScanner
      → MetadataExtractor (EXIF/GPS/Date)
      → FileHasher
      → FileStorageService (라이브러리 복사)
      → PlaceAssignmentService
      → IMediaRepository.AddAsync (SQLite)
      → MediaLibraryPathSyncService (year/place 경로)
  → (표시 시) ThumbnailService 온디맨드 생성
```

---

## 2. SQLite 의존성 분석

모든 영속화는 EF Core → SQLite. 아래는 Repository·직접 DB 조작 기준.

### 2.1 `MediaRepository` / `IMediaRepository`

| 메서드 | 기능 | 질의 요약 |
|--------|------|-----------|
| `GetByIdAsync` / `GetByIdsAsync` | 단건·다건 | `TB_MEDIA` PK |
| `GetByContentHashAsync` | Import 중복 | ContentHash |
| `GetAllAsync` | 전체 | AsNoTracking |
| `GetByPlace*` / `GetByYearAsync` | 장소·연도 | PlaceId / 연도(일부 클라이언트 필터) |
| `GetWithGpsAsync` | 지도용 GPS | Lat/Lon not null |
| `GetUnassignedAsync` | Pending | Status/PlaceId null |
| `SearchAsync` | 검색 | year/placeIds |
| `GetPhotoDetailAsync` | 상세 | Include Place, Storage |
| `UpdateFavoriteAsync` | 즐겨찾기 | IsFavorite |
| `GetRelatedPhotosAsync` | 같은 장소 | PlaceId |
| `GetFavoritesAsync` | 즐겨찾기 목록 | IsFavorite |
| `Add/Update/DeleteAsync` | CRUD | Insert/Update/Delete |

**기능 영역:** Gallery, Favorites, Pending, Import, Detail, Map, Search.

### 2.2 `PlaceRepository` / `IPlaceRepository`

| 메서드 | 기능 |
|--------|------|
| Get/Search/CRUD | 장소 관리, 지도, Import 배정, 여행, 검색 (`Like` DisplayName/City/Country) |

### 2.3 `StorageRepository` / `IStorageRepository`

PhotoRoot 등 스토리지 CRUD. Import·경로·Storage UI.

### 2.4 `SettingRepository` / `ISettingRepository`

키-값 설정 (API Key, Home location, 최근 검색 등). Setup / Settings / Map.

### 2.5 `TagRepository` / `ITagRepository`

태그 CRUD·인기·검색. Tag UI, Search analyzer, Travel chips.

### 2.6 `MediaTagRepository` / `IMediaTagRepository`

미디어-태그 링크, AND 필터용 MediaId 집합. Gallery tag filter, Search, Travel.

### 2.7 `DashboardRepository` / `IDashboardRepository`

| 메서드 | 기능 |
|--------|------|
| `GetOnThisDayPhotosAsync` | 홈 “이날의 사진” |
| `GetRecentImportsAsync` | 최근 Import |
| `GetFavoritePhotosAsync` | 홈 즐겨찾기 샘플 |
| `GetStatisticsAsync` | Photo/Place/Favorite/Tag/Visit 카운트 |
| `GetPendingBreakdownAsync` | Pending 분해 |

### 2.8 `TravelRecordsRepository` / `ITravelRecordsRepository`

`GetPlaceAggregatesAsync` — 활성 Place + 사진 Media 집계, 대표 경로 해석. Travel Records UI.

### 2.9 SQLite 직접 (Repository 외)

| 클래스 | 역할 |
|--------|------|
| `DatabaseInitializer` | Migrate + 건수 확인 |
| `RelativePathDataMigrator` | RelativePath 일회 정규화 |
| `PrototypeMaintenanceService` | DB 삭제·백업·복원·재 Migrate |
| `MemoryKeeperDbContextFactory` | Design-time |

### 2.10 SQLite를 쓰지 않는 Application 로직

- `VisitRecordService` — 순수 날짜 집계
- `MemoryGroupingService` — 메모리 그룹핑

---

## 3. API 대응표 (SQLite / App → TC-Backend V1.0)

**Base URL (설계 기본값):** `http://localhost:8000`  
**참고 문서:** `tc-backend/docs/API_REFERENCE.md` (v1.0.0 Freeze)

공통 Query 권장: `service_name=MemoryKeeper`

### 3.1 Gallery / Media 조회

| MemoryKeeper (현재) | TC-Backend V1.0 | 비고 |
|---------------------|-----------------|------|
| `IMediaRepository.GetAllAsync` / `MediaService` 목록 | `GET /api/common/gallery` | page, page_size, sort |
| `IMediaRepository.GetPhotoDetailAsync` / `PhotoDetailService` | `GET /api/common/gallery/{file_id}` | metadata, tags, URLs |
| `IMediaRepository.SearchAsync` / `MemorySearchService` | `GET /api/common/gallery/search` | year/country/city/tag/favorite/keyword/… |
| `IMediaRepository.GetWithGpsAsync` / Map·Visit | `GET /api/common/gallery/map` | year, service_name |
| `TimelineViewModel` / 연도 그룹 | `GET /api/common/gallery/timeline` | 연도별 건수 |
| `IDashboardRepository.GetStatisticsAsync` / Home 통계 | `GET /api/common/gallery/statistics` | 스키마 차이 → Adapter 필요 |
| `IMediaRepository.GetFavoritesAsync` / Favorites | `GET /api/common/gallery/search?favorite=true` | 전용 즐겨찾기 API 없음 |
| `IMediaRepository.GetByYearAsync` | `GET /api/common/gallery/search?year=` | |
| Gallery hierarchy (year/country/city/place) | `search` + `timeline` + client group **또는** 후속 API | V1.0에 계층 API 없음 → UI 유지 시 클라이언트 조합 |
| `ITravelRecordsRepository.GetPlaceAggregatesAsync` | `map` + `search` + `statistics` 조합 | Travel 전용 API 없음 → Adapter |
| Visit records (장소별·연도) | `map` / `search` 조합 | 동일 |

### 3.2 Upload / Import

| MemoryKeeper (현재) | TC-Backend V1.0 | 비고 |
|---------------------|-----------------|------|
| `MediaImportService.ImportFileAsync` 전체 로컬 파이프라인 | `POST /api/common/upload` | multipart `file` → UploadJob |
| (후처리: EXIF/Thumb/Preview/Storage) | **UploadWorker + Plugins** (서버) | 클라이언트 금지 |
| `IMediaRepository.GetByContentHashAsync` | Worker `HashPlugin` (서버) | 클라 중복 검사 제거 방향 |
| Import 완료 후 Gallery 반영 | Worker 완료 후 `GET /gallery*` | Job 폴링은 V1.0에 공개 Job GET 없음 → 후속 또는 gallery refresh |

**Upload 응답 (V1.0):** `id`, `job_id`, `status=WAITING`, `incoming_path`

### 3.3 System / Monitoring / Keys

| 용도 | TC-Backend |
|------|------------|
| 연결 확인 | `GET /health`, `GET /api/common/health` |
| 서버·워커 대시보드 | `GET /api/common/dashboard` |
| 서비스 정보 | `GET /` |
| API Key 관리 (서버측) | `GET\|POST\|DELETE /api/common/api-keys/` |
| 로컬 `ISettingRepository` (UI 설정) | **유지 후보** 또는 일부만 API Keys로 이전 | Google Key 등은 서버 `.env` / api-keys |

### 3.4 대응 불가 · Gap (V1.0 Freeze 범위 밖 → 후속 버전 또는 Adapter)

| MemoryKeeper 기능 | Gap |
|-------------------|-----|
| Place CRUD (`IPlaceRepository`, PlaceManagement) | Common Gallery는 place_name 메타 필드; Place 엔티티 API 없음 |
| Tag CRUD / MediaTag 수동 부여 | Detail에 `user_tags`/`ai_tags` 조회만; 쓰기 API 없음 |
| `UpdateFavoriteAsync` | favorite **읽기** 필터만; 쓰기 API 없음 |
| Media Delete / Memo 갱신 | 쓰기 API 없음 |
| Pending / Place 수동 배정 | 클라이언트 Place 모델 의존 → 서버 Place 정책 필요 |
| Storage PhotoRoot 관리 | 서버 `PHOTO_PLATFORM_ROOT` |
| DB Backup/Reset (`PrototypeMaintenanceService`) | 서버/운영 영역 |
| OnThisDay / RecentImports 전용 | statistics + search로 근사 또는 후속 API |
| Album (사용자 앨범 엔티티) | **현재 MK에도 전용 Album 테이블 없음**; Travel/Place 기반. V2도 Gallery/search로 표현 |

> **원칙:** V1.0에 없는 쓰기는 구현하지 않고, 대응표에 Gap으로 남긴다. UI는 유지하되 후속 Backend 버전 또는 읽기 전용 축소 정책을 별도 결정한다.

### 3.5 메서드 단위 예시 매핑

```
PhotoRepository/MediaRepository.GetPhotos()     → GET  /api/common/gallery
MediaRepository.Search / MemorySearchService    → GET  /api/common/gallery/search
DashboardRepository.GetStatisticsAsync          → GET  /api/common/gallery/statistics
MediaRepository.GetWithGpsAsync (Map)           → GET  /api/common/gallery/map
Timeline (연도)                                 → GET  /api/common/gallery/timeline
MediaRepository.GetPhotoDetailAsync             → GET  /api/common/gallery/{file_id}
Favorites (목록)                                → GET  /api/common/gallery/search?favorite=true
MediaImportService (파일 1건)                   → POST /api/common/upload
```

---

## 4. Repository 교체 설계

### 4.1 원칙

- 기존 `Infrastructure/Repositories/*` **삭제하지 않음**
- 클래스/인터페이스에 **`[Obsolete("V2: use *ApiRepository")]`** 또는 문서 Deprecated 표시만 (구현 단계에서 적용)
- ViewModel은 가급적 **기존 Application Service** 유지 → Service 내부 Port만 API로 교체
- 신규 API Repository는 **TC-Backend DTO ↔ 기존 Application DTO** 어댑팅

### 4.2 신규 구조 (설계 위치)

```
MemoryKeeper.Infrastructure/
  Repositories/                    # 기존 SQLite — Deprecated 예정 (삭제 금지)
  ApiRepositories/                 # 신규 (후속 구현)
    GalleryApiRepository.cs
    UploadApiRepository.cs
    StatisticsApiRepository.cs
    MapApiRepository.cs
    TimelineApiRepository.cs
```

사용자 요청 명칭과 대응:

| 신규 클래스 | TC-Backend | 대체 대상 (주) |
|-------------|------------|----------------|
| `GalleryApiRepository` | `/gallery`, `/gallery/{id}`, `/gallery/search` | `MediaRepository` 조회, Search 일부 |
| `UploadApiRepository` | `POST /upload` | `MediaImportService` 영속·파일 파이프라인 |
| `StatisticsApiRepository` | `/gallery/statistics` | `DashboardRepository` 통계 |
| `MapApiRepository` | `/gallery/map` | `GetWithGpsAsync` / Visit map 데이터 |
| `TimelineApiRepository` | `/gallery/timeline` | Timeline 연도 집계 |

선택적 후속:

| 클래스 | 용도 |
|--------|------|
| `MonitoringApiRepository` | `/api/common/health`, `/dashboard` |
| `ApiKeysApiRepository` | `/api/common/api-keys/` |

### 4.3 인터페이스 전략 (후속)

옵션 A (권장): 기존 `IMediaRepository` 등을 유지하고 **API 구현체를 같은 인터페이스에 바인딩** (시그니처 불일치 시 Adapter).  
옵션 B: `IGalleryQueryPort`, `IUploadPort` 등 신규 Port 도입 후 Service만 수정.

본 단계에서는 옵션만 고정하고 코드 변경 없음.

### 4.4 Deprecated 대상 (표시만, 삭제 금지)

- `MediaRepository`, `PlaceRepository`, `StorageRepository`, `SettingRepository`(일부), `TagRepository`, `MediaTagRepository`, `DashboardRepository`, `TravelRecordsRepository`
- `MemoryKeeperDbContext` 직접 사용 경로 (Init/Maintenance 포함) — V2에서 로컬 DB 역할 축소 시

---

## 5. API Client 설계

### 5.1 위치 (후속 생성)

```
MemoryKeeper.Infrastructure/
  Services/
    ApiClient/
      IApiClient.cs              # 또는 IBaseApiClient
      BaseApiClient.cs           # GET/POST/DELETE, JSON, Retry, Timeout
      ApiClientOptions.cs        # Options 바인딩
      ApiException.cs
      # 선택: GalleryApiClient, UploadApiClient (얇은 래퍼)
```

App 계층이 아닌 **Infrastructure**에 두는 이유: 외부 I/O.  
요청 문서의 `Services/ApiClient/`는 Infrastructure 하위로 해석.

### 5.2 책임

| 항목 | 설계 |
|------|------|
| HttpClient | **Singleton** (`IHttpClientFactory` named client `TcBackend` 권장) |
| Base URL | `ApiBaseUrl` |
| GET / POST / DELETE | 공통 메서드 + multipart Upload |
| Timeout | `TimeoutSeconds` |
| Retry | 지수 백오프, `RetryCount` (멱등 GET 우선; POST upload는 정책 별도) |
| JSON | `System.Text.Json` Deserialize |
| 오류 | HTTP status → `ApiException`; 본문 로그 |

### 5.3 이번 단계

**클래스 파일 생성·연결하지 않음.** 구조만 본 문서에 고정.

---

## 6. Config 설계

### 6.1 `appsettings.json` 추가 키 (설계)

```json
{
  "MemoryKeeper": {
    "AppName": "Memory Keeper",
    "Channel": "Prototype"
  },
  "TcBackend": {
    "ApiBaseUrl": "http://localhost:8000",
    "TimeoutSeconds": 30,
    "RetryCount": 3,
    "Version": "1.0.0",
    "ServerName": "TC-Backend",
    "ServiceName": "MemoryKeeper"
  }
}
```

| Key | 의미 | TC-Backend 대응 |
|-----|------|-----------------|
| `ApiBaseUrl` | API 루트 | uvicorn `:8000` |
| `TimeoutSeconds` | HttpClient Timeout | `API_CLIENT_TIMEOUT`과 맞춤 |
| `RetryCount` | 재시도 | `API_CLIENT_RETRY_COUNT` |
| `Version` | 기대 서버 버전 | `/health`의 version 검증용 |
| `ServerName` | 표시용 | UI/로그 |
| `ServiceName` | gallery query `service_name` | `MemoryKeeper` |

### 6.2 이번 단계

**appsettings 변경·Options 바인딩 코드 추가하지 않음.** 키 스키마만 확정.

---

## 7. Upload 구조 변경 설계

### 7.1 As-Is

```
Import
  → SQLite (Media row)
  → Metadata (EXIF)
  → Thumbnail (클라 캐시)
  → Preview (원본 파일 직접)
  → Gallery (로컬 쿼리)
```

### 7.2 To-Be (V2)

```
Import (파일 선택만)
  → POST /api/common/upload
  → UploadJob (WAITING)
  → TC-Backend UploadWorker
       Hash → Preview → Storage → Metadata → Exif → Gps → Vision Queue
  → (VisionWorker: AI Tags)
  → Gallery API (list/detail/search/…)
```

### 7.3 MemoryKeeper V2에서 **하지 않는 것**

| 금지 | 현재 위치 |
|------|-----------|
| Metadata 생성 | `MetadataExtractorService` / EXIF |
| Preview 생성 | (원본 로컬 처리; 서버 PreviewPlugin으로 이전) |
| Thumbnail 생성 | `ThumbnailService` → 서버 `thumbnail_url` 사용 |
| Storage 이동·규칙 | `FileStorageService`, Storage Rule (서버) |
| Hash 중복 파이프라인 | `FileHasher` (서버 HashPlugin) |
| GPS/Geocode Import 시 | `PlaceAssignmentService` + Google (서버 GpsPlugin) |
| AI Tag 생성 | (현재 없음; 서버 VisionPlugin) |
| Worker / Watcher | 서버·`watcher` 프로세스 |

클라 Import UI는 **파일 전송 + 진행/완료 피드백(후속)** 만 담당.

### 7.4 표시 경로 변경 (설계)

| 현재 | V2 |
|------|-----|
| `ThumbnailService` 로컬 JPEG | API `thumbnail_url` / `preview_url` |
| AbsoluteLibraryPath Bitmap | API `original` URL (Detail 스키마) |

---

## 8. 삭제 예정 기능 (목록만 — 삭제 금지)

후속 단계에서 제거·비활성 후보. **본 단계에서는 코드 삭제하지 않음.**

| 영역 | 대상 (경로/클래스) | 이유 |
|------|-------------------|------|
| SQLite 영속 | `MemoryKeeperDbContext`, EF Configurations, Migrations (앱 런타임) | Backend DB로 이전 |
| SQLite Repos | `Infrastructure/Repositories/*` (Deprecated 후 제거) | ApiRepository 대체 |
| Metadata/EXIF | `MetadataExtractorService`, `ExifReader`, `GpsParser`, `DateResolver`, `CoordinateConverter` | Worker Plugins |
| Hash | `FileHasher` | HashPlugin |
| 로컬 Storage 파이프 | `FileStorageService` Import 경로, `MediaLibraryPathSyncService` | StoragePlugin + Rule |
| Thumbnail | `ThumbnailService` + ThumbnailCache | 서버 thumb URL |
| Place 자동 배정(Import) | `PlaceAssignmentService` (Import 경로) | GpsPlugin |
| AI Tag (예약) | 클라 AI 생성 (현재 없음) | VisionPlugin |
| Maintenance DB | `PrototypeMaintenanceService` 로컬 DB reset | 서버 운영 |
| Watcher | (미구현) 클라 Watcher 도입 금지 | `tc-backend/watcher` |
| Worker | 클라 백그라운드 처리 금지 | UploadWorker / VisionWorker |

**조건부 유지/축소:** `IPlaceRepository` UI, Tag 쓰기, Favorite 쓰기 — **V1.0 API Gap**이 해소되기 전까지는 “삭제”가 아니라 **비활성 또는 후속 API 대기**.

---

## 9. 유지 기능

| 기능 | UI | 데이터 소스 (V2 목표) |
|------|-----|----------------------|
| 메인(홈) 화면 | `HomePage` | statistics + search/list Adapter |
| 갤러리 | `GalleryPage` | Gallery + Search API |
| 지도 | `VisitRecordPage` / Map | Map API |
| 검색 | Timeline/Search UI | Search API |
| 타임라인 | `TimelinePage` | Timeline API |
| 통계 | Home / Travel 요약 | Statistics API |
| 즐겨찾기 | `FavoritesPage` | Search `favorite=true` (쓰기 Gap) |
| 앨범에 준하는 묶음 | Travel / Place 기반 UI | search/map 조합 (전용 Album API 없음) |
| MVVM · Navigation · Design System | 유지 | — |
| Import **진입 UI** | `ImportPage` | Upload API만 호출하도록 후속 변경 |

**UI는 최대한 수정하지 않는다.** 바인딩 데이터 출처만 Service/Repository 뒤에서 교체.

---

## 10. 신규 구조 요약

```
[App] Views / ViewModels / Styles     ← 유지
[Application] Services / DTOs / Ports ← 유지 (내부 구현 교체)
[Infrastructure]
  ApiClient/          ← 신규 설계
  ApiRepositories/    ← 신규 설계
  Repositories/       ← Deprecated (삭제 금지)
  Metadata/File/...   ← 삭제 예정 목록
[Config] TcBackend.*  ← 설계 키
[TC-Backend :8000]    ← Upload + Gallery + Monitoring
```

---

## 11. 후속 단계 제안 (구현 순서)

1. Config + `BaseApiClient` 골격 (연결 스모크: `/health`)
2. `GalleryApiRepository` 읽기 전용 → Home/Gallery/Map/Timeline/Statistics 순 전환
3. `UploadApiRepository` → Import UI를 Upload만 하도록 축소
4. Thumbnail/Preview URL 바인딩으로 `ThumbnailService` 우회
5. Gap(Favorite/Tag/Place 쓰기) → Backend 차기 버전 협의
6. SQLite Repository Obsolete → 테스트 더블을 API Fake로 교체 → 로컬 DB 제거

---

## 12. 참고 경로

| 구분 | 경로 |
|------|------|
| MemoryKeeper | `D:\999. etc\MemoryKeeper` |
| TC-Backend | `D:\999. etc\tc-backend` |
| API Reference | `tc-backend/docs/API_REFERENCE.md` |
| Worker/Plugin | `tc-backend/docs/WORKER_GUIDE.md`, `PLUGIN_GUIDE.md` |
| 기존 MK 아키텍처 | `Docs/ARCHITECTURE.md` |

---

## 13. 문서 이력

| 날짜 | 내용 |
|------|------|
| 2026-08-04 | V2 전환 설계 초안 (분석·대응표·구조만, 구현 없음) |
