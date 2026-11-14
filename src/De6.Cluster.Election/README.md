# De6.Cluster.Election

Owner: Đức An.

Implement Bully rounds, eligibility, heartbeat and monotonic timeouts. Candidate selection NEVER opens ServingEnabled and cannot replace physical fencing. ElectionNotice is a draft message shape, not an algorithm. V12 section 12; T22-T26, T35-T36.

Specification: [V12](../../docs/De6_Thiet_Ke_Trien_Khai_V12.md). Team handoff and commands: [repository README](../../README.md).

Definition of done for future implementation: reviewed shared contracts, tests for owned V12 cases, error/uncertainty paths, and reproducible evidence. A successful template build does not satisfy business acceptance.

## Hướng dẫn bắt đầu

Đức An thực hiện theo các bước **A1–A6** trong [phân công chi tiết](../../docs/team/Phan_Cong_Nhom9.md#4-đức-an--bully-và-clustercontrol). Mỗi bước nêu file/API cần cài, cách tự kiểm và phần giao cho bạn khác. Các tên file bổ sung trong hướng dẫn là file cần tạo, không phải implementation đã có.

Đọc thêm mục 9–12 của tài liệu đó để ghép fixture, chạy luồng HOLD xuyên suốt, chuẩn bị PR và hiểu các trạng thái LocalApplied/Stable/FINAL. Cả năm người đều code, test và hoàn thiện phần lý thuyết riêng.
