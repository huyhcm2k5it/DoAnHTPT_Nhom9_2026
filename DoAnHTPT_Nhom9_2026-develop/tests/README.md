# Verification boundaries

Run `./scripts/test.ps1` from any directory. It builds the Windows solution and executes `De6.Template.SmokeTests`, a dependency-free console assertion harness. Any failed assertion exits nonzero. It also executes the Persistence canonical-contract harness. These console harnesses are not discovered by dotnet test. SQL component tests run separately via scripts/test-huy-sql.ps1; no cluster/network fault acceptance is implied.

Current checks cover startup configuration validation, the immutable closed bootstrap, and server/client rejection of invalid configuration files with clean exit code 2. Startup `-Check` validates files only. Future test ownership follows module ownership in [the repository README](../README.md):

- `unit/`: canonical bytes/hash fixtures, domain decisions, gate transitions, framing, receipt matching and pending identity.
- `integration/`: independent SQL databases, durable atomic Apply, stable reads, TLS identity, real SDK/UI boundaries.
- `fault-injection/`: V12 [T01-T36](../docs/De6_Thiet_Ke_Trien_Khai_V12.md), deterministic crash/partition/duplicate/ACK and response loss scenarios.
- `evidence/`: use [the evidence template](evidence/TEMPLATE.md) for each case; redact secrets. Bulky/raw runtime outputs should be archived outside source and linked with checksums.

These directories describe future work. No planned V12 business/fault case has passed merely because the smoke harness is green.

Hướng dẫn thao tác và bàn giao theo người: [Phân công chi tiết](../docs/team/Phan_Cong_Nhom9.md). Mục 9–12 nêu thứ tự ghép, test project cần tạo, record và checklist PR. Bộ test nghiệp vụ tương lai phải được nối vào script/CI bằng lệnh thực; không suy PASS từ bootstrap.

Huy H1–H4: [runbook](../docs/implementation/HUY_WEEK1_RUNBOOK.md). The SQL harness uses real temporary databases and test-only gate/decision fixtures, then removes only databases it created.
