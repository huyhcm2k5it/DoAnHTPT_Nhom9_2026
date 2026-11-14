# Triển khai Huy tuần 2

Tiếp tục H5–H6 sau H1–H4, theo V12 mục 9, 14, 16–21. Không triển khai thay coordinator/Control/network của thành viên khác. Database phát triển tên de_6 theo yêu cầu.

## H5 Oracle và phục hồi projection

- Thêm oracle SQL chỉ đọc cho I01–I06 và continuity cục bộ. Không suy I07/I10 hoặc FINAL từ một DB.
- Load toàn bộ log từ seed, xác minh canonical payload/hash/prefix/index, RequestId/EventId và decision bằng projection tạm. Tạo digest canonical SHA256 của Seats/Reservations/Tickets/RequestResults sắp theo khóa, giữ đủ độ chính xác UTC/null/GUID. NodeId/credentials/authority không thuộc digest dữ liệu.
- AuditLocal chỉ là quản trị cục bộ, không cấp bằng chứng Stable. FrozenPrefix và Rebuild yêu cầu lease maintenance từ Control, mốc K đúng LastAppliedIndex, không có API mở phục vụ.
- Rebuild chỉ projection/results từ log nguyên vẹn tại K hiện hành, không xóa/sửa OperationLog, không copy NodeId hay thay AcceptedEpoch/Leader. Toàn bộ rebuild một transaction; log hỏng hoặc mốc sai thì từ chối; lỗi trước commit rollback.
- TDD: audit/digest equality trên DB primary/backup; corruption được phát hiện; rebuild sửa projection/results; log conflict từ chối và giữ dữ liệu; metadata/bytes bất biến; thiếu maintenance lease không sửa.

## H6 Cấu hình và ghép Host

- Cấu hình SQL local/LAN rõ theo từng NodeId, Windows auth, không secrets trong Git. Local một SQL dùng ba DB khác nhau; LAN mỗi máy có thể cùng tên de_6.
- Host dùng probe SQL không tự seed/activate; --check vẫn chỉ validate file, --check-sql kiểm tra DB/dataset/node/schema/durable mode rồi thoát với ServingEnabled=false. Composition factory yêu cầu gate/decision thật được bên sở hữu cung cấp, không default-open stub.
- Tool init/status/audit và script tái lập; CI chạy smoke+contract và SQL integration khi provision được instance. Record component/cluster phân biệt rõ; README và phần báo cáo ACID liên kết code/test.
- Review độc lập toàn phần tuần 2, sửa lỗi quan trọng bằng RED→GREEN rồi chạy lại full suite. Giữ nhánh feature/huy-database, không tự merge develop/main.
