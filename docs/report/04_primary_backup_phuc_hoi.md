# Primary-backup và phục hồi

Phần này do Huy, Gia Quân và Đức An cùng hoàn thiện. Mỗi người trình bày phần gắn với module và test mình trực tiếp làm. Đây là khung báo cáo, chưa phải kết luận nghiệm thu.

Nguồn sách là ND14/ND12 và sách Nguyễn Xuân Anh, *Bài giảng Hệ thống phân tán*, mục 2.8.5.2 về giao thức dựa trên bản chính, tr. 106–107, cùng mục 2.6.6 về phục hồi, tr. 88. Phạm vi kỹ thuật là V12 mục 10–14 và 20–23.

## Huy về durable Apply và receipt

Giải thích transaction ghi event/result/projection, thời điểm commit bền vững, cách receiver xử lý duplicate/GAP/conflict và vì sao ACK chỉ được tạo sau receipt từ store. Liên hệ `SqlBookingStore`, `AppendAndApplyAsync`, T11, T13–T15 và oracle SQL. Nêu giới hạn của một node local.

## Gia Quân về replication và FINAL

Giải thích sender chuyển đúng event đã tạo, backup trả receipt khớp NodeId/epoch/index/EventId/hash và coordinator chỉ công bố Stable/FINAL khi có chứng cứ hợp lệ. Liên hệ `CommandCoordinator`, `IReplicaSender`, T10–T12, T27 và luồng LocalApplied → ACK → Stable → FINAL. Nêu cách xử lý timeout và ACK sai.

## Đức An về recovery và fencing

Giải thích Bully chỉ chọn candidate, quyền writer cần fencing và kiểm tra manifest/readback. Recovery so hai participant ở cùng mốc K, dùng RecoveryContext để replay lịch sử mà không cấp quyền phục vụ cho node phục hồi. Liên hệ Cluster.Control, T18, T20–T25, T28 và giới hạn khi thiếu participant hoặc prefix conflict.

## Trace cần hoàn thiện

Ghi một trace bình thường từ Apply primary đến backup ACK và FINAL. Ghi riêng tình huống backup commit nhưng ACK mất, retry cùng event, duplicate không Apply lần hai rồi ACK lại. Kèm tên test, commit SHA, log đã loại bí mật, SQL oracle/HeadHash/digest và giới hạn của môi trường chạy.

## Câu hỏi bảo vệ

- Vì sao primary SQL commit chưa đủ để trả FINAL?
- Receipt phải khớp những trường nào và vì sao TCP ACK không thay thế receipt?
- Vì sao Bully không tự chứng minh node mới được quyền ghi?
- Khi hai participant khác hash tại cùng index thì nhóm dừng ở đâu?
- Điều gì giúp retry sau khi mất response không tạo vé hoặc event thứ hai?

## Tài liệu tham khảo

- Nguyễn Xuân Anh, *Bài giảng Hệ thống phân tán*, PTIT, 12/2014, mục 2.8.5.2 và 2.6.6.
- Bài giảng PTIT ND14 về replication và ND12 về phục hồi.
- [Thiết kế triển khai V12](../De6_Thiet_Ke_Trien_Khai_V12.md), mục 10–14, 20–23.
- [Ma trận kiểm thử](../testing/TEST_MATRIX.md) và [mẫu record](../testing/TEST_RECORD_TEMPLATE.md).
