# De6.Client.Sdk

Owner: Văn Khoa.

Implement authenticated reconnect to three endpoints, durable exclusive per-client pending store, same RequestId on retries, and owner isolation. IClientPendingStore is a boundary only; network, authentication, retry and disk locking are NOT IMPLEMENTED. V12 sections 15-17; T17, T31-T34.

Specification: [V12](../../docs/De6_Thiet_Ke_Trien_Khai_V12.md). Team handoff and commands: [repository README](../../README.md).

Definition of done for future implementation: reviewed shared contracts, tests for owned V12 cases, error/uncertainty paths, and reproducible evidence. A successful template build does not satisfy business acceptance.

## Hướng dẫn bắt đầu

Văn Khoa thực hiện theo các bước **K1–K6** trong [phân công chi tiết](../../docs/team/Phan_Cong_Nhom9.md#6-văn-khoa--clientsdk-và-winforms). Mỗi bước nêu file/API cần cài, cách tự kiểm và phần giao cho bạn khác. Các tên file bổ sung trong hướng dẫn là file cần tạo, không phải implementation đã có.

Đọc thêm mục 9–12 của tài liệu đó để ghép fixture, chạy luồng HOLD xuyên suốt, chuẩn bị PR và hiểu các trạng thái LocalApplied/Stable/FINAL. Cả năm người đều code, test và hoàn thiện phần lý thuyết riêng.
