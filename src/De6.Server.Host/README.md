# De6.Server.Host

## TLS runtime

Set `DE6_HOST_TLS_CONFIG` to a local JSON file with `certificatePath` (a PFX with
private key), `peerPins` (remote NodeId to SHA-256 certificate fingerprint), and
`accounts` (entries with `username`, `ownerId`, `salt`, and `passwordHash`).
`ClientSessionManager.HashAccount` produces the salted PBKDF2 account record.
Provide the PFX password via `DE6_TLS_CERTIFICATE_PASSWORD`. Provision one account
for each counter A/B/C, and use certificates whose names match the configured
endpoint addresses. The peer port requires a certificate mapped to a remote node.

With this config, normal startup probes SQL and starts separate client and peer
TLS listeners. `--check` and `--check-sql` remain read-only checks.

### Initial local cluster

Use three seeded databases from `config/local/s1.json` through `s3.json`, all at
index zero, with distinct PFX files and one shared set of account records. From
the repo root, prepare a manifest while the three Hosts are stopped:

```powershell
$run = [guid]::NewGuid().ToString('D')
dotnet run --project src/De6.Server.Host -- --config config/local/s1.json --prepare-local-bootstrap $run
```

Record the RunId. In each of three terminals, set `DE6_HOST_TLS_CONFIG` to that
node's TLS JSON and provision its PFX password through the environment. Start S1,
S2, then S3, waiting for each TLS listener to open:

```powershell
dotnet run --project src/De6.Server.Host --no-build -- --config config/local/s1.json --activate-local-bootstrap <run-id>
```

Use the matching `s2.json` and `s3.json` for the other two commands. Every activation
reads back the completed seed manifest from all three databases. S3 becomes the
primary and S1/S2 are backups. Use the same RunId when resuming interrupted manifest
preparation. Nonzero log indexes require a recovery workflow. The CLI bootstrap is
limited to local loopback addresses. Hosts exchange authenticated heartbeats and
run Bully when a backup suspects the current primary. Candidate selection does
not grant serving rights. Operator fencing, promotion and rejoin after an
existing history still require runtime recovery orchestration.

On the client, set `DE6_CLIENT_TLS_PINS` to the path of a JSON map from all three
NodeIds to their SHA-256 certificate fingerprints. Run `scripts/start-client.ps1`
with ClientId 1, 2 or 3 and sign in as A, B or C respectively. Each client stores
pending commands under its configured data directory.

Owner: Huy (tích hợp cả nhóm).

After a successful probe, Host composes a shared Control, SQL store and authenticated peer receiver. `HostPersistence.CreateCoordinator` wires the SQL-backed coordinator and replica sender. The Domain decision runs once inside SQL Apply.

SQL settings, local/LAN database names, exit codes, and the composition boundary are documented in [HUY_HOST_SQL](../../docs/implementation/HUY_HOST_SQL.md). The factory requires the real gate and decision engine supplied by their module owners; the Host never constructs substitutes.

Thành phần đã ghép:
- `ClientSessionManager`: Quản lý tài khoản quầy (A, B, C), xác minh mật khẩu bằng PBKDF2 và cấp phiên 8 giờ.
- `ClientRequestDispatcher`: Lắng nghe và điều phối cổng quầy qua `IFrameChannel`. Hỗ trợ LOGIN, GET_CLUSTER_STATUS, GET_SEATS, GET_MY_RESERVATIONS, GET_RESULT, HOLD, CONFIRM, CANCEL. Từ chối RPC peer/admin trên cổng client.
- `PeerRequestDispatcher`: Lắng nghe và điều phối cổng peer replication. Tiếp nhận `APPEND_APPLY`, gọi `ReplicaReceiver.AppendAsync`, trả `AppliedReceipt` bền vững. Từ chối RPC client trên cổng peer.
- `SqlCoordinatorStore`: Cầu nối bền vững giữa SQL store và `CommandCoordinator`.

Specification: [V12](../../docs/De6_Thiet_Ke_Trien_Khai_V12.md). Team handoff and commands: [repository README](../../README.md).

Definition of done for future implementation: reviewed shared contracts, tests for owned V12 cases, error/uncertainty paths, and reproducible evidence. A successful template build does not satisfy business acceptance.

## Hướng dẫn bắt đầu

Huy điều phối, chủ từng module cùng tích hợp thực hiện theo các bước **H6 và G0–G5** trong [phân công chi tiết](../../docs/team/Phan_Cong_Nhom9.md#7-cổng-tích-hợp-g0g5). Mỗi bước nêu file/API cần cài, cách tự kiểm và phần giao cho bạn khác. Các tên file bổ sung trong hướng dẫn là file cần tạo, không phải implementation đã có.

Đọc phần cổng ghép và luồng HOLD trong tài liệu đó để phối hợp fixture và hiểu các trạng thái LocalApplied/Stable/FINAL. Cả bốn người đều code, test và hoàn thiện phần lý thuyết riêng.
