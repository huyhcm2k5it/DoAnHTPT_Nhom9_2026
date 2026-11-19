# Đồ án HTPT — Nhóm 9

Mô phỏng bán vé một chuyến tàu 10 ghế theo [đặc tả V12](docs/De6_Thiet_Ke_Trien_Khai_V12.md): coordinator tập trung, Bully chọn ứng viên và primary-backup đồng bộ. Repo: [DoAnHTPT_Nhom9_2026](https://github.com/huyhcm2k5it/DoAnHTPT_Nhom9_2026).

## Trạng thái hiện tại

Đây là bản cơ sở đang được bốn thành viên phát triển. Server kiểm tra cấu hình, có probe SQL chỉ đọc khi được cấu hình, và luôn báo `RECOVERING`, `ServingEnabled=false`; chưa lắng nghe như dịch vụ bán vé thực. WinForms có 10 ghế bị vô hiệu hóa và nhãn kết nối chưa triển khai. Nhánh `feature/huy-database` có Persistence H1–H6, oracle/rebuild, probe/composition SQL của Host và test SQL thật. Nhánh `feature/duc-an-election` bổ sung Control/gate, Bully state machine, heartbeat detector và ánh xạ certificate pin sang NodeId. Booking service, kết nối coordinator với SQL, TLS/TCP listener và vòng election qua mạng vẫn cần ghép. Không có kết quả FINAL giả. Các phép kiểm tra bootstrap không thay cho nghiệm thu T01–T36.

Đích triển khai là ba server có ba SQL Server 2022 độc lập và ba quầy A/B/C. Máy kiểm thử Persistence hiện là SQL Server 2025 Express, chưa xác nhận riêng trên engine 2022; build và chạy kiểm tra bộ khung hiện tại không cần cơ sở dữ liệu.

Hướng dẫn phần Huy: [chạy H1–H4 và xem database trong SSMS](docs/implementation/HUY_WEEK1_RUNBOOK.md), [oracle/rebuild tuần 2](docs/implementation/HUY_WEEK2_RUNBOOK.md), và [hồ sơ hoàn tất H1–H6 kèm kết quả kiểm chứng](docs/implementation/HUY_COMPLETION_RECORD.md). SQL test riêng: `scripts/test-huy-sql.ps1`.

## Chuẩn bị và kiểm tra nhanh

Dùng Windows, PowerShell và .NET SDK **10.x**. `global.json` đặt mốc `10.0.100`, `rollForward=latestFeature`, không dùng prerelease; môi trường kiểm chứng bộ khung dùng **10.0.404**. Không cần cài SDK toàn máy nếu đã có bản cục bộ tại `.tools/dotnet/dotnet.exe`. Các script tìm `dotnet` trong PATH trước, rồi dùng đường dẫn cục bộ này. Thư mục `.tools/` không đưa vào Git. Chạy từ thư mục repo:

```powershell
# Kiểm tra SDK đang có; hoặc dùng .\.tools\dotnet\dotnet.exe --info
dotnet --info
.\scripts\build.ps1
.\scripts\test.ps1

.\scripts\start-server.ps1 -NodeId 1 -Check
.\scripts\start-server.ps1 -NodeId 2 -Check
.\scripts\start-server.ps1 -NodeId 3 -Check
.\scripts\start-client.ps1 -ClientId 1 -Check
.\scripts\start-client.ps1 -ClientId 2 -Check
.\scripts\start-client.ps1 -ClientId 3 -Check
```

`test.ps1` build solution rồi chạy smoke, Control/Election và Domain harness. Đây là các executable harness, không được `dotnet test` tự khám phá. `test-huy-sql.ps1` kiểm tra Persistence/Host trên SQL Server thật bằng các database tạm. `-Check` xác minh cấu hình rồi thoát, không mở giao diện hoặc tạo dịch vụ bán vé. Mỗi script trả exit code để phát hiện lỗi. Nếu chưa có SDK ở cả PATH lẫn `.tools/dotnet/`, cài/giải nén SDK .NET 10 tương thích trước khi chạy; script không tự cài phần mềm hay đổi execution policy.

Mở từng terminal riêng để quan sát tiến trình:

```powershell
.\scripts\start-server.ps1 -NodeId 1
.\scripts\start-server.ps1 -NodeId 2
.\scripts\start-server.ps1 -NodeId 3
.\scripts\start-client.ps1 -ClientId 1
.\scripts\start-client.ps1 -ClientId 2
.\scripts\start-client.ps1 -ClientId 3
```

Server chờ đến khi dừng bằng Ctrl+C; UI vẫn là bộ khung chưa kết nối. Script không tự mở nhiều cửa sổ hay tự khởi động cả cụm.

## Nếu PowerShell chặn file script

Không cần đổi chính sách bảo mật để kiểm tra bộ khung. Từ thư mục repo, có thể chạy trực tiếp CLI (cần `dotnet` trên PATH; nếu dùng SDK cục bộ thì thay `dotnet` bằng `& .\.tools\dotnet\dotnet.exe`):

```powershell
dotnet restore DoAnHTPT_Nhom9_2026.sln --configfile scripts/nuget-template.config
dotnet build DoAnHTPT_Nhom9_2026.sln --no-restore
dotnet run --project tests/De6.Template.SmokeTests --no-restore
dotnet run --project src/De6.Server.Host --no-restore -- --config config/local/s1.json --check
dotnet run --project src/De6.Client.WinForms --no-restore -- --config config/local/c1.json --check
```

## Cấu trúc thư mục

```text
DoAnHTPT_Nhom9_2026/
├── src/
│   ├── De6.Contracts/            # DTO, ID, cấu hình, canonical bytes dùng chung
│   ├── De6.Transport.Tcp/        # TCP/TLS, framing, kết nối và dispatcher
│   ├── De6.Booking.Domain/       # Quyết định nghiệp vụ và coordinator
│   ├── De6.Booking.Persistence/  # SQL Apply, snapshot, log và projection
│   ├── De6.Cluster.Election/     # Bully chọn ứng viên
│   ├── De6.Cluster.Replication/  # APPEND_APPLY, ACK, catch-up/replay
│   ├── De6.Cluster.Control/      # Authority gate, fencing, manifest, recovery
│   ├── De6.Server.Host/          # Composition root và vòng đời server
│   ├── De6.Client.Sdk/           # Routing, retry, pending và reconnect
│   └── De6.Client.WinForms/      # Giao diện 10 ghế và trạng thái quầy
├── tests/
│   ├── De6.Template.SmokeTests/  # Harness kiểm tra bộ khung, chạy bằng test.ps1
│   ├── unit/                    # Khu vực test đơn vị dự kiến
│   ├── integration/             # Khu vực test SQL/đa tiến trình dự kiến
│   └── fault-injection/         # Khu vực test điểm lỗi dự kiến
├── scripts/                     # Build, test, start-server, start-client
├── config/
│   ├── local/                   # S1–S3 và C1–C3 trên một máy phát triển
│   └── lan/                     # Mẫu cho demo nhiều máy, cần điền IP thực
├── database/
│   ├── schema/                  # Hợp đồng schema tương lai
│   ├── migrations/              # Migration có phiên bản tương lai
│   ├── seed/                    # Seed dataset dùng chung tương lai
│   └── oracle/                  # Truy vấn chứng minh bất biến tương lai
├── docs/
│   ├── team/                    # Phân công và quy trình Git
│   ├── testing/                 # Ma trận và mẫu ghi bằng chứng
│   ├── report/                  # Năm phần lý thuyết gắn với code của từng người
│   └── validation/              # Kiểm tra mô hình thiết kế, không thay test triển khai
├── .github/workflows/           # CI Windows: build + smoke, không deploy
├── .tools/                      # SDK cục bộ, không commit
├── global.json                  # Khóa SDK .NET 10
├── Directory.Build.props        # Thuộc tính build dùng chung
└── DoAnHTPT_Nhom9_2026.sln       # Solution
```

Các thư mục module có hướng dẫn sở hữu; thư mục SQL/test dự kiến không đồng nghĩa đã có schema hoặc kiểm thử nghiệp vụ hoàn chỉnh. Tài liệu V11 giữ để tham khảo lịch sử; triển khai theo V12 và `APPEND_APPLY`, không ghép PREPARED/COMMITTED của V11.

## Cấu hình local và LAN

| Môi trường | Server | Client | Điều cần giữ |
| --- | --- | --- | --- |
| Local | `config/local/s1.json`–`s3.json`; loopback, client 9001/9002/9003, peer 9101/9102/9103 | `c1.json`–`c3.json`; owner A/B/C | DatasetId giống nhau, NodeId/endpoint và thư mục dữ liệu riêng |
| LAN | `config/lan/`; ba IP máy thật, mỗi server client 9001 và peer 9100 | Danh sách ba endpoint theo IP thật | Sửa IP mẫu trước dùng; không dùng loopback để nối máy khác |

Demo vật lý: M1=S1/SQL1, M2=S2/SQL2, M3=S3/SQL3, M4=C1/A, M5=C2/B và C3/C. Trên M5 chạy hai tiến trình client với cấu hình, phiên đăng nhập và thư mục pending riêng; không cho hai tiến trình ghi chung store. SQL chỉ nhận kết nối cục bộ từ server, client không giữ connection string SQL. Bộ khung chưa thực hiện khóa pending hay kết nối LAN/TLS; đó là hạng mục triển khai.

## Thành viên và quy tắc tích hợp

| Người | Phần chính | Nhánh |
| --- | --- | --- |
| Huy | SQL/Persistence, oracle; điều phối tích hợp | `feature/huy-database` |
| Gia Quân | Domain, coordinator và dedup | `feature/gia-quan-domain` |
| Đức An | Bully, Cluster.Control và quyền phục vụ | `feature/duc-an-election` |
| Văn Khoa | Client.Sdk, pending và WinForms | `feature/van-khoa-client` |

**Bốn thành viên hiện tại đều trực tiếp viết code, test và bàn giao phần của mình.** Không ai chỉ làm báo cáo, QA hay tích hợp. Huy phụ trách SQL/Apply và receiver; Gia Quân phụ trách Domain/Contracts và sender; Đức An phụ trách Bully, Control và xác thực peer; Văn Khoa phụ trách SDK/UI, framing TCP và kết nối client. Mỗi người bàn giao luồng chức năng, test, reviewer và phần lý thuyết gắn với code.

Mỗi người tạo PR từ nhánh feature vào `develop`, được reviewer module liên quan duyệt sau build/smoke và test có bằng chứng. `main` chỉ nhận PR phát hành từ `develop` sau các cổng nghiệm thu. Không push thẳng `main`; không tự sửa hợp đồng chung ở một module. Nếu remote chưa có `develop`, trưởng nhóm phải công bố nhánh này trước khi thành viên checkout. Xem [quy trình Git](docs/team/Git_Workflow.md) để có lệnh đầy đủ.


Mỗi người đồng thời phụ trách lý thuyết gắn với code và demo: Huy — ACID/tương tranh (ND11; sách Nguyễn Xuân Anh 2014 mục 2.5/2.5.1/2.5.3, tr. 74–77); Gia Quân — loại trừ tập trung và primary-backup (ND7/ND14; mục 2.3.3.1, tr. 56–57 và 2.8.5.2, tr. 106–107); Đức An — Bully và phục hồi (ND7/ND12; mục 2.3.5.1, tr. 59 và 2.6.6, tr. 88); Văn Khoa — RPC/định danh/khách-chủ tin cậy (ND2/ND12; mục 2.1.2, tr. 25–29; 2.2.1, tr. 34; 2.6.3, tr. 82–84). Số trang là trang in trong sách đã đối chiếu. Mỗi phần bảo vệ trình bày giả thiết, các bước, bất biến, giới hạn, trace và demo module mình code. Cả bốn cùng hiểu coordinator, Bully và primary-backup. Xem [phân công](docs/team/Phan_Cong_Nhom9.md).

## Bàn giao và nghiệm thu

[Phân công chi tiết](docs/team/Phan_Cong_Nhom9.md) nêu đầu vào/đầu ra, 6 bước mỗi người, reviewer và Definition of Done từng module. Thứ tự tích hợp là G0 hợp đồng → G1 Domain/SQL/coordinator → G2 primary+backup thật → G3 ba node → G4 recovery/client/lỗi → G5 báo cáo và demo. G1 không đủ để tuyên bố FINAL hoặc chịu lỗi.

Bản Word phân công bốn thành viên, chia theo hai tuần và ghi rõ đầu việc, file/API, cách tự kiểm, bàn giao, reviewer và lý thuyết nằm tại [Phân công nhiệm vụ nhóm 9](docs/team/Phan_Cong_Nhiem_Vu_Nhom9_4_Thanh_Vien.docx). Bản Markdown có cùng nội dung chi tiết tại [docs/team/Phan_Cong_Nhom9.md](docs/team/Phan_Cong_Nhom9.md).

Mỗi module chỉ hoàn tất khi build được, hợp đồng V12 thống nhất, có test/oracle phù hợp và được reviewer xác nhận. Từng mã [T01–T36](docs/testing/TEST_MATRIX.md) phải có [record bằng chứng](docs/testing/TEST_RECORD_TEMPLATE.md): commit SHA, dataset, cấu hình, log ba node, SQL oracle và sự khác nhau giữa LocalApplied, Stable, FINAL. Tất cả test nghiệp vụ trong ma trận bắt đầu ở trạng thái **CHƯA CHẠY**; CI smoke thành công không đổi trạng thái này.

## Báo cáo lý thuyết gắn với code

Mỗi người tự hoàn thiện phần sách và code/test của mình trong các file dưới đây; đây là khung, chưa phải nội dung đã hoàn tất.

- Huy: [file báo cáo riêng](docs/report/01_huy_giao_tac.md).
- Gia Quân: [file báo cáo riêng](docs/report/02_gia_quan_loai_tru.md).
- Đức An: [file báo cáo riêng](docs/report/03_duc_an_bully.md).
- Huy, Gia Quân và Đức An: [khung báo cáo primary-backup và phục hồi](docs/report/04_primary_backup_phuc_hoi.md).
- Văn Khoa: [file báo cáo riêng](docs/report/05_van_khoa_truyen_thong.md).
