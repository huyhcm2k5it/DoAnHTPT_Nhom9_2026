# Kế hoạch hai tuần cho phần Đức An

Huy đảm nhiệm cả phần SQL/Persistence của Huy và phần Bully, Control, xác thực peer và điều phối phục hồi của Đức An. Phần Đức An làm trên `feature/duc-an-election`, tách theo phạm vi PR để nhóm dễ review. Hai tuần dưới đây là thứ tự triển khai và bàn giao.

## Điểm xuất phát trên develop

- `De6.Cluster.Election` có `ElectionNotice`, `ElectionRound` và `IElectionObserver`. `De6.Cluster.Control` có `BootstrapState` cố định `RECOVERING`, `ServingEnabled=false`, `StableIndex=0`. Host có kiểm tra cấu hình/SQL và chưa có đường mở quyền phục vụ hoàn chỉnh.
- Phần Huy đã có `IStoreAccessGate`, `SqlBookingStore`, `AppendAndApplyAsync`, `ApplyReplayEntryAsync`, `AuditLocalAsync`, `GetFrozenPrefixAsync` và `RebuildProjectionAsync`. Cần nối gate thật, đọc log theo mốc và lưu/đọc lại manifest. `RebuildProjectionAsync` dựng projection từ log cục bộ hiện có, nên sửa một log có đuôi xung đột cần đường repair riêng có archive.
- Coordinator của Gia Quân đã có `IAuthorityGate`. `De6.Booking.Domain.IBookingStore` và `De6.Contracts.IBookingStore` là hai interface khác nhau. Chốt cách ghép coordinator với SQL và đường công bố `StableIndex` ở Host cùng Gia Quân trước khi nối gate vào luồng thật. Nhánh này bám [thiết kế V12](../De6_Thiet_Ke_Trien_Khai_V12.md), mục 5, 6, 10–17 và 20.

## Tuần 1 — Kiểm soát quyền và bầu ứng viên

| Việc | Nơi làm chính | Đầu ra để bàn giao |
| --- | --- | --- |
| A1 Boot đóng | `De6.Cluster.Control`, `De6.Server.Host` | Mọi lần boot/restart bắt đầu `RECOVERING`, từ chối thao tác nghiệp vụ. |
| A2 Bully | `De6.Cluster.Election` | Hội tụ ứng viên trong nhóm node đủ điều kiện, với trace cho nhánh timeout. |
| A3 Gate và quyền peer | `De6.Cluster.Control`, biên Host/TCP và adapter SQL/Domain | Dùng chung một nguồn trạng thái quyền cho coordinator và store. Peer được định danh trước khi dispatch. |

### A1 Khởi động ở trạng thái đóng

1. Giữ luồng kiểm tra cấu hình và SQL hiện có của Host. Thêm trạng thái Control với một nơi ghi trạng thái phục vụ. Khi khởi động, dù SQL lưu `AcceptedEpoch`/`AcceptedLeader` của lần trước, quyền xử lý vẫn đóng và `StableIndex` RAM bắt đầu từ 0.
2. Lưu kết quả bầu ứng viên và round riêng với `AcceptedLeader`, epoch đã chấp nhận và quyền phục vụ. Cho phép tra trạng thái quản trị kèm node, dataset, role, epoch, cờ phục vụ và mốc dữ liệu. Ghi log chuyển trạng thái và lý do.
3. Tự kiểm bằng hai lần khởi động: DB mới và DB đã có quyền từ lần chạy cũ. Cả hai phải báo `RECOVERING`, `ServingEnabled=false`. Test đường cấp quyền ở A4/A6.

### A2 Bầu chọn Bully

1. Cài vòng bầu trên `IElectionObserver`. Nhận notice đã qua xác thực, kiểm dataset, epoch hiện hành, sender, round và điều kiện admitted/đã đồng bộ. Gửi `ELECTION` tới các ID cao hơn. Node đủ điều kiện trả `ANSWER` và khởi động hoặc duy trì vòng của mình. Khử lặp theo loại bản tin, sender và `ElectionRound`, đồng thời kiểm câu trả lời thuộc vòng đang chờ.
2. Áp dụng mốc demo V12: heartbeat 1 giây, nghi ngờ sau 3 lần thiếu, chờ `ANSWER` 2 giây, chờ `COORDINATOR` 5 giây, backoff 0–500 ms khi tái bầu. Dùng thời gian đơn điệu và bộ gửi/đồng hồ thay được để kiểm riêng các nhánh. Không có `ANSWER` thì công bố ứng viên, có `ANSWER` mà thiếu `COORDINATOR` thì tái bầu.
3. `COORDINATOR` cập nhật ứng viên trong giai đoạn phục hồi. Quyền của primary đang hoạt động chỉ thay đổi qua Control. Tự kiểm ba node đủ điều kiện, node ID cao nhất vắng mặt, hai vòng đồng thời, mất notice, bản tin lặp/epoch cũ và node đang `RECOVERING`. Ghi trace T24 và kiểm gate vẫn đóng ở ứng viên.

