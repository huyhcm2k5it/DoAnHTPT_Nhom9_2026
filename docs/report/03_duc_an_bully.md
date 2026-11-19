# Bầu chọn Bully, quyền phục vụ và phục hồi

**Đồ án Các hệ thống phân tán PTIT · Nhóm 9 · Đề 6 · Phần Đức An, Huy thực hiện thay**

Cụm bán vé có ba node S1, S2, S3. Khi primary xảy ra sự cố, hệ thống cần tìm một ứng viên, xác định lịch sử được giữ và cấp lại quyền ghi. Đề 6 dùng Bully cho bước đầu. Control, fencing và đối soát dữ liệu theo V12 quyết định hai bước sau. [Phân công A1–A6](../team/Phan_Cong_Nhom9.md) · [Thiết kế V12](../De6_Thiet_Ke_Trien_Khai_V12.md)

## 1. Cơ sở lý thuyết và liên hệ môn học

Bầu chọn là bài toán chọn một tiến trình giữ vai trò điều phối trong một tập tiến trình phân tán. Bully dùng ID có thứ tự để phân xử. Khi liên lạc ổn định và các thành viên đủ điều kiện nhìn thấy nhau, tiến trình có ID cao nhất trong nhóm đó có thể công bố kết quả bầu. Giả thiết về liên lạc rất quan trọng vì một tiến trình không thể suy ra chắc chắn rằng peer đã chết chỉ từ việc chờ quá hạn.

| Nội dung môn học | Liên hệ trong đề 6 | Minh chứng |
| --- | --- | --- |
| ND7, bầu chọn điều phối viên | ID có thứ tự, ELECTION, ANSWER, COORDINATOR và bầu lại | Trace S1 và S2 khi S3 mất liên lạc, T24 |
| ND12, chịu lỗi và phục hồi | Phát hiện nghi ngờ, giữ dữ liệu đã xác nhận, phục hồi bằng replay, node cũ quay lại | Đọc lại manifest cùng RunId, T18, T20–T26, T28, T35–T36 |
| ND13, xác thực và quyền | Ánh xạ danh tính peer sang NodeId rồi kiểm quyền theo từng thao tác | Kiểm pin, sender, epoch và nguồn replay, T33 |
| ND14, primary-backup | Đối chiếu bản sao tại cùng mốc và bảo toàn kết quả FINAL | Kiểm HeadHash và digest tại K, T18, T20, T30 |

