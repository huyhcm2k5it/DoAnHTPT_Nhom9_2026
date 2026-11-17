# Chạy phần Huy tuần 2

H5 cung cấp oracle, digest canonical và rebuild projection từ log. H6 cung cấp cấu hình/probe SQL và composition để các chủ module ghép implementation thật. Đây chưa phải nghiệm thu cả cụm hoặc quyền trả FINAL.

## Xem oracle của de_6

```powershell
dotnet run --project tools/De6.Sql.Tool -c Release -- audit --database de_6
```

Nếu dùng SDK cục bộ, thay dotnet bằng `& .\.tools\dotnet\dotnet.exe`. Lệnh dùng Windows Authentication trên SQL local, chỉ đọc. Kết quả gồm LocalApplied K, HeadHash, BusinessDigest, MatchesHistory và vi phạm. MatchesHistory=true không chứng minh Stable, FINAL, fencing hoặc chịu lỗi nhiều máy.

`database/oracle/001_local_invariants.sql` kiểm tra 10 ghế, quan hệ SOLD/CONFIRMED/ticket/owner, trùng giữ ghế, log/result và continuity. Runner còn xác minh tất cả event bằng SHA256 rồi replay trên projection tạm. Không dùng CHECKSUM SQL ngắn làm bằng chứng duy nhất.

## Đối soát tại K

`SqlBookingStore.GetFrozenPrefixAsync(K)` cần lease `IStoreAccessGate.EnterMaintenance(K)` do Control cấp. K phải bằng LastAppliedIndex hiện tại; không trả snapshot lịch sử từ bảng đang ở mốc mới hơn. Prefix report chứa DatasetId, SchemaVersion, NodeId, K, HeadHash và BusinessDigest.

Digest v1 chứa dataset/K, Seats, Reservations, Tickets và RequestResults. Ghế sắp theo trip/seat; GUID theo chuỗi N ordinal; null/GUID/UTC bảy chữ số được canonical hóa; kết quả giữ nguyên bytes. NodeId, credential và quyền phục vụ không thuộc digest dữ liệu. Hai node khác ID nhưng cùng dữ liệu tại K phải cùng digest.

## Rebuild có kiểm soát

`RebuildProjectionAsync(K)` cũng cần lease maintenance thật. CLI không cấp quyền maintenance hoặc tự mở ghi. Không xóa log/projection trong SSMS để thử trên DB đồ án đang dùng.

Runner kiểm tra toàn bộ log TRƯỚC khi sửa dữ liệu. Trong một transaction: dựng lại seed/projection/reservations/tickets/result từ after-image, khôi phục derived index/head, chạy oracle và commit. Giữ nguyên OperationLog bytes, NodeId, DatasetId, AcceptedEpoch/Leader và schema metadata. Log hỏng, hash/index sai hoặc K khác thì dừng và giữ chứng cứ. Rebuild không archive/đổi lịch sử; RepairPlan cho nhánh cũ vẫn thuộc Control.

Test dùng DB tạm để tạo corruption, kiểm tra oracle, inject lỗi trước commit để xác nhận rollback rồi rebuild và so digest. Không dùng de_6 làm nơi reset test.

## Chạy lại và phối hợp

```powershell
.\scripts\test.ps1 -Configuration Release
.\scripts\test-huy-sql.ps1 -Configuration Release
```

Các executable harness không được dotnet test tự khám phá. SQL là thật; gate/decision fixture chỉ thay module chưa cài của các bạn khác. T01–T36 còn cần coordinator, network, Control và client thật. Hướng dẫn Host/probe được bổ sung trong tài liệu HUY_HOST_SQL.md.

Gia Quân review Contracts/digest, sender và transaction; Đức An review maintenance/grant/replay/metadata; Văn Khoa review frame/TLS. Không bỏ review chéo của các module dùng hợp đồng trước khi tích hợp.

Kết quả chạy bản ghép cuối, môi trường, trạng thái `de_6`, các review finding đã sửa và giới hạn được ghi trong [HUY_COMPLETION_RECORD.md](HUY_COMPLETION_RECORD.md).
