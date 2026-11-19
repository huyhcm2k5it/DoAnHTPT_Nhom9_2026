# De6.Cluster.Election

Owner: Đức An.

`BullyElection` implements the Bully message state machine, eligibility checks and answer/coordinator timeout transitions. The future Host/transport adapter must schedule those transitions with a monotonic timer and forward only notices whose peer certificate was pinned. `HeartbeatFailureDetector` tracks authenticated members and suspects a peer after three missed one-second intervals. Election results only nominate a candidate. They never open serving rights or replace physical fencing. The isolated week-one checks are in `tests/unit/De6.Cluster.Control.Tests`. V12 section 12; T22-T26, T35-T36.

Specification: [V12](../../docs/De6_Thiet_Ke_Trien_Khai_V12.md). Team handoff and commands: [repository README](../../README.md).

Network transport, timeout scheduling, coordinator integration and multi-node fault runs remain for integration. Passing the election state-machine tests does not satisfy cluster acceptance.

## Hướng dẫn bắt đầu

Đức An thực hiện theo các bước **A1–A6** trong [phân công chi tiết](../../docs/team/Phan_Cong_Nhom9.md#4-đức-an--bully-và-clustercontrol). Mỗi bước nêu file/API cần cài, cách tự kiểm và phần giao cho bạn khác. Các tên file bổ sung trong hướng dẫn là file cần tạo, không phải implementation đã có.

Đọc phần cổng ghép và luồng HOLD trong tài liệu đó để phối hợp fixture và hiểu các trạng thái LocalApplied/Stable/FINAL. Cả bốn người đều code, test và hoàn thiện phần lý thuyết riêng.
