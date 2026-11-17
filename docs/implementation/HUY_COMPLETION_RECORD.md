# Hồ sơ hoàn tất phần Huy — H1 đến H6

Ngày kiểm chứng: 16/11/2026. Phạm vi là component SQL Persistence, oracle/rebuild, cấu hình và SQL probe của Host. Kết quả này không phải nghiệm thu cluster hoặc xác nhận CLO của lớp.

## Môi trường

- Nhánh: `feature/huy-database`; checkpoint tuần 1: `3240354`.
- .NET SDK: `10.0.404`.
- SQL Server local: Microsoft SQL Server 2025 RTM Express, `17.0.1000.7`, Windows Authentication qua `lpc:.\SQLEXPRESS`.
- Database phát triển `de_6` được kiểm tra read-only sau test. Không database tạm nào còn lại theo truy vấn `sys.databases`.
- Không có bằng chứng chạy riêng SQL Server 2022 hoặc ba máy SQL độc lập.

## Kiểm chứng cuối

| Phần | Kết quả |
| --- | --- |
| Build Release toàn solution | Thành công, 0 warning, 0 error |
| Template smoke | 23/23 assertions |
| Persistence contracts | 15/15 assertions, gồm digest fixture độc lập |
| Host/persistence offline | 23 ca, 0 lỗi |
| SQL Persistence integration | 46 ca, gồm H1–H5, 8 regression, transaction/dedup/owner/prefix/replay, cancellation, durability, oracle và rollback rebuild |
| Host/persistence với SQL | 34 ca, gồm 11 SQL probe; 0 lỗi |
| Cấu hình Host | Sáu cấu hình S1–S3 local/LAN qua `--check` |
| `de_6` | NodeId 1, DatasetId `124a4f06-4ac1-4c12-8b27-202600000009`, schema 1, `LastAppliedIndex=0`, 10 ghế, 0 vé, `MatchesHistory=True`; Host còn `RECOVERING`, `ServingEnabled=false` |
| SQL test fixture cleanup | Không còn database `De6%Test%` sau khi suite kết thúc |
| Diff whitespace | `git diff --check` không có lỗi |

### Lệnh đã chạy

```powershell
$env:DE6_NUGET_SOURCE='https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-public/nuget/v3/index.json'
.\scripts\test.ps1 -Configuration Release
.\scripts\test-huy-sql.ps1 -Configuration Release -SqlServer 'lpc:.\SQLEXPRESS'
```

Lần restore đầu bị lỗi do máy không truy cập được vulnerability service của nuget.org. Chạy lại với mirror Microsoft `dotnet-public` qua quyền mạng; audit không bị tắt và không thêm suppress vào repo.

Trong lượt kiểm thử mới, ba ca SQL regression timeout ở post-login do fixture chỉ chờ 5 giây khi SQL Express đang tạo/dọn nhiều database. Timeout của fixture tích hợp/Host được tăng lên 15 giây (không đổi cấu hình ứng dụng). Chạy lại `test.ps1` và toàn bộ `test-huy-sql.ps1`: build 0 warning/0 error; toàn bộ harness đạt, gồm 46 ca Persistence và 34 ca Host SQL; 8 regression không lỗi.

## Review đã xử lý

1. SQL probe trước đây đọc `NodeState` bằng shared lock rồi mới đọc log. Writer khóa `NodeState` trước, vì vậy có thể tạo chu trình chờ khi probe giữ S(NodeState). Probe nay lấy `UPDLOCK,HOLDLOCK` theo cùng thứ tự với Apply. Test SQL giữ U-lock của writer, xác nhận probe yêu cầu U-lock và chỉ tiếp tục sau khi lock được nhả; transaction probe vẫn không ghi dữ liệu.
2. SQL Host harness trước đây hard-code `lpc:.\SQLEXPRESS`, trong khi persistence harness nhận `-SqlServer`. Cả fixture và JSON Host được tạo nay dùng `DE6_TEST_SQL_SERVER`, do script gán từ một tham số duy nhất.
3. Hợp đồng `EnterMaintenance` nay ghi rõ Control/Recovery phải xác thực quyền, giữ node đóng, freeze tại `expectedIndex == LastAppliedIndex`, loại trừ mutation/grant transition trong suốt lease và không bật quyền phục vụ. `EnterRecoveryInspection` chỉ dành cho chứng cứ local; không thay freeze để tạo frozen prefix hoặc rebuild.
4. Fixture test log không rỗng được căn lại theo quy tắc replica: S1 nhận event từ leader S2, tránh tự append event do chính node mình ghi.
5. Sau khi gặp timeout post-login trong ba regression, tăng timeout kết nối của SQL test fixture từ 5 lên 15 giây; rerun xác nhận các ca đều qua.

## Trạng thái `de_6`

`status`, `audit` và Host `--check-sql` đều thành công. Audit tại K=0 trả seed HeadHash `a79c44cee6baf83e69fd2302e2a49210317de75ae15829b48eb14e21ac14c327` và `MatchesHistory=True`. `AcceptedEpoch=0`, `AcceptedLeader=NULL`; chưa có authority, StableIndex hay FINAL. Probe không tạo DB và không seed.

## Giới hạn bàn giao

- Host vẫn khởi động đóng; chưa có listener bán vé, coordinator, production decision engine/gate, Bully/election, replication TCP/TLS, activation hay client end-to-end.
- Review chéo của Gia Quân/Đức An/Nguyên Chương, test T01–T36 trên ba SQL Server độc lập và CI/PR về `develop` còn phải do nhóm thực hiện trước merge.
- SQL Server 2025 Express là engine đã chạy kiểm thử; không suy rộng kết quả thành đã chạy SQL Server 2022.

Commit implementation tuần 2: `f0ba4f0`; commit ổn định test fixtures: `6fc30b3`; cả hai đã push lên `origin/feature/huy-database`. PR chưa mở vì phiên này không có GitHub CLI/connector và công cụ trình duyệt không khởi chạy được; link tạo PR: https://github.com/huyhcm2k5it/DoAnHTPT_Nhom9_2026/pull/new/feature/huy-database.
