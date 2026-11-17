# Báo Cáo Chuyên Đề: Giải Thuật Loại Trừ Tương Hỗ Tập Trung & Nhân Bản Primary-Backup

- **Người thực hiện**: Nguyễn Gia Quân
- **Phân công trách nhiệm**: Chủ trì thiết kế & cài đặt `De6.Booking.Domain` (Máy trạng thái nghiệp vụ, Điều phối viên `CommandCoordinator`, Xác thực định danh `CommandIdentityValidator`), điều phối hợp đồng dữ liệu chuẩn hóa `De6.Contracts`, và cài đặt thuật toán gửi nhân bản đồng bộ `SynchronousReplicaSender` (`De6.Cluster.Replication`).
- **Mã nguồn thực hiện & kiểm chứng**:
  - `src/De6.Contracts/ProtocolContracts.cs`, `CanonicalProtocol.cs`
  - `src/De6.Booking.Domain/BookingDecisionEngine.cs`, `CommandIdentityValidator.cs`, `ICommandCoordinator.cs`, `CommandCoordinator.cs`
  - `src/De6.Cluster.Replication/IPeerChannel.cs`, `SynchronousReplicaSender.cs`
  - `tests/unit/De6.Domain.UnitTests/`
- **Tài liệu quy chiếu**: Đặc tả thiết kế [De6_Thiet_Ke_Trien_Khai_V12.md](../De6_Thiet_Ke_Trien_Khai_V12.md) và [Phân công nhóm 9](../team/Phan_Cong_Nhom9.md).

---

## 1 Cơ sở lý thuyết

Đồ án ứng dụng hai giải thuật phân tán nền tảng được giảng dạy trong học phần:

### 1.1 Loại trừ tương hỗ tập trung (Centralized Mutual Exclusion)
- **Căn cứ bài giảng**: Slide **Nội dung 7: Loại trừ tương hỗ - Giải thuật bầu cử**, slide 2–3.
- **Căn cứ giáo trình**: Sách *Các Hệ Thống Phân Tán* (Nguyễn Xuân Anh, 2014), mục 2.3.3.1 "Giải thuật tập trung", trang 56–57.
- **Nguyên lý hoạt động**:
  - Để giải quyết bài toán tranh chấp vùng găng (Critical Section - CS) giữa nhiều tiến trình quầy vé cùng truy cập vào tài nguyên dùng chung (10 ghế của chuyến tàu `TripId=1`), một tiến trình điều phối trung tâm (**Coordinator / Primary**) duy nhất được chỉ định để tuần tự hóa toàn bộ yêu cầu.
  - Ba bước cơ bản của giao thức:
    1. **Request**: Tiến trình muốn vào CS phải gửi thông điệp yêu cầu kèm định danh và nội dung lệnh tới Coordinator.
    2. **Grant**: Coordinator duy trì hàng đợi (FIFO/Bounded Queue). Khi vùng găng rảnh và Coordinator có thẩm quyền hợp lệ, Coordinator cấp quyền thực thi (Grant) tuần tự cho từng lệnh.
    3. **Release**: Sau khi lệnh hoàn tất bền vững, Coordinator giải phóng khóa để xử lý yêu cầu tiếp theo trong hàng đợi.

### 1.2 Giao thức nhân bản Primary-Backup đồng bộ (Primary-Based Synchronous Replication)
- **Căn cứ bài giảng**: Slide **Nội dung 14: Nhất quán và nhân bản**, slide 18 ("Giao thức dựa trên bản chính").
- **Căn cứ giáo trình**: Sách *Các Hệ Thống Phân Tán* (Nguyễn Xuân Anh, 2014), mục 2.8.5.2 "Giao thức dựa trên bản chính", trang 106–107.
- **Nguyên lý hoạt động**:
  - Để đảm bảo tính sẵn sàng và khả năng chịu lỗi dừng (crash-fault tolerance), mô hình gồm một bản chính (**Primary**) và hai bản sao lưu (**Backup**).
  - Mọi thao tác ghi (mutation) đều phải gửi tới Primary. Primary đóng vai trò phân định thứ tự logic (`LogIndex`), sinh sự kiện bất biến (`PersistedEvent`), ghi nhận cục bộ (`LocalApplied`), và đồng thời phát tán sự kiện tới các Backup (`SynchronousReplicaSender`).
  - Giao thức yêu cầu đạt **Quorum đa số ($\ge 2/3$)**: Primary chỉ được công nhận kết quả là bền vững (`StableIndex`) và trả kết quả chính thức (`FINAL`) cho client khi có ít nhất 1 node Backup gửi lại biên lai xác nhận (`AppliedReceipt`) hợp lệ khớp toàn bộ chữ ký băm SHA-256, Epoch và LogIndex.

