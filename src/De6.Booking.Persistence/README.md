# De6.Booking.Persistence

Owner: Huy.

Implement SQL Server 2022 migrations and local atomic Apply, replay, stable snapshot reads and immutable results. No SQL connection, package, schema or health check is supplied. See database/ and V12 sections 9, 11, 14, 16; T01, T03-T09, T13-T21, T28-T30.

Specification: [V12](../../docs/De6_Thiet_Ke_Trien_Khai_V12.md). Team handoff and commands: [repository README](../../README.md).

Definition of done for future implementation: reviewed shared contracts, tests for owned V12 cases, error/uncertainty paths, and reproducible evidence. A successful template build does not satisfy business acceptance.

## Hướng dẫn bắt đầu

Huy thực hiện theo các bước **H1–H6** trong [phân công chi tiết](../../docs/team/Phan_Cong_Nhom9.md#2-huy--sqlpersistence-và-điều-phối-tích-hợp). Mỗi bước nêu file/API cần cài, cách tự kiểm và phần giao cho bạn khác. Các tên file bổ sung trong hướng dẫn là file cần tạo, không phải implementation đã có.

Đọc thêm mục 9–12 của tài liệu đó để ghép fixture, chạy luồng HOLD xuyên suốt, chuẩn bị PR và hiểu các trạng thái LocalApplied/Stable/FINAL. Cả năm người đều code, test và hoàn thiện phần lý thuyết riêng.
