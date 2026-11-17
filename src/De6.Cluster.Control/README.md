# De6.Cluster.Control

Owner: Đức An (phối hợp Huy về store/replay và Văn Khoa về kết nối peer).

`ClusterControl` implements both the Domain `IAuthorityGate` and SQL `IStoreAccessGate` contracts so Host can supply one shared instance when it composes the booking service. It starts every process in `RECOVERING` with `StableIndex=0`, restricts mutation, append, snapshot, replay and maintenance leases by role/epoch/index, and closes admission before maintenance. Election results are recorded separately from serving authority. Week-two core adds promotion/bootstrap/rejoin planning, physical-fence verification ports, manifest readback verification and SQL-backed durable manifests. `ApplyVerifiedActivation` requires an authorization minted by the verifier, and the node's local applied index must equal the manifest target. The current Host still only prints the closed boot state because it does not compose recovery, election, transport or a booking service. V12 sections 5, 12-14, 16-17.

Specification: [V12](../../docs/De6_Thiet_Ke_Trien_Khai_V12.md). Team handoff and commands: [repository README](../../README.md).

Control/planner code is covered by `tests/unit/De6.Cluster.Control.Tests`; SQL manifest persistence has a separate integration harness. No production physical-fence adapter or Host recovery workflow is wired yet. The Host/transport path and three-node SQL failure tests remain integration work, so passing isolated tests does not satisfy cluster acceptance.

## Công cụ cần có

- .NET 10 SDK để build và chạy harness; không cần cài thư viện riêng cho `Cluster.Control` hoặc `Cluster.Election`.
- SQL Server chỉ cần khi chạy SQL integration của manifest/replay hoặc ghép database thật. SSMS là tùy chọn để xem database.
- Test tự tạo chứng thư tạm để kiểm pin peer. Chứng thư/TLS production chỉ cần khi Host/transport thật được nối.

## Hướng dẫn bắt đầu

Đức An thực hiện theo các bước **A1–A6** trong [phân công chi tiết](../../docs/team/Phan_Cong_Nhom9.md#4-đức-an--bully-và-clustercontrol). Mỗi bước nêu file/API cần cài, cách tự kiểm và phần giao cho bạn khác. Các tên file bổ sung trong hướng dẫn là file cần tạo, không phải implementation đã có.

Đọc phần cổng ghép và luồng HOLD trong tài liệu đó để phối hợp fixture và hiểu các trạng thái LocalApplied/Stable/FINAL. Cả bốn người đều code, test và hoàn thiện phần lý thuyết riêng.
