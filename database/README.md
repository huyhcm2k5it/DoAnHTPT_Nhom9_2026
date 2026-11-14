# SQL implementation workspace

Owner: Huy. No executable SQL schema, migration, seed or oracle is supplied. Building the solution and running startup checks do not connect to SQL, create tables, seed data or verify durability.

Follow [V12 sections 8-9, 11, 14, 17](../docs/De6_Thiet_Ke_Trien_Khai_V12.md). Keep schema definitions, ordered migrations, controlled seed and read-only oracle separate. Credentials/connection strings/private keys stay outside Git. Each server owns a separate SQL Server 2022 database on its own machine; clients never connect to SQL.

Before integration, implement atomic event/projection/result/NodeState Apply, strict request identity, canonical bytes and fully durable commit. Test SQL exceptions with an uncertain commit outcome. Do not hold a SQL transaction while waiting for TCP. SQL UNIQUE constraints are local barriers, not distributed mutual exclusion.

Hướng dẫn thao tác và bàn giao theo người: [Phân công chi tiết](../docs/team/Phan_Cong_Nhom9.md). Mục 9–12 nêu thứ tự ghép, test project cần tạo, record và checklist PR. Bộ test nghiệp vụ tương lai phải được nối vào script/CI bằng lệnh thực; không suy PASS từ bootstrap.
