# Kết quả kiểm tra thiết kế V12

Ngày kiểm tra 14/11/2026. Đối tượng là đặc tả thiết kế, chưa phải chương trình C#/SQL đã triển khai.

## Review độc lập

Một reviewer riêng đọc bản thiết kế theo góc nhìn người phải lập kế hoạch triển khai, tìm chỗ thiếu quyết định hoặc có thể gây mất tính an toàn. Reviewer phát hiện một điểm P1: node rejoin có AcceptedEpoch/Leader cũ sẽ không nhận được replay nếu mọi bản tin đều dùng điều kiện quyền ghi mới.

Đã sửa ở mục 14-16 của V12: admin cài RecoveryContext có giới hạn RunId, nguồn/đích, dataset, mốc và hash; dùng REPLAY_ENTRY/ApplyReplayEntryAsync riêng; giữ metadata lịch sử; không nới quyền APPEND_APPLY. Reviewer đã đọc lại phần sửa và kết luận không còn câu hỏi quan trọng cản việc lập kế hoạch trong mô hình được mô tả. Đây là review kỹ thuật, không phải chứng nhận của PTIT hay bảo đảm không còn mọi lỗi.

## Kiểm tra mô hình hữu hạn

Chạy [mô hình Python](<C:/Users/ADMIN/Downloads/DoAnHTPT_Nhom9_2026/docs/validation/v12_model_check.py>) bằng Python tiêu chuẩn. [Kết quả máy đọc được](<C:/Users/ADMIN/Downloads/DoAnHTPT_Nhom9_2026/docs/validation/v12_model_results.json>) chứa SHA-256 của bản Markdown được kiểm tra.

| Nội dung | Kết quả |
| --- | --- |
| Miền mô hình | Hai ghế, hai chủ hoạt động, bảy lịch sử đại diện; một mutation đang xử lý |
| Điểm cắt | Trước/sau local Apply, tập backup đã Apply, ACK và phản hồi |
| Sự cố | Thử mọi node bị lỗi một lần ở mỗi trạng thái sinh ra |
| Lịch lỗi kiểm tra | 2.886, không tìm thấy vi phạm các assertion được mô hình hóa |
| Retry trùng/xung đột | 26 trường hợp |
| Kiểm tra gate công bố | 962 trạng thái |
| Nhánh cũ cần loại khi rejoin | 74 trường hợp |
| Biến thể sai đối chứng | Bốn biến thể đều tạo phản ví dụ dự kiến |

Các đối chứng sai gồm trả FINAL trước replication, chỉ lấy DB của candidate khi phục hồi, cho đọc trước Stable và ACK trước khi dữ liệu được ghi bền vững. Mục đích là kiểm tra mô hình có phát hiện các lỗi nguyên lý này.

Mô hình không chạy SQL thật, không kiểm tra TCP/TLS/JSON, race giữa thread, thời gian hội tụ Bully, khả năng fence vật lý hay thao tác lưu manifest của admin. Mô hình cũng không bao phủ mọi lịch sử vô hạn hoặc thay cho chứng minh tổng quát. Ba mươi sáu ca T01-T36 của V12 là kế hoạch nghiệm thu sau khi có chương trình, chưa phải 36 PASS.

## Quyết định cần giữ khi code

Chỉ FINAL sau hai bản bền vững; không lộ LocalApplied chưa Stable; một mutation chưa Stable; retry cùng identity; hai survivor tương thích được đối soát trước phục vụ; old writer được fence; quyền replay tách quyền ghi mới; boot luôn đóng gate. Một đợt chịu một lỗi bắt đầu từ ba node đã đồng bộ; node chưa rejoin xong vẫn tính là không sẵn sàng.

Giữ primary-backup, Bully và điều phối tập trung theo các slide đã đối chiếu. Chưa có CLO/rubric đúng lớp nên chưa thể kết luận đạt CLO chính thức. Việc nghiệm thu cuối cùng cần code, SQL và bằng chứng chạy thật.
