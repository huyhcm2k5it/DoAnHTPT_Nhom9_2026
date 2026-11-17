# De6.Cluster.Control

Owner: Đức An (phối hợp Huy về store/replay và Văn Khoa về kết nối peer).

Own serving gates and coordinated recovery/activation. Only immutable BootstrapState exists: RECOVERING, ServingEnabled=false, StableIndex=0 every boot. No activation, promotion, manifest, fencing, SQL or recovery is implemented. Future ClusterControl must synchronize grants and administrative transitions. V12 sections 5, 12-14, 16-17.

Specification: [V12](../../docs/De6_Thiet_Ke_Trien_Khai_V12.md). Team handoff and commands: [repository README](../../README.md).

Definition of done for future implementation: reviewed shared contracts, tests for owned V12 cases, error/uncertainty paths, and reproducible evidence. A successful template build does not satisfy business acceptance.

## Hướng dẫn bắt đầu

Đức An thực hiện theo các bước **A1–A6** trong [phân công chi tiết](../../docs/team/Phan_Cong_Nhom9.md#4-đức-an--bully-và-clustercontrol). Mỗi bước nêu file/API cần cài, cách tự kiểm và phần giao cho bạn khác. Các tên file bổ sung trong hướng dẫn là file cần tạo, không phải implementation đã có.

Đọc phần cổng ghép và luồng HOLD trong tài liệu đó để phối hợp fixture và hiểu các trạng thái LocalApplied/Stable/FINAL. Cả bốn người đều code, test và hoàn thiện phần lý thuyết riêng.
