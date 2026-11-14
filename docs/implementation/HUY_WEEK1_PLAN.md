# Triển khai Huy tuần 1

Phạm vi H1–H4 theo phân công và V12 mục 7–11, 14, 16. Nhánh `feature/huy-database`; không triển khai coordinator, bầu chọn, replication mạng hoặc tự mở quyền bán vé.

## Quyết định triển khai

- Dùng Microsoft.Data.SqlClient 7.1.2, SQL Server 2022 trở lên. Máy kiểm thử hiện có SQL Server 2025 Express; phải báo đúng môi trường, không gọi là đã kiểm thử trên 2022.
- Giữ bảy bảng V12. Phiên bản/checksum migration lưu bằng extended properties của database, không thêm bảng nghiệp vụ khác. Seed chỉ tạo dataset mới trong DB chưa có dữ liệu; chạy lại cùng dataset/node là idempotent, khác thì từ chối.
- Primary gọi decision engine được truyền vào; engine chạy đồng bộ trên trạng thái đọc trong cùng transaction. Huy không thay Gia Quân cài toàn bộ nghiệp vụ. Fixture engine chỉ nằm trong test.
- Contracts bổ sung after-images, booking event và các interface store/gate/decision. Encoder có thứ tự cố định và fixture; cần Gia Quân cùng các bên review trước merge G0.
- Store bắt buộc nhận gate do Control/coordinator cung cấp, không có gate mở mặc định. Lease phải đồng bộ với freeze và giữ suốt transaction; store không tự cấp authority, tăng StableIndex hoặc trả FINAL.
- SQL dùng SERIALIZABLE, khóa hàng NodeState trước truy cập projection/log/result. Snapshot là dữ liệu và mốc nhất quán tại trạng thái hiện tại, không phải snapshot lịch sử K. Đọc nghiệp vụ cần gate và expected AppliedIndex khớp.
- Từ chối database bật forced delayed durability. Không giữ SQL transaction khi chờ TCP. Mọi lỗi SQL/commit chưa rõ phải báo gate đóng và ném lỗi cần đối soát; không tự retry mutation.
- Append và replay chung lõi transaction nhưng tách kiểm tra authority. Replay chỉ trên node đóng gate với context được cài bởi Control; không đổi NodeId, AcceptedEpoch/Leader hoặc ID/thời gian lịch sử.

## H1 Schema và seed

Tạo migration/seed và bootstrap runner; test SQL thật cho đủ bảng/ràng buộc, 10 ghế, seed lại, dataset/node sai, database có dữ liệu khác và migration bị đổi. Chạy test thất bại trước khi cài runner.

## H2 Apply primary

Cài transaction đọc state → dedup → gọi engine → kiểm tra decision → lưu event/result/projection/NodeState → commit. Test HOLD/CONFIRM/CANCEL qua fixture, NO_CHANGE, owner/identity conflict, rollback trước commit, concurrent duplicate và full durable mode. Không có FINAL ở tầng store.

## H3 Append và replay

Cài kiểm tra event/canonical/hash/before-version, authority, index/prefix, duplicate/GAP/conflict và recovery context. Test trên DB backup thật, giữ bytes/ID/UTC, replay lịch sử epoch cũ và từ chối không đúng quyền.

## H4 Snapshot và commit chưa rõ

Cài đọc ghế/result/reservation đúng owner cùng mốc; từ chối state chưa Stable qua gate và expected-index sai. Fault hook trước/sau commit để chứng minh rollback hoặc event đã tồn tại nhưng caller chưa biết; gate bị đóng, không tự tạo event mới.

## Bàn giao

Thêm project test SQL tích hợp vào solution, script riêng chạy SQL rõ ràng, unit/codec tests vào CI và tài liệu chạy lại. Baseline 23 smoke assertions phải giữ xanh. Ma trận T01–T36 không tự đánh PASS: chỉ ghi bằng chứng phần Persistence, các case cần cluster thật còn chưa nghiệm thu. Một reviewer độc lập xem toàn nhánh sau cùng; xử lý lỗi quan trọng bằng test RED → GREEN.

## Nguồn kỹ thuật

[SqlClient trên NuGet](https://www.nuget.org/packages/Microsoft.Data.SqlClient/7.1.2), [Microsoft về transaction durability](https://learn.microsoft.com/en-us/sql/relational-databases/logs/control-transaction-durability), [V12](../De6_Thiet_Ke_Trien_Khai_V12.md), [phân công](../team/Phan_Cong_Nhom9.md).
