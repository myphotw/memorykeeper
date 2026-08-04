# DATABASE_SCHEMA.md

SQLite 스키마 (EF Core 설정·스냅샷 기준).

DB 파일: `%LocalAppData%\MemoryKeeper\MemoryKeeper.db`

---

## 테이블 개요

| 테이블 | Entity | 역할 |
|--------|--------|------|
| `TB_MEDIA` | `Media` | 라이브러리 미디어 메타 |
| `TB_STORAGE` | `Storage` | PhotoRoot / 저장소 |
| `TB_PLACE` | `Place` | 장소 |
| `TB_SETTING` | `Setting` | 키-값 설정 |
| `TB_TAG` | `Tag` | 태그 |
| `TB_MEDIA_TAG` | `MediaTag` | Media↔Tag |

**존재하지 않음:** `TB_VISIT_RECORD` (방문은 Media/Place로 계산)

---

## TB_MEDIA

| 컬럼 | 설명 |
|------|------|
| `Id` | PK (Guid) |
| `FileName` | 파일명 |
| `MediaType` | Photo / Video |
| `Status` | Pending / Imported / Duplicate / Failed |
| `OriginalPath` | 원본 절대 경로 (원본 미수정 전제) |
| `RelativePath` | `PhotoRoot` 기준 상대 경로 (예: `2026/Osaka/IMG0001.jpg`) |
| `ContentHash` | 중복 검사용 |
| `CapturedAt` | 촬영 시각 (nullable) |
| `ImportedAt` | Import 시각 |
| `Latitude` / `Longitude` / `Altitude` | GPS (nullable) |
| `StorageId` | FK → `TB_STORAGE` (Restrict) |
| `PlaceId` | FK → `TB_PLACE` (SetNull, nullable) |
| `IsFavorite` | 즐겨찾기 |
| `CreatedAt` / `UpdatedAt` | 감사 필드 |

인덱스: `ContentHash`, `StorageId`, `PlaceId`, `CapturedAt`, `IsFavorite`

---

## TB_STORAGE

| 컬럼 | 설명 |
|------|------|
| `Id` | PK |
| `Name` | 이름 (unique) |
| `StorageType` | Local / External / Nas |
| `PhotoRoot` | 라이브러리 루트 경로 |
| `IsActive` | 활성 여부 |
| `CreatedAt` / `UpdatedAt` | |

절대 경로 = `PhotoRoot` + `RelativePath` (`IFileAccessService`)

---

## TB_PLACE

| 컬럼 | 설명 |
|------|------|
| `Id` | PK |
| `DisplayName` | 표시명 |
| `Country` / `Province` / `City` / `Address` | 지역 정보 |
| `Latitude` / `Longitude` | 좌표 |
| `Radius` | 반경 |
| `IsActive` | |
| `CreatedAt` / `UpdatedAt` | |

---

## TB_SETTING

| 컬럼 | 설명 |
|------|------|
| `Id` | PK |
| `Key` | unique |
| `Value` | 문자열 |
| `CreatedAt` / `UpdatedAt` | |

주요 Key (`SettingKeys`):

- `GoogleMaps:ApiKey`
- `Place:DefaultRadiusMeters`
- `Tag:RecentTagIds`
- `Search:RecentQueries`
- `Travel:HomeLatitude` / `Travel:HomeLongitude` / `Travel:HomeAddress`
- `App:SetupCompleted`

---

## TB_TAG

| 컬럼 | 설명 |
|------|------|
| `Id` | PK |
| `Name` | unique |
| `Color` | |
| `UsageCount` | |
| `Source` | User / Ai |
| `IsPinned` | Pinned Tag |
| `CreatedAt` / `UpdatedAt` | |

---

## TB_MEDIA_TAG

| 컬럼 | 설명 |
|------|------|
| `Id` | PK |
| `MediaId` | FK → `TB_MEDIA` (Cascade) |
| `TagId` | FK → `TB_TAG` (Cascade) |

`(MediaId, TagId)` unique

---

## Visit Record (논리 모델)

테이블 없음. Application에서 Place별 Media의 `CapturedAt ?? ImportedAt` 날짜를 묶어 방문 횟수·타임라인 DTO를 만든다.

---

## Migration 목록

경로: `MemoryKeeper.Infrastructure/Database/Migrations/`

| Id | 클래스 | 요약 |
|----|--------|------|
| `20260723065546` | `InitialCreate` | Media/Settings/Storages 초기 |
| `20260723070232` | `RenameTablesToTbConvention` | TB_ 네이밍 |
| `20260723071938` | `AddPlaceEntity` | Place + Media.PlaceId |
| `20260724010000` | `AddMediaFavorite` | `IsFavorite` |
| `20260724020000` | `AddPhotoTagSystem` | Tag / MediaTag |
| `20260724030000` | `AddPinnedTag` | `IsPinned` |
| `20260724110000` | `RenameLibraryPathAndPhotoRoot` | `LibraryPath`→`RelativePath`, `RootPath`→`PhotoRoot` |

스냅샷: `MemoryKeeperDbContextModelSnapshot.cs`

### 스키마 변경 시

1. Domain Entity / Configuration 수정
2. `dotnet ef migrations add ...` (Infrastructure)
3. 앱 기동 시 `DatabaseInitializer` → `MigrateAsync`
4. 데이터 변환이 필요하면 Migrator 클래스 추가 (예: `RelativePathDataMigrator`)

임의로 SQLite 파일을 손으로 고치지 말 것.
