# K6 — UI WinForms và demo ba quầy (Văn Khoa)

Trạng thái: **mã đã viết, bằng chứng chạy trên máy nhóm CHƯA CÓ.** Bảng record cuối trang để trống cho đến khi chạy thật.
Mọi lần chạy bằng `--demo` dùng **cụm giả lập (FAKE)**, không chứng minh FINAL bền vững, nhân bản hay chịu lỗi (V12 mục 20); không ghi PASS cho T01–T36 từ các lần chạy này.

## Phạm vi đã làm

| Yêu cầu K6 (Phan_Cong_Nhom9.md) | Nơi cài |
| --- | --- |
| Hiển thị 10 ghế AVAILABLE/HELD/SOLD (nhãn + màu + hoa văn) | `SeatTile.cs`, `MainForm.cs` |
| Trạng thái pending/UNKNOWN tách riêng khỏi ghế đã xác nhận | `SeatCell.Pending` + nhãn cam trên ô ghế, bảng "Lệnh chờ kết quả" |
| Danh sách giữ ghế của chính quầy | `BookingController.MyReservations` |
| Chỉ báo thành công khi nhận FINAL hợp lệ | `BookingController.ApplyOutcomeAsync` (chỉ `Final` đặt `Last`) |
| Restart client, khôi phục pending, tra cùng RequestId | `BookingController.StartAsync` → `ReadPendingAsync` → `ResolveAsync` |
| Mỗi quầy có config/login/pending riêng | `config/local/cN.json`, `LoginForm`, `data/clients/cN/` + khóa file |
| Ghi trace và số liệu | `data/clients/cN/evidence/trace-*.log`, nút "Xuất số liệu" → `metrics-*.json` |

Logic nằm ở `src/De6.Client.Sdk/Booking/` (build và test được ngoài Windows); WinForms chỉ vẽ. Cụm giả lập ở `src/De6.Client.Simulation/`.

## Chạy

```powershell
./scripts/start-client.ps1 -ClientId 1 -Check          # kiểm cấu hình
./scripts/start-demo-clients.ps1                       # mở C1, C2, C3 trên cụm giả lập
./scripts/start-client.ps1 -ClientId 2 -Demo           # mở riêng một quầy
./scripts/test.ps1 -Configuration Release              # gồm bộ test K6 (De6.Client.K6.Tests)
```

Đăng nhập demo: tài khoản `A`/`B`/`C` tương ứng C1/C2/C3, mật khẩu `demo`. Reset cụm giả lập: đóng cả ba quầy rồi xóa `data/demo`.
Chạy không có `--demo` sẽ báo chưa nối được cụm thật.

## Kịch bản demo

| Mã | Thao tác | Kết quả cần thấy |
| --- | --- | --- |
| D1 | Cả ba quầy cùng chọn ghế 5 và bấm Giữ ghế | Một quầy nhận `OK_HELD`; hai quầy `SEAT_BUSY`. Ghế hiện HELD "của tôi" ở quầy thắng, HELD sọc ở quầy kia |
| D2 | Quầy thắng bấm Xác nhận mua | `OK_SOLD`, ghế SOLD ở cả ba quầy sau lần làm mới kế tiếp |
| D3 | Menu demo: "Rớt response lần sau", rồi Giữ ghế | SDK tự tra `GET_RESULT`, ra `OK_HELD`, cluster chỉ có một event |
| D4 | Menu demo: "Rớt response + cắt mạng", rồi Giữ ghế | Nhãn cam UNKNOWN trên ghế, lệnh nằm trong bảng chờ, **không** có thông báo thành công. Bỏ chọn "Mất mạng" → tự tra → FINAL cùng RequestId |
| D5 | Lặp D4, đóng quầy khi còn UNKNOWN, mở lại | Log "Khôi phục 1 lệnh pending", rồi FINAL cùng RequestId, không tạo event thứ hai |
| D6 | Mở lần hai cùng quầy khi quầy đang chạy | Báo pending store đang được dùng (khóa file, T34) |

## Mẫu ghi record (điền khi chạy thật)

| Kịch bản | Ngày/giờ | Commit SHA | File trace | Số liệu (gửi / FINAL / UNKNOWN / p95 ms) | Kết quả thực tế |
| --- | --- | --- | --- | --- | --- |
| D1 | — | — | — | — | CHƯA CHẠY |
| D2 | — | — | — | — | CHƯA CHẠY |
| D3 | — | — | — | — | CHƯA CHẠY |
| D4 | — | — | — | — | CHƯA CHẠY |
| D5 | — | — | — | — | CHƯA CHẠY |
| D6 | — | — | — | — | CHƯA CHẠY |

## Giới hạn và phụ thuộc còn lại

- Server.Host chưa lắng nghe; chưa có triển khai `IClientLoginProtocol` / `IClientNodeWireProtocol` và chưa có nơi cấu hình TLS pin. Chế độ thật cần các phần này (K2/K4 phối hợp Đức An, Gia Quân).
- Hình dạng JSON của `GET_SEATS` / `GET_MY_RESERVATIONS` là **giả định** của client, tập trung ở `ReadModelCodec.cs`. Cần chốt với Gia Quân (Q6) trước G4.
- Lệnh bị cluster trả `REJECTED` vẫn nằm trong pending store (SDK chưa có thao tác xóa); UI hiển thị "BỊ TỪ CHỐI" và không tự thử lại.
- Lớp WinForms chưa được biên dịch/chạy trong môi trường viết mã (không có targeting pack Windows); cần `./scripts/build.ps1` trên Windows.
