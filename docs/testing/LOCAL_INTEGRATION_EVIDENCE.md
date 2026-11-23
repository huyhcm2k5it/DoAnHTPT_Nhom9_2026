# Kết quả kiểm tra tích hợp local

Mốc kiểm tra: working tree trên `d637abe`, nhánh `feature/gia-quan-domain`.
Môi trường: Windows, .NET 10, SQL Server Express local, TLS loopback. Test tạo
chứng thư riêng cho từng node và ba DB seed có cùng DatasetId. Test dọn tài nguyên
tạm khi kết thúc.

```powershell
dotnet build DoAnHTPT_Nhom9_2026.sln --no-restore
dotnet run --project tests/unit/De6.Domain.UnitTests --no-build
dotnet run --project tests/unit/De6.Cluster.Control.Tests --no-build
dotnet run --project tests/De6.Template.SmokeTests --no-build
dotnet run --project tests/integration/De6.Booking.Persistence.Tests --no-restore
dotnet run --project tests/integration/De6.Booking.Persistence.Tests --no-restore -- --standalone
```

| Luồng đã kiểm tra | Bằng chứng |
| --- | --- |
| Bootstrap local | CLI ghi và đọc lại cùng manifest trên ba DB, sau đó khởi chạy ba tiến trình Host tại các cổng riêng. |
| Đặt vé qua mạng | SDK đăng nhập TLS, HOLD và CONFIRM trả FINAL. GET_RESULT trả reservation cũ và mốc AppliedIndex. SQL xác nhận primary và ít nhất một backup có cùng head hash. |
| Tranh ghế | Ba phiên quầy gửi HOLD cùng ghế. Có một OK_HELD và hai SEAT_BUSY. |
| Backup không sẵn sàng | Primary trả CLUSTER_NOT_READY, LastAppliedIndex không đổi. |
| Mất ACK sau commit | Primary trả UNKNOWN và đóng gate. Gửi lại đúng event nhận ACK, nâng Stable. Backup vẫn chỉ có một log entry. |
| Mở lại pending store | Quầy mở lại store sau khi chưa lưu kết quả, tra đúng RequestId qua TLS và xóa pending sau FINAL. |
| Primary dừng | Heartbeat hết hạn, Bully ghi nhận S2 là ứng viên. Gửi HOLD tới S2 vẫn bị từ chối. |

Kết quả này ghi nhận các ca local đã chạy. Cần kiểm thử WinForms trực tiếp, cài
quy trình fencing/promotion/rejoin và hoàn tất các record trong TEST_MATRIX.md
trước nghiệm thu cuối.
