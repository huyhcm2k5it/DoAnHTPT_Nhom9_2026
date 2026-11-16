# Kế hoạch hoàn tất phần Huy

Trạng thái cập nhật ngày 16 tháng 11 năm 2026. Người dùng đã tiếp tục yêu cầu hoàn thành các task của Huy và review kỹ. Kế hoạch này được giữ làm checklist cùng hồ sơ kiểm chứng cuối tại [HUY_COMPLETION_RECORD.md](HUY_COMPLETION_RECORD.md).

## Mốc hiện tại

- Nhánh làm việc: feature/huy-database. Tuần 1 H1–H4 đã lưu tại commit 3240354; đã review, tái hiện và sửa lỗi precision datetime2(7) cùng cancellation trước khi bắt đầu.
- Tuần 1 có 42 ca SQL component, 14 ca contract và 23 ca smoke đã qua ở mốc kiểm chứng tương ứng.
- H5 có oracle, SHA256 digest và rebuild projection có maintenance; lượt hoàn tất có 46 ca SQL Persistence và 15 ca contract.
- H6 có cấu hình/probe SQL/Host và factory. Hai nhận xét quan trọng đã sửa: probe dùng cùng thứ tự U-lock NodeState trước log; test Host lấy SQL instance từ `DE6_TEST_SQL_SERVER`.
- Database phát triển đã đổi tên thành de_6, giữ 7 bảng/10 ghế; khởi tạo chưa cấp quyền ghi. Database cá nhân không được dùng để reset test.
- Tuần 2 còn thay đổi chưa commit. Chưa push nhánh feature này và chưa merge develop/main.

## Thứ tự thực hiện khi tiếp tục

### 1 Chốt hai lỗi H6 từ review

- [x] `SqlStorageHealth` dùng `WITH(UPDLOCK,HOLDLOCK)` khi đọc `NodeState`, trước các truy vấn log. Ca SQL điều phối khóa giữ U-lock và xác nhận probe chờ U-lock; transaction probe vẫn chỉ SELECT, không seed hoặc cấp quyền.
- [x] SQL fixture và cấu hình Host test đọc `DE6_TEST_SQL_SERVER`; `scripts/test-huy-sql.ps1 -SqlServer` truyền cùng instance cho hai harness.

Điều kiện xong: test tái hiện được hành vi lỗi trước sửa khi khả thi, sau sửa xanh; không tắt TLS hay ghi cấu hình bỏ qua bảo mật vào source để né lỗi môi trường SSPI.

### 2 Chốt hợp đồng maintenance

- [x] `StorageContracts.cs` ghi rõ Control/Recovery phải cấp maintenance lease trên node đóng, freeze tại K hiện hành, loại trừ mutation/grant transition trong thời hạn lease và không bật phục vụ.
- [x] Phân biệt `EnterRecoveryInspection`/`AuditLocal` với `GetFrozenPrefix`/`Rebuild`. Review chéo của chủ module vẫn là điều kiện trước khi merge vào `develop`.

Điều kiện xong: không có gate mở mặc định; không suy Stable/FINAL từ database có dữ liệu hoặc từ việc probe SQL thành công.

### 3 Chạy lại toàn bộ kiểm tra theo thứ tự

- [x] Release build toàn solution, smoke 23/23, contracts 15/15, Host offline 23 ca.
- [x] SQL thật: Persistence 46 ca; Host/persistence harness 34 ca (gồm 11 SQL probe), 0 lỗi. Fixture dùng DB tạm và dọn xong.
- [x] `de_6`: status/audit và Host `--check-sql` đã qua. Node1, dataset đúng, schema 1, K=0, 10 ghế, 0 vé, `MatchesHistory=True`, `ServingEnabled=false`; không có authority/FINAL.
- [x] Host `--check` qua cho sáu cấu hình S1–S3 local/LAN. Đây chỉ là kiểm tra cấu hình, không phải chạy ba máy.
- [x] Restore ban đầu bị hạn chế mạng; dùng Microsoft `dotnet-public` mirror với quyền truy cập mạng, giữ vulnerability audit bật. Build cuối 0 warning/0 error; không thêm cấu hình tắt audit.

Điều kiện xong: đọc đủ exit code và kết quả thực, không còn lỗi build/test. Các ca SQL trước/sau commit, UTC precision, cancellation, duplicate, owner, prefix, corruption, rebuild rollback và log bất biến vẫn qua.

### 4 Hoàn thiện hồ sơ bàn giao

- [x] Ghi môi trường, SHA/commit bổ sung, lệnh/số test, trạng thái `de_6`, quyết định review và giới hạn trong [HUY_COMPLETION_RECORD.md](HUY_COMPLETION_RECORD.md). Môi trường SQL ghi đúng 2025 Express, không tuyên bố chạy SQL 2022.
- [x] Đồng bộ README, runbook tuần 1/2, H6 Host và báo cáo ACID với code; `git diff --check` sạch.
- [x] Xử lý hai review finding H6 và chạy lại đầy đủ harnesses sau khi sửa.

### 5 Lưu Git và mở bàn giao

- [x] Kiểm tra diff/file chưa track và `git diff --check`; `.tools`, `bin/obj`, DB, key và secret không nằm trong thay đổi dự kiến.
- [x] Commit tuần 2 `f0ba4f0` trên `feature/huy-database` và push thành công lên `origin/feature/huy-database`.
- [ ] Mở PR về `develop` và yêu cầu Gia Quân/Đức An/Nguyên Chương review hợp đồng dùng chung. Không có GitHub CLI/connector trong phiên; giao diện máy tính không khởi tạo được. Link tạo PR đã sẵn: https://github.com/huyhcm2k5it/DoAnHTPT_Nhom9_2026/pull/new/feature/huy-database. Không tự merge; chờ review/CI nhóm.

## Phần cần phối hợp sau khi Huy bàn giao

| Bên phụ trách | Phần cần ghép với Huy |
| --- | --- |
| Gia Quân | Decision engine và coordinator thật; grant, dedup và công bố Stable/FINAL |
| Đức An | Gate/maintenance/recovery context thật, fencing/manifest và activation |
| Nguyên Chương | TCP/TLS, append/replay nguyên bytes và ACK sau durable commit |
| Văn Khoa | SDK/pending/reconnect và UI dùng kết quả có thẩm quyền |
| Cả nhóm | G0–G5, ba server/SQL độc lập, test T01–T36 với record/oracle thật |

Huy có thể hoàn tất component và hồ sơ phần mình; không lấy gate/engine fixture trong test thay implementation production của các bạn khác. Kiểm thử component không chứng nhận toàn hệ thống hoặc CLO của lớp.

## Kết quả bàn giao

Phần implementation Huy H1–H6 đã hoàn tất và kiểm chứng ở mức component. Các mục còn để trống chỉ là lưu/publish Git và review chéo tích hợp của nhóm; chúng không đổi kết luận test local. Không tuyên bố nghiệm thu toàn hệ thống hoặc CLO.
