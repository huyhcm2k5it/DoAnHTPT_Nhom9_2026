# Lưu trữ SQL của Huy

Migration 001 tạo bảy bảng nghiệp vụ; migration 002 nâng schema lên v2 và thêm bảng manifest phục hồi. Seed có kiểm soát ở seed/001_seed.sql. SqlDatabaseBootstrap nhúng các script, chạy transaction và kiểm checksum từng version; có thể nâng DB v1 đúng checksum lên v2 và không reset DatasetId/NodeId khác. Seed không cấp quyền writer.

Chạy scripts/init-huy-sql.ps1 và scripts/test-huy-sql.ps1 theo [runbook](../docs/implementation/HUY_WEEK1_RUNBOOK.md). Mỗi server dùng DB riêng. Client không giữ SQL credentials. Oracle và rebuild projection H5 đã được cài, có kiểm thử SQL; xem docs/implementation/HUY_WEEK2_RUNBOOK.md. Full cluster recovery/promotion vẫn cần Control và các module liên quan.
