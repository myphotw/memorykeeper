# FEATURE_HISTORY.md

MK 단위 개발 이력. DB/Migration은 리포지토리 실제 파일 기준.

---

## 기초 (MK 번호 이전 / 초기)

| 항목 | 내용 |
|------|------|
| **기능** | SQLite + EF Core 기반, Storage/Media/Setting 골격 |
| **주요 변경** | 초기 테이블 생성, TB_ 네이밍, Place 엔티티 추가 |
| **DB 변경** | 예 |
| **Migration** | `20260723065546_InitialCreate`<br>`20260723070232_RenameTablesToTbConvention`<br>`20260723071938_AddPlaceEntity` |
| **테스트** | Import/Storage/Place 등 단위 테스트 존재 |

---

## MK-033 — Pending Memory

| 항목 | 내용 |
|------|------|
| **기능명** | 미완성 추억 (Pending Memory) |
| **주요 변경** | Place 미할당·Pending 상태 미디어 조회/그룹화, Pending UI |
| **DB 변경** | 아니오 (기존 `MediaStatus` / `PlaceId` 활용) |
| **Migration** | — |
| **테스트** | `PendingMemoryServiceTests` |

---

## MK-034 — Photo Detail + Favorite

| 항목 | 내용 |
|------|------|
| **기능명** | Photo Detail, Favorite, Related Photos |
| **주요 변경** | 상세 화면, 즐겨찾기 토글, 동일 Place Related |
| **DB 변경** | 예 — `IsFavorite` |
| **Migration** | `20260724010000_AddMediaFavorite` |
| **테스트** | `PhotoDetailServiceTests` |

---

## MK-035 — Photo Tag System

| 항목 | 내용 |
|------|------|
| **기능명** | 사진 태그 |
| **주요 변경** | `TB_TAG`, `TB_MEDIA_TAG`, TagService, Gallery/Detail 태그 UI |
| **DB 변경** | 예 |
| **Migration** | `20260724020000_AddPhotoTagSystem` |
| **테스트** | `TagServiceTests` |

---

## MK-035A — Tag UX 개선

| 항목 | 내용 |
|------|------|
| **기능명** | Pinned / Recent / Common Tag |
| **주요 변경** | `IsPinned`, Recent Tag Ids 설정, 태그 피커 UX |
| **DB 변경** | 예 — `TB_TAG.IsPinned` |
| **Migration** | `20260724030000_AddPinnedTag` |
| **테스트** | Tag 관련 단위 테스트 유지 |

---

## MK-036 — Memory Search 2.0

| 항목 | 내용 |
|------|------|
| **기능명** | 자연어/칩 기반 Memory Search |
| **주요 변경** | `MemorySearchService`, Chip, `IMemorySearchAnalyzer` / `RuleBasedMemorySearchAnalyzer` |
| **DB 변경** | 아니오 (검색 조건은 기존 Media/Place/Tag 활용). Recent queries는 Setting |
| **Migration** | — |
| **테스트** | `MemorySearchServiceTests`, `MemoryGroupingServiceTests` |

---

## MK-037 — 방문지도 통합 (Timeline + Map, 구 방문기록)

| 항목 | 내용 |
|------|------|
| **기능명** | Visit Record 통합 화면 |
| **주요 변경** | Timeline+Map, Marker, Preview Strip, `VisitRecordQueryService` |
| **DB 변경** | 아니오 — **TB_VISIT_RECORD 없음**, Media/Place 집계 |
| **Migration** | — |
| **테스트** | `VisitRecordQueryServiceTests` |

---

## MK-038 — Home Dashboard

| 항목 | 내용 |
|------|------|
| **기능명** | Home Dashboard |
| **주요 변경** | Hero carousel, 오늘/최근/즐겨찾기/Pending/Import 요약, 기본 랜딩 Home |
| **DB 변경** | 아니오 (`IDashboardRepository` 쿼리) |
| **Migration** | — |
| **테스트** | `HomeDashboardServiceTests` |

---

## MK-039 — 여행기록 (구 나의 여행기록)

