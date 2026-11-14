# Kết quả kiểm tra bộ khung ngày 14 tháng 11 năm 2026

Môi trường: Windows 10 x64, .NET SDK 10.0.404; cấu hình Release. Đây là kết quả bộ khung khởi động, không phải nghiệm thu hệ thống bán vé phân tán.

| Kiểm tra | Kết quả thực tế |
| --- | --- |
| `scripts/test.ps1 -Configuration Release` | Exit 0; build 11 project thành công, 0 cảnh báo, 0 lỗi |
| Harness `De6.Template.SmokeTests` | 23/23 assertion đạt |
| S1, S2, S3 với ba cấu hình local và `--check` | Cả ba exit 0, RECOVERING, ServingEnabled=false |
| C1, C2, C3 với ba cấu hình local và `--check` | Cả ba exit 0; owner A/B/C và thư mục dữ liệu riêng |
| Liên kết README, team, testing, report tại lúc bàn giao | Không có liên kết nội bộ thiếu file |
| Ma trận nghiệm thu nghiệp vụ/phục hồi T01–T36 | Đủ 36 ca, vẫn CHƯA CHẠY |

21 assertion kiểm tra cấu hình/bootstrap: mẫu local hợp lệ, membership cố định, trùng ID/cổng, cổng ngoài khoảng, dataset thiếu, data path trống/tuyệt đối/traversal/dùng chung, owner sai và quyền phục vụ luôn đóng. Hai assertion còn lại chạy executable server/client với file cấu hình không hợp lệ và kiểm tra exit code 2 thay vì exception không được xử lý. Bản sửa đã bắt rõ InvalidDataException ở cả hai điểm khởi động.

Lệnh tái lập từ root:

```powershell
.\scripts\test.ps1 -Configuration Release
foreach ($id in 1..3) {
    .\scripts\start-server.ps1 -NodeId $id -Check
    if ($LASTEXITCODE -ne 0) { throw 'Server check failed' }
    .\scripts\start-client.ps1 -ClientId $id -Check
    if ($LASTEXITCODE -ne 0) { throw 'Client check failed' }
}
```

UI đã được build; kiểm tra `--check` chỉ xác minh cấu hình rồi thoát, không xác nhận thao tác giao diện. Chưa có SQL Apply, TCP/TLS listener, login, booking, Bully, replication, fencing/promotion hoặc pending store thật. Những phần đó phải được năm thành viên triển khai và nghiệm thu theo [V12](../De6_Thiet_Ke_Trien_Khai_V12.md) cùng [ma trận kiểm thử](TEST_MATRIX.md).

GitHub Actions đã được cấu hình trong `.github/workflows/build.yml`. Kết quả chạy CI trên GitHub cần xem tại trang Actions; không suy từ kết quả local thành CI từ xa đã đạt.
