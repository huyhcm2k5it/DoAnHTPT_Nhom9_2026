# Migration có phiên bản

001_initial.sql tạo bảy bảng nghiệp vụ V12. 002_recovery_manifest.sql nâng NodeState lên schema 2 và thêm manifest phục hồi có kiểm tra một run đang dở trên mỗi database. Runner `SqlDatabaseBootstrap` tạo DB mới hoặc nâng DB schema 1 lên schema 2 trong transaction; mỗi version có checksum riêng. DB có bảng lạ, checksum sai hoặc version không hỗ trợ bị từ chối. Không sửa migration đã công bố để nâng version âm thầm.

Xem [runbook Huy](../../docs/implementation/HUY_WEEK1_RUNBOOK.md). Không chạy lặp file DDL thô trong SSMS; dùng runner để có kiểm tra version/idempotence.