### A3 Ràng buộc gate và xác thực peer

1. Chốt với Gia Quân cách cấp grant và cách freeze làm coordinator ngừng nhận thao tác mới. Dùng cùng trạng thái quyền cho `IAuthorityGate` và adapter `IStoreAccessGate`. Store giữ lease qua `await`. Freeze chặn lease mới trước khi chờ thao tác hiện tại. `Block` có thể đóng quyền khi caller đang giữ lease, nên tách việc báo đóng khỏi việc chờ lease thoát để tránh deadlock.
2. Kiểm riêng từng lease. Primary phải đúng epoch/leader và thứ tự grant. Snapshot đòi `PRIMARY_READY` và `StableIndex == LastAppliedIndex == expectedIndex`. Append kiểm quyền leader đã chấp nhận trên backup đủ điều kiện. Recovery inspection chỉ đọc chứng cứ. Maintenance chỉ cấp khi đã đóng, freeze đúng mốc và chặn chuyển quyền trong thời gian lease. Replay dùng context cục bộ ở A5.
3. Chốt với Văn Khoa cách Host nhận danh tính TLS đã xác thực. Ánh xạ chứng thư đã cấu hình sang `NodeId`, kiểm lại `SenderId`/dataset/epoch theo loại bản tin rồi mới chuyển cho handler. Cổng quầy chặn peer/admin RPC. Gia Quân dùng cùng danh tính kết nối khi so receipt. Tự kiểm peer sai danh tính, lease sai epoch/role và freeze xen giữa thao tác (T22, T33).

Cuối tuần 1 bàn giao bảng chuyển trạng thái, trace Bully và chữ ký adapter cho Huy/SQL, Gia Quân/coordinator và Văn Khoa/TCP. Có thể kiểm A1–A3 riêng với fixture, miễn ghi rõ phần nào được thay bằng fake.

## Tuần 2 — Chuyển quyền, replay và rejoin

| Việc | Nơi làm chính | Đầu ra để bàn giao |
| --- | --- | --- |
| A4 Fence, đối soát và manifest | `De6.Cluster.Control`, Host quản trị, `De6.Booking.Persistence`, migration theo phiên bản | Quy trình promote có kiểm chứng cách ly, so log và đọc lại manifest trên hai participant. |
| A5 Quyền replay theo run | `De6.Cluster.Control`, receiver/SQL của Huy | `RecoveryContext` được cài cục bộ trên node đóng, có giới hạn nguồn, đích, mốc và thời hạn. |
| A6 Bootstrap, restart và rejoin | Host, Control, SQL và các adapter mạng | Bootstrap dataset mới, tiếp tục run dở, đưa node cũ về đúng lịch sử, có trace và record. |

### A4 Đối soát và chuyển quyền có kiểm soát

1. Mở một recovery run qua đường quản trị. Đóng quyền nghiệp vụ hai participant, chờ hoặc đánh dấu operation còn chưa rõ, rồi xác nhận đã cách ly primary cũ theo V12: dừng Host và ngăn khởi động lại, hoặc tắt máy. Heartbeat timeout chỉ khởi động kiểm tra. Thiếu bằng chứng fence thì giữ đóng.
2. Kiểm hai participant khác nhau, cùng dataset/schema và quyền hiện hành. Đọc bằng chứng khi đã freeze. So hash tại cùng mốc, kiểm hash chain và projection/result. Nếu một lịch sử là tiền tố của lịch sử kia, dùng lịch sử dài hơn làm nguồn replay ở A5. Cùng index khác hash là conflict và dừng. ID cao hơn chỉ dùng để bầu ứng viên, mốc dữ liệu do đối soát quyết định.
3. Phía Huy bổ sung migration lưu manifest và API ghi/đọc lại qua adapter quản trị. Mỗi participant lưu manifest cùng quyền mới bền vững trong một transaction cục bộ sau khi dữ liệu khớp. Manifest có `RunId`, old/new epoch, candidate, hai participant, `K`, `HeadHash` và digest. Tiếp tục run dở bằng cùng dữ liệu manifest và cùng epoch. Crash sau lần ghi thứ nhất phải giữ gate đóng cho tới khi xác minh đủ hai bản.
4. Đọc lại manifest, quyền mới và mốc dữ liệu trên cả hai. Sau khi đều khớp, đặt `StableIndex=K`, mở `PRIMARY_READY` chỉ cho candidate và đánh dấu node kia `BACKUP_READY`. Tự kiểm thiếu participant, thiếu fence, prefix conflict, crash giữa hai lần ghi và readback lệch (T18, T22–T26).

