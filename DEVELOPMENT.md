# Memory Keeper Development Guide

## 1. Development Philosophy

Memory Keeper는 장기간 사용할 개인 사진 아카이브 시스템입니다.

빠른 기능 구현보다 다음을 우선합니다.

- 유지보수성
- 확장성
- 안정성
- 데이터 보존
- 명확한 책임 분리

모든 개발은 현재 Architecture와 Domain Model을 기준으로 진행합니다.

---

# 2. Technology Standard

## Platform

- Windows Desktop Application

## Language

- C#

## Framework

- .NET 8 LTS
- WinUI 3

## Database

- SQLite
- Entity Framework Core

## Architecture

- MVVM
- Layered Architecture
- Repository Pattern

---

# 3. Solution Architecture Rules

Project Structure:
MemoryKeeper

├ MemoryKeeper.App

├ MemoryKeeper.Domain

├ MemoryKeeper.Application

├ MemoryKeeper.Infrastructure

└ MemoryKeeper.Tests


---

# 4. Layer Responsibility

## App Layer

Role:

- UI
- User Interaction
- Navigation
- ViewModel

Allowed:

- View
- ViewModel
- UI State Management

Forbidden:

- Database Access
- File System Access
- Business Logic

---

## Domain Layer

Role:

Core business model.

Contains:

- Entity
- Enum
- Domain Interface

Examples:


Media
Place
Storage
Job


Rules:

- External Library Dependency 금지
- UI Dependency 금지
- Database Dependency 금지

Domain은 순수하게 유지합니다.

---

## Application Layer

Role:

Business Workflow 처리.

Contains:

- Service
- DTO
- Use Case

Examples:


ImportService

MediaService

StorageService

ClassificationService


Rules:

Business Logic은 Application Layer에서 처리합니다.

---

## Infrastructure Layer

Role:

External System 구현.

Contains:

- Database
- File System
- Storage Provider
- External API

Examples:


SqliteRepository

FileStorageProvider

NasStorageProvider

MetadataExtractor


---

# 5. MVVM Rules

## View

담당:

- 화면 표시
- 사용자 입력

금지:

- Business Logic 작성
- Database 호출
- File 처리

---

## ViewModel

담당:

- View 상태 관리
- Command 처리
- Service 호출

---

## Service

담당:

- 업무 처리
- Domain 객체 조작
- Repository 호출

---

# 6. Database Rules

Database 접근은 반드시 Repository를 사용합니다.

금지:


ViewModel
↓
DbContext


허용:


ViewModel

↓

Service

↓

Repository

↓

DbContext


---

# 7. Entity Rules

Entity는 Database 저장 목적과 Business 의미를 함께 가집니다.

예:


Media

Place

Storage


Entity 변경 시:

- Database 영향 검토
- Migration 필요 여부 확인
- 관련 Service 영향 확인

후 수정합니다.

---

# 8. File Management Rules

## Original File

원본 파일은 변경하지 않습니다.

원칙:

- 이동 금지
- 삭제 금지
- 수정 금지

Memory Keeper는 원본을 기준으로 라이브러리를 생성합니다.

---

## Library File

관리 대상:


Library

{Year}

└ {Country}

 └ {Place}

     └ Media

---

# 9. Storage Rules

저장 위치는 특정 장치에 종속하지 않습니다.

지원:

- Local Disk
- External HDD
- NAS

Storage Provider Pattern 사용.

예:


IStorageProvider

├ LocalStorageProvider

└ NasStorageProvider


---

# 10. Async Programming Rules

파일 처리 및 대량 작업은 반드시 비동기로 처리합니다.

대상:

- Import
- Hash 생성
- Metadata 분석
- Thumbnail 생성
- File Copy

UI Thread Block 금지.

---

# 11. Error Handling Rules

예외 발생 시:

- 사용자 메시지 제공
- Log 기록
- 작업 상태 저장

Silent Failure 금지.

---

# 12. Naming Convention

## Class

PascalCase

Example:


MediaService
StorageRepository


---

## Method

PascalCase

Example:


ImportMediaAsync()
GetLibraryAsync()


---

## Variable

camelCase

Example:


mediaList
storagePath


---

# 13. Code Quality Rules

작성 코드:

- 의미 있는 변수명 사용
- 중복 코드 최소화
- 하나의 클래스는 하나의 책임
- 주석보다 명확한 코드 우선
- 불필요한 Framework 종속 제거

---

# 14. Development Process

기능 개발 순서:

1. 요구사항 확인

2. Domain 영향 검토

3. Interface 설계

4. Service 구현

5. Repository 구현

6. UI 연결

7. Test

8. Refactoring


---

# 15. AI Coding Rules

Cursor AI 사용 시:

- 기존 Architecture 유지
- 임의 구조 변경 금지
- 새로운 Library 추가 전 검토
- 기존 Entity 변경 전 영향 분석
- 하나의 Task 단위로 개발

대규모 코드는 한번에 생성하지 않습니다.

---

# 16. Current Development Phase

Current:

Phase 1 MVP

Goal:

사진 Import 및 조회 기능 구현


Included:

- Storage 설정
- Media Import
- Metadata Extraction
- Hash Duplicate Check
- SQLite Storage
- Gallery View


Excluded:

- Google Maps
- Automatic Place Classification
- Mobile Application
- Advanced Report
