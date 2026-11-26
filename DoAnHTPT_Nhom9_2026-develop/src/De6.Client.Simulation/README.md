# De6.Client.Simulation (FAKE)

Owner: Văn Khoa. **Không phải server thật.** Giả lập cụm S1–S3 ở mức `IClientNodeConnection` để chạy UI K6 khi
Server.Host chưa lắng nghe. Mọi thứ phía client (pending store, route manager, resolver, controller) là code thật.

- `SimulatedCluster`: trạng thái 10 ghế dùng chung giữa các tiến trình quầy qua `data/demo/cluster.json` (khóa file).
- `SimulatedNodeConnectionFactory`: node 3 là PRIMARY_READY, node 1/2 là BACKUP_READY; OwnerId lấy từ phiên, không từ JSON.
- `SimulatedFaults`: mất mạng, rớt response, rớt response rồi cắt mạng (để tạo UNKNOWN thật).
- `DemoAuthenticator`: đăng nhập giả (tài khoản A/B/C, mật khẩu `demo`). Không dùng cho hệ thống thật.

Kết quả chạy bằng simulation không được ghi là PASS của T01–T36 (V12: test fake phải ghi rõ là fake).
