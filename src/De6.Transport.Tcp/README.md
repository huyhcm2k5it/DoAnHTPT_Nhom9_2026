# De6.Transport.Tcp

Owner: Nguyên Chương.

Implement TLS authentication and length-prefixed UTF-8 JSON, max 1 MiB, partial/coalesced reads, EOF and single socket writer. Current IFrameChannel is only a boundary; no listener, TLS or TCP implementation. Coordinate with Văn Khoa for SDK; V12 T31-T33.

Specification: [V12](../../docs/De6_Thiet_Ke_Trien_Khai_V12.md). Team handoff and commands: [repository README](../../README.md).

Definition of done for future implementation: reviewed shared contracts, tests for owned V12 cases, error/uncertainty paths, and reproducible evidence. A successful template build does not satisfy business acceptance.

## Hướng dẫn bắt đầu

Nguyên Chương thực hiện theo các bước **C1–C6** trong [phân công chi tiết](../../docs/team/Phan_Cong_Nhom9.md#5-nguyên-chương--replication-và-transporttcp). Mỗi bước nêu file/API cần cài, cách tự kiểm và phần giao cho bạn khác. Các tên file bổ sung trong hướng dẫn là file cần tạo, không phải implementation đã có.

Đọc thêm mục 9–12 của tài liệu đó để ghép fixture, chạy luồng HOLD xuyên suốt, chuẩn bị PR và hiểu các trạng thái LocalApplied/Stable/FINAL. Cả năm người đều code, test và hoàn thiện phần lý thuyết riêng.
