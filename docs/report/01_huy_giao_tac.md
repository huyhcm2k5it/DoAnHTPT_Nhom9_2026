# Giao tác ACID và điều khiển tương tranh của Huy

**Đồ án Các hệ thống phân tán PTIT · Nhóm 9 · Đề 6 · Huy, H1–H6**

Mỗi thao tác bán vé phải giữ nhất quán ghế, chỗ giữ, vé và kết quả tra cứu, kể cả khi yêu cầu bị gửi lại hoặc node gặp sự cố. Phần Huy áp dụng ND11 về giao tác và tương tranh, sau đó nối bằng chứng lưu cục bộ với primary-backup theo ND14 và phục hồi theo ND12. Phần đối chiếu nội dung theo đề và V12. Mã CLO chính thức cần xác nhận theo đề cương lớp. [Phân công nhóm](../team/Phan_Cong_Nhom9.md) · [Thiết kế V12](../De6_Thiet_Ke_Trien_Khai_V12.md)

## ACID trong transaction Apply

Atomicity: reservation/seat/ticket, OperationLog, RequestResults và NodeState cùng một SQL transaction. Test trước commit gây lỗi rồi kiểm tra không có nửa Apply; lỗi sau commit vẫn thấy event/result đã tồn tại.

Consistency: FK/UNIQUE/CHECK trong migration cùng ValidateDecision kiểm tra quan hệ ghế/reservation/chủ/vé. Constraint đơn bảng không tự chứng minh tất cả bất biến xuyên bảng hoặc toàn cụm; oracle SQL và replay từ log bổ sung kiểm tra.

Isolation: SERIALIZABLE và khóa UPDLOCK/HOLDLOCK trên NodeState sắp thứ tự các truy cập local. Không dùng NOLOCK. Đây là hàng rào local; coordinator của Gia Quân còn phải sắp thứ tự toàn bộ thao tác và giữ grant tới Stable hoặc đóng cụm.

Durability: SqlClient chỉ trả Apply sau commit; từ chối chế độ forced delayed durability. datetime2(7) được bind rõ để event và dữ liệu SQL giữ nguyên độ chính xác UTC. SQL commit local chưa đủ FINAL: Gia Quân kiểm receipt ACK sau khi backup commit bền vững rồi coordinator mới công bố.

## Trace thực hiện

HOLD: kiểm tra phiên/command tại lớp trên → grant qua gate → BEGIN → đọc NodeState/RequestId/ghế → decision engine → ghi projection/event/result/head → COMMIT → trả PersistedEvent cho coordinator. Replication/Stable/FINAL chưa thuộc lời hứa của store.

Mất outcome: lỗi trước commit rollback; lỗi sau commit đóng gate và ném StorageUncertainException. InspectLocalRequestAsync chỉ giúp quản trị tìm chứng cứ local; không tự mở lại quyền, không tạo event mới để thử lại. Client timeout không có nghĩa operation thất bại.

## Oracle và rebuild

AuditLocalAsync kiểm tra chain hash/canonical/prefix, dựng projection tạm từ log và so với các bảng hiện hành. Digest SHA256 sắp theo khóa, chuẩn hóa GUID/null/UTC và giữ nguyên result bytes. LocalApplied K khác StableIndex và FINAL.

GetFrozenPrefixAsync/RebuildProjectionAsync cần maintenance lease của Control. Rebuild kiểm tra log trước, sửa projection/result trong một transaction và giữ nguyên OperationLog/NodeId/authority. Hash conflict hoặc K sai thì dừng. Đây là đối soát ở mốc tĩnh, không phải thuật toán snapshot toàn cục ND6.

## Từ receiver đến FINAL

`ReplicaReceiver` nhận danh tính peer từ lớp xác thực, kiểm phiên bản giao thức, dataset, writer và epoch. Nó chuyển nguyên event vào SQL. SQL kiểm quyền bằng lease và bằng metadata chấp nhận đã lưu, sau đó kiểm index kế tiếp, previous hash và before-version. Khi commit xong, Control ghi nhận mốc local trong khi lease còn hiệu lực. Receipt mang đúng node, epoch quyền, epoch event, index, EventId và EventHash. Sender chỉ dùng receipt khớp để báo đã có bản sao bền vững cho coordinator.

Khi gửi lại cùng event, backup so toàn bộ định danh, bytes, hash và kết quả với bản ghi đã lưu. Bản trùng trả lại receipt của cùng mốc và không tăng index. Thiếu index trả GAP để phía gửi biết mốc cần bù. Event khác tại index đã lưu làm cổng đóng và dữ liệu cũ được giữ nguyên. Replay có quyền riêng, giới hạn theo peer nguồn, RunId, mốc base và K. Event lịch sử giữ epoch và writer ban đầu.

