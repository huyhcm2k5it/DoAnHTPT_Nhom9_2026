# Quy trình Git — Nhóm 9

Repo: [DoAnHTPT_Nhom9_2026](https://github.com/huyhcm2k5it/DoAnHTPT_Nhom9_2026). Phân công và reviewer theo [bảng nhiệm vụ](Phan_Cong_Nhom9.md). Nhóm dùng tên thành viên để phân công, không suy đoán tài khoản GitHub của từng người; trưởng nhóm mời đúng tài khoản do thành viên cung cấp.

## 1. Vai trò nhánh

| Nhánh | Mục đích | Đích PR |
| --- | --- | --- |
| `main` | Chỉ bản phát hành đã nghiệm thu | Nhận PR từ `develop` |
| `develop` | Tích hợp theo cổng G0–G5 | Nhận PR feature |
| `feature/huy-database` | Huy: SQL/Persistence và tích hợp | `develop` |
| `feature/gia-quan-domain` | Gia Quân: Domain/Coordinator/Contracts | `develop` |
| `feature/duc-an-election` | Đức An: Bully/Cluster.Control | `develop` |
| `feature/nguyen-chuong-replication` | Nguyên Chương: Replication/TCP | `develop` |
| `feature/van-khoa-client` | Văn Khoa: SDK/WinForms | `develop` |

Không push thẳng `main`; không merge feature trực tiếp vào `main`. Thay đổi Contracts, schema, authority hoặc framing phải được chủ hai bên sử dụng review trước merge.

## 2. Trưởng nhóm công bố develop lần đầu

Remote có thể chưa có `develop`. Trước khi thành viên checkout, trưởng nhóm kiểm tra working tree, xác nhận đúng bản bộ khung đã review và commit các file cần chia sẻ. Không đưa tài liệu cá nhân, secret, `.tools/`, `bin/`, `obj/` hoặc pending runtime vào commit.

```powershell
git status
git remote -v
git branch --show-current
git ls-remote --heads origin develop
# Nếu develop đã có ở local và đang là nhánh tích hợp được duyệt:
git switch develop
git push -u origin develop
```

Nếu chưa có nhánh local, trưởng nhóm tạo `develop` từ commit nền đã thống nhất (`git switch -c develop`), đưa bộ khung đã duyệt vào commit rồi mới push. Không chạy `switch -c` nếu nhánh đã tồn tại. Lệnh push ở đây là hướng dẫn cho người được phân quyền; tài liệu không xác nhận remote đã được công bố. Trưởng nhóm thông báo commit nền và mời thành viên sau khi push thành công.

## 3. Thành viên clone và tạo nhánh

```powershell
git clone https://github.com/huyhcm2k5it/DoAnHTPT_Nhom9_2026.git
cd DoAnHTPT_Nhom9_2026
git fetch origin
git switch --track origin/develop
# Ví dụ Huy; các thành viên khác dùng tên nhánh trong bảng.
git switch -c feature/huy-database
.\scripts\build.ps1
.\scripts\test.ps1
```

Nếu local đã có `develop`, dùng `git switch develop` rồi `git pull --ff-only origin develop`. Nếu báo không có `origin/develop`, chờ trưởng nhóm hoàn tất bước 2; không tự chọn `main` làm nhánh tích hợp thay thế.

## 4. Đồng bộ, commit và PR

Trước khi đồng bộ, commit phần việc phù hợp hoặc cất thay đổi cá nhân có chủ đích; không dùng reset/clean để xóa thay đổi chưa kiểm tra.

```powershell
git fetch origin
git switch feature/huy-database
git merge origin/develop
# Xử lý conflict cùng chủ module, rồi chạy lại kiểm tra.
.\scripts\build.ps1
.\scripts\test.ps1
git diff --check
git status
# Thêm đúng các file đã review; ví dụ:
git add src/De6.Booking.Persistence database
git commit -m "feat(persistence): implement atomic apply"
git push -u origin feature/huy-database
```

Mở PR trên GitHub với **base `develop`**, compare là nhánh feature. Thay tên nhánh, đường dẫn và commit message cho đúng công việc thực; không commit tuyên bố implementation nếu chỉ mới có scaffold. Không force-push nhánh người khác hoặc nhánh chia sẻ.

PR phải ghi: vấn đề/hành vi mới; file/module và hợp đồng đổi; dependency/cổng G; lệnh kiểm tra và kết quả thực; mã T liên quan kèm record; giới hạn/chưa triển khai; reviewer. Build/smoke không thay T01–T36. Reviewer chéo: Huy ↔ Gia Quân cho Apply/domain; Đức An ↔ Nguyên Chương cho authority/replication; Văn Khoa nhờ Gia Quân và Nguyên Chương kiểm tra identity/reconnect.

## 5. Tích hợp và phát hành

1. Chủ module xử lý nhận xét, cập nhật fixture chung và bằng chứng test chịu ảnh hưởng.
2. Người tích hợp kiểm tra CI Windows, build/smoke, startup check liên quan và test nghiệp vụ của cổng; chỉ merge PR sau review.
3. Huy phối hợp chạy lại tích hợp sau merge, ghi commit SHA kết quả; nếu lỗi gate/identity/FINAL thì chặn cổng tiếp theo.
4. Khi G5 đạt, mở PR `develop` → `main` kèm ma trận T01–T36, record, cấu hình demo và giới hạn. Nhóm review bản phát hành trước merge; không tự động deploy.
5. Sau merge, các thành viên fetch và đồng bộ `develop`; nhánh feature đã xong có thể xóa sau khi xác nhận commit đã được tích hợp.

Tài liệu chuẩn: [V12](../De6_Thiet_Ke_Trien_Khai_V12.md), [ma trận kiểm thử](../testing/TEST_MATRIX.md), [mẫu record](../testing/TEST_RECORD_TEMPLATE.md). Mọi đề xuất đổi V12 phải sửa thiết kế, Contracts và test cùng một luồng review.
