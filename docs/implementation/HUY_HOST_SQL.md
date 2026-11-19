# Huy H6: cấu hình Host và SQL cục bộ

Host chỉ có bootstrap đóng và probe chỉ đọc. Kết quả SQL hợp lệ không cấp quyền primary, không khôi phục StableIndex, không bật listener và không tạo phản hồi FINAL. V12 mục 4 yêu cầu SQL nằm trên chính máy server; mục 5 và 17 yêu cầu mọi restart bắt đầu RECOVERING. Việc bootstrap/activate toàn cụm vẫn thuộc Control và quy trình vận hành đã đối soát.

## Cấu hình từng node

`ServerOptions` có tham số cuối tùy chọn `Sql = null`; code cũ truyền bốn tham số vẫn hợp lệ. JSON server có thể thêm:

```json
"sql": {
  "server": "lpc:.\\SQLEXPRESS",
  "database": "de_6",
  "connectTimeoutSeconds": 5
}
```

Chỉ chấp nhận shared memory cục bộ `lpc:.`, `lpc:(local)` hoặc các dạng có tên instance chữ/số/underscore. Không chấp nhận TCP, IP/DNS remote, alias máy khác hoặc connection string tùy ý. Tên database là `de_6` hoặc bắt đầu `De6_`, dài tối đa 128 ký tự ASCII chữ/số/underscore. Timeout là 1–30 giây. Các field không biết, kể cả password/userId bên trong sql, vẫn bị từ chối.

Factory dùng Windows Authentication của account chạy Host, `Encrypt=true`, `TrustServerCertificate=true` cho SQL phát triển cục bộ, không lưu password/token. SQL shared memory không thay các yêu cầu TLS của giao thức client/peer. Nếu chạy Host bằng account khác, cấp quyền SQL cho account đó qua quy trình quản trị riêng; Host không tự CREATE LOGIN/GRANT.

| Profile | Node | SQL trên máy đang chạy node | Database |
| --- | --- | --- | --- |
| local | S1 | `.\SQLEXPRESS` | `de_6` |
| local | S2 | `.\SQLEXPRESS` | `De6_S2` |
| local | S3 | `.\SQLEXPRESS` | `De6_S3` |
| lan | S1/S2/S3 | `.\SQLEXPRESS` trên từng máy tương ứng | `de_6` trên mỗi máy |

Profile local dùng ba DB riêng trên cùng laptop chỉ kiểm tra component. Profile LAN phải chạy từng node trên máy riêng; không chạy cả ba profile LAN trên một instance có chung `de_6` vì NodeId sẽ không khớp. Có thể bỏ hoàn toàn field sql khi chỉ cần template chưa gắn SQL. Client không có SQL settings.

## Lệnh và exit code

Chạy tại repository root, dùng `dotnet` hoặc SDK đóng gói `& .\.tools\dotnet\dotnet.exe`:

```powershell
dotnet run --project src/De6.Server.Host -c Release -- --config config/local/s1.json --check
dotnet run --project src/De6.Server.Host -c Release -- --config config/local/s1.json --check-sql
dotnet run --project src/De6.Server.Host -c Release -- --config config/local/s1.json
```

`--check` chỉ đọc/validate JSON, kể cả khi SQL chưa cài hoặc database chưa tồn tại. `--check-sql` cần cấu hình sql: probe thành công trả 0; lỗi SQL/identity/schema/durability/local boundary trả 1; thiếu cấu hình hoặc sai cú pháp/config trả 2. Ctrl+C trong khi probe trả 130. Normal startup có sql thì chạy cùng probe; lỗi thoát đóng, thành công vẫn chờ ở RECOVERING/ServingEnabled=false. Không có sql thì chỉ chờ template đóng.

Probe mở đúng DB đã cấu hình, không kết nối master để tạo DB. Toàn bộ câu lệnh bên trong là SELECT trong transaction Serializable: kiểm tra delayed durability không FORCED, migration version/checksum và bảy bảng đã biết, đúng một NodeState với DatasetId/NodeId/schema đúng, LastAppliedIndex không âm và bằng độ dài log liên tục từ 1, HeadHash trùng log tail hoặc SeedHash tại K=0. Dispose transaction giải phóng read locks; không seed, sửa projection/log, ghi authority, CREATE/ALTER hay GRANT. SQL permissions thiếu hoặc DB không tồn tại đều là lỗi, không có fallback khởi tạo.

Đây là probe cục bộ, không chạy lại full canonical history/oracle của H5 và không chứng minh prefix trùng backup hay stable. `LastAppliedIndex` được in đúng tên để tránh diễn giải thành StableIndex. Schema marker/checksum là nhận diện migration, không thay audit DDL ngoài luồng hoặc kiểm chứng nghiệp vụ.

