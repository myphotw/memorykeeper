# NEXT_STEP_GUIDE.md

현재( **Version 1.0.0** / MK-052 기능 완료 + V1 UI 정리 ) 이후 권장 진행 순서. (갱신: 2026-08-04)

---

## 우선순위 1 — 위치정보 UX E2E 검증

| # | 시나리오 | 확인 |
|---|----------|------|
| 1 | 사진 정보 → 위치정보 추가/수정 | Preview Card · 현재→변경 예정 · 적용/취소 |
| 2 | Google 해외 장소 검색 | 이름·주소·GPS 좌표 일치 (서울 기본값 아님) |
| 3 | GPS 없는 사진 | Preview 빈 상태 → 장소 선택 → 적용 → 미분류 제외 |
| 4 | 사진 정보 → 지도보기 | 방문지도 해당 사진 위치 포커스 |

---

## 우선순위 2 — 제조사별 사진등록 E2E

| # | 시나리오 | 확인 |
|---|----------|------|
| 1 | Apple / Samsung / Google / Sony / Canon / Nikon / DJI / GoPro | 촬영일 · GPS · Place · Pending · RelativePath |
| 2 | GPS 없는 사진 | Pending · 미완성 추억 폴더 |
| 3 | Debug 빌드 사진 정보 | **EXIF 보기** 표시 · Release에서는 숨김 |

---

## 우선순위 3 — V1 화면 스모크 (UI 정리 후)

| # | 시나리오 | 확인 |
|---|----------|------|
| 1 | 홈 | Hero · Preview · 빠른 작업 · GNB에「검색」없음 |
| 2 | 사진첩 | Grid 감상 · Empty 가져오기 |
| 3 | 방문지도 | 검색 오버레이(지도 미밀림) · LostFocus 시 최근검색 닫힘 · 연도 펼침 썸네일 |
| 4 | 여행기록 | Insight 4카드 · 타임라인 Row · 클릭→방문지도 |

---

## 우선순위 4 — Publish 재생성

```powershell
dotnet publish MemoryKeeper.App/MemoryKeeper.App.csproj -c Release -p:PublishProfile=FolderProfile
```

---

## 우선순위 5 — 선택 개선 (V1.x / 이후)

- 위치정보 팝업 Undo (적용 후 되돌리기)
- 다중 선택 / 일괄 태그 / 일괄 즐겨찾기
- EXIF 재스캔(기존 라이브러리 보강)
- 전역 검색 화면(별도 IA) 재도입 여부 검토

---

## 새 PC에서

1. `TRANSFER_INFO.md` → `Docs/CURRENT_STATUS.md`
2. `dotnet build` / `dotnet test` → **118/118**
3. `MemoryKeeper.App\bin\x64\Release\net8.0-windows10.0.19041.0\MemoryKeeper.exe`
