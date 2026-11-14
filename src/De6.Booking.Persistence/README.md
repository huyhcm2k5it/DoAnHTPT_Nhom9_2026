# De6.Booking.Persistence

Huy phụ trách. H1–H4 có SqlDatabaseBootstrap, SqlBookingStore, SqlApplyTransaction, SqlReplicaApply và SqlSnapshotReader. SQL Apply/log/result/NodeState commit nguyên tử; append/replay kiểm tra quyền riêng; snapshot cần gate và expected-index khớp. Không có FINAL hoặc tự cấp quyền phục vụ ở tầng này.

Xem [hướng dẫn chạy và tích hợp](../../docs/implementation/HUY_WEEK1_RUNBOOK.md), [kế hoạch](../../docs/implementation/HUY_WEEK1_PLAN.md), [V12](../../docs/De6_Thiet_Ke_Trien_Khai_V12.md). Gate và decision engine thật do Đức An/Gia Quân cung cấp; fixture trong test không phải implementation production. H5–H6/rebuild/full recovery/Host wiring còn phải làm.
