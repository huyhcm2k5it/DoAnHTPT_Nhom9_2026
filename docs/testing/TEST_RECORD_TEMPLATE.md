# Mẫu record kiểm thử V12

Sao chép mẫu này cho mỗi lần chạy thực và liên kết từ [TEST_MATRIX](TEST_MATRIX.md). Không dùng kết quả mô hình hữu hạn hoặc smoke của bộ khung để ghi PASS cho test C#/SQL đa máy. Nguồn oracle là [V12 mục 20](../De6_Thiet_Ke_Trien_Khai_V12.md).

## 1. Định danh lần chạy

| Trường | Giá trị cần điền |
| --- | --- |
| Record ID / test ID | … / Txx |
| Người chạy / reviewer | … / … |
| Commit SHA / nhánh | … / … |
| Thời điểm bắt đầu/kết thúc và múi giờ | … |
| Kết quả | CHƯA CHẠY / PASS / FAIL / BLOCKED |
| DatasetId / SchemaVersion / SDK | … / … / … |
| Epoch / writer / Recovery RunId | … / … / … hoặc không áp dụng |
| Cổng tích hợp / invariant | G… / I… |

## 2. Môi trường và tiền điều kiện

| Node/quầy | Máy/IP; client/peer port | SQL DB / data directory | Role; AcceptedEpoch/Leader; LastAppliedIndex/HeadHash |
| --- | --- | --- | --- |
| S1 | … | … | … |
| S2 | … | … | … |
| S3 | … | … | … |
| C1/A | … | pending store … | session … |
| C2/B | … | pending store … | session … |
| C3/C | … | pending store … | session … |

Ghi phiên bản SQL Server 2022, fully durable setting, OS, cấu hình TLS/peer identity đã loại secrets, timeout/deadline/queue limit, cấu hình fault hook, hash file cấu hình và lệnh khởi chạy. Nêu trạng thái đồng bộ trước lỗi: ba bản cùng tiền tố tại K, node nào đang không sẵn sàng. Nếu tiền điều kiện không đạt, ghi BLOCKED; không chạy rồi diễn giải thành PASS.

## 3. Các bước tái lập

1. Chuẩn bị dataset/cấu hình theo cách có kiểm soát; ghi baseline oracle, K và SeedHash/HeadHash. Không reset dữ liệu để che lỗi.
2. Gửi command cụ thể: owner, action, trip, seat, reservation; ghi RequestId, CommandHash và canonical payload bytes/fixture.
3. Gây lỗi đúng mốc của Txx: trước/sau local commit, backup Apply, ACK hoặc response; ghi fault hook/command, node và chứng cứ thời điểm.
4. Thu trace phản hồi và retry cùng RequestId; nếu recovery, ghi fence proof, hai participant, manifest/RunId, freeze và activation/readback.
5. Chạy SQL oracle trên từng node ở mốc xác định; đối chiếu event/result, ghế/reservation/ticket và log/prefix tại K.
6. Khôi phục/rejoin theo runbook, xác minh giới hạn một lỗi và lưu đầy đủ artifact để reviewer tái lập.

## 4. Trace và oracle

| Mốc/thứ tự | Node | RequestId / EventId / LogIndex / EventHash | LocalApplied | StableIndex | Response FINAL/UNKNOWN/khác | Artifact |
| --- | --- | --- | --- | --- | --- | --- |
| … | … | … | … | … | … | … |

| Kiểm tra bắt buộc của Txx | Giá trị mong đợi theo V12 | Giá trị quan sát/SQL query | Kết luận + bằng chứng |
| --- | --- | --- | --- |
| … | … | … | … |

Đính kèm log **cả ba node**, log client, nguyên payload/hash cần đối chiếu, output SQL oracle và truy vấn đã chạy; với node chết, lưu phần log còn lại và ghi rõ thời gian không thu được. Thiếu chứng cứ bắt buộc phải ghi FAIL/BLOCKED hoặc chưa đủ kết luận. Không lấy màu ghế làm bằng chứng duy nhất. Không suy FINAL từ local SQL commit hay TCP ACK không đúng receipt.

## 5. Số liệu và kết luận

Nếu đo latency, dùng một đồng hồ đơn điệu ở client; ghi số mẫu, tải, median/p95/max, đơn vị và cách tính. Failover tách: phát hiện → chờ vận hành/fence → đối soát → activation → reconnect. Không trừ timestamp hai máy chưa đồng bộ để tính thời lượng.

- Kết quả thực: …
- Invariant/oracle đạt hoặc vi phạm: …
- Sai lệch, issue/PR liên quan và giới hạn: …
- Đường dẫn artifact tương đối trong repo hoặc nơi lưu được nhóm truy cập, đã loại mật khẩu/token: …
- Reviewer, kết luận review và lần chạy lại nếu cần: …

Chỉ chuyển test sang PASS trong ma trận khi reviewer kiểm tra record đầy đủ; giữ record FAIL cũ để không mất dấu lỗi.
