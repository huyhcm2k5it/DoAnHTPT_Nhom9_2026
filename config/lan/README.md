# Five-machine LAN examples

These are samples, not detected machine addresses. Replace 192.168.1.11/12/13 consistently in all six JSON files with the three actual server IPs. Keep distinct NodeId 1/2/3; each IP uses client port 9001 and peer port 9100. Duplicate IP+port endpoints are rejected; identical port numbers on distinct IPs are valid. Literal IPs are required by the template validator.

M1/M2/M3 run S1/S2/S3 respectively, each with its own future local SQL Server 2022 database. M4 runs C1/owner A. M5 runs C2/owner B and C3/owner C as separate processes and stores. Copy code/config, never private keys, SQL credentials, database files or serving rights between nodes.

All files share DatasetId `124a4f06-4ac1-4c12-8b27-202600000009`. Do not generate a DatasetId per boot or silently reset an existing dataset. This sample is not permission to seed or activate SQL.

The start scripts select local configs. For LAN configuration validation, after `./scripts/build.ps1`, run from the repository root (substitute SDK path if dotnet is not on PATH):

```powershell
dotnet ./src/De6.Server.Host/bin/Debug/net10.0/De6.Server.Host.dll --config ./config/lan/s1.json --check
dotnet ./src/De6.Client.WinForms/bin/Debug/net10.0-windows/De6.Client.WinForms.dll --config ./config/lan/c1.json --check
```

Omit `--check` to wait in safe server bootstrap or display the disabled client UI. SQL/TLS/TCP/replication/authentication are NOT IMPLEMENTED. When implemented, provision trusted server certificates, peer certificates mapped to NodeId and A/B/C account password hashes outside source control; configure least-privilege firewall rules for reviewed endpoints, not public SQL access. Never add an always-true certificate validator.
