# De6.Client.WinForms

Owner: Văn Khoa. Giao diện quầy K6, tông trắng và xanh da trời.

- Sơ đồ 10 ghế: nhãn AVAILABLE/HELD/SOLD cùng màu và hoa văn; nhãn cam ĐANG GỬI/UNKNOWN tách riêng khỏi trạng thái ghế đã xác nhận.
- Chỉ báo thành công khi nhận FINAL. Dữ liệu cũ được gắn nhãn "DỮ LIỆU CŨ" và khóa nút thao tác.
- Khởi động lại: khôi phục pending, tra cùng RequestId. Trace/số liệu ghi vào `data/clients/cN/evidence/`.
- Logic nằm trong `De6.Client.Sdk/Booking`; form chỉ hiển thị.

```powershell
./scripts/start-client.ps1 -ClientId 1 -Check
./scripts/start-demo-clients.ps1        # ba quầy trên cụm giả lập (FAKE)
```

`--demo` dùng `De6.Client.Simulation` (không phải server). Chế độ thật chưa nối được: xem [K6_DEMO_RUNBOOK](../../docs/testing/K6_DEMO_RUNBOOK.md).

Đặc tả: [V12](../../docs/De6_Thiet_Ke_Trien_Khai_V12.md) mục 15–17; T02–T09, T17, T31, T34.
