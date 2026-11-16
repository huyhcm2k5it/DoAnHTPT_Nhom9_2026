# Chạy phần Huy tuần 1

H1–H4 có implementation Persistence và test SQL tích hợp. Đây là component để các bạn ghép, chưa phải hệ thống bán vé đầu cuối. Host vẫn boot RECOVERING, client vẫn là UI bộ khung. Nhánh này không tự trả FINAL hoặc promote.

## Mở đúng nhánh và chuẩn bị

```powershell
git fetch origin
git switch feature/huy-database
```

Máy mới chưa có nhánh local dùng `git switch --track origin/feature/huy-database`. Không ghi đè thay đổi cá nhân. Cần Windows, .NET SDK 10.x và SQL Server local. Máy kiểm thử hiện là 2025 Express 17.0.1000.7; DDL hướng tới 2022 trở lên nhưng chưa có bằng chứng chạy riêng engine 2022.

Trong SSMS kết nối `.\SQLEXPRESS`, Windows Authentication. Tool dùng cùng instance qua shared memory `lpc:.\SQLEXPRESS`. TrustServerCertificate chỉ dành cho kết nối SQL local trong môi trường phát triển, không thay quy tắc TLS peer/client của V12. Script tìm dotnet trên PATH rồi `.tools/dotnet/dotnet.exe`.

NuGet.Config dùng nuget.org, pin Microsoft.Data.SqlClient 7.1.2. Nếu NuGet.org lỗi mạng, restore trước bằng mirror Microsoft:

```powershell
dotnet restore DoAnHTPT_Nhom9_2026.sln --source https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-public/nuget/v3/index.json
```

Để các script test/build cũng dùng mirror trong PowerShell hiện tại, đặt biến môi trường trước khi chạy:

```powershell
$env:DE6_NUGET_SOURCE='https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-public/nuget/v3/index.json'
.\scripts\test.ps1 -Configuration Release
.\scripts\test-huy-sql.ps1 -Configuration Release
```

Không đặt biến này thì script tiếp tục dùng nguồn trong `scripts/nuget-template.config`.

Nếu dùng SDK cục bộ, thay dotnet bằng `& .\.tools\dotnet\dotnet.exe`. Không cần đổi execution policy; có thể dùng CLI trực tiếp bên dưới.

## Tạo database riêng và xem trên SSMS

Chạy từ thư mục repo:

```powershell
.\scripts\init-huy-sql.ps1 -NodeId 1 -Database de_6
```

DatasetId lấy từ `config/local/s1.json`. Tool chấp nhận tên de_6 hoặc tên bắt đầu De6_ trên SQL local. Database cá nhân như testhuy432026 không bị mở/sửa. DB có bảng không được nhận diện, marker/schema khác hoặc DatasetId/NodeId khác sẽ bị từ chối, không reset.

CLI tương đương:

```powershell
dotnet run --project tools/De6.Sql.Tool -c Release -- init --database de_6 --node 1 --dataset 124a4f06-4ac1-4c12-8b27-202600000009 --create
dotnet run --project tools/De6.Sql.Tool -c Release -- status --database de_6
```

Trong SSMS: Databases → Refresh → de_6 → Tables. Mở New Query:

```sql
USE de_6;
SELECT * FROM dbo.Seats ORDER BY SeatNo;
SELECT NodeId,DatasetId,AcceptedEpoch,AcceptedLeader,
       LastAppliedIndex,HeadHash,SchemaVersion FROM dbo.NodeState;
```

DB mới có 10 ghế AVAILABLE, AcceptedEpoch=0, AcceptedLeader=NULL, LastAppliedIndex=0. Đây là trạng thái chưa cấp quyền. Không UPDATE authority tay để mở bán; UPDATE kích hoạt trong test chỉ dành cho DB tạm. Cấp quyền thật thuộc Control của Đức An theo điều kiện V12.

## Các file phải nắm

