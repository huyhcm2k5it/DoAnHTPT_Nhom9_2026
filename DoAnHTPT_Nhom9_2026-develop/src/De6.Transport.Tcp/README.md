# De6.Transport.Tcp

Văn Khoa phụ trách framing TCP và xác minh TLS phía client. Đức An phụ trách xác thực peer phía server. Huy và Gia Quân dùng interface transport cho receiver và sender.

Implement TLS authentication and length-prefixed UTF-8 JSON, max 1 MiB, partial/coalesced reads, EOF and single socket writer. Current IFrameChannel is only a boundary; no listener, TLS or TCP implementation. Coordinate with Văn Khoa for SDK; V12 T31-T33.

Specification: [V12](../../docs/De6_Thiet_Ke_Trien_Khai_V12.md). Team handoff and commands: [repository README](../../README.md).

Definition of done for future implementation: reviewed shared contracts, tests for owned V12 cases, error/uncertainty paths, and reproducible evidence. A successful template build does not satisfy business acceptance.

## Hướng dẫn bắt đầu

Framing, TLS, receiver và sender được giao theo module trong [phân công chi tiết](../../docs/team/Phan_Cong_Nhom9.md). Các tên file dự kiến trong hướng dẫn chưa đồng nghĩa implementation đã có.

Đọc phần cổng ghép và luồng HOLD trong tài liệu đó để dùng cùng fixture và phân biệt LocalApplied/Stable/FINAL. Cả bốn thành viên đều code, test và hoàn thiện phần lý thuyết gắn với code.

## Trạng thái K1 (framing) — đã cài

- `FrameCodec`: định dạng frame `UInt32 big-endian N` + N byte UTF-8, `1 <= N <= 1 MiB`; kiểm tra độ dài **trước khi** cấp phát bộ nhớ.
- `StreamFrameChannel : IFrameChannel`: chạy trên mọi `Stream` hai chiều. Đọc đủ frame qua nhiều lần nhận, tách nhiều frame trong một lần nhận, phân biệt EOF sạch (`FrameChannelClosedException`) với frame hỏng (`FrameProtocolException` + `FrameError`). Đọc và ghi mỗi bên tuần tự (một writer / một reader tại một thời điểm).
- Mọi lỗi sau khi byte đã di chuyển (header sai, EOF giữa frame, lỗi I/O, hủy giữa lúc đọc/ghi) **đóng kênh**, vì luồng byte không còn chắc nằm đúng ranh giới frame. Hủy khi còn đang chờ lượt thì kênh vẫn dùng được.
- Payload ghi ra sai (rỗng, quá 1 MiB, không phải UTF-8) bị từ chối bằng `ArgumentException`, kênh không bị ảnh hưởng.
- **Chưa có TLS/xác thực (K2).** `StreamFrameChannel` không tự xác thực; K2 sẽ truyền vào một `SslStream` đã xác minh chứng thư. Không cung cấp bộ kết nối TCP thuần để tránh lộ frame trước khi xác thực.
- Kiểm thử: `tests/unit/De6.Transport.Tcp.Tests` (T31), chạy bằng `scripts/test.ps1`.
