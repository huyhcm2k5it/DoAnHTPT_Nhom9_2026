# De6.Transport.Tcp

Văn Khoa phụ trách framing TCP và xác minh TLS phía client. Đức An phụ trách xác thực peer phía server. Huy và Gia Quân dùng interface transport cho receiver và sender.

Implement TLS authentication and length-prefixed UTF-8 JSON, max 1 MiB, partial/coalesced reads, EOF and single socket writer. Current IFrameChannel is only a boundary; no listener, TLS or TCP implementation. Coordinate with Văn Khoa for SDK; V12 T31-T33.

Specification: [V12](../../docs/De6_Thiet_Ke_Trien_Khai_V12.md). Team handoff and commands: [repository README](../../README.md).

Definition of done for future implementation: reviewed shared contracts, tests for owned V12 cases, error/uncertainty paths, and reproducible evidence. A successful template build does not satisfy business acceptance.

## Hướng dẫn bắt đầu

Framing, TLS, receiver và sender được giao theo module trong [phân công chi tiết](../../docs/team/Phan_Cong_Nhom9.md). Các tên file dự kiến trong hướng dẫn chưa đồng nghĩa implementation đã có.

Đọc phần cổng ghép và luồng HOLD trong tài liệu đó để dùng cùng fixture và phân biệt LocalApplied/Stable/FINAL. Cả bốn thành viên đều code, test và hoàn thiện phần lý thuyết gắn với code.
