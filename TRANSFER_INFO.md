# TRANSFER_INFO.md

MemoryKeeper 이관·인수인계 요약. 상세는 `Docs/` 폴더를 참고한다.

---

## 생성 / 갱신 정보

| 항목 | 값 |
|------|-----|
| 최초 생성 | 2026-07-24 |
| **최종 갱신** | **2026-08-04** |
| 원본 경로 (초기) | `D:\999. etc\MemoryKeeper` |
| **현재 개발 경로** | 로컬 워크스페이스 `MemoryKeeper` |
| **제품 버전** | **1.0.0** (`app.manifest` `1.0.0.0`, SDK 기본 AssemblyVersion) |
| 현재 개발 단계 | **Version 1.0.0** — MK-052 기능 완료 + V1 UI/UX 정리 반영 |

---

## 현재 상태 요약 (2026-08-04)

| 항목 | 상태 |
|------|------|
| 제품 버전 | **1.0.0** |
| Release / Debug x64 Build | **성공** |
| Unit Tests | **118/118 통과** (`dotnet test -c Release -p:Platform=x64`) |
| .NET SDK | 8.0.x (`dotnet --version`, 개발 PC 예: 8.0.423) |
| MainWindow 실행 | Debug/Release exe **정상 실행** |
| DB Migration | 기동 시 `DatabaseInitializer` → EF `MigrateAsync` 자동 적용 |

**실행 경로 (소스 빌드, Debug):**

```text
MemoryKeeper.App\bin\x64\Debug\net8.0-windows10.0.19041.0\MemoryKeeper.exe
```

**실행 경로 (소스 빌드, Release):**

```text
MemoryKeeper.App\bin\x64\Release\net8.0-windows10.0.19041.0\MemoryKeeper.exe
```

**Publish 산출물 (재Publish 시):**

```text
MemoryKeeper.Release\MemoryKeeper.exe
```

**로컬 데이터 (실행 시 자동 생성):**

```text
%LocalAppData%\MemoryKeeper\MemoryKeeper.db
%LocalAppData%\MemoryKeeper\ThumbnailCache\
%LocalAppData%\MemoryKeeper\Logs\startup.log
%LocalAppData%\MemoryKeeper\map-host\          (방문지도 WebView HTML 캐시)
```

---

## Version 1.0.0에서 실제로 동작하는 주요 기능

| 영역 | 동작 내용 |
|------|-----------|
| **홈** | Hero 추천 추억, 최근 가져오기/등록 사진/방문, 빠른 작업. GNB 루트 화면(뒤로·전역「검색」없음) |
| **사진첩** | 연도/장소 트리 + 사진 Grid 감상, Hero·선택 상세, Empty→가져오기 |
| **방문지도** | 연도 Timeline·지도·선택 장소 Card, 검색/최근검색 드롭다운(오버레이), 연도 펼침 썸네일 |
| **여행기록** | Memory Insight 4카드 + 연도별 타임라인 Row + 하단 통계(집계). 클릭→방문지도 포커스 |
| **사진등록 / 미완성 추억 / 장소·태그·저장소** | 설정·빠른 작업·화면 진입으로 사용 |
| **사진 정보 / 뷰어 / 즐겨찾기** | 상세·Slideshow·지도보기·위치정보 팝업(MK-052) |
| **Setup Wizard** | 최초 실행 저장소·설정 |

기능 티켓 이력(MK-033~MK-052)은 `Docs/FEATURE_HISTORY.md` 참고.

---

## MK-042K ~ MK-052 + V1 UI 요약

### 기능 (MK-042M ~ MK-052)

| 티켓 | 요약 |
|------|------|
| **MK-042M~T** | PlaceType·Import·Gallery 계층·Google Place·복사 무결성·정규화·사진정보 지도·PlacePicker |
| **MK-048** | `ICatalogInvalidation` — 장소 변경 후 관련 화면 재로드 |
| **MK-049** | 방문지도 연도·미분류 집계 (`ScopeToYear`) |
| **MK-050** | Google 장소 GPS 좌표 버그 수정 |
| **MK-051** | 사진 → 방문지도 `FocusMediaId` 포커스 |
| **MK-052** | 위치정보 추가/수정 Preview Card·적용/취소 UX |

