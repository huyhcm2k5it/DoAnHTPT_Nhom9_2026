# De6.Contracts

Owner: Gia Quân (phối hợp cả nhóm).

Shared DTOs, typed IDs/enums and config validation. `CanonicalProtocol` now implements command, seed and event hashes plus canonical result/event bytes. Fixed byte fixtures run in `tests/unit/De6.Domain.UnitTests`. These contracts still need review against the SQL store, replica sender and client before G0 can be closed.

Specification: [V12](../../docs/De6_Thiet_Ke_Trien_Khai_V12.md). Team handoff and commands: [repository README](../../README.md).

Definition of done for future implementation: reviewed shared contracts, tests for owned V12 cases, error/uncertainty paths, and reproducible evidence. A successful template build does not satisfy business acceptance.

## Hướng dẫn bắt đầu

Gia Quân cùng các bên dùng dữ liệu thực hiện theo các bước **Q1–Q3** trong [phân công chi tiết](../../docs/team/Phan_Cong_Nhom9.md#3-gia-quân--domaincoordinator-và-hợp-đồng-chung). Mỗi bước nêu file/API cần cài, cách tự kiểm và phần giao cho bạn khác. Các tên file bổ sung trong hướng dẫn là file cần tạo, không phải implementation đã có.

Đọc phần cổng ghép và luồng HOLD trong tài liệu đó để phối hợp fixture và hiểu các trạng thái LocalApplied/Stable/FINAL. Cả bốn người đều code, test và hoàn thiện phần lý thuyết riêng.
