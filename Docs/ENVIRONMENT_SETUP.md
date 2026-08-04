# ENVIRONMENT_SETUP.md

개발·빌드·Publish·실행 환경 안내.  
수치는 **이 문서 작성 시점의 실제 프로젝트 파일 / 현재 개발 PC 측정값**이다.  
제품 버전: **1.0.0** (갱신: 2026-08-04).

---

## 1. 개발 환경

### OS (현재 개발 PC에서 확인)

| 항목 | 값 |
|------|-----|
| OS | Microsoft Windows 11 Pro |
| NT / Build | `10.0.26200` (`Microsoft Windows NT 10.0.26200.0`) |

`MemoryKeeper.App\app.manifest`는 Windows 10/11 호환 OS ID를 선언한다.  
앱 Target은 Windows 10 **19041** 이상을 기준으로 한다 (`TargetFramework` 아래 참고).

### .NET SDK (현재 개발 PC에서 확인)

| 항목 | 값 |
|------|-----|
| 설치된 SDK | `8.0.423` (`C:\Program Files\dotnet\sdk`) |
| `dotnet --version` | `8.0.423` |

`global.json`은 리포지토리에 **없다**. SDK 8.x로 빌드하는 구성이다.

### 프로젝트 Target Framework

| 프로젝트 | TargetFramework |
|----------|-----------------|
| `MemoryKeeper.App` | `net8.0-windows10.0.19041.0` |
| `MemoryKeeper.Domain` | `net8.0` |
| `MemoryKeeper.Application` | `net8.0` |
| `MemoryKeeper.Infrastructure` | `net8.0` |
| `MemoryKeeper.Tests` | `net8.0` |

App 추가 속성 (`MemoryKeeper.App.csproj`):

| 속성 | 값 |
|------|-----|
| `TargetPlatformMinVersion` | `10.0.17763.0` |
| `Platforms` / `PlatformTarget` | `x64` |
| `RuntimeIdentifier` / `RuntimeIdentifiers` | `win-x64` |
| `UseWinUI` | `true` |
| `WindowsPackageType` | `None` (unpackaged) |
| `WindowsAppSDKSelfContained` | `true` |
| `AssemblyName` | `MemoryKeeper` |

### Windows App SDK / Windows SDK Build Tools

`MemoryKeeper.App.csproj` PackageReference:

| 패키지 | 버전 |
|--------|------|
| `Microsoft.WindowsAppSDK` | `1.6.250205002` |
| `Microsoft.Windows.SDK.BuildTools` | `10.0.26100.1742` |

관련 NuGet (참고):

| 패키지 | 버전 | 프로젝트 |
|--------|------|----------|
| `Microsoft.EntityFrameworkCore` / `.Sqlite` / `.Design` | `8.0.29` | Infrastructure |
| `CommunityToolkit.Mvvm` | `8.4.2` | App |
| `Microsoft.NET.Test.Sdk` | `17.8.0` | Tests |
| `xunit` | `2.5.3` | Tests |

### Visual Studio / Build Tool 요구사항 (프로젝트에서 확인 가능한 범위)

리포지토리에 Visual Studio 버전을 고정한 설정 파일은 **없다**.

실제로 사용·검증된 빌드 경로:

- **.NET 8 SDK** + `dotnet` CLI
- App는 **WinUI 3** (`UseWinUI=true`) + **Windows App SDK 1.6** + **Windows SDK BuildTools 10.0.26100.1742**
- 플랫폼은 **x64** 고정 (`-p:Platform=x64` 권장)

IDE를 쓸 경우: WinUI / Windows 개발이 가능한 Visual Studio 2022 계열을 쓰는 것이 일반적이나, **필수 VS 버전 번호는 csproj에 명시되어 있지 않다.**  
CLI만으로 restore / build / test / publish가 가능하다.

---

## 2. Build 방법

솔루션 루트 (`MemoryKeeper.sln`이 있는 폴더)에서:

```bash
dotnet restore
dotnet build MemoryKeeper.sln -c Release -p:Platform=x64
```

App만:

```bash
dotnet build MemoryKeeper.App/MemoryKeeper.App.csproj -c Release -p:Platform=x64
```

개발 실행:

```bash
dotnet run --project MemoryKeeper.App/MemoryKeeper.App.csproj
```

App은 `RuntimeIdentifier=win-x64`이므로 architecture mismatch를 피하려면 x64 환경을 사용한다.

---

## 3. Test 방법

```bash
dotnet test MemoryKeeper.Tests/MemoryKeeper.Tests.csproj
```

또는:

```bash
dotnet test MemoryKeeper.sln
```