---

## 2 Ranh giới trách nhiệm & Phạm vi kiểm chứng

Để đảm bảo tính trung thực kỹ thuật và bám sát tiến độ phân công, ranh giới giữa phần việc của Gia Quân và các thành viên khác trong Nhóm 9 được phân định rõ ràng như sau:

### 2.1 Phần việc Gia Quân sở hữu và hoàn thành
1. **Domain Decision Engine (`BookingDecisionEngine`)**: Máy trạng thái nghiệp vụ thuần túy (Pure In-Memory State Machine) kiểm soát 3 thao tác `HOLD`, `CONFIRM`, `CANCEL` trên 10 ghế, đảm bảo không thể bán trùng và vé đã mua mang tính bất biến.
2. **Canonical Contracts & SHA-256 (`CanonicalProtocol`)**: Chuẩn hóa cấu trúc UTF-8 không khoảng trắng thừa và sinh băm SHA-256 độc lập (`CommandHash`, `SeedHash`, `EventHash`).
3. **Identity & Idempotency Validator (`CommandIdentityValidator`)**: Đối soát toàn vẹn gói tin, kiểm tra tính lặp lại (Idempotent) dựa trên cặp `RequestId + CommandHash`, và phát hiện xung đột `REQUEST_CONFLICT`.
4. **Command Coordinator (`CommandCoordinator`)**:
   - Quản lý hàng đợi vào có giới hạn (`BoundedChannel` 256 phần tử).
   - Tiến trình worker đơn tuần tự hóa việc cấp Grant cho từng mutation.
   - Cơ chế khóa hai đầu (Double-ended Fencing): Kiểm tra thẩm quyền `AuthorityGate` trước và sau các thao tác đọc có thẩm quyền (`GetSeatsAsync`, `GetMyReservationsAsync`, `GetResultAsync`).
   - Duy trì bất biến: Không cho phép xử lý mutation tiếp theo khi mutation hiện tại chưa đạt `StableIndex`.
5. **Replica Sender Protocol (`SynchronousReplicaSender`)**:
   - Thuật toán gửi nhân bản song song qua trừu tượng hóa `IPeerChannel`.
   - Đối chiếu biên lai biên nhận (`AppliedReceipt`) nghiêm ngặt: Kiểm tra đúng kênh đích (`receipt.NodeId == channel.TargetNodeId`), khớp `EventEpoch`, `LogIndex`, `EventId`, không mang cờ phục hồi và khớp mảng byte băm `EventHash`.

### 2.2 Ranh giới ngoài phạm vi (Do thành viên khác phụ trách / Chờ tích hợp G1–G2)
- **Truyền thông Socket TCP/TLS & Framing**: Thuộc trách nhiệm của **Văn Khoa** (`De6.Transport.Tcp`). `IPeerChannel` trong module của Quân là giao diện hợp đồng phục vụ điều phối và kiểm thử, chưa đấu nối trực tiếp vào socket listener thực tế trên mạng.
- **Lưu trữ vật lý SQL Server & Receiver Apply**: Thuộc trách nhiệm của **Huy** (`De6.Booking.Persistence`). Trong module Domain của Quân, các tương tác lưu trữ được kiểm chứng qua hợp đồng `IBookingStore`.
- **Tiến trình Server Host & Thuật toán Bầu cử Bully**: Thuộc trách nhiệm của **Đức An & Huy** (`De6.Server.Host`, `De6.Cluster.Election`). Cổng phân quyền `IAuthorityGate` trong kiểm thử Domain được mô phỏng qua mock/stub.
- **Giao diện Client WinForms & Pending Store**: Thuộc trách nhiệm của **Văn Khoa** (`De6.Client.WinForms`, `De6.Client.Sdk`).

---

## 3 Bất biến an toàn và Giới hạn hệ thống

