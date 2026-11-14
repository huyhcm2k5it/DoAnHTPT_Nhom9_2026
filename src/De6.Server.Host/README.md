# De6.Server.Host

Owner: Huy (tích hợp cả nhóm).

Runnable safe composition shell: validates config, prints template status and RECOVERING/ServingEnabled=false, waits for Ctrl+C unless --check. No TCP listener, SQL check, election, replication or booking processing. Add module composition only after ownership gates and V12 acceptance evidence.

Specification: [V12](../../docs/De6_Thiet_Ke_Trien_Khai_V12.md). Team handoff and commands: [repository README](../../README.md).

Definition of done for future implementation: reviewed shared contracts, tests for owned V12 cases, error/uncertainty paths, and reproducible evidence. A successful template build does not satisfy business acceptance.

## Hướng dẫn bắt đầu

Huy điều phối, chủ từng module cùng tích hợp thực hiện theo các bước **H6 và G0–G5** trong [phân công chi tiết](../../docs/team/Phan_Cong_Nhom9.md#7-cổng-tích-hợp-g0g5). Mỗi bước nêu file/API cần cài, cách tự kiểm và phần giao cho bạn khác. Các tên file bổ sung trong hướng dẫn là file cần tạo, không phải implementation đã có.

Đọc thêm mục 9–12 của tài liệu đó để ghép fixture, chạy luồng HOLD xuyên suốt, chuẩn bị PR và hiểu các trạng thái LocalApplied/Stable/FINAL. Cả năm người đều code, test và hoàn thiện phần lý thuyết riêng.
