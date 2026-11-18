# Constitution đồ án HTPT — Nhóm 9

> **Trạng thái: dự thảo để nhóm review; chưa tự nhận là CLO/rubric chính thức.** Nội dung này ghi lại các giới hạn đã có trong README và thiết kế V12 để Spec Kit không tự sinh kiến trúc khác.

## Nguyên tắc cốt lõi

### I. Theo hợp đồng V12 và đề bài

Đề 6/bản 26-27 và `docs/De6_Thiet_Ke_Trien_Khai_V12.md` là căn cứ nghiệp vụ, thuật ngữ, bất biến, trạng thái và nghiệm thu. V11 là tư liệu lịch sử nếu mâu thuẫn với V12. Không tự đổi hành vi, giao thức, schema hoặc tiêu chí bảo vệ khi chưa nêu tác động và được nhóm review.

### II. Giữ đúng kiến trúc và phạm vi môn HTPT

Thay đổi phải đặt đúng module và hợp đồng hiện hành trong solution. Mô hình triển khai dùng C#/.NET 10, TCP/TLS, WinForms và SQL Server theo V12. Mục tiêu học thuật là minh họa các cơ chế đã chọn trong V12 như điều phối, Bully, primary-backup, giao tác/tương tranh và phục hồi; không trình bày nền tảng .NET, TCP hay ACID như thuật toán phân tán. Không tự thêm Raft/Paxos, message broker, Redis, nhiều chuyến hoặc thanh toán thật. Mã CLO chưa có tài liệu chính thức của lớp thì phải ghi cần xác nhận, không suy đoán.

### III. Không nới lỏng an toàn và ngữ nghĩa dữ liệu

Mọi thay đổi persistence/replication/recovery phải bảo toàn invariant V12. `LocalApplied`, `StableIndex` và `FINAL` là ba ý nghĩa khác nhau; một commit cục bộ không đủ để trả `FINAL`. Chỉ trả `FINAL` khi event và kết quả đã bền vững ở primary cùng ít nhất một backup. Timeout chỉ tạo nghi ngờ; không tự promote primary khi chưa cách ly writer cũ và đối soát lịch sử. Nếu không chứng minh được điều kiện phục vụ, dừng an toàn và báo trạng thái chưa sẵn sàng.

### IV. Thiết kế thay đổi có thể kiểm chứng

Mỗi thay đổi nêu kết quả mong đợi, trường hợp lỗi, bất biến bị ảnh hưởng và cách kiểm chứng. Tận dụng cấu trúc, thư viện, script và harness hiện có trước khi thêm dependency. Kiểm tra build/smoke hiện có không được mô tả thành nghiệm thu toàn bộ ma trận T01–T36; chỉ ghi các kiểm thử đã thực sự chạy cùng kết quả và giới hạn của chúng.

### V. Đặc tả trước, review cùng code

Spec, plan và task chỉ áp dụng cho thay đổi cụ thể được giao, không phải bản mô tả hồi tố của toàn hệ thống. Task phải chia được cho thành viên theo phân công hiện hành, nêu phụ thuộc và đầu ra bàn giao. Reviewer kiểm tra tính phù hợp V12 và phạm vi môn cùng code/test trước khi nhập PR theo quy trình Git của nhóm.

## Giới hạn và căn cứ

- Căn cứ thiết kế: `docs/De6_Thiet_Ke_Trien_Khai_V12.md`.
- Hướng dẫn chạy và cấu trúc hiện tại: `README.md`.
- Phân công và review: `docs/team/Phan_Cong_Nhom9.md`, `docs/team/Git_Workflow.md`.
- Tài liệu lý thuyết và ánh xạ nội dung: `docs/report/` và phần đối chiếu trong V12. Đây không thay CLO/rubric chính thức của lớp.
- Môi trường runtime/test phải theo README, `global.json` và script trong `scripts/`; không giả định SQL Server có sẵn trên mọi máy hoặc test DB đã chạy.

## Quy trình và quản trị

Trước khi code, người phụ trách review spec, plan, tasks và kết quả `$speckit-analyze`; thay đổi liên quan dữ liệu phân tán phải đối chiếu các invariant V12. PR phải chỉ rõ kiểm tra nào đã chạy và kết quả; reviewer không suy ra trạng thái test từ CI bootstrap. Sửa constitution cần PR riêng hoặc phần thay đổi được nêu rõ trong PR, có căn cứ và nhóm review trước khi dùng nó làm gate.

**Version:** 0.1.0 (draft) | **Ratified:** pending team review | **Last amended:** 2026-11-18
