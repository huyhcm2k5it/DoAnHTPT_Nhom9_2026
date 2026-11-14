# De6.Contracts

Owner: Gia Quân (phối hợp cả nhóm).

Shared DTOs, typed IDs/enums and config validation. Protocol records are draft shapes; BookingCanonical, BookingEventCodec and storage contracts now cover the Persistence v1 fixture; other network/login/envelope contracts remain unfinished. Gia Quân and every consumer must review these additions before G0 merge. Changes require all consumers to review V12 sections 7, 8, 15, 16.

Specification: [V12](../../docs/De6_Thiet_Ke_Trien_Khai_V12.md). Team handoff and commands: [repository README](../../README.md).

Definition of done for future implementation: reviewed shared contracts, tests for owned V12 cases, error/uncertainty paths, and reproducible evidence. A successful template build does not satisfy business acceptance.

## Hướng dẫn bắt đầu

Gia Quân cùng các bên dùng dữ liệu thực hiện theo các bước **Q1–Q3** trong [phân công chi tiết](../../docs/team/Phan_Cong_Nhom9.md#3-gia-quân--domaincoordinator-và-hợp-đồng-chung). Mỗi bước nêu file/API cần cài, cách tự kiểm và phần giao cho bạn khác. Các tên file bổ sung trong hướng dẫn là file cần tạo, không phải implementation đã có.

Đọc thêm mục 9–12 của tài liệu đó để ghép fixture, chạy luồng HOLD xuyên suốt, chuẩn bị PR và hiểu các trạng thái LocalApplied/Stable/FINAL. Cả năm người đều code, test và hoàn thiện phần lý thuyết riêng.
