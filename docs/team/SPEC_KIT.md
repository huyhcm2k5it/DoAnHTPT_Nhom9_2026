# Dùng Spec Kit trong đồ án HTPT Nhóm 9

## Mục đích và giới hạn

Repo dùng Spec Kit 1.4.2 với tích hợp Codex và script PowerShell. Công cụ giúp chuyển một yêu cầu đã thống nhất thành đặc tả, kế hoạch và danh sách task có thể review trước khi code. Công cụ không chứng minh thuật toán đúng, không tự xác nhận code chạy, không thay thế đề 26-27, tài liệu thiết kế V12, bài giảng HTPT hay yêu cầu của giảng viên.

Thứ tự căn cứ khi viết đặc tả:

1. Đề 6/bản đề 26-27 và yêu cầu chính thức của lớp.
2. Thiết kế triển khai [V12](../De6_Thiet_Ke_Trien_Khai_V12.md), nhất là giả thiết, bất biến, ngữ nghĩa FINAL, phục hồi và ma trận nghiệm thu.
3. Cấu trúc code, hợp đồng module, README, cấu hình, script và kiểm thử đang có trong repo.
4. Bài giảng HTPT PTIT của thầy Lê Ngọc Bảo và sách đã được nhóm đối chiếu. CLO/mã rubric chưa có nguồn chính thức thì ghi “cần xác nhận”, không tự đặt.

V11 và các tài liệu cũ chỉ là lịch sử khi có điểm mâu thuẫn; không dùng để khôi phục giao thức PREPARED/COMMITTED đã được V12 thay thế. Không mở rộng sang Raft/Paxos, Kafka, Redis, nhiều chuyến, thanh toán thật hoặc yêu cầu ngoài đề nếu nhóm chưa thống nhất và chưa sửa căn cứ thiết kế.

## Cài CLI trên từng máy Windows

Mỗi người chạy Spec Kit cần CLI riêng trên máy. Repo đã chứa phần cấu hình và skill dùng chung, nhưng không chứa Python/CLI. Khuyến nghị dùng `uv` để cô lập CLI khỏi các gói Python khác; có thể cài CLI đúng phiên bản của repo:

```powershell
uv tool install specify-cli==1.4.2
specify version
```

Yêu cầu Python 3.11 trở lên và `uv`. Nếu máy đã có `pipx`, có thể dùng `pipx install specify-cli==1.4.2`. Phiên bản CLI phải là `1.4.2`; không nâng cấp riêng lẻ rồi tạo skill khác phiên bản trong cùng PR.

Máy Huy hiện cài `specify-cli` 1.4.2 bằng `pip --user`. Nếu PowerShell báo không tìm thấy `specify`, thêm thư mục Scripts của Python user vào PATH của terminal hiện tại:

```powershell
$pyVersion = python -c "import sys; print(f'Python{sys.version_info.major}{sys.version_info.minor}')"
$pythonUserScripts = Join-Path $env:APPDATA "Python\$pyVersion\Scripts"
$env:Path = "$pythonUserScripts;$env:Path"
specify version
```

Kết quả phải báo `CLI Version 1.4.2`. Lệnh `specify check` kiểm tra các công cụ coding-agent được phát hiện. Không cần cài Spec Kit hoặc SQL Server trên máy chỉ dùng để chạy ứng dụng; SQL Server chỉ cần cho những kịch bản persistence cần database thật theo README.

## Skill và tài liệu được chia sẻ

- `.specify/memory/constitution.md`: nguyên tắc dự án; bản hiện tại là dự thảo chờ nhóm review.
- `.specify/templates/`, `.specify/scripts/powershell/`, `.specify/workflows/`: template và quy trình do Spec Kit cài.
- `.agents/skills/speckit-*/SKILL.md`: skill Codex dùng chung, gọi bằng `$speckit-...` trong chat Codex.
- `specs/<số>-<tên-tính-năng>/`: đặc tả và kế hoạch cho từng thay đổi, được tạo khi bắt đầu một yêu cầu mới.

Không đưa token, mật khẩu, connection string bí mật hoặc dữ liệu cá nhân vào `.agents/`, `specs/`, prompt hay commit. Trước khi commit kiểm tra `git status` và diff; chỉ đưa skill Spec Kit cùng tài liệu đặc tả đã review vào PR.

## Quy trình cho mỗi thay đổi

Chỉ tạo đặc tả cho một thay đổi được nhóm giao, có phạm vi đủ nhỏ để review độc lập. Không yêu cầu Spec Kit mô tả lại toàn bộ hệ thống hiện hữu. Trong chat Codex tại repo, thực hiện từng bước và review kết quả trước khi sang bước kế tiếp:

1. `$speckit-specify <yêu cầu, điều kiện chấp nhận, module và giới hạn tương thích>`
2. `$speckit-clarify` nếu còn giả thiết hoặc hành vi chưa rõ.
3. `$speckit-plan` để lập kế hoạch theo kiến trúc và công nghệ đang dùng.
4. `$speckit-tasks` để chia task có đầu ra, file/module và cách kiểm chứng.
5. `$speckit-analyze` để rà mâu thuẫn giữa spec, plan, task và constitution.
6. Thành viên phụ trách code theo task; `$speckit-implement` chỉ được thực hiện sau khi người phụ trách đã review và chấp nhận kế hoạch/task.
7. Chạy đúng kiểm tra repo phù hợp với thay đổi, xem log thật, review diff và ghi rõ kiểm tra nào chưa chạy. Cuối cùng dùng `$speckit-converge` để tìm phần còn thiếu; xử lý task còn lại trước khi báo hoàn tất.

Không chấp nhận mô tả kiểu “hệ thống phải an toàn/nhanh/đúng” nếu không có điều kiện quan sát được. Với thay đổi liên quan replication, recovery, quyền phục vụ hoặc persistence, đặc tả phải nêu mốc lưu bền vững, trạng thái node, tình huống lỗi, kết quả client và invariant V12 bị ảnh hưởng. Không suy ra FINAL từ một SQL commit cục bộ hoặc từ log CI smoke.

## Cổng review trước khi code/merge

- Spec không mâu thuẫn đề 26-27, V12, giao diện module hoặc các giả thiết HTPT đã chốt.
- Các thay đổi ngoài phạm vi môn học được loại bỏ hoặc đánh dấu cần nhóm/giảng viên xác nhận.
- Plan không thêm thư viện, giao thức, schema hoặc trạng thái mới nếu chưa có lý do và chưa cập nhật hợp đồng liên quan.
- Task chỉ rõ người thực hiện, module/file dự kiến, phụ thuộc, cách kiểm chứng và đầu ra bàn giao; tên người phải theo phân công hiện hành trong README.
- Reviewer đọc cả đặc tả, kế hoạch, task và code. Merge theo quy trình Git hiện tại của nhóm.
- Kiểm tra repo dùng khi phù hợp: `scripts/build.ps1`, `scripts/test.ps1`; thay đổi SQL cần bổ sung kiểm chứng SQL thật theo `scripts/test-huy-sql.ps1` khi môi trường database khả dụng. Không báo pass nếu chưa chạy.

## Nâng cấp hoặc đổi tích hợp

Không để các thành viên tự nâng cấp CLI hoặc chạy lại `specify init` trên nhánh tính năng. Việc nâng phiên bản/đổi tích hợp phải là PR riêng, có baseline sạch, ghi rõ phiên bản, xem toàn bộ diff sinh ra và xác nhận tương thích với Codex cùng script PowerShell. Không cài extension/preset cộng đồng vào repo khi chưa review nội dung và phạm vi thay đổi.
