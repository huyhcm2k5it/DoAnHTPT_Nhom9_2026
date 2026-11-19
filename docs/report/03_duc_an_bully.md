# Bầu chọn Bully và quyền phục vụ

Phần Đức An, Huy thực hiện thay. Phạm vi gồm bầu ứng viên, quyền phục vụ, định danh peer và kiểm tra trước khi chuyển quyền theo [V12](../De6_Thiet_Ke_Trien_Khai_V12.md).

Nguồn cần đọc: ND7; Nguyễn Xuân Anh 2014 mục 2.3.5.1. Phạm vi code: `src/De6.Cluster.Election/ và src/De6.Cluster.Control/`. Đặc tả triển khai: [V12](../De6_Thiet_Ke_Trien_Khai_V12.md).

## 1 Cơ sở lý thuyết

Bully ưu tiên tiến trình có ID cao nhất trong nhóm còn liên lạc và đủ điều kiện. Một node nghi ngờ điều phối viên bị lỗi sẽ hỏi các node có ID cao hơn. Quy tắc chọn theo thứ tự ID giúp các vòng hội tụ khi liên lạc ổn định. Đề 6 áp dụng ND7 slide 9–10 và Nguyễn Xuân Anh, mục 2.3.5.1, tr. 59. Phần phục hồi liên hệ ND12 và mục 2.6.6, tr. 88.

## 2 Giả thiết và các bước

Ba thành viên có ID cố định S1–S3. Node chưa được admit hoặc chưa đồng bộ không vào nhóm ứng viên. Sau ba nhịp heartbeat bị thiếu, node có thể gửi `ELECTION` tới các ID cao hơn. Node đủ điều kiện trả `ANSWER` và duy trì vòng của mình. Hết 2 giây mà thiếu `ANSWER`, node công bố ứng viên bằng `COORDINATOR`. Nếu có `ANSWER` nhưng thiếu `COORDINATOR` trong 5 giây thì tái bầu sau backoff tối đa 500 ms. Mốc thời gian bầu dùng đồng hồ đơn điệu.

## 3 Bất biến và giới hạn

Timeout chỉ tạo nghi ngờ. Candidate có thể thuộc node có lịch sử ngắn hơn, do đó nguồn replay chọn theo bằng chứng về tiền tố đã kiểm tra. Trước khi mở quyền, phải cách ly writer cũ, xác nhận hai participant có cùng hash và digest tại K và đọc lại manifest đã lưu. Khi thiếu một participant hợp lệ, prefix xung đột hoặc chưa xác minh fencing, cổng nghiệp vụ giữ đóng. Restart luôn bắt đầu ở `RECOVERING`.

## 4 Ánh xạ vào code

- `BullyElection` và `HeartbeatFailureDetector` giữ các vòng bầu và trạng thái nghi ngờ.
- `PeerCertificateAuthenticator` ánh xạ certificate pin sang NodeId. `ClusterControl` giữ cổng phục vụ, cổng lưu trữ và context replay.
- `ClusterRecoveryPlanner` so sánh lịch sử và tạo manifest. `RecoveryManifestCoordinator` ghi, xác minh và hoàn tất cùng manifest trên các participant trước khi trả quyền kích hoạt cho caller.
- `SqlBookingStore` lưu manifest cùng accepted authority trong transaction cục bộ. Host và Transport cần nối các bước này với peer thật và quy trình quản trị. [PR #5](https://github.com/huyhcm2k5it/DoAnHTPT_Nhom9_2026/pull/5)

## 5 Trace và demo

**Trace thiết kế cho T24.** S3 mất heartbeat → S1 gửi ELECTION cho S2 và S3 → S2 trả ANSWER và hỏi S3 → hết thời hạn ANSWER của S2 → S2 công bố COORDINATOR. Gate của S2 chỉ mở sau khi quy trình quản trị xác minh fence, hai participant và manifest.

**Trace fixture cho T25.** Cả hai gate đóng → S1 lưu manifest → thao tác ghi S2 lỗi → thử lại với cùng RunId → S1 và S2 xác minh manifest → lỗi khi S2 đánh dấu hoàn tất → thử lại cùng RunId → đọc lại trạng thái hoàn tất trên cả hai → S2 nhận quyền kích hoạt. Fixture giả lập lỗi ở store, chưa mô phỏng sự cố trên hai SQL Server thật.

## 6 Kiểm thử và bằng chứng

Chạy `scripts/test.ps1` để kiểm tra harness `De6.Cluster.Control.Tests`. Harness kiểm riêng timeout và nhận diện peer (liên quan T24/T33), điều kiện fence và đọc lại manifest (T22/T25/T26), replay và rejoin có prefix (T20/T28). Giữ trạng thái nghiệm thu theo [ma trận T01–T36](../testing/TEST_MATRIX.md) cho tới khi có record từ cụm thật.

## 7 Nhận xét và tài liệu tham khảo

Khi bảo vệ, giải thích vì sao node ID cao nhất chỉ là ứng viên, vì sao epoch không xác minh được writer đã bị cách ly và vì sao so hash phải dùng cùng index. Phần còn cần ghép với Host/TCP là lịch heartbeat, quản trị fencing, replay qua mạng và kịch bản tổng hợp G3/G4. Đuôi lịch sử xung đột hiện được chặn và báo cần archive/repair.