| Trace để bảo vệ | Quan sát cần ghi |
| --- | --- |
| HOLD hợp lệ | RequestId, transaction ghi chỗ giữ và event/result, mốc LocalApplied, ACK đúng event, rồi Stable/FINAL khi coordinator đã xác minh. |
| Backup commit nhưng ACK thất lạc | Bỏ receipt đầu, gửi lại cùng EventId và hash. SQL vẫn có đúng một event và một result. Receipt thứ hai khớp receipt thứ nhất. |
| Lỗi trước và sau commit | Trước commit, assert log/result/projection giữ nguyên. Sau commit, tra cứu local có chứng cứ và node giữ đóng để đối soát trạng thái. |
| Projection sai nhưng log còn đúng | Oracle báo sai ở K. Rebuild trong maintenance rồi chạy lại audit. Event bytes, NodeId và authority giữ nguyên. |

## Câu hỏi bảo vệ

| Câu hỏi | Trả lời ngắn |
| --- | --- |
| Vì sao đưa cả result vào transaction Apply? | Result và nghiệp vụ cùng bền vững. Retry có thể tìm đúng kết quả đã quyết định cho RequestId. |
| Dùng khóa UNIQUE đã đủ chống hai primary chưa? | UNIQUE kiểm tra dữ liệu của một DB. Cụm dựa vào điều kiện chuyển quyền và gate để duy trì một writer. |
| Vì sao phải giữ nguyên payload khi retry? | Định danh và hash của event ràng buộc nội dung đã quyết định. Event mới tại cùng index là xung đột. |
| Vì sao COMMIT ở primary chưa đủ để trả FINAL? | Cần thêm receipt khớp từ backup sau durable commit, rồi coordinator mới công bố mốc Stable. |
| Oracle cho thấy gì? | Tại mốc K, nó kiểm dữ liệu nghiệp vụ và kết quả có khớp với log hợp lệ khi dựng lại. Để kết luận về nhiều node cần so chứng cứ ở từng node và kiểm quyền. |
| Event epoch cũ còn được replay không? | Có, khi run được cấp quyền, nguồn khớp và entry nằm đúng prefix. Quyền của run được kiểm riêng với metadata lịch sử. |

Nguồn tham khảo dùng khi trình bày: Lê Ngọc Bảo, bài giảng HTPT PTIT ND11, ND12, ND14 theo ánh xạ trong V12. Nguyễn Xuân Anh, *Bài giảng Hệ thống phân tán*, PTIT, 2014, mục 2.5/2.5.1/2.5.3, tr. 74–77, và mục 2.8.5.2, tr. 106–107.

## Code và kiểm thử để bảo vệ

- [Migration](../../database/migrations/001_initial.sql), [Apply](../../src/De6.Booking.Persistence/SqlApplyTransaction.cs), [transaction và dedup](../../src/De6.Booking.Persistence/SqlBookingStore.cs).
- [Snapshot](../../src/De6.Booking.Persistence/SqlSnapshotReader.cs), [oracle/rebuild](../../src/De6.Booking.Persistence/SqlMaintenance.cs).
- [Receiver](../../src/De6.Cluster.Replication/ReplicaReceiver.cs), [Apply phía backup và replay](../../src/De6.Booking.Persistence/SqlReplicaApply.cs), [ghép Control và SQL trong Host](../../src/De6.Server.Host/HostPersistence.cs).
- [SQL tests](../../tests/integration/De6.Booking.Persistence.Tests/Program.cs), [fault/cancellation/durability](../../tests/integration/De6.Booking.Persistence.Tests/AdvancedCases.cs), [review regressions](../../tests/integration/De6.Booking.Persistence.Tests/ReviewRegressionCases.cs), [maintenance tests](../../tests/integration/De6.Booking.Persistence.Tests/MaintenanceCases.cs).
- [Chạy tuần 1](../implementation/HUY_WEEK1_RUNBOOK.md), [chạy tuần 2](../implementation/HUY_WEEK2_RUNBOOK.md).

Các test dùng SQL thật trên database tạm và gate/decision fixture trong test. Chưa thay được nghiệm thu T01–T36 trên cluster, SDK và Control thật; không gọi fault observer là đã kill process hoặc gây partition. Máy kiểm thử là SQL Server 2025 Express, không ghi thành đã chạy engine 2022.

Huy nên demo một HOLD/CONFIRM, một duplicate RequestId, một lỗi trước/sau commit và một corruption được oracle phát hiện/rebuild sửa; trỏ đúng lệnh test, code và dữ liệu của lượt demo. Giải thích vì sao UNIQUE không thay coordinator và vì sao local commit không thay ACK backup.
