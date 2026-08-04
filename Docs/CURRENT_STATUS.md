# CURRENT_STATUS.md

코드베이스 기준 현재 개발 상태. (갱신: 2026-08-04, **Version 1.0.0**)

---

## 제품 버전

| 항목 | 값 |
|------|-----|
| Version | **1.0.0** |
| 표시 근거 | `app.manifest` `assemblyIdentity version="1.0.0.0"`, 빌드 기본 AssemblyVersion |
| 범위 | MK-052까지 기능 완료 + V1 UI/UX 정리 (기능 추가 티켓 없음) |

---

## 완료 기능

### MK-033 ~ MK-042J

이전 이력은 `FEATURE_HISTORY.md` 참고. (Pending, Photo Detail, Tag, Search, 방문지도, Home, 여행기록, Storage, EXIF, 설정 UX, 장소 Master Data 등)

### MK-042M ~ MK-042T — 장소·Import·사진 정보

- **MK-042M:** PlaceTypeCatalog, Import 파이프라인 로그, Gallery Year→Country→City→Place 계층
- **MK-042O:** Google Place Details, `CanonicalName` 불변, `CreateOrGetFromGooglePlaceAsync`
- **MK-042P:** 라이브러리 복사 무결성 (중복 복사 금지, `LibraryCopyIntegrityService`)
- **MK-042Q:** `PlaceNormalizer`, `PlaceRenormalizationService`
- **MK-042S:** 사진 정보 패널 너비·메모·지도 반경, Photo Detail 우측 WebView 지도
- **MK-042T:** `PlacePickerService` — 장소 등록 팝업용 최근/즐겨찾기/계층/검색

### MK-048 — Catalog Invalidation (화면 갱신)

- `ICatalogInvalidation` / `CatalogInvalidation` DI 싱글톤
- 장소 배정·생성·수정 시 관련 화면 dirty 표시
- `MainWindow`: Home, 방문지도, 미완성 추억, 사진첩 등 캐시된 페이지 재로드
- 연동: `MediaPlaceAssignmentService`, `PhotoDetailService`, `PlaceService`

### MK-049 — 방문지도 연도·미분류

- `VisitRecordQueryService.ScopeToYear` — 연도별 사진·장소 수 집계
- 합성 **미분류** 타임라인 (`UnclassifiedPlaceId`), 지도 마커 제외
- `MediaDate.ResolveYear` (로컬 연도)
- Gallery 연도 vs 방문지도 연도 불일치 수정
- `GetUnassignedAsync`: `PlaceId == null` 기준

### MK-050 — Google 장소 GPS 좌표 버그

- Google 검색 적용 시 Place Details 좌표 우선 (`PreferCoordinate`)
- 기존 Place 재사용 시 `RefreshExistingGooglePlaceAsync`로 stale 좌표 갱신
- PhotoDetail/Pending: Google 검색 시 서울 기본 seed 미전달
- 장소 배정 시 미디어 GPS를 Place에서 항상 덮어씀

### MK-051 — 사진 → 방문지도 포커스

- `IPlaceFocusState.FocusMediaId` — 사진 정보 **지도보기** → 방문지도 해당 사진 위치
- `VisitRecordViewModel.ApplyPendingFocusCommand`
- 사진 정보 우측 지도 WebView 좌표 동기화

### MK-052 — 사진 위치정보 추가/수정 UX

- 팝업 상단 **선택된 장소 Preview Card** (Card 스타일, 좌표 6자리, 반경)
- **현재 → 변경 예정** 비교 UI (기존 장소 등록 사진)
- `PlaceLocationPreview` + `OriginalLocation` / `SelectedLocation` 분리
- 최근·검색·주변·지도 선택 → Preview 즉시 갱신
- **적용** 버튼: `CanApplyPlaceChange` (변경 없으면 비활성)
- **취소** → `CancelPlaceRegistration()` 원래 장소 복원
- GPS 없음: Preview **위치정보 없음** 빈 상태
- Preview 상단 고정, 선택 영역만 스크롤
- 공유 다이얼로그: `PlaceRegistrationDialog.cs` (PhotoDetail + PendingMemory)

### V1.0.0 UI/UX 정리 (Style / Layout)