### A5 Giới hạn quyền replay

1. Cài `RecoveryContext` ở phía đích sau khi xác nhận run quản trị. Dùng kiểu hiện có cho IDs, target epoch/leader, base index/hash và target K/head hash. Theo dõi loại Join/Promotion và thời hạn ở trạng thái run cục bộ. Context chỉ dùng trong run được cấp và bị thu hồi khi kết thúc/hủy. So nguồn đã xác thực với context cục bộ trước khi gọi SQL.
2. Gửi/lưu nguyên event bytes, kết quả và metadata lịch sử qua `ApplyReplayEntryAsync`. SQL kiểm index/prefix và phân biệt `AuthorityEpoch` với `EventEpoch`. Đọc lại trạng thái tại K để so cả head hash và business/result digest. Dữ liệu nhận qua mạng trước khi vào transaction cục bộ.
3. Tự kiểm nguồn hoặc run sai, context hết hạn, index ngoài mốc, event lịch sử có epoch cũ hợp lệ, GAP và duplicate. Replay thành công vẫn giữ gate đóng cho đến bước xác nhận quyền phù hợp (T20, T21, T28, T30).

### A6 Bootstrap, restart và rejoin

1. Bootstrap mới theo V12 mục 17: xác minh đủ ba dataset mới cùng seed tại K=0, `NodeId` riêng, rồi ghi và đọc lại quyền epoch 1/leader S3 trên cả ba trước khi mở S3. Dữ liệu đã có thì phục hồi theo trạng thái/manifest hiện hành.
2. Khi node cũ quay lại, giữ `RECOVERING`. Freeze primary ở mốc `K=StableIndex=LastAppliedIndex` sau khi giải quyết thao tác đang chờ. Nếu đúng tiền tố thì gửi phần còn thiếu. Nếu có đuôi khác thì yêu cầu `RepairPlan` do admin xác nhận, archive trước khi dựng lại từ seed và log hiện hành, kiểm lại NodeId riêng. So hash/digest tại K, lưu quyền đã chấp nhận rồi admit node về `BACKUP_READY`. Primary phải còn ít nhất một backup hợp lệ trước khi mở lại.
3. Chạy G3 với ba server và ba database SQL độc lập khi sender/receiver và transport đã nối. Chạy phần liên quan ở G4 với Gia Quân và Văn Khoa. Lưu SHA, dataset, node/epoch, `RunId`, mốc K, hash/digest, lỗi đã chèn và kết quả thực theo [mẫu record](../testing/TEST_RECORD_TEMPLATE.md). Theo [ma trận nghiệm thu](../testing/TEST_MATRIX.md), đặc biệt T18, T20–T26, T28, T30 và T33.

## Bàn giao khi một người giữ hai phần

| Ranh giới | Huy cần bàn giao ở hai vai | Người review cùng |
| --- | --- | --- |
| Control ↔ SQL | Lease/gate, kiểm chứng mốc log, manifest transaction, replay và repair. Tách commit quyền phục vụ khỏi commit SQL để review hợp đồng trước. | Gia Quân review grant/Stable và điều kiện mở quyền. |
| Control ↔ coordinator/sender | Freeze, quyền theo epoch/leader, `StableIndex` và điều kiện dùng receipt. | Gia Quân |
| Control ↔ Host/TCP | NodeId từ chứng thư, phân loại client/peer/admin, đăng ký handler. | Văn Khoa |

Trước khi mở PR, ghi phần nào đã chạy riêng với fixture, phần nào chạy với SQL thật và phần nào đã ghép đủ ba node. Dùng `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test.ps1` để chạy các kiểm tra bộ khung và Domain có sẵn. Dùng [runbook SQL](HUY_WEEK2_RUNBOOK.md) cho kiểm tra Persistence. Những kết quả đó không thay record G3/G4. Đích PR là `develop`.

## Phần lý thuyết và demo

Đọc ND7 slide 9–10 và sách Nguyễn Xuân Anh, mục 2.3.5.1, tr. 59, để trình bày Bully. Đọc ND12 và mục 2.6.6, tr. 88, cho phục hồi. Hoàn thiện [khung báo cáo Bully](../report/03_duc_an_bully.md) và phần Control trong [khung primary-backup và phục hồi](../report/04_primary_backup_phuc_hoi.md). Chuẩn bị hai trace để bảo vệ: S3 mất heartbeat, S2 thắng bầu nhưng chỉ mở sau fence và readback; crash sau khi ghi manifest ở một participant, restart ở trạng thái đóng và tiếp tục cùng `RunId`.