| 항목 | 내용 |
|------|------|
| **기능명** | My Travel Records |
| **주요 변경** | 6종 탐색 카드 + Detail TOP20, Home Location, Visit Record 필터 연동 |
| **DB 변경** | 아니오 (Home Location은 `TB_SETTING`) |
| **Migration** | — |
| **테스트** | `TravelRecordsServiceTests` |

---

## MK-040 — Storage 경로 추상화

| 항목 | 내용 |
|------|------|
| **기능명** | RelativePath + PhotoRoot + FileAccess |
| **주요 변경** | 컬럼 rename, `IFileAccessService`/`LocalFileAccessService`, Root 재선택 시 RelativePath 유지 |
| **DB 변경** | 예 |
| **Migration** | `20260724110000_RenameLibraryPathAndPhotoRoot` |
| **테스트** | Storage/Import/FileAccess 관련 테스트; App x64 빌드·28 tests 통과 이력 |

---

## MK-041 — Prototype Release Preparation

| 항목 | 내용 |
|------|------|
| **기능명** | Publish / Setup Wizard / Backup / 내비 정리 |
| **주요 변경** | win-x64 self-contained, SetupWizard, Favorites/Settings, PrototypeMaintenance(Backup·Restore·Reset), 일반/관리 메뉴 |
| **DB 변경** | 아니오 (`App:SetupCompleted` 등 Setting 키 추가) |
| **Migration** | — |
| **테스트** | Unit **30/30** 통과. Publish exe MainWindow **기동 확인됨** (MK-041B) |

### MK-041 후속 진단

| 항목 | 내용 |
|------|------|
| **기능명** | Startup 진단 (로그 + MessageBox) |
| **주요 변경** | `StartupDiagnostics` → `startup.log`, MK-041B 단계별 MessageBox |
| **DB 변경** | 아니오 |
| **Migration** | — |
| **테스트** | 수동 실행 확인용. MessageBox는 진단 후 제거 예정 |

### MK-041B — Startup 안정화 (완료)

| 항목 | 내용 |
|------|------|
| **기능명** | Publish exe MainWindow 기동 수정 |
| **주요 변경** | EF Migration 기동 적용, DI bootstrap, MK-041B 단계 MessageBox **제거**, 오류 시에만 MessageBox |
| **DB 변경** | 아니오 |
| **Migration** | — |
| **테스트** | Release exe 수동 기동 확인 |

---

## MK-042 — MemoryKeeper 저장소 UI 단순화

| 항목 | 내용 |
|------|------|
| **기능명** | 저장소 UI 단순화 + 용어 통일 |
| **주요 변경** | MemoryKeeper 저장소 명칭, Storage 화면 [폴더 변경]/[연결 확인], Settings 저장소 섹션, Import ComboBox 제거, `StorageConnectionChecker`/`StorageUiOperations` |
| **DB 변경** | 아니오 |
| **Migration** | — |
| **테스트** | StorageServiceTests 유지 |

---

## MK-042C — 저장소 폴더 변경 버그 수정

| 항목 | 내용 |
|------|------|
| **기능명** | PhotoRoot 변경 DB 미반영 수정 |
| **주요 변경** | `StorageRepository.UpdateAsync` tracked update, `StorageUiOperations` scope 분리, `UpdatePhotoRootAsync` 자동 활성화, `ImportViewModel` active storage |
| **DB 변경** | 아니오 |
| **Migration** | — |
| **테스트** | `UpdatePhotoRootAsync_ActivatesStorageWhenInactive` 추가 |

---

## MK-042D — 사용자 용어 통일 + Gallery·사진등록

| 항목 | 내용 |
|------|------|
| **기능명** | UI 용어 통일, Pending/Gallery/Home/방문지도 개선 |
| **주요 변경** | 방문지도/사진등록/미완성 추억/태그/여행기록/설정, Gallery Pending 포함, Home Pending 카드, `[Photo Register]` 로그, Navigation 재정렬, `MediaQueryFilters.WhereInYear` |
| **DB 변경** | 아니오 |
| **Migration** | — |
| **테스트** | **30/30** 통과 (`SearchGalleryAsync_IncludesPendingPhotosWithoutPlace` 등) |

