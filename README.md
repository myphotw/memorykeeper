# Memory Keeper

개인 사진과 영상을 시간과 장소 기준으로 정리하고 관리하기 위한 로컬 기반 사진 아카이브 관리 프로그램입니다.

## 1. Project Overview

Memory Keeper의 목적은 사진을 단순히 저장하는 것이 아니라,

"내가 언제 어디에 갔었는지 쉽게 찾아볼 수 있는 개인 기억 저장소"

를 만드는 것입니다.

주요 관리 기준:

- 촬영 시간
- 촬영 장소
- 방문 기록
- 사진/영상 원본 보존

천체사진 관리와 같이 촬영 대상이나 통계 분석을 목적으로 하는 프로그램이 아니라,
일상 사진을 장기간 보관하고 빠르게 검색하기 위한 프로그램입니다.

---

# 2. Core Features

## Photo Management

- 사진 및 영상 통합 관리
- 원본 파일 보존
- 라이브러리 복사 방식 저장
- Hash 기반 중복 파일 검사
- 중복 결과 리포트 제공
- 사진 상세 정보 조회

---

## Automatic Classification

- 사진 Metadata 분석
- 촬영일 기반 분류
- GPS 기반 장소 분석
- 장소 범위 기반 자동 분류
- 미분류 사진 관리

---

## Place Management

- 방문 장소 관리
- 사용자 정의 장소 생성
- 장소명(Display Name) 관리
- 장소 범위 설정
- 장소 변경 및 재분류 지원

---

## Library Management

라이브러리 구조:
Library
└ Year
└ Country
└ Place
└ Media File


예:
Library
└ 2025
└ 일본
└ 오사카
└ IMG001.jpg


초기 분류가 어려운 파일:
Library
└ 2025
└ Unknown
└ 미분류
└ IMG001.jpg


---

# 3. Design Principles

## Original File First

원본 파일은 변경하거나 삭제하지 않습니다.

Memory Keeper는 원본을 기준으로 라이브러리를 구성하며,
사용자가 최종 확인 후 원본 파일을 관리합니다.

---

## Storage Independent

저장 위치는 특정 장치에 종속되지 않습니다.

지원 예정:

- Local Disk
- External HDD
- NAS

Storage Layer를 통해 다양한 저장 환경을 지원합니다.

---

## Maintainability First

장기간 사용할 개인 데이터 관리 프로그램이므로
빠른 개발보다 유지보수 가능한 구조를 우선합니다.

---

# 4. Technology Stack

## Client

- C#
- .NET 8
- WinUI 3

## Architecture

- MVVM Pattern
- Layered Architecture

## Database

- SQLite
- Entity Framework Core

## Image Metadata

- EXIF Metadata Extraction

## Development Tool

- Visual Studio
- Cursor AI

---

# 5. System Architecture


MemoryKeeper.App

    |
    |

MemoryKeeper.Application

    |
    |

MemoryKeeper.Domain

    |
    |

MemoryKeeper.Infrastructure

    |
    |

SQLite
File System
Storage Provider


---

# 6. Project Structure


MemoryKeeper

├ MemoryKeeper.App
│
├ MemoryKeeper.Domain
│
├ MemoryKeeper.Application
│
├ MemoryKeeper.Infrastructure
│
└ MemoryKeeper.Tests


---

# 7. Development Roadmap

## Phase 1 - MVP

목표:

사진 등록 및 조회가 가능한 기본 라이브러리 구축

구현:

- 저장소 설정
- 사진 Import
- Metadata 추출
- Hash 중복 검사
- SQLite 저장
- 라이브러리 생성
- 사진 조회
- 사진 상세 보기


## Phase 2 - Location Intelligence

구현:

- GPS 기반 장소 분류
- Google Maps 연동
- Place 관리
- 지도 기반 조회


## Phase 3 - Photo Organization

구현:

- 미완성 추억 관리
- 자동 그룹핑
- 장소 일괄 등록
- 사진 이동
- 재분류


## Phase 4 - Expansion

구현:

- 모바일 확장
- 외부 공유
- 백업 기능
- 추가 Storage 지원

---

# 8. Development Rules

- MVVM 구조 준수
- View에 Business Logic 작성 금지
- Service Layer 사용
- Database 직접 접근 금지
- Repository Pattern 사용
- Domain Layer는 외부 기술에 의존하지 않음
- 기능 추가 전 Architecture 영향 검토

---

# 9. Project Status

**Current Status (2026-08-05): Version 2.0.0**

- TC-Backend V1.0 Gallery / Upload API 연동 (M2–M8)
- M9 Release Cleanup: Deprecated SQLite UI 서비스·UseBackendUpload 제거
- Import → Backend Upload only; Gallery/Search/Map/Timeline/Statistics → Backend API
- 상세: `CHANGELOG.md`, `Docs/MemoryKeeper_V2_Migration.md`, `Docs/ARCHITECTURE.md`

**Next Step:**

- Import / Gallery / Search / Timeline / Statistics / Map E2E 수동 검증
- Publish 산출물 재생성 (`Docs/NEXT_STEP_GUIDE.md`)