| Bước | File và hành vi |
| --- | --- |
| H1 | database/migrations/001_initial.sql tạo 7 bảng/FK/UNIQUE/CHECK; database/seed/001_seed.sql; SqlDatabaseBootstrap chạy transaction, version/checksum, không reset dữ liệu |
| H2 | SqlBookingStore.DecideAndApplyAsync đọc state/dedup/decision trong transaction; SqlApplyTransaction lưu projection/log/result/NodeState nguyên tử |
| H3 | SqlReplicaApply tách quyền append/replay, kiểm tra prefix/index/hash/duplicate; receipt chỉ trả sau commit |
| H4 | SqlSnapshotReader đọc đúng mốc/owner qua gate; InspectLocalRequestAsync là chứng cứ local dành cho quản trị khi outcome chưa rõ |

Các class ở `src/De6.Booking.Persistence/`. Canonical/event/storage contracts ở `src/De6.Contracts/`. Engine/gate fixture chỉ nằm trong project test, không dùng làm production coordinator/authority gate.

## Chạy test

```powershell
.\scripts\test.ps1 -Configuration Release
.\scripts\test-huy-sql.ps1 -Configuration Release
```

Lệnh thứ nhất build solution và chạy smoke/contract tests, không cần SQL. Lệnh thứ hai cần Windows login có quyền tạo database trên SQL local. Test tự tạo tên ngẫu nhiên De6_HuyW1_Test_GUID rồi xóa đúng những DB do lượt đó tạo thành công. Không dùng DB cá nhân/đồ án để reset test. Nếu process bị kill trước cleanup, phải xác minh chính xác tên DB test trước khi dọn thủ công.

CLI nếu script bị chặn:

```powershell
dotnet run --project tests/unit/De6.Persistence.Contracts.Tests -c Release
dotnet run --project tests/integration/De6.Booking.Persistence.Tests -c Release
```

Các test là console harness có exit code, không được dotnet test tự khám phá. SQL test dùng SqlClient/tables/transactions thật và fixture cho các module chưa cài. Bao gồm rollback trước commit, event còn sau lỗi sau commit, duplicate, owner filtering, GAP/conflict, replay epoch cũ và forced delayed durability. Lỗi observer là fault injection tại ranh giới commit, không phải tuyên bố đã kill thật primary hoặc partition mạng.

## Hợp đồng ghép module

Gia Quân cài IBookingDecisionEngine thật: quyết định đồng bộ trên BookingState trong transaction, không gọi mạng. Review command/result/event fixture cùng các bên trước merge Contracts. Lỗi nghiệp vụ hợp lệ dùng NO_CHANGE với result cố định; một mutation chỉ tạo một event.

Đức An cấp IStoreAccessGate thật, không có gate mở mặc định. Primary lease gắn grant; snapshot lease chỉ cho PRIMARY_READY và StableIndex=LastAppliedIndex; replay cần context cài cục bộ trên node đóng gate. Lease an toàn qua await và đồng bộ freeze; Block không được deadlock khi đang giữ lease.

Nguyên Chương truyền nguyên PersistedEvent bytes. Chỉ gửi ACK sau AppendAndApplyAsync trả receipt; phân biệt AuthorityEpoch/EventEpoch. Replay dùng API riêng, không nới điều kiện append mới.

Store không gọi mạng trong SQL transaction. Operation đã bắt đầu không bị bỏ vì socket mất; token từ coordinator phải tách khỏi socket cancellation. StorageUncertainException thông báo gate đã đóng: inspect log bằng đường quản trị, không tự cấp EventId mới/mở phục vụ lại. Tìm được result local chưa đủ FINAL.

## Giới hạn và tuần 2

H5 đã bổ sung oracle/digest và rebuild projection có maintenance; xem HUY_WEEK2_RUNBOOK.md. Full repair/promotion, coordinator/network và nghiệm thu cluster vẫn phụ thuộc các module liên quan. Ba DB cùng laptop kiểm tra component, không chứng minh chịu lỗi ba máy. T01–T36 vẫn cần cluster thật và record/oracle; PASS component không biến ma trận nghiệm thu thành PASS.