### 3.1 Bất biến an toàn (Safety Invariants)
1. **Không bán trùng ghế (No Double-Booking)**:
   - Một ghế đã ở trạng thái `HELD` hoặc `SOLD` thì mọi yêu cầu `HOLD` sau đó đều bị từ chối với mã kết quả `SEAT_BUSY` hoặc `ALREADY_SOLD`.
2. **Tuần tự hóa đơn mutation (Single In-Flight Mutation)**:
   - Coordinator chỉ cho phép tối đa một mutation ở trạng thái dở dang (`LocalApplied` nhưng chưa `Stable`).
   - Mọi truy vấn đọc có thẩm quyền (`GetSeatsAsync`, `GetMyReservationsAsync`) bắt buộc phải đợi khóa `_mutationLock` giải phóng sau khi mutation đạt `StableIndex` để ngăn chặn hoàn toàn dirty read.
3. **Kiểm tra biên lai đối ngẫu (Mutual Receipt Matching)**:
   - Receipt từ backup phải xuất phát từ đúng kênh kết nối được chỉ định (`receipt.NodeId == channel.TargetNodeId`), ngăn chặn việc nhận sai hoặc giả mạo nguồn gốc node ACK.
4. **Kiểm tra rào chắn thẩm quyền hai đầu (Double-Ended Epoch Fencing)**:
   - Mọi thao tác đọc đều ghi nhận `CurrentEpoch` trước khi truy vấn store và xác minh lại tính hợp lệ của gate/epoch sau khi truy vấn hoàn tất. Nếu Primary bị tước quyền (demote/freeze) giữa chừng, kết quả đọc sẽ bị hủy bỏ ngay lập tức.
5. **Tính bất biến định danh và kết quả (Idempotency Invariance)**:
   - Gửi lại lệnh cùng `RequestId` và cùng `CommandHash` luôn nhận lại kết quả cũ (kể cả kết quả thất bại nghiệp vụ). Thay đổi dữ liệu trong lệnh với cùng `RequestId` lập tức bị chặn với lỗi `REQUEST_CONFLICT`.

### 3.2 Giới hạn hệ thống
- **Nút cổ chai tập trung**: Thông lượng ghi bị giới hạn bởi tốc độ của Single Worker Loop và độ trễ mạng khi đợi Quorum ACK từ backup. Tuy nhiên, với bài toán 10 ghế của 1 chuyến tàu, thiết kế này đem lại tính nhất quán tuần tự tuyệt đối (Strict Sequential Consistency).
- **Phụ thuộc vào tích hợp đa tiến trình**: Các kiểm chứng hiện tại được thực thi trên môi trường kiểm thử đơn vị độc lập (`De6.Domain.UnitTests`) sử dụng bộ giả lập hợp đồng (`FakePeerChannel`, `FakeBookingStore`). Việc kiểm chứng khả năng chịu lỗi phân vùng mạng thật sự cần được tiến hành tại các Cổng tích hợp G1 và G2.

---

## 4 Ánh xạ cấu trúc mã nguồn (Code Mapping)

| Thành phần | Đường dẫn file | Trách nhiệm chính |
| :--- | :--- | :--- |
| `BookingDecisionEngine` | `src/De6.Booking.Domain/BookingDecisionEngine.cs` | Máy trạng thái 10 ghế, ra quyết định nghiệp vụ thuần túy (HOLD, CONFIRM, CANCEL). |
| `CommandIdentityValidator` | `src/De6.Booking.Domain/CommandIdentityValidator.cs` | Đối soát cấu trúc lệnh, băm SHA-256, kiểm tra Dedup / Idempotency và Conflict. |
| `ICommandCoordinator` | `src/De6.Booking.Domain/ICommandCoordinator.cs` | Khai báo API giao tiếp: `SubmitAsync`, `GetSeatsAsync`, `GetResultAsync`, `GetMyReservationsAsync`. |
| `CommandCoordinator` | `src/De6.Booking.Domain/CommandCoordinator.cs` | Bounded queue 256, Single worker loop loại trừ tương hỗ, điều phối StableIndex, kiểm soát đọc an toàn. |
| `IPeerChannel` | `src/De6.Cluster.Replication/IPeerChannel.cs` | Trừu tượng hóa kênh truyền gửi `PersistedEvent` và nhận `AppliedReceipt` từ từng peer. |
| `SynchronousReplicaSender`| `src/De6.Cluster.Replication/SynchronousReplicaSender.cs` | Gửi nhân bản song song, kiểm tra biên lai hợp lệ (bao gồm NodeId khớp kênh), xác lập Quorum 2/3. |
| `CanonicalProtocol` | `src/De6.Contracts/CanonicalProtocol.cs` | Serialize JSON chuẩn hóa UTF-8 và tính băm SHA-256 (`CommandHash`, `SeedHash`, `EventHash`). |

