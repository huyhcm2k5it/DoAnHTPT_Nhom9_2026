# De6.Booking.Domain

## Reservation snapshot handoff

`GetMyReservationsAsync` returns `StableReservationsSnapshot` with DatasetId, Epoch,
AppliedIndex and Reservations. The domain store adapter must implement
`ReadReservationsSnapshotAsync(ownerId, expectedIndex)` using one transaction for rows
and metadata, rejecting an index mismatch. The coordinator checks that metadata against
its dataset, epoch and StableIndex before publishing the response.

The SQL component already exposes `ReadMyReservationsAsync(owner, expectedIndex)`;
the Host/domain adapter still needs to bridge that API and obtain its metadata under
the same SQL transaction and authority lease. The fake store tests verify the domain
contract only; they do not establish SQL atomicity or two-database acceptance.

The sender now rejects a mismatching AuthorityEpoch before choosing a receipt, so
a stale receipt from one backup cannot hide a valid receipt from another backup.

Owner: Gia Quân.

`BookingDecisionEngine`, `CommandIdentityValidator` and `CommandCoordinator` implement the week 1 in-memory domain path. The coordinator serializes mutations, blocks uncertain local commits, checks replica receipts and StableIndex before FINAL, and gates `GetSeats`/`GetResult`. Unit tests use fake storage, authority and replication. SQL transaction atomicity, real gate/replica wiring and three-node acceptance still depend on the other modules. V12 sections 6-10, 16; T02-T12, T29, T32.

Specification: [V12](../../docs/De6_Thiet_Ke_Trien_Khai_V12.md). Team handoff and commands: [repository README](../../README.md).

Definition of done for future implementation: reviewed shared contracts, tests for owned V12 cases, error/uncertainty paths, and reproducible evidence. A successful template build does not satisfy business acceptance.

## Hướng dẫn bắt đầu

Gia Quân thực hiện theo các bước **Q1–Q6** trong [phân công chi tiết](../../docs/team/Phan_Cong_Nhom9.md#3-gia-quân--domaincoordinator-và-hợp-đồng-chung). Mỗi bước nêu file/API cần cài, cách tự kiểm và phần giao cho bạn khác. Các tên file bổ sung trong hướng dẫn là file cần tạo, không phải implementation đã có.

Đọc phần cổng ghép và luồng HOLD trong tài liệu đó để phối hợp fixture và hiểu các trạng thái LocalApplied/Stable/FINAL. Cả bốn người đều code, test và hoàn thiện phần lý thuyết riêng.
