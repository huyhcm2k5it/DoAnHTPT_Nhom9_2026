# Migration có phiên bản

001_initial.sql tạo bảy bảng V12 và các ràng buộc. Runner SqlDatabaseBootstrap thực hiện DDL + seed trong cùng transaction, ghi schema version/checksum bằng database extended properties, dùng applock để ngăn hai lần init đồng thời. DB có bảng lạ hoặc marker khác bị từ chối. Không sửa migration đã công bố để nâng version âm thầm.

Xem [runbook Huy](../../docs/implementation/HUY_WEEK1_RUNBOOK.md). Không chạy lặp file DDL thô trong SSMS; dùng runner để có kiểm tra version/idempotence.
