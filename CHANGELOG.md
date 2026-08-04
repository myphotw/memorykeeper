# Changelog

## 2.0.0 — 2026-08-05

### Changed
- Gallery / Search / Timeline / Map / Statistics read paths use TC-Backend Gallery API.
- Import always uses TC-Backend Upload + Upload Job polling (`UploadMonitorService`).
- Home statistics overlay uses Backend `GetStatisticsAsync`.

### Removed (M9 Release Cleanup)
- `UseBackendUpload` / `BackendUploadOptions`
- SQLite Import pipeline from `MediaImportService`
- Deprecated SQLite UI services: `MemorySearchService`, `VisitRecordQueryService`, `GalleryHierarchyService`, `HomeDashboardService`
- Unused search analyzer: `IMemorySearchAnalyzer`, `RuleBasedMemorySearchAnalyzer`

### Kept (compile-required local paths)
- `PhotoDetailService` — local writes (favorite / place / memo / delete)
- `MediaRepository` / `IMediaRepository` — Place / Tag / Pending / write paths
- Watcher and non-Gallery SQLite repositories

### API stack (V2)
- `GalleryApiRepository`
- `UploadApiRepository`
- `UploadJobApiRepository`
- `UploadMonitorService`
- `GalleryBackendBridge`