---

## MK-043 — Prototype UI/UX 리팩토링

| 항목 | 내용 |
|------|------|
| **기능명** | 상단 GNB, 설정 허브, 용어/레이아웃 정리 |
| **주요 변경** | Top NavigationView, 관리 메뉴→설정, 사진첩/사진 정보, 태그 관리(설정), `미완성 추억/` RelativePath, 방문지도 30/70, Home Pending→사진 정보 |
| **DB 변경** | 아니오 |
| **Migration** | — |
| **테스트** | **30/30** 통과 |

---

## MK-043A — 사진첩 UI/UX 개선

| 항목 | 내용 |
|------|------|
| **기능명** | 사진첩 조회/레이아웃/사용성 개선 (Business Logic 유지) |
| **주요 변경** | 연도(존재·내림차순·개수), 전체 조회 수정, 빠른 필터, 선택 모드 제거, 기본=전체, 사진 정보 정리·태그 관리, 반응형 |
| **DB 변경** | 아니오 |
| **Migration** | — |
| **테스트** | **33/33** 통과 (`GetGallerySidebarSummaryAsync` / `QueryGalleryAsync` 추가) |

---

## MK-042E — 사진첩 진입 오류 수정

| 항목 | 내용 |
|------|------|
| **기능명** | Gallery 진입 예외 격리 · 진단 로그 |
| **주요 변경** | `gallery.log`/`startup.log`, MapGallery path-safe, Thumbnail empty-path skip, UnhandledException Handled, 사용자 오류 메시지 |
| **DB 변경** | 아니오 |
| **Migration** | — |
| **테스트** | **34/34** 통과 |

---

## MK-042F — Error Dialog 개선

| 항목 | 내용 |
|------|------|
| **기능명** | 복사/스크롤 가능한 Error Dialog (MessageBox 대체) |
| **주요 변경** | ReadOnly TextBox, 전체 복사, 로그 열기, Unhandled/Startup/Gallery/Import 공용 |
| **DB 변경** | 아니오 |
| **Migration** | — |
| **테스트** | **40/40** 통과 (`ErrorReportFormatterTests`) |

---

## MK-042G — EXIF 메타데이터 엔진 개선

| 항목 | 내용 |
|------|------|
| **기능명** | EXIF Date/GPS/Orientation/Camera 메타데이터 엔진 (AstroJournal 참고·독립 구현) |
| **주요 변경** | ExifReader/DateResolver/GpsParser/CoordinateConverter, 선택 EXIF DB 컬럼, 등록 로그 강화, Debug EXIF 보기 |
| **DB 변경** | 예 |
| **Migration** | `20260728093000_AddMediaExifFields` |
| **테스트** | `ExifEngineTests` |

---

## MK-042I — 설정 및 사용자 피드백(UI/UX) 개선

| 항목 | 내용 |
|------|------|
| **기능명** | 설정 메뉴 재구성, 집 위치 Autocomplete, 유지보수 작업 |
| **주요 변경** | 설정 일반/사진/AI/유지보수, Google API Key 보존 규칙, InfoBar/ContentDialog 피드백 |
| **DB 변경** | 아니오 |
| **Migration** | — |
| **테스트** | — |

---

## MK-042J — 장소관리 Master Data

| 항목 | 내용 |
|------|------|
| **기능명** | Place Master Data + 설정 Navigation |
| **주요 변경** | PostalCode/GooglePlaceId/Category/IsFavorite/UsageCount/LastUsedAt, 장소관리 UI+지도 |
| **DB 변경** | 예 |
| **Migration** | `20260728103000_AddPlaceMasterDataFields` |
| **테스트** | — |

---

## MK-042M ~ MK-042T — 장소·Import·사진 정보