Ánh xạ này theo mục 2 của V12 và phần phân công. Mã CLO và rubric chính thức của lớp cần được đối chiếu khi nhóm nhận đúng đề cương từ giảng viên. [Đối chiếu slide trong V12](../De6_Thiet_Ke_Trien_Khai_V12.md#2-căn-cứ-bài-giảng-và-kết-quả-cần-chứng-minh)

## 2. Thuật toán Bully trong đề 6

**Giả thiết.** S1=1, S2=2, S3=3. Mỗi node biết danh sách thành viên và ID của nó. Một node đủ điều kiện bầu khi đã được admit và đồng bộ trong đợt hoạt động hiện hành. Node đang RECOVERING bị loại khỏi tập ứng viên. Timeout có thể phát sinh do crash, nghẽn hoặc mạng bị chia cắt. Hệ thống dùng đồng hồ đơn điệu để đo thời gian chờ.

| Sự kiện | Xử lý |
| --- | --- |
| Thiếu ba heartbeat liên tiếp với chu kỳ 1 giây | Đánh dấu nghi ngờ. Có thể bắt đầu bầu ứng viên trong quy trình phục hồi. |
| Bắt đầu vòng bầu | Gửi ELECTION tới các ID cao hơn và chờ ANSWER trong 2 giây. |
| Node cao hơn nhận ELECTION hợp lệ và đủ điều kiện | Trả ANSWER, rồi bắt đầu hoặc duy trì vòng bầu của chính nó. |
| Hết hạn mà không có ANSWER hợp lệ | Công bố COORDINATOR và ghi ứng viên. |
| Đã có ANSWER hợp lệ | Chờ COORDINATOR tối đa 5 giây. Hết hạn thì lên lịch bầu lại với backoff từ 0 đến 500 ms. |
| Nhận COORDINATOR hợp lệ trong giai đoạn phục hồi | Cập nhật ứng viên và vòng bầu. Quyền phục vụ tiếp tục do Control quản lý. |

`ElectionRound` gồm ID tiến trình khởi xướng và GUID của vòng. Round giúp đối chiếu ANSWER và khử bản tin lặp. `AuthorityEpoch` xác định đợt quyền hiện hành. Sau khi chuyển epoch, bản tin bầu từ epoch cũ bị bỏ. Thời gian trong bảng là tham số demo V12.

**Vì sao chọn được ứng viên?** Khi kết nối ổn định, một ID thấp hơn nhận được ANSWER từ node đủ điều kiện cao hơn sẽ chờ kết quả. Node đủ điều kiện cao nhất không còn node cao hơn có thể trả lời, nên nó công bố ứng viên. Nếu nhiều node khởi động bầu và đều phải hỏi các ID cao hơn, số bản tin của một đợt bầu có thể tăng bậc hai theo số thành viên. Trong mạng bị chia cắt, từng phía có thể nghi ngờ khác nhau. Điều kiện cấp quyền ở mục 4 vẫn phải được kiểm tra.

## 3. Trạng thái và quyền phục vụ

| Khái niệm | Ý nghĩa |
| --- | --- |
| Candidate và round | Kết quả bầu của một vòng, lưu riêng với quyền của primary. |
| AcceptedEpoch và AcceptedLeader | Cặp epoch và ID leader đã được chấp nhận trên node. |
| RECOVERING | Mỗi lần boot bắt đầu ở trạng thái đóng. Accepted authority lưu từ lần trước vẫn có thể được đọc để đối soát. |
| MAINTENANCE | Dừng nhận xử lý mới và chốt dữ liệu tại một mốc để kiểm tra hoặc đồng bộ. |
| PRIMARY_READY và BACKUP_READY | Primary đủ điều kiện thực hiện nghiệp vụ theo gate. Backup đã đồng bộ nhận cập nhật được cấp quyền. |
| BLOCKED_UNCERTAIN | Cổng đóng khi còn thao tác chưa rõ hoặc bằng chứng chưa đủ để tiếp tục. |
| LastAppliedIndex, StableIndex và FINAL | Mốc đã Apply cục bộ, mốc đã kiểm chứng trên các bản sao, và kết quả có thẩm quyền trả cho client. |

Gate là điểm kiểm tra điều kiện trước mỗi thao tác. Quyền ghi của primary gắn với epoch, leader và index dự kiến. Quyền đọc nghiệp vụ cần mốc đã ổn định, với `StableIndex == LastAppliedIndex == expectedIndex` khi cấp quyền snapshot. Freeze dừng cấp quyền cho thao tác mới rồi chờ các lease đang hoạt động kết thúc. Nếu thao tác còn dở khiến dữ liệu chưa thể chốt an toàn, node giữ trạng thái đóng. Mỗi chuyển trạng thái mở quyền phải đi qua Control.

## 4. Chuyển quyền và bảo toàn dữ liệu

1. Chọn hai participant sống đủ điều kiện và freeze chúng. Kiểm chứng primary cũ đã bị cách ly bằng quy trình quản trị, ví dụ dừng hẳn Host và ngăn tự khởi động lại, hoặc tắt máy. Timeout, kết quả bầu và epoch mới chỉ cung cấp thông tin cho quy trình.
2. Kiểm tra hai node khác nhau, cùng DatasetId, schema và quyền đã chấp nhận. Kiểm chuỗi hash và dữ liệu nghiệp vụ. Tại mỗi index chung, các checkpoint phải khớp. Nếu cùng index có hash hoặc digest khác, giữ đóng để điều tra.
3. Khi một log là tiền tố của log kia, lấy log dài hơn làm nguồn. Ví dụ S2 thắng bầu với log tới 4, S1 có log tới 5 và hai log khớp tới 4. S1 gửi event 5 để S2 bắt kịp. Cả hai phải đạt K=5 và có cùng HeadHash cùng digest dữ liệu nghiệp vụ và kết quả. Vai trò ứng viên của S2 được giữ.
4. Ghi một manifest chung gồm RunId, DatasetId, old/new epoch, candidate, hai participant, K, HeadHash, digest và tham chiếu bằng chứng fencing. Mỗi participant ghi manifest và accepted authority mới trong một transaction cục bộ. Khi một bước ghi hoặc hoàn tất bị gián đoạn, sử dụng lại chính manifest và RunId. Control hiện có bước ghi và đọc lại trạng thái hoàn tất trên đủ participant.
5. Khi đã có authorization hợp lệ, áp dụng quyền trên node đúng dataset và participant, với mốc local khớp K. Đặt mốc ổn định tại K. Candidate trở thành PRIMARY_READY và node còn lại thành BACKUP_READY theo quy trình V12. Client reconnect, đăng nhập lại và tra cứu yêu cầu đang chờ bằng RequestId cũ.

**Vì sao giữ được FINAL khi mất một node?** Một FINAL phải có cùng event và kết quả lưu bền vững trên primary và ít nhất một backup. Trong tập ba node, mọi cặp hai node còn sống giao với mọi cặp hai node từng lưu FINAL. Vì thế, bất kỳ FINAL nào cũng còn trong ít nhất một log của hai survivor, với giả thiết một lỗi và dữ liệu còn nguyên. Kiểm quan hệ tiền tố và giữ log dài hơn giúp giữ lại các event đó. Chỉ còn một survivor thì lập luận giao hai tập không còn áp dụng.

**Nếu client chưa thấy kết quả.** Sau local commit mà chưa có backup Apply, event có thể chỉ tồn tại trên primary cũ. Nếu backup đã Apply nhưng ACK hoặc phản hồi thất lạc, log của backup có thể vẫn giữ event. Client thấy UNKNOWN trong cả hai tình huống và phải tra cứu hoặc retry cùng RequestId. Giao thức ghi nhận kết quả theo lịch sử được giữ sau phục hồi. [V12, mục 12–13](../De6_Thiet_Ke_Trien_Khai_V12.md#12-bầu-chọn-bully-và-chuyển-quyền)

## 5. Xác thực peer và quyền replay

Xác thực peer trả lời câu hỏi node từ xa là ai. V12 dùng chứng thư peer đã cấu hình, ánh xạ pin sang NodeId rồi đối chiếu với danh tính khai báo. Sau bước đó, mỗi loại bản tin được kiểm quyền riêng. Khi nối đầy đủ Host/TCP, cổng dành cho quầy phải chặn peer/admin RPC.

| Thao tác | Điều kiện cấp quyền |
| --- | --- |
| ELECTION, ANSWER, COORDINATOR | Peer và sender khớp, cùng dataset, đúng epoch, round hợp lệ và đáp ứng điều kiện theo loại bản tin. |
| APPEND_APPLY | Nguồn là leader đã chấp nhận tại epoch hiện hành, đích có quyền nhận bản sao và event đúng index/prefix. |
| REPLAY_ENTRY | Nguồn đã xác thực trùng nguồn được chỉ định trong RecoveryContext cục bộ. RunId, dataset, đích, authority đích, thời hạn và mốc base/K phải khớp. |

Context replay là quyền giới hạn để nhận lịch sử đã chốt trên node đang đóng cổng. Nó được thu hồi khi hoàn tất, hủy hoặc chặn run. Event được replay giữ nguyên EventEpoch, WriterId, EventId, payload và hash. AuthorityEpoch của quyền vận hành hiện tại được kiểm tra riêng. Nhờ vậy, event cũ hợp lệ vẫn có thể được sao chép dưới một run mới mà không biến thành một giao tác bán vé mới.

## 6. Bootstrap, restart và rejoin

- **Bootstrap lần đầu.** V12 yêu cầu đủ ba kho dữ liệu mới với NodeId khác nhau và cùng dataset/schema/seed tại K=0. So seed hash và digest, lưu rồi đọc lại quyền epoch 1 cho S3 trên cả ba. Lõi hiện có planner kiểm tra dữ liệu đầu vào và tạo manifest bootstrap.
- **Restart.** Node boot vào RECOVERING. Khi có manifest dở, đọc bằng chứng trên các participant rồi tiếp tục đúng run. Không tạo run cạnh tranh hoặc hạ epoch khi thấy metadata không đồng nhất. Nếu thiếu bằng chứng, giữ đóng.
- **Rejoin theo tiền tố.** Chốt trên primary `K = StableIndex = LastAppliedIndex` sau khi xử lý xong mutation đang chờ. Node trở lại nhận các event còn thiếu. So HeadHash và digest tại K trước khi lưu quyền hiện hành và admit thành backup. NodeId và bí mật cấu hình vẫn thuộc từng máy.
- **Rejoin có đuôi xung đột.** Planner trả `RequiresArchiveRepair`. Thiết kế yêu cầu người vận hành duyệt RepairPlan, lưu trữ log cũ và dựng lại từ seed cùng log hiện hành. Đường thực hiện archive/repair còn chờ tích hợp. S3 trở lại với ID cao hơn vẫn phải qua các bước rejoin.

## 7. Đối chiếu lý thuyết với code

| Đầu việc | Nơi đọc code | Điểm cần chỉ khi bảo vệ |
| --- | --- | --- |
| A1, boot đóng | [ClusterControl.cs](../../src/De6.Cluster.Control/ClusterControl.cs) | Constructor và snapshot thể hiện vai trò ban đầu, StableIndex và quyền phục vụ. |
| A2, heartbeat và Bully | [BullyElection.cs](../../src/De6.Cluster.Election/BullyElection.cs) · [HeartbeatFailureDetector.cs](../../src/De6.Cluster.Election/HeartbeatFailureDetector.cs) | Trình bày thời gian chờ, round, nhận notice và kết quả candidate. |
| A3, gate và peer | [ClusterControl.cs](../../src/De6.Cluster.Control/ClusterControl.cs) · [AuthenticatedPeerIdentity.cs](../../src/De6.Contracts/AuthenticatedPeerIdentity.cs) | Phân biệt xác thực danh tính với cấp quyền. Chỉ ra lease và freeze. |
| A4, đối soát và chuyển quyền | [ClusterRecoveryPlanner.cs](../../src/De6.Cluster.Control/ClusterRecoveryPlanner.cs) · [RecoveryManifestCoordinator.cs](../../src/De6.Cluster.Control/RecoveryManifestCoordinator.cs) · [SqlRecoveryManifest.cs](../../src/De6.Booking.Persistence/SqlRecoveryManifest.cs) | Chọn nguồn theo prefix, kiểm fence, ghi cùng manifest, đọc lại và cấp authorization. |
| A5, replay | [ClusterControl.cs](../../src/De6.Cluster.Control/ClusterControl.cs) · [SqlReplicaApply.cs](../../src/De6.Booking.Persistence/SqlReplicaApply.cs) | Đối chiếu nguồn, context, index, hash và epoch lịch sử. |
| A6, bootstrap và rejoin | [ClusterRecoveryPlanner.cs](../../src/De6.Cluster.Control/ClusterRecoveryPlanner.cs) · [Control test harness](../../tests/unit/De6.Cluster.Control.Tests/Program.cs) | Kiểm seed, từ chối nhánh lệch, tiếp tục cùng manifest khi ghi dở. |

## 8. Trace trình bày và kết quả kiểm thử

| Kịch bản | Trình tự và điểm quan sát | Mức bằng chứng |
| --- | --- | --- |
| T24, S3 mất liên lạc | S1 gửi ELECTION đến S2/S3. S2 trả ANSWER và hỏi S3. Không có ANSWER từ S3, S2 công bố COORDINATOR. Ghi round, sender và timestamp. Quan sát candidate S2 cùng cổng đang đóng. | Trace thiết kế. Harness kiểm riêng chọn ứng viên, timeout và notice lỗi. |
| T22–T23, primary cũ còn chạy | Hai node nghi ngờ S3. S2 có thể được bầu, nhưng khi thiếu bằng chứng fence thì quy trình từ chối mở quyền mới. Quan sát cờ phục vụ. | Harness kiểm từ chối thiếu fence. Phân vùng trên cụm thật còn chờ. |
| T18/T25, candidate thiếu event và ghi manifest dở | Replay từ node có prefix dài hơn, sau đó S1 ghi manifest thành công, S2 chưa ghi. Retry cùng RunId và K. Chỉ cấp quyền khi đọc lại đủ. | Lõi có test chọn prefix dài hơn và một fixture độc lập chèn lỗi ghi/hoàn tất manifest. Hai ca chưa ghép thành demo SQL hai participant. |
| T26/T28/T33, bằng chứng sai | Thiếu participant, replay sai nguồn hoặc bên gửi khai sai NodeId. Quan sát lỗi và trạng thái gate. | Kiểm riêng ở harness, cần record mạng thực trước nghiệm thu. |

Chạy từ thư mục repo:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test.ps1 -Configuration Release
```

Lần kiểm chứng ngày 19-11-2026 sau khi sửa fixture, build Release đạt 0 lỗi và 0 cảnh báo. Smoke đạt 23/23, Control/Bully đạt 22/22 và Domain đạt 89/89. Bộ Control dùng đồng hồ, vận chuyển hoặc store giả lập cho các tình huống liên quan. Lần chạy SQL đã qua replay và 27 ca nâng cao. Bộ SQL đầy đủ còn cần xác nhận lại sau timeout kết nối và sau khi sửa kỳ vọng của test schema bị sửa trái phép.

Khi chạy G3/G4, ghi commit SHA, node và dataset, round và epoch, RunId, mốc K, hash/digest, lỗi được tạo, trạng thái cổng trước/sau và kết quả client. Giữ trạng thái nghiệm thu theo ma trận đến khi có record của cụm thật. Các đường nối lịch heartbeat, Host/TCP, quản trị fencing, replay và archive/repair vẫn cần tích hợp. [PR #5](https://github.com/huyhcm2k5it/DoAnHTPT_Nhom9_2026/pull/5) · [Ma trận T01–T36](../testing/TEST_MATRIX.md) · [Mẫu ghi bằng chứng](../testing/TEST_RECORD_TEMPLATE.md)

## 9. Câu hỏi bảo vệ

| Câu hỏi | Trả lời ngắn |
| --- | --- |
| Vì sao S2 thắng Bully mà có thể vẫn đóng cổng? | Kết quả bầu chỉ ghi ứng viên. Quyền ghi cần cách ly primary cũ, đối soát lịch sử và xác minh manifest. |
| Timeout ba heartbeat chứng minh điều gì? | Nó báo nghi ngờ mất liên lạc theo mốc demo. Crash và trễ mạng đều có thể tạo cùng quan sát. |
| Tại sao log dài hơn được chọn? | Chỉ chọn khi log ngắn là tiền tố đã kiểm chứng. Nếu hai bản cùng index mà khác hash hoặc digest thì dừng. |
| Vì sao vừa kiểm hash chuỗi vừa kiểm digest? | Hash kiểm lịch sử event. Digest giúp kiểm tra projection và kết quả nghiệp vụ có khớp với lịch sử tại cùng K. |
| Nếu chỉ một node đã ghi manifest rồi tiến trình chết? | Boot giữ cổng đóng, lấy lại đúng RunId và manifest, hoàn tất phần còn thiếu rồi đọc lại đủ participant. |
| Vì sao node cũ có ID cao hơn không tự giành lại primary? | Node cũ phải catch-up và được admit. Mỗi lần thay primary lại cần qua quy trình cấp quyền. |
| Vì sao peer có chứng thư hợp lệ vẫn có thể bị từ chối replay? | Danh tính chỉ xác định nguồn. RecoveryContext còn ràng buộc RunId, nguồn/đích, mốc dữ liệu, epoch và thời hạn. |
| Cụm cam kết chịu bao nhiêu lỗi? | Một node không sẵn sàng trong đợt hoạt động bắt đầu từ ba bản sao đã đồng bộ, theo giả thiết V12. Lỗi thứ hai trước khi phục hồi đủ ba node nằm ngoài mức cam kết. |

**Trình tự trình bày đề xuất.** Giới thiệu ba node và lý do cần bầu. Chạy qua trace S3 mất heartbeat rồi chỉ ra cổng vẫn đóng. Dùng ví dụ S2 tới index 4 và S1 tới index 5 để giải thích đối soát. Kết thúc bằng ca retry cùng RunId, kết quả kiểm thử đã có và phần cần nghiệm thu trên cụm.

## Tài liệu tham khảo

1. Lê Ngọc Bảo, bài giảng Các hệ thống phân tán, PTIT. ND7 slide 9–10 về Bully, ND12 slide 2–3, 6, 8–9, 11–14 về chịu lỗi và phục hồi, ND13 và ND14 theo ánh xạ tại mục 2 của V12.
2. Nguyễn Xuân Anh, *Bài giảng Hệ thống phân tán*, PTIT, 2014. Mục 2.3.5.1, tr. 59 về Bully, mục 2.6.6, tr. 88 về phục hồi, mục 2.8.5.2, tr. 106–107 về primary-based.
3. Héctor García-Molina, *Elections in a Distributed Computing System*, IEEE Transactions on Computers 31(1), 1982, tr. 48–59, phụ lục I. [Bài báo gốc](https://csis.pace.edu/~marchese/CS865/Papers/garcia_elections.pdf)
4. Nhóm 9, [Thiết kế V12](../De6_Thiet_Ke_Trien_Khai_V12.md), mục 2, 5–6, 12–15, 17 và phần nghiệm thu. [Báo cáo chung primary-backup và phục hồi](04_primary_backup_phuc_hoi.md)