- **디자인 시스템:** `Themes/DesignSystem.xaml` — `Mk*` Color/Spacing/Radius/Type + Card/Button/Empty/Image
- **홈:** 추억 출발점(Hero·Preview·빠른 작업). 루트 화면으로 전역「검색」GNB 제거
- **사진첩:** 사진 우선 Grid, 대표 썸네일, 선택 시 간단 정보, Empty「아직 사진이 없습니다.」
- **방문지도:** 선택 장소 Card(대표·미리보기), 검색 Width 고정·결과 오버레이(지도 미밀림), LostFocus 시 최근검색 닫힘, 연도 `ForYear` 썸네일 전파
- **여행기록:** 사진 중심 카드 제거 → Memory Insight 4카드 + 연도 타임라인 Row + 하단 통계

---

## 실제 동작 화면 (GNB)

| UI | 내부 | 상태 |
|----|------|------|
| 홈 | `HomePage` | 동작 |
| 사진첩 | `GalleryPage` | 동작 |
| 방문지도 | `VisitRecordPage` | 동작 |
| 여행기록 | `TravelRecordsPage` (+ Detail) | 동작 |
| 설정 | `SettingsPage` 및 하위 | 동작 |

진입만 (GNB 아님): 사진등록, 미완성 추억, 장소 관리, 태그, 저장소, 사진 정보, 뷰어, 즐겨찾기, Setup Wizard.

---

## 현재 미완료 / 주의

### E2E 수동 검증 (권장)

| 시나리오 | 확인 |
|----------|------|
| 사진 정보 → 위치정보 추가/수정 | Preview Card · 비교 UI · 적용/취소 |
| Google 장소 검색 (해외) | 이름·주소·GPS 좌표 일치 |
| GPS 없는 사진 → 수동 장소 등록 | 좌표 반영 · 미분류 제외 · 화면 갱신 |
| 사진 정보 → 지도보기 | 방문지도 해당 사진 위치 포커스 |
| 방문지도 연도 펼침 | 썸네일 표시 · 검색 드롭다운 LostFocus |
| 미완성 → 장소 등록 | 자동완성 · 지도 · 반경 |
| 제조사별 사진등록 | Apple/Samsung/Google/Sony/Canon 등 EXIF·GPS |

### Visit Record 테이블

- `TB_VISIT_RECORD` 없음 — Media/Place 집계

### 내부 vs UI 용어

| 내부 | UI |
|------|-----|
| `ImportPage` | 사진등록 |
| `VisitRecordPage` | 방문지도 |
| `GalleryPage` | 사진첩 |
| `PhotoDetailPage` | 사진 정보 |
| `MediaStatus.Pending` | 미완성 추억 |
| RelativePath `미완성 추억/` | (폴더) |

---

## 네비게이션 (현재 UI)

**상단 GNB**

- 홈
- 사진첩
- 방문지도
- 여행기록
- (우측 Footer) 설정

**제거됨 (V1):** GNB 「검색」(방문지도로만 가던 항목 — 혼란 유발)

**설정 안**

- Overview → 상세(← 뒤로 + Breadcrumb)
- 일반: MemoryKeeper 저장소, 집 위치, Google Maps
- 사진: 사진등록, 미완성 추억, 장소 관리, 메타데이터, 태그 관리
- 유지보수 / 정보 / 로그

즐겨찾기·사진 정보는 화면 진입으로만 (GNB 항목 아님).

---

## 테스트 / 빌드 (2026-08-04)

| 항목 | 상태 |
|------|------|
| Unit tests | **118/118 통과** |
| x64 Build (Debug/Release) | **성공** |

주요 테스트:

- `PlaceLocationPreviewTests` (MK-052)
- `PlaceServiceTests` — Google 좌표 refresh
- `VisitRecordQueryServiceTests` — `ScopeToYear`

---

## 설정 키 (`SettingKeys`)

- `GoogleMaps:ApiKey`
- `Place:DefaultRadiusMeters`
- `PhotoDetail:PanelWidth` (MK-042S)
- `MapPick:DefaultRadiusMeters` (MK-042S)
- `Tag:RecentTagIds`
- `Search:RecentQueries`
- `Travel:HomeLatitude` / `Travel:HomeLongitude` / `Travel:HomeAddress`
- `App:SetupCompleted`