| 티켓 | 기능명 | 주요 변경 |
|------|--------|-----------|
| **MK-042M** | PlaceType·Gallery 계층 | PlaceTypeCatalog, Import 파이프라인 로그, Year→Country→City→Place |
| **MK-042O** | Google Place Details | `CreateOrGetFromGooglePlaceAsync`, CanonicalName 불변 |
| **MK-042P** | 라이브러리 무결성 | 중복 복사 금지, `LibraryCopyIntegrityService` |
| **MK-042Q** | Place 정규화 | `PlaceNormalizer`, `PlaceRenormalizationService` |
| **MK-042S** | 사진 정보 확장 | 패널 너비, 메모, 지도 반경, Photo Detail WebView 지도 |
| **MK-042T** | 장소 피커 | `PlacePickerService` (최근/즐겨찾기/계층/검색) |

---

## MK-048 — Catalog Invalidation

| 항목 | 내용 |
|------|------|
| **기능명** | 장소 변경 후 관련 화면 자동 갱신 |
| **주요 변경** | `ICatalogInvalidation`, `MainWindow` dirty 페이지 재로드 |
| **DB 변경** | 아니오 |
| **Migration** | — |
| **테스트** | Place/Media 배정 연동 |

---

## MK-049 — 방문지도 연도·미분류

| 항목 | 내용 |
|------|------|
| **기능명** | 연도별 집계·미분류 타임라인 |
| **주요 변경** | `ScopeToYear`, `UnclassifiedPlaceId`, `MediaDate.ResolveYear` |
| **DB 변경** | 아니오 |
| **Migration** | — |
| **테스트** | `VisitRecordQueryServiceTests` |

---

## MK-050 — Google 장소 GPS 좌표 버그

| 항목 | 내용 |
|------|------|
| **기능명** | Google 검색 장소 GPS 오류 수정 |
| **주요 변경** | `RefreshExistingGooglePlaceAsync`, Place Details 우선, seed 좌표 미전달 |
| **DB 변경** | 아니오 (기존 stale 데이터는 수동/재적용으로 보정) |
| **Migration** | — |
| **테스트** | `PlaceServiceTests.CreateOrGetFromGooglePlace_RefreshesStaleCoordinatesOnExistingPlace` |

---

## MK-051 — 사진 → 방문지도 포커스

| 항목 | 내용 |
|------|------|
| **기능명** | 사진 정보 지도보기 → 방문지도 포커스 |
| **주요 변경** | `FocusMediaId`, `ApplyPendingFocusCommand`, Photo Detail 지도 동기화 |
| **DB 변경** | 아니오 |
| **Migration** | — |
| **테스트** | — |

---

## MK-052 — 사진 위치정보 추가/수정 UX

| 항목 | 내용 |
|------|------|
| **기능명** | 선택 결과 확인 중심 위치정보 팝업 UX |
| **주요 변경** | `PlaceLocationPreview`, Preview Card, 현재→변경 예정 비교, 적용/취소, `PlaceRegistrationDialog` |
| **DB 변경** | 아니오 |
| **Migration** | — |
| **테스트** | `PlaceLocationPreviewTests` |

---

## Version 1.0.0 — UI/UX 정리 (2026-08)

| 항목 | 내용 |
|------|------|
| **기능명** | V1 화면 일관성·추억 탐험 UX (기능 티켓 아님) |
| **주요 변경** | `Themes/DesignSystem.xaml`(`Mk*`), Home/사진첩/방문지도/여행기록 Layout 정리, GNB「검색」제거, 방문지도 검색 오버레이·연도 썸네일 전파, 여행기록 Insight+타임라인 |
| **DB 변경** | 아니오 |
| **Migration** | — |
| **테스트** | 기존 유닛 테스트 유지 (118) |

---

## Migration 목록 (전체)

1. `20260723065546_InitialCreate`
2. `20260723070232_RenameTablesToTbConvention`
3. `20260723071938_AddPlaceEntity`
4. `20260724010000_AddMediaFavorite`
5. `20260724020000_AddPhotoTagSystem`
6. `20260724030000_AddPinnedTag`
7. `20260724110000_RenameLibraryPathAndPhotoRoot`
8. `20260728093000_AddMediaExifFields`
9. `20260728103000_AddPlaceMasterDataFields`

경로: `MemoryKeeper.Infrastructure/Database/Migrations/`
