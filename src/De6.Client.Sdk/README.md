# De6.Client.Sdk

Owner: Văn Khoa.

K3–K5 provide a file-backed pending store with exclusive locking, routing/reconnect, and result resolution with the original RequestId. The TLS connection factory depends on an implementation of `IClientNodeWireProtocol`, and login depends on `IClientLoginProtocol`. Host dispatch and WinForms integration are pending. V12 sections 15-17; T17, T31-T34.

Specification: [V12](../../docs/De6_Thiet_Ke_Trien_Khai_V12.md). Team handoff and commands: [repository README](../../README.md).

Definition of done for future implementation: reviewed shared contracts, tests for owned V12 cases, error/uncertainty paths, and reproducible evidence. A successful template build does not satisfy business acceptance.

## Hướng dẫn bắt đầu

Văn Khoa thực hiện theo các bước **K1–K6** trong [phân công chi tiết](../../docs/team/Phan_Cong_Nhom9.md#6-văn-khoa--clientsdk-và-winforms). Mỗi bước nêu file/API cần cài, cách tự kiểm và phần giao cho bạn khác. Các tên file bổ sung trong hướng dẫn là file cần tạo, không phải implementation đã có.

Đọc phần cổng ghép và luồng HOLD trong tài liệu đó để phối hợp fixture và hiểu các trạng thái LocalApplied/Stable/FINAL. Cả bốn người đều code, test và hoàn thiện phần lý thuyết riêng.