## Ranh giới ghép module

`SqlPersistenceComposition.CreateStoreOptions(ServerOptions)` chỉ tạo `SqlStoreOptions`, không mở kết nối. `CreateStore(options, gate, decisionEngine)` bắt buộc đủ `IStoreAccessGate` thật từ Đức An và `IBookingDecisionEngine` thật từ Gia Quân, không có overload mở gate mặc định. Gate đóng được truyền vào vẫn từ chối operation trước SQL. Factory không thay đổi transaction/cancellation/commit semantics của H1–H5.

Host hiện chỉ gọi `SqlStorageHealth`, chưa gọi factory để dựng luồng booking. Việc nối coordinator/grant, authority freeze, network ACK, replication, election và activation cần implementation/evidence của các chủ module. Không lấy `BootstrapState.StableIndex=0` làm kết quả phục hồi.

## Kiểm thử tái lập

```powershell
dotnet build tests/unit/De6.Host.Persistence.Tests -c Release -m:1
dotnet tests/unit/De6.Host.Persistence.Tests/bin/Release/net10.0/De6.Host.Persistence.Tests.dll
dotnet tests/unit/De6.Host.Persistence.Tests/bin/Release/net10.0/De6.Host.Persistence.Tests.dll --sql
```

Harness không phải test framework, trả nonzero khi lỗi. Mặc định kiểm tra config/offline startup/factory/cancellation, không cần SQL Server. `--sql` cần Windows account có quyền CREATE DATABASE trên instance local; fixture chỉ tạo và dọn các DB ngẫu nhiên `De6_HostProbe_Test_GUID` mà chính lượt đó CREATE thành công. Không dùng/xóa `de_6`, `De6_S2`, `De6_S3` hoặc DB cá nhân để thử lỗi.

SQL cases kiểm tra seeded DB, sai DatasetId/NodeId, DB thiếu/rỗng, checksum sai, forced delayed durability và LastAppliedIndex vượt log. Một ca đồng thời giữ U-lock trên NodeState xác nhận probe chờ đúng thứ tự khóa trước khi đọc log. Snapshot dữ liệu bảy bảng và migration metadata trước/sau probe xác minh không có sửa/repair. `scripts/test-huy-sql.ps1 -SqlServer <local-instance>` truyền cùng instance vào Persistence và Host harness qua `DE6_TEST_SQL_SERVER`. Trong sandbox không có Windows SSPI, chạy SQL bằng quyền process phù hợp; không tắt encryption để né lỗi SSPI/TLS.

Tạo DB phát triển riêng là bước quản trị bằng [HUY_WEEK1_RUNBOOK](HUY_WEEK1_RUNBOOK.md), không thuộc startup Host. Full oracle/digest/rebuild xem [HUY_WEEK2_PLAN](HUY_WEEK2_PLAN.md). Kết quả component vẫn không thay ma trận nghiệm thu cluster V12.

## Bàn giao receiver và Control

Sau probe thành công, `HostPersistence.Compose` tạo Control từ accepted authority và mốc local của SQL. Nó dùng cùng Control cho SQL store và receiver. Node khởi tạo RECOVERING với StableIndex bằng 0. Probe chỉ đọc schema/metadata của database đã có. Khi DB phát triển còn schema 1, người vận hành thực hiện migration theo [runbook SQL](HUY_WEEK1_RUNBOOK.md).

Điểm nối cho dispatcher peer là `ReplicaReceiver.AppendAsync` và `ReplicaReceiver.ReplayAsync`. Dispatcher chuyển danh tính peer đã xác thực và envelope v1. Replay dùng context đã cài ở node đích. SQL giữ lease qua commit và cập nhật mốc local trước khi nhả lease. Dispatcher chỉ gửi ACK_APPLIED từ receipt khi lời gọi hoàn thành thành công. Nếu gửi ACK thất bại, sender có thể retry cùng event. SQL cuối cùng vẫn có một event và một result.

Tiếp tục nghiệm thu G1/G2 khi nối coordinator và TCP/TLS. `De6.Booking.Domain.IBookingStore` hiện đọc trạng thái và quyết định trước khi gọi Apply. `De6.Contracts.IBookingStore` nhận command và quyết định bên trong transaction. Khi ghép hai đường này, cần chốt một nơi quyết định duy nhất trong transaction để tránh tạo hai reservation/ticket ID khác nhau. G2 cần bằng chứng từ cả primary và backup. Ca H3 hiện kiểm receiver bằng SQL thật với peer fixture và mô phỏng mất ACK tại biên handler.
