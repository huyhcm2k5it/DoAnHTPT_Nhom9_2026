# Giao tác ACID và điều khiển tương tranh của Huy

Nguồn học: ND11, sách Nguyễn Xuân Anh 2014 mục 2.5/2.5.1/2.5.3, trang in 74–77. Đây là phần liên hệ implementation Huy với lý thuyết; Huy cần đọc, tự giải thích và bổ sung phần bảo vệ theo rubric lớp. Không coi nội dung này là xác nhận CLO chính thức.

## ACID trong transaction Apply

Atomicity: reservation/seat/ticket, OperationLog, RequestResults và NodeState cùng một SQL transaction. Test trước commit gây lỗi rồi kiểm tra không có nửa Apply; lỗi sau commit vẫn thấy event/result đã tồn tại.

Consistency: FK/UNIQUE/CHECK trong migration cùng ValidateDecision kiểm tra quan hệ ghế/reservation/chủ/vé. Constraint đơn bảng không tự chứng minh tất cả bất biến xuyên bảng hoặc toàn cụm; oracle SQL và replay từ log bổ sung kiểm tra.

Isolation: SERIALIZABLE và khóa UPDLOCK/HOLDLOCK trên NodeState sắp thứ tự các truy cập local. Không dùng NOLOCK. Đây là hàng rào local; coordinator của Gia Quân còn phải sắp thứ tự toàn bộ thao tác và giữ grant tới Stable hoặc đóng cụm.

Durability: SqlClient chỉ trả Apply sau commit; từ chối chế độ forced delayed durability. datetime2(7) được bind rõ để event và dữ liệu SQL giữ nguyên độ chính xác UTC. SQL commit local chưa đủ FINAL: Gia Quân kiểm receipt ACK sau khi backup commit bền vững rồi coordinator mới công bố.

## Trace thực hiện

HOLD: kiểm tra phiên/command tại lớp trên → grant qua gate → BEGIN → đọc NodeState/RequestId/ghế → decision engine → ghi projection/event/result/head → COMMIT → trả PersistedEvent cho coordinator. Replication/Stable/FINAL chưa thuộc lời hứa của store.

Mất outcome: lỗi trước commit rollback; lỗi sau commit đóng gate và ném StorageUncertainException. InspectLocalRequestAsync chỉ giúp quản trị tìm chứng cứ local; không tự mở lại quyền, không tạo event mới để thử lại. Client timeout không có nghĩa operation thất bại.

## Oracle và rebuild

AuditLocalAsync kiểm tra chain hash/canonical/prefix, dựng projection tạm từ log và so với các bảng hiện hành. Digest SHA256 sắp theo khóa, chuẩn hóa GUID/null/UTC và giữ nguyên result bytes. LocalApplied K khác StableIndex và FINAL.

GetFrozenPrefixAsync/RebuildProjectionAsync cần maintenance lease của Control. Rebuild kiểm tra log trước, sửa projection/result trong một transaction và giữ nguyên OperationLog/NodeId/authority. Hash conflict hoặc K sai thì dừng. Đây là đối soát ở mốc tĩnh, không phải thuật toán snapshot toàn cục ND6.

## Code và kiểm thử để bảo vệ

- [Migration](../../database/migrations/001_initial.sql), [Apply](../../src/De6.Booking.Persistence/SqlApplyTransaction.cs), [transaction và dedup](../../src/De6.Booking.Persistence/SqlBookingStore.cs).
- [Snapshot](../../src/De6.Booking.Persistence/SqlSnapshotReader.cs), [oracle/rebuild](../../src/De6.Booking.Persistence/SqlMaintenance.cs).
- [SQL tests](../../tests/integration/De6.Booking.Persistence.Tests/Program.cs), [fault/cancellation/durability](../../tests/integration/De6.Booking.Persistence.Tests/AdvancedCases.cs), [review regressions](../../tests/integration/De6.Booking.Persistence.Tests/ReviewRegressionCases.cs), [maintenance tests](../../tests/integration/De6.Booking.Persistence.Tests/MaintenanceCases.cs).
- [Chạy tuần 1](../implementation/HUY_WEEK1_RUNBOOK.md), [chạy tuần 2](../implementation/HUY_WEEK2_RUNBOOK.md).

Các test dùng SQL thật trên database tạm và gate/decision fixture trong test. Chưa thay được nghiệm thu T01–T36 trên cluster, SDK và Control thật; không gọi fault observer là đã kill process hoặc gây partition. Máy kiểm thử là SQL Server 2025 Express, không ghi thành đã chạy engine 2022.

Huy nên demo một HOLD/CONFIRM, một duplicate RequestId, một lỗi trước/sau commit và một corruption được oracle phát hiện/rebuild sửa; trỏ đúng lệnh test, code và dữ liệu của lượt demo. Giải thích vì sao UNIQUE không thay coordinator và vì sao local commit không thay ACK backup.
