# De6.Cluster.Replication

Gia Quân phụ trách sender và đối chiếu receipt. Huy phụ trách receiver gọi SQL Apply. Đức An cung cấp peer authorization và RecoveryContext.

Hiện thực bao gồm:
- `SynchronousReplicaSender`: Hiện thực `IReplicaSender`, gửi `PersistedEvent` tới các backup node, kiểm tra receipt nghiêm ngặt (NodeId, Epoch, LogIndex, EventId, Hash) để đạt Quorum 2/3 (Primary + ít nhất 1 Backup bền vững) trước khi nâng `StableIndex`.
- `NetworkPeerChannel`: Hiện thực `IPeerChannel` qua stream `IFrameChannel` (TCP/TLS framing) theo chuẩn wire protocol V12.
- `ReplicaReceiver`: Tiếp nhận `APPEND_APPLY` và `REPLAY_ENTRY` từ peer đã xác thực, ủy quyền vào `IBookingStore`.

Đặc tả: [V12](../../docs/De6_Thiet_Ke_Trien_Khai_V12.md). Handoff và ma trận kiểm thử: [Phân công](../../docs/team/Phan_Cong_Nhom9.md).

Definition of done for future implementation: reviewed shared contracts, tests for owned V12 cases, error/uncertainty paths, and reproducible evidence. A successful template build does not satisfy business acceptance.

## Hướng dẫn bắt đầu

Các phần sender, receiver, replay và fault test được giao theo module trong [phân công chi tiết](../../docs/team/Phan_Cong_Nhom9.md). Các tên file dự kiến trong hướng dẫn chưa đồng nghĩa implementation đã có.

Đọc phần cổng ghép và luồng HOLD trong tài liệu đó để dùng cùng fixture và phân biệt LocalApplied/Stable/FINAL. Cả bốn thành viên đều code, test và hoàn thiện phần lý thuyết gắn với code.
