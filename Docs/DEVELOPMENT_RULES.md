# DEVELOPMENT_RULES.md

다른 PC·Cursor 세션에서도 동일하게 지킬 개발 규칙.

---

## 반드시 유지

1. **기존 Architecture 유지**  
   Domain → Application → Infrastructure → App. 계층을 건너뛰지 않는다.

2. **DB Schema 임의 변경 금지**  
   SSMS/DB Browser로 컬럼을 직접 추가·삭제하지 않는다. **EF Migration만** 사용.

3. **Migration 사용**  
   스키마 변경 = Entity/Config + Migration + (필요 시) 데이터 Migrator.

4. **사진 원본 변경 금지**  
   원본 파일을 덮어쓰거나 인코딩하지 않는다. Thumbnail은 LocalAppData 캐시에만 생성.

5. **RelativePath 유지**  
   `TB_MEDIA`에 절대 라이브러리 경로를 다시 넣지 않는다. `PhotoRoot` + `RelativePath`.

6. **FileAccessService 사용**  
   파일 존재/열기/경로 조합은 `IFileAccessService`를 통한다. UI에서 `Path.Combine(PhotoRoot, …)` 남발 금지.

7. **Service Layer 유지**  
   ViewModel은 Application Service를 호출한다. Repository를 ViewModel에 직접 주입하지 않는 현재 패턴을 유지한다. (예외가 필요하면 인수인계 문서에 이유를 남긴다.)

8. **DateTime 처리 표준 (MK-042E)**  
   - **DB 저장**: UTC `DateTime` (`Media.CapturedAt`, `ImportedAt`, `CreatedAt`, `UpdatedAt`)
   - **Repository Query / SQLite 정렬**: `DateTime`만 사용. `DateTimeOffset`을 SQLite `ORDER BY` / `GROUP BY` / `WHERE` / `MAX` / `MIN`에 쓰지 않는다.
   - SQLite에서 날짜 정렬이 필요하면 `ToListAsync()`(또는 `AsEnumerable()`) 이후 LINQ to Objects로 정렬한다.
   - **DateTimeOffset**: 외부 API · TimeZone 계산 · UI/DTO 표시용으로만 사용 (`DateTimeHelper.ToUtcOffset` / `ToLocal`)
   - **화면 표시**: UTC → Local Time 변환 후 표시
   - 공통 헬퍼: `MemoryKeeper.Application.Time.DateTimeHelper`

9. **설정 / API Key (MK-042I)**  
   - Google API Key는 Settings(`GoogleMaps:ApiKey`)에 영구 저장한다.
   - **전체 초기화**에서만 API Key를 삭제한다.
   - 등록사진 초기화 · 장소 재생성 · 썸네일 삭제 · 저장소 변경 · Setup Wizard 건너뛰기는 API Key를 유지한다.
   - 집 위치는 Places Autocomplete로 선택하며, 위도/경도 수기 입력을 사용하지 않는다.
   - 위험 작업(초기화/삭제/복원)은 ContentDialog 확인 후 수행하고, 일반 완료는 InfoBar로 알린다.

10. **장소 Master Data (MK-042J)**  
    - Place는 사진 자동분류·여행기록·검색·통계의 기준 데이터다.
    - 장소관리 UI는 Google Map + Places Autocomplete 중심. 배정 알고리즘(Business Logic)은 변경하지 않는다.
    - 반경 미리보기(`CountMediaInRadiusAsync`)는 UI 피드백용이며 배정을 바꾸지 않는다.

---

## 플랫폼 / 빌드

- App은 **Windows x64 / win-x64** 고정 (`MemoryKeeper.App.csproj`).
- Publish:  
  `dotnet publish MemoryKeeper.App\MemoryKeeper.App.csproj -c Release -p:PublishProfile=FolderProfile`  
  → `MemoryKeeper.Release\`
- 실행 진단 로그: `%LocalAppData%\MemoryKeeper\Logs\startup.log`
- MK-041B 단계별 MessageBox는 **제거됨**. 기동 실패 시 `StartupDiagnostics.ShowErrorMessageBox`만 사용.

---

## 새 기능 추가 순서

권장 순서:

1. **Domain** — Entity/Enum/규칙 (스키마 필요 시)
2. **Application** — Interface, DTO, Service
3. **Infrastructure** — Repository/외부 구현, Migration
4. **App** — ViewModel, Page, DI 등록, Navigation
5. **Tests** — Application 단위 테스트 추가

UI만 먼저 만들고 DB를 나중에 맞추는 방식은 피한다.

---

## 검색 / AI 확장

- 자연어 검색 로직은 `IMemorySearchAnalyzer` 뒤에 둔다.
- 현재: `RuleBasedMemorySearchAnalyzer`
- AI 도입 시 **인터페이스 구현체만 교체**. `MemorySearchService` 계약을 깨지 않는다.

---

## Backup / 초기화

- Backup zip은 **DB(+ Settings/Tag/Place 데이터)** 중심. 사진 원본은 포함하지 않는다.
- Import 데이터 초기화는 Media/Place/Tag 테이블만 지우고 **원본 파일은 삭제하지 않는다.**

---

## 문서

- 기능 완료·스키마 변경 시 `Docs/CURRENT_STATUS.md`, `FEATURE_HISTORY.md`, `DATABASE_SCHEMA.md`를 갱신한다.
- 구현되지 않은 기능을 “완료”로 적지 않는다.
