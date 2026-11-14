# De6.Cluster.Replication

Owner: Nguyên Chương.

Implement immutable event delivery, exact receipt matching, duplicate/GAP/conflict handling and catch-up. FINAL requires primary plus one backup durable storage. IReplicaSender has no implementation. V12 sections 10, 11, 14; T10-T12, T15-T16, T20-T21, T27-T30.

Specification: [V12](../../docs/De6_Thiet_Ke_Trien_Khai_V12.md). Team handoff and commands: [repository README](../../README.md).

Definition of done for future implementation: reviewed shared contracts, tests for owned V12 cases, error/uncertainty paths, and reproducible evidence. A successful template build does not satisfy business acceptance.

## Hướng dẫn bắt đầu

Nguyên Chương thực hiện theo các bước **C1–C6** trong [phân công chi tiết](../../docs/team/Phan_Cong_Nhom9.md#5-nguyên-chương--replication-và-transporttcp). Mỗi bước nêu file/API cần cài, cách tự kiểm và phần giao cho bạn khác. Các tên file bổ sung trong hướng dẫn là file cần tạo, không phải implementation đã có.

Đọc thêm mục 9–12 của tài liệu đó để ghép fixture, chạy luồng HOLD xuyên suốt, chuẩn bị PR và hiểu các trạng thái LocalApplied/Stable/FINAL. Cả năm người đều code, test và hoàn thiện phần lý thuyết riêng.