테스트 프로젝트: `net8.0`, xUnit (`MemoryKeeper.Tests.csproj`).

---

## 4. Publish 방법

프로필: `MemoryKeeper.App/Properties/PublishProfiles/FolderProfile.pubxml`

```bash
dotnet publish MemoryKeeper.App/MemoryKeeper.App.csproj -c Release -p:PublishProfile=FolderProfile
```

프로필 요약:

| 항목 | 값 |
|------|-----|
| Configuration | Release |
| Platform | x64 |
| RuntimeIdentifier | win-x64 |
| SelfContained | true |
| WindowsAppSDKSelfContained | true |
| PublishDir | `../MemoryKeeper.Release/` |
| PublishSingleFile | false |

산출물 예:

- `MemoryKeeper.Release/MemoryKeeper.exe`
- 종속 DLL / Windows App SDK 런타임 파일
- `appsettings.json` (CopyToPublishDirectory)

---

## 5. 최초 실행 요구사항

앱 기동 시 `SetupWizardService`가 설정을 검사한다.  
`App:SetupCompleted`가 없거나 Storage/PhotoRoot가 없으면 **Setup Wizard**가 표시된다.

| 단계 | 내용 | 저장 위치 |
|------|------|-----------|
| 1. Storage Root (Photo Root) | 사진 라이브러리 루트 폴더 | `TB_STORAGE.PhotoRoot` |
| 2. Home Location | 주소 또는 위도/경도 | `Travel:HomeLatitude`, `Travel:HomeLongitude`, `Travel:HomeAddress` (`TB_SETTING`) |
| 3. Google Maps API Key | **선택** — 없으면 지도 기능만 비활성 | `GoogleMaps:ApiKey` |
| 4. 완료 | Setup 완료 표시 | `App:SetupCompleted` = `true` |

완료 후 Home 화면으로 진입한다.  
이후에도 Settings에서 API Key / Home Location / Backup·Restore를 변경할 수 있다.

---

## 6. 문제 해결

### Publish exe 실행

**현재 (MK-041B 이후):** Release/Publish exe **MainWindow 정상 기동** 확인됨.

**확인:**

1. 시작 로그  
   `%LocalAppData%\MemoryKeeper\Logs\startup.log`  
   (`StartupDiagnostics`, 단계 `[1]`~`[6]`)
2. MK-041B **단계별 진단 MessageBox는 제거됨.** 기동 실패 시에만 오류 MessageBox + 로그 경로 표시.
3. Dev 빌드 실행:

```powershell
.\MemoryKeeper.App\bin\x64\Release\net8.0-windows10.0.19041.0\MemoryKeeper.exe
```

**재Publish:**

```bash
dotnet publish MemoryKeeper.App/MemoryKeeper.App.csproj -c Release -p:PublishProfile=FolderProfile
```

x86/ARM이 아닌 **win-x64** 산출물인지 확인한다.

### Migration 문제

- 기동 시 `DatabaseInitializer.InitializeAsync` → EF Core `MigrateAsync` 적용.
- Migration 파일: `MemoryKeeper.Infrastructure/Database/Migrations/`
- 스키마를 DB 도구로 직접 고치지 말고 Migration을 추가한다.
- 기존 DB와 모델이 어긋나면 로그/예외를 확인하거나, 개발용으로 Settings의 **Database 초기화**(SQLite 삭제 후 재Migrate)를 사용할 수 있다. **사진 원본은 삭제하지 않는다.**

### SQLite 위치

코드 (`App.DatabaseDirectory` + `SqliteConnectionFactory.DatabaseFileName`):

| 항목 | 경로 |
|------|------|
| DB 디렉터리 | `%LocalAppData%\MemoryKeeper\` |
| DB 파일 | `%LocalAppData%\MemoryKeeper\MemoryKeeper.db` |
| Thumbnail cache | `%LocalAppData%\MemoryKeeper\ThumbnailCache\` |
| Startup log | `%LocalAppData%\MemoryKeeper\Logs\startup.log` |
| Map HTML cache | `%LocalAppData%\MemoryKeeper\map-host\` |

사진 라이브러리 파일은 DB가 아니라 `TB_STORAGE.PhotoRoot` + `TB_MEDIA.RelativePath`에 있다.

---

## 관련 문서

- [PROJECT_CONTEXT.md](./PROJECT_CONTEXT.md)
- [CURRENT_STATUS.md](./CURRENT_STATUS.md)
- [NEXT_STEP_GUIDE.md](./NEXT_STEP_GUIDE.md)
- [DATABASE_SCHEMA.md](./DATABASE_SCHEMA.md)
