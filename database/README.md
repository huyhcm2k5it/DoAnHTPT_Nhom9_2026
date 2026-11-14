# Lưu trữ SQL của Huy

Schema v1 ở migrations/001_initial.sql, seed có kiểm soát ở seed/001_seed.sql. SqlDatabaseBootstrap nhúng hai script, chạy transaction và version/checksum, không reset dataset/NodeId khác. Seed không cấp quyền writer.

Chạy scripts/init-huy-sql.ps1 và scripts/test-huy-sql.ps1 theo [runbook](../docs/implementation/HUY_WEEK1_RUNBOOK.md). Mỗi server dùng DB riêng. Client không giữ SQL credentials. Thư mục oracle hiện vẫn là công việc H5 tuần 2, chưa có full recovery/rebuild hoặc oracle toàn cụm.
