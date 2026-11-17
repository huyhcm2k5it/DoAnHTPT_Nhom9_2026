# De6.Cluster.Control

Owner: Đức An (phối hợp Huy về store/replay và Văn Khoa về kết nối peer).

`ClusterControl` implements both the Domain `IAuthorityGate` and SQL `IStoreAccessGate` contracts so Host can supply one shared instance when it composes the booking service. It starts every process in `RECOVERING` with `StableIndex=0`, restricts mutation, append, snapshot, replay and maintenance leases by role/epoch/index, and closes admission before maintenance. Election results are recorded separately from serving authority. Verified activation remains internal to this assembly for the recovery coordinator work in week two. The current Host only prints the closed boot state because it does not yet compose a booking service. V12 sections 5, 12-14, 16-17.

Specification: [V12](../../docs/De6_Thiet_Ke_Trien_Khai_V12.md). Team handoff and commands: [repository README](../../README.md).

Week-one code is covered by `tests/unit/De6.Cluster.Control.Tests`. Activation, distributed manifest, physical fencing and Host/SQL service composition remain for later integration. Passing the isolated tests does not satisfy cluster acceptance.

## Hướng dẫn bắt đầu

Đức An thực hiện theo các bước **A1–A6** trong [phân công chi tiết](../../docs/team/Phan_Cong_Nhom9.md#4-đức-an--bully-và-clustercontrol). Mỗi bước nêu file/API cần cài, cách tự kiểm và phần giao cho bạn khác. Các tên file bổ sung trong hướng dẫn là file cần tạo, không phải implementation đã có.

Đọc phần cổng ghép và luồng HOLD trong tài liệu đó để phối hợp fixture và hiểu các trạng thái LocalApplied/Stable/FINAL. Cả bốn người đều code, test và hoàn thiện phần lý thuyết riêng.