### V1 UI/UX 정리 (기능 추가 없음, Style/Layout 중심)

| 화면 | 변경 요지 |
|------|-----------|
| 공통 | `Themes/DesignSystem.xaml` — `Mk*` 토큰·카드·버튼·Empty |
| 홈 | 추억 출발점 구성, GNB에서 「검색」제거 |
| 사진첩 | 사진 우선 Grid·대표 썸네일·하단 간단 정보 |
| 방문지도 | 선택 Card·고정폭 검색·결과 오버레이·연도 썸네일 전파 |
| 여행기록 | 사진 카드 제거 → Insight + 타임라인 Row + 통계 |

---

## 포함 내용 (이관 시)

- `MemoryKeeper.sln`
- 소스: Domain / Application / Infrastructure / App / Tests
- `Docs/` (인수인계 문서 전체)
- `README.md`, `DEVELOPMENT.md`, `TRANSFER_INFO.md`, `.gitignore`
- `MemoryKeeper.Release/` (Publish 시 생성, exe 포함 가능)

---

## 제외 항목

절대 복사하지 않음:

- `.vs`, `bin`, `obj`, `TestResults`
- `*.user`, `*.suo`, `*.cache`

실행 시 자동 생성 (소스 미포함):

- `%LocalAppData%\MemoryKeeper\MemoryKeeper.db`
- `%LocalAppData%\MemoryKeeper\ThumbnailCache\`
- `%LocalAppData%\MemoryKeeper\Logs\startup.log`
- `%LocalAppData%\MemoryKeeper\map-host\`

---

## 새 PC / Cursor에서 실행 순서

1. 프로젝트 폴더를 Cursor에서 연다.
2. [.NET 8 SDK](https://dotnet.microsoft.com/download) 설치: `dotnet --version` → `8.0.x`
3. 검증:

```powershell
dotnet restore MemoryKeeper.sln
dotnet build MemoryKeeper.sln -c Release -p:Platform=x64
dotnet test MemoryKeeper.sln -c Release -p:Platform=x64
```

4. 실행:

```powershell
Start-Process ".\MemoryKeeper.App\bin\x64\Release\net8.0-windows10.0.19041.0\MemoryKeeper.exe"
```

Debug 확인 시:

```powershell
dotnet build .\MemoryKeeper.App\MemoryKeeper.App.csproj -c Debug -p:Platform=x64
Start-Process ".\MemoryKeeper.App\bin\x64\Debug\net8.0-windows10.0.19041.0\MemoryKeeper.exe"
```

5. 문서 읽기 순서: `Docs/PROJECT_CONTEXT.md` → `Docs/CURRENT_STATUS.md` → `Docs/FEATURE_HISTORY.md` → `Docs/NEXT_STEP_GUIDE.md` → `Docs/DESIGN_SYSTEM.md` / `UX_PHILOSOPHY.md` / `INFORMATION_ARCHITECTURE.md`

---

## 폴더 구조 (코드 기준)

```
MemoryKeeper.sln
├── MemoryKeeper.Domain          # Entity, Enum, Domain interface
├── MemoryKeeper.Application     # DTO, Service, Interface, UseCase
├── MemoryKeeper.Infrastructure  # EF Core, Repository, File, Metadata, Location
├── MemoryKeeper.App             # WinUI MVVM
│   ├── Views / ViewModels / Models / Dialogs
│   ├── Themes/                  # DesignSystem + Tokens + Styles (Mk*)
│   ├── Maps/Google/             # WebView2 지도 호스트
│   ├── Services / Converters / Diagnostics
│   └── Resources / Properties
├── MemoryKeeper.Tests           # xUnit
└── Docs/
```

---

## 사용 라이브러리·버전 (csproj)

| 패키지 | 버전 | 프로젝트 |
|--------|------|----------|
| Microsoft.WindowsAppSDK | 1.6.250205002 | App |
| Microsoft.Windows.SDK.BuildTools | 10.0.26100.1742 | App |
| CommunityToolkit.Mvvm | 8.4.2 | App |
| Microsoft.Extensions.Hosting / DI | 8.0.1 | App |
| SixLabors.ImageSharp | 3.1.12 | App, Infrastructure |
| Microsoft.EntityFrameworkCore (+ Sqlite, Design) | 8.0.29 | Infrastructure |
| MetadataExtractor | 2.9.3 | Infrastructure |
| Microsoft.Extensions.Http | 8.0.1 | Infrastructure |
| xunit / Microsoft.NET.Test.Sdk | 2.5.3 / 17.8.0 | Tests |

Target: App `net8.0-windows10.0.19041.0`, 나머지 `net8.0`. **Platform x64 / win-x64** 전용.

상세 환경: `Docs/ENVIRONMENT_SETUP.md`

---

## 데이터 구조

| 리소스 | 설명 |
|--------|------|
| SQLite | `%LocalAppData%\MemoryKeeper\MemoryKeeper.db` |
| 테이블 | `TB_MEDIA`, `TB_STORAGE`, `TB_PLACE`, `TB_SETTING`, `TB_TAG`, `TB_MEDIA_TAG` |
| **없음** | `TB_VISIT_RECORD` — 방문지도/여행기록은 Media·Place **집계** |
| 라이브러리 파일 | `TB_STORAGE.PhotoRoot` + `TB_MEDIA.RelativePath` |

스키마 상세: `Docs/DATABASE_SCHEMA.md`

---

## 핵심 파일 (V1.0.0)

| 영역 | 파일 |
|------|------|
| 디자인 시스템 | `Themes/DesignSystem.xaml`, `Themes/Tokens/*`, `Themes/Styles/*` |
| Shell / 내비 | `MainWindow.xaml(.cs)` |
| 홈 | `HomePage.xaml`, `HomeViewModel.cs` |
| 사진첩 | `GalleryPage.xaml(.cs)`, `GalleryViewModel.cs` |
| 방문지도 | `VisitRecordPage.xaml(.cs)`, `VisitRecordViewModel.cs`, `VisitRecordQueryService.cs` |
| 여행기록 | `TravelRecordsPage.xaml(.cs)`, `TravelRecordsViewModel.cs`, `TravelRecordsService.cs` |
| 위치정보 팝업 | `Dialogs/PlaceRegistrationDialog.cs`, `PlaceLocationPreview.cs` |
| Google 장소·좌표 | `PlaceService.cs`, `MediaPlaceAssignmentService.cs` |
| 화면 갱신 | `ICatalogInvalidation`, `MainWindow.xaml.cs` |
| 지도 호스트 | `Maps/Google/GoogleMapController.cs`, `GoogleMapHtmlBuilder.cs` |
| 사진등록 | `MediaImportService.cs`, `ImportViewModel.cs` |
| Startup | `App.xaml.cs`, `Diagnostics/StartupDiagnostics.cs` |

---

## 알려진 제한사항 · 미완성

| 항목 | 내용 |
|------|------|
| Visit Record 테이블 | 없음 — 집계 뷰만 |
| GNB 「검색」 | V1에서 제거. 검색은 방문지도 검색창·설정 경로 등으로 수행 |
| E2E 수동 검증 | 제조사별 Import·해외 Google 장소·위치정보 UX는 실사진 검증 권장 |
| NAS / Server / Mobile | 미구현 — `IFileAccessService` 확장 전제만 유지 |
| AI 검색 | 규칙 기반 Analyzer만 (`RuleBasedMemorySearchAnalyzer`) |
| 위치정보 Undo / 일괄 태그 | 미구현 (선택 개선) |

---

## 참고

- App은 **Windows x64 / win-x64** 전용.
- 내부 RelativePath 폴더명과 UI「미완성 추억」용어는 구분한다.
- Visit Record는 **DB 테이블 없음** — `VisitRecordQueryService` / `TravelRecordsService` 집계.
- `VisitRecordPage` 등 **내부 클래스명**은 UI 문자열만 **방문지도**.
- Google Maps API Key는 설정에서 등록. Place 검색·지도·Reverse Geocoding에 사용.
- IA: `Docs/INFORMATION_ARCHITECTURE.md` · UX: `Docs/UX_PHILOSOPHY.md` · Design: `Docs/DESIGN_SYSTEM.md`
