# De6.Cluster.Replication

Gia Quân phụ trách sender và đối chiếu receipt. Huy phụ trách receiver gọi SQL Apply. Đức An cung cấp peer authorization và RecoveryContext.

Implement immutable event delivery, exact receipt matching, duplicate/GAP/conflict handling and catch-up. FINAL requires primary plus one backup durable storage. IReplicaSender has no implementation. V12 sections 10, 11, 14; T10-T12, T15-T16, T20-T21, T27-T30.

Specification: [V12](../../docs/De6_Thiet_Ke_Trien_Khai_V12.md). Team handoff and commands: [repository README](../../README.md).

Definition of done for future implementation: reviewed shared contracts, tests for owned V12 cases, error/uncertainty paths, and reproducible evidence. A successful template build does not satisfy business acceptance.

## Hướng dẫn bắt đầu

Các phần sender, receiver, replay và fault test được giao theo module trong [phân công chi tiết](../../docs/team/Phan_Cong_Nhom9.md). Các tên file dự kiến trong hướng dẫn chưa đồng nghĩa implementation đã có.

Đọc phần cổng ghép và luồng HOLD trong tài liệu đó để dùng cùng fixture và phân biệt LocalApplied/Stable/FINAL. Cả bốn thành viên đều code, test và hoàn thiện phần lý thuyết gắn với code.