---

## 5 Dấu vết mô phỏng qua kịch bản kiểm thử (Test Harness Simulation Traces)

Dưới đây là các dấu vết luồng thực thi được kiểm chứng thực tế thông qua bộ kiểm thử `De6.Domain.UnitTests`:

### Trace 1: Xung đột tương tranh 3 quầy đồng thời tranh chấp 1 ghế (Kịch bản T02)
- **Thiết lập mô phỏng**: Ba tiến trình quầy vé gửi đồng thời 3 lệnh `HOLD` ghế số 1 (`Req_A`, `Req_B`, `Req_C`) với định danh khác nhau vào `CommandCoordinator`.
- **Dấu vết ghi nhận**:
  1. Hàng đợi `_queue` tiếp nhận cả 3 lệnh tuần tự vào bộ đệm.
  2. Worker lấy `Req_A` xử lý: Ghế 1 đang `AVAILABLE` $\rightarrow$ `BookingDecisionEngine` sinh `OK_HELD` (`HeldCreated`).
  3. Coordinator gọi `ApplyLocalAsync` $\rightarrow$ sinh `PersistedEvent` tại `LogIndex = 1` $\rightarrow$ Đạt `LocalApplied`.
  4. Bộ gửi `SynchronousReplicaSender` gửi sự kiện qua kênh backup $\rightarrow$ nhận `AppliedReceipt` hợp lệ từ Backup 2.
  5. Coordinator gọi `MarkStableAsync(1)` $\rightarrow$ cập nhật `StableIndex = 1`.
  6. `Req_A` hoàn tất và trả về kết quả `TransportStatus.Final` kèm mã `OK_HELD`.
  7. Worker nhả khóa và lấy `Req_B` ra xử lý: Ghế 1 lúc này đã có trạng thái `HELD` $\rightarrow$ Engine sinh quyết định `SEAT_BUSY` (`NoChange`).
  8. Sự kiện `NO_CHANGE` được nhân bản và trả về `TransportStatus.Final` kèm mã `SEAT_BUSY` cho Quầy B.
  9. Tương tự, `Req_C` được xử lý và trả về `TransportStatus.Final` kèm mã `SEAT_BUSY` cho Quầy C.
- **Minh chứng**: Test `Q4: Concurrency - 3 quầy HOLD same seat simultaneously -> exactly 1 OK_HELD, 2 SEAT_BUSY` chạy thành công.

### Trace 2: Phối hợp hợp đồng 2 Node & Cơ chế Quorum (Kịch bản T12 / Q5-Q6)
- **Thiết lập mô phỏng**: Primary Node 1 kết nối với Node 2 qua `FakePeerChannel` có mô phỏng logic lưu trữ của Backup.
- **Dấu vết ghi nhận**:
  1. Client gửi `HOLD` ghế 3 tới Primary.
  2. Primary thực thi cục bộ $\rightarrow$ gửi `PersistedEvent` sang Backup Node 2.
  3. Backup Node 2 tiếp nhận sự kiện, ghi nhận vào danh sách sự kiện bền vững và sinh `AppliedReceipt(NodeId: 2, LogIndex: 1, EventHash: ...)`.
  4. `SynchronousReplicaSender` xác minh biên lai: `receipt.NodeId == 2`, khớp Epoch, LogIndex và EventHash $\rightarrow$ Trả về kết quả Quorum thành công.
  5. Primary nâng `StableIndex = 1` và trả `TransportStatus.Final` về cho client.
  6. **Kịch bản lỗi Backup**: Khi kênh Backup bị ngắt kết nối (`ShouldSucceed = false`), `SynchronousReplicaSender` timeout và trả về kết quả không chắc chắn (Uncertain). Primary không được phép tăng `StableIndex` và trả về `TransportStatus.Unknown` cho client.
