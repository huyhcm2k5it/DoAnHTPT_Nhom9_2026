# De6.Server.Host

Owner: Huy (tích hợp cả nhóm).

Runnable closed bootstrap: validates configuration and always starts RECOVERING/ServingEnabled=false. `--check` validates files without SQL access. `--check-sql` probes an existing configured database read-only and exits. Normal startup probes configured SQL before waiting for Ctrl+C. No TCP listener, coordinator, election, replication, activation or booking processing is wired.

SQL settings, local/LAN database names, exit codes, and the composition boundary are documented in [HUY_HOST_SQL](../../docs/implementation/HUY_HOST_SQL.md). The factory requires the real gate and decision engine supplied by their module owners; the Host never constructs substitutes.

Specification: [V12](../../docs/De6_Thiet_Ke_Trien_Khai_V12.md). Team handoff and commands: [repository README](../../README.md).

Definition of done for future implementation: reviewed shared contracts, tests for owned V12 cases, error/uncertainty paths, and reproducible evidence. A successful template build does not satisfy business acceptance.

## Hướng dẫn bắt đầu

Huy điều phối, chủ từng module cùng tích hợp thực hiện theo các bước **H6 và G0–G5** trong [phân công chi tiết](../../docs/team/Phan_Cong_Nhom9.md#7-cổng-tích-hợp-g0g5). Mỗi bước nêu file/API cần cài, cách tự kiểm và phần giao cho bạn khác. Các tên file bổ sung trong hướng dẫn là file cần tạo, không phải implementation đã có.

Đọc phần cổng ghép và luồng HOLD trong tài liệu đó để phối hợp fixture và hiểu các trạng thái LocalApplied/Stable/FINAL. Cả bốn người đều code, test và hoàn thiện phần lý thuyết riêng.