- **Minh chứng**: Test `Q5/Q6: Two-Node Contract Simulation - Primary Coordinator + ReplicaSender to Backup Channel achieves Stable FINAL` và test lỗi backup chạy thành công.

---

## 6 Bằng chứng kiểm thử nghiệm thu (Verification Evidence)

Toàn bộ các yêu cầu thuộc phạm vi của Gia Quân được bao phủ độc lập bằng bộ kiểm thử tự động dạng console executable (không phụ thuộc NuGet ngoài):

- **Lệnh thực thi**:
  ```powershell
  Set-Location C:\QTD\Nam4\CacHeThongPhanTan\DoAnHTPT_Nhom9_2026
  .\scripts\test.ps1 -Configuration Release
  ```
- **Kết quả nghiệm thu**:
  - **Smoke Tests**: `23/23 assertions passed` (Kiểm tra tính hợp lệ của cấu hình node, thư mục dữ liệu, endpoint và trạng thái boot `RECOVERING`).
  - **Domain Unit Tests**: **`85/85 tests PASSED`** (Tỷ lệ đạt 100%, 0 Warning, 0 Error).

### Bảng đối chiếu các nhóm kiểm thử trọng tâm:
| Nhóm kiểm thử | Số lượng test | Nội dung kiểm chứng chính |
| :--- | :---: | :--- |
| **Q1: Contracts & Canonical** | 20 | Băm chuẩn SHA-256 (`CommandHash`, `SeedHash`, `EventHash`), định dạng JSON UTF-8 cố định, cấu trúc receipt bàn giao. |
| **Q2: Business State Machine** | 18 | Đầy đủ bảng quyết định `HOLD`, `CONFIRM`, `CANCEL`, bảo toàn thông tin vé, chặn confirm/cancel chéo ghế hoặc sai chủ sở hữu. |
| **Q3: Identity & Dedup** | 9 | Chống trùng lặp lệnh (Idempotency), phát hiện `REQUEST_CONFLICT`, duy trì kết quả lịch sử khi retry. |
| **Q4: Coordinator & Mutual Exclusion**| 25 | Bounded queue 256 (`RETRY_LATER`), tương tranh 3 quầy, bảo vệ snapshot đọc, chặn đọc uncommitted, xử lý client hủy. |
| **Q5: Synchronous Replication** | 6 | Đạt Quorum 2/3 với 1 backup ACK, timeout khi không đủ backup, từ chối biên lai sai băm/epoch và từ chối biên lai sai `NodeId`. |
| **Q6: Query & Two-Node Coordination**| 7 | Truy vấn `GetMyReservationsAsync` bảo mật theo owner, khóa đợi mutation, kiểm tra rào chắn epoch hai đầu, và mô phỏng 2 node đạt Stable/FINAL. |

---

## 7 Kết luận

Phần việc của **Nguyễn Gia Quân** đã hoàn thành đầy đủ các cam kết cho Tuần 1 và Tuần 2 theo đặc tả V12 và kế hoạch phân công nhóm:
1. Đã cài đặt hoàn chỉnh lõi nghiệp vụ bán vé tàu cao tốc với tính đúng đắn về loại trừ tương hỗ tập trung.
2. Đã hoàn thiện giao thức gửi nhân bản đồng bộ và cơ chế xác thực biên lai Quorum 2/3 nghiêm ngặt.
3. Đã sẵn sàng các giao diện hợp đồng (`IPeerChannel`, `ICommandCoordinator`, `IBookingStore`) để bước vào các cổng tích hợp **G1** (Domain + SQL với Huy) và **G2** (Primary + Backup qua TCP với Văn Khoa và Đức An).

### Tài liệu tham khảo:
1. ThS. Lê Ngọc Bảo, *Bài giảng Các Hệ Thống Phân Tán*, PTIT HCM (Nội dung 7: Loại trừ tương hỗ; Nội dung 14: Nhất quán và nhân bản).
2. TS. Nguyễn Xuân Anh (2014), *Giáo trình Các Hệ Thống Phân Tán*, NXB Thông Tin và Truyền Thông.
3. Nhóm 9 (2026), *Đặc tả thiết kế triển khai mô phỏng bán vé tàu 10 ghế V12*.
