# Phân công triển khai — Nhóm 9

## Kế hoạch triển khai trong hai tuần

Mục tiêu là hoàn thiện bản cơ sở bán vé một chuyến 10 ghế theo V12. Bảng này đặt đầu ra cho từng tuần; các bước H1–H6, Q1–Q6, A1–A6, C1–C6 và K1–K6 được giải thích chi tiết ở phần mỗi người phía sau. Cả năm người đều code, test, review và viết phần lý thuyết gắn với code.

| Thành viên | Tuần 1 làm nền tảng và ghép luồng thật | Tuần 2 hoàn thiện chịu lỗi và bàn giao |
| --- | --- | --- |
| Huy | H1–H4: schema/seed, Apply nguyên tử, duplicate và snapshot; ghép SQL với Domain/Host; giao store và oracle cơ bản. | H5–H6: digest/rebuild, SQL failure và cấu hình ba DB độc lập; phối hợp recovery, chạy oracle và tổng hợp bản tích hợp. |
| Gia Quân | Q1–Q4, ghép sớm Q5: chốt Contracts/fixture, nghiệp vụ, dedup và coordinator; cùng Huy/Nguyên Chương chạy HOLD–CONFIRM với backup thật. | Hoàn thiện Q5–Q6: Stable/FINAL, gate đọc, cancellation và queue; chạy tương tranh/retry/ACK sai, sửa lỗi tích hợp, hoàn thiện phần loại trừ tập trung. |
| Đức An | A1–A3: gate boot đóng, grant/freeze, Bully và test timer; chốt API quản trị/fencing, manifest và RecoveryContext với các bên. | A4–A6: compare/promotion, tiếp tục manifest, rejoin và fault test; chứng minh chưa fence không mở writer; bàn giao runbook và phần Bully. |
| Nguyên Chương | C1–C4: framing, TLS, peer identity, receiver/sender; ghép Apply thật và ACK sau commit, đủ một backup mới hoàn tất. | C5–C6: catch-up/replay, GAP/conflict, ACK mất/cũ và fault hooks; chạy ba node/rejoin, giao trace và phần primary-backup/phục hồi. |
| Văn Khoa | K1–K3, ghép sớm K5: SDK, pending bền vững, router/login và UI tối thiểu; nối luồng giữ/xác nhận thật, không báo thành công giả. | K4–K6: resolve/retry cùng identity, client restart/failover, hoàn thiện UI; chạy ba quầy với store riêng, thu số liệu/demo và phần RPC/truyền thông. |
| Cả nhóm | Chốt G0, đạt G1 và hướng tới G2: ít nhất primary + một backup thật, FINAL đúng điều kiện. Mỗi người có PR/code/test; mở khung báo cáo và giải thích trace phần mình. | Hoàn tất G2 nếu còn thiếu rồi G3–G5 theo phụ thuộc: ba node, lỗi/recovery, ma trận T01–T36 có record, báo cáo/video và review phát hành. |

**Cách chốt kết quả:** cuối mỗi tuần, từng người bàn giao PR, lệnh test và record thực; ghi rõ phần đạt, lỗi còn lại và phụ thuộc đang chờ. Tuần 2 phải dành công sức ghép/test và sửa lỗi, không để tới cuối mới nối các module. Cổng nào chưa đủ bằng chứng thì giữ trạng thái chưa đạt; bảng là mục tiêu tiến độ, không tự biến T01–T36 thành PASS. Không chia lịch theo ngày/giờ.

Nguồn chuẩn là [thiết kế V12](../De6_Thiet_Ke_Trien_Khai_V12.md), đặc biệt mục 6–17, 20–23. Tài liệu này phân công công việc cần làm, không xác nhận hệ thống đã hoàn tất. Bộ khung hiện tại chỉ kiểm tra cấu hình, server đóng quyền ở RECOVERING và UI vô hiệu hóa 10 ghế; SQL, TLS, booking và replication còn phải triển khai. Dùng cho đợt triển khai trong 2 tuần theo yêu cầu của nhóm; không chia lịch ngày/giờ. Thứ tự dưới đây là thứ tự phụ thuộc kỹ thuật; chỉ chuyển cổng khi đủ bằng chứng. Phạm vi cần hoàn thành vẫn là bản cơ sở V12, không giảm điều kiện an toàn để kịp thời gian.

**Yêu cầu bắt buộc: cả năm người đều trực tiếp viết code, viết/chạy test và bàn giao PR riêng.** Không có vai trò chỉ làm báo cáo, QA hoặc tích hợp. Mỗi người hoàn thiện ít nhất một luồng chức năng qua các module liên quan trong ownership của mình, có test và review; trách nhiệm reviewer/điều phối là phần bổ sung. Đánh giá bằng PR, file code, hành vi và bằng chứng test; không dùng số commit làm thước đo đóng góp.

## 1. Bảng trách nhiệm

| Thành viên | Sở hữu chính | Bàn giao cốt lõi | Reviewer chính |
| --- | --- | --- | --- |
| Huy | Booking.Persistence; database; tích hợp Host/CI | Apply nguyên tử, oracle, bản tích hợp | Gia Quân; Đức An cho recovery |
| Gia Quân | Booking.Domain; điều phối Contracts | Coordinator, dedup, canonical contracts | Huy; Nguyên Chương |
| Đức An | Cluster.Election; Cluster.Control | Candidate, authority gate, manifest/recovery | Nguyên Chương; Huy |
| Nguyên Chương | Cluster.Replication; Transport.Tcp | ACK sau durable Apply, replay, TCP/TLS | Đức An; Huy |
| Văn Khoa | Client.Sdk; Client.WinForms | Pending bền vững, retry và UI quầy | Gia Quân; Nguyên Chương |

Các tên file chi tiết bên dưới là **file dự kiến cần tạo/hoàn thiện**, không phải tuyên bố chúng đã tồn tại. Contracts là phần dùng chung: Gia Quân điều phối thay đổi, cả bên gửi/nhận duyệt DTO và fixture trước merge. Huy điều phối thứ tự tích hợp, không thay chủ module viết toàn bộ test. Người sửa `src/De6.Server.Host/`, `config/`, `scripts/` hoặc CI phải báo Huy trong PR để tránh sửa chồng.


### Lý thuyết gắn với code

Mỗi người viết và bảo vệ phần lý thuyết của mình, đồng thời trực tiếp cài code/test tương ứng. Mỗi phần phải có giả thiết, các bước thuật toán, bất biến, giới hạn, trace message và demo trên module do chính người đó phụ trách. Cả năm cùng hiểu ba cơ chế: coordinator tập trung, Bully và primary-backup đồng bộ; không chia một người chỉ viết báo cáo.

Nguồn sách: ThS. Nguyễn Xuân Anh, *Bài giảng Hệ thống phân tán*, PTIT, 12/2014. Các mục/trang dưới đây đã đối chiếu bản trích xuất được nhóm lưu; số trang là **trang in trong sách**, không phải số trang của trình đọc PDF. Sách hỗ trợ nền tảng; giao thức và giới hạn triển khai cụ thể theo V12. Đặc biệt, mục 2.6.6 là Phục hồi; mục 2.6.5 là Cam kết phân tán, không được thay nhầm.

| Người | Phần lý thuyết/sách | Code trực tiếp | Trace và demo bảo vệ |
| --- | --- | --- | --- |
| Huy | ACID, tương tranh; 2.5, 2.5.1, 2.5.3 (tr. 74–77); ND11 | SqlBookingStore, Apply và oracle | Transaction không nửa Apply; crash/commit không rõ: T13, T19 |
| Gia Quân | Loại trừ tập trung; 2.3.3.1 (tr. 56–57); ND7 | CommandCoordinator, grant/release, domain | Request → Grant → Release, ba quầy cùng ghế: T02, T10, T29 |
| Đức An | Bully; 2.3.5.1 (tr. 59); ND7 | Election, AuthorityGate, manifest | ELECTION → ANSWER → COORDINATOR; không fence không promote: T22, T24–T25 |
| Nguyên Chương | Primary-backup 2.8.5.2 (tr. 106–107), phục hồi 2.6.6 (tr. 88); ND14/ND12 | Replication, TCP, replay | APPEND_APPLY → durable Apply → ACK; mất ACK/rejoin: T11, T20, T28 |
| Văn Khoa | RPC 2.1.2 (tr. 25–29), định danh 2.2.1 (tr. 34), khách/chủ tin cậy 2.6.3 (tr. 82–84); ND2/ND12 | SDK, pending, reconnect, WinForms | Persist → send → UNKNOWN → GET_RESULT/retry cùng identity: T17, T34 |

## 2. Huy — SQL/Persistence và điều phối tích hợp

**Phạm vi sở hữu:** `src/De6.Booking.Persistence/`; file dự kiến `SqlBookingStore.cs`, `SqlApplyTransaction.cs`, `SqlSnapshotReader.cs`; `database/schema/`, `database/migrations/`, `database/seed/`, `database/oracle/`. Huy điều phối wiring tại `src/De6.Server.Host/`, cấu hình SQL, `scripts/`, `.github/workflows/` và tiến độ G0–G5. Nhánh: `feature/huy-database`.

| Đầu vào | Đầu ra chính xác |
| --- | --- |
| CommandIdentity, AcceptedAuthority, expected next index; Decision/bytes theo Contracts | `DecideAndApplyAsync`: PersistedEvent hoặc ExistingResult, cùng một transaction |
| EventBytes và AuthenticatedPeerAuthority; replay có RecoveryContext riêng | `AppendAndApplyAsync`/`ApplyReplayEntryAsync`: AppliedReceipt hoặc GAP/CONFLICT; không tự sinh GUID/thời gian |
| expected AppliedIndex, loại đọc và owner | `ReadStableSnapshotAsync`: dữ liệu và AppliedIndex từ cùng snapshot SQL |
| Log, DatasetId/NodeId và schema version | Migration/seed có kiểm soát, rebuild projection, SQL oracle và chứng cứ durable commit |

### Tuần 1 Làm H1 đến H4

#### H1 Tạo schema và seed có thể chạy lại an toàn

**Làm:** đọc mục 9 V12, tạo migration có phiên bản trong `database/migrations/` cho đủ bảy bảng đã nêu. Viết seed ở `database/seed/`: TripId=1, ghế 1–10, AVAILABLE, chưa có reservation/ticket. DatasetId dùng chung, NodeId của từng DB khác nhau. Kiểm tra khóa ngoại ghép, UNIQUE một vé/ghế và một giữ HELD/ghế; không trông chờ CHECK đơn bảng kiểm tra mọi quan hệ xuyên bảng.

**Tự kiểm và bàn giao:** chạy trên DB trống; chạy lại đúng version không tạo thêm ghế; DB khác DatasetId phải báo lỗi, không xóa dữ liệu. Giao Gia Quân sơ đồ bảng, tên cột/kiểu/nullability và script seed để cùng chốt G0. Ghi cách cấp connection string cục bộ ngoài Git.

#### H2 Cài một lần Apply nguyên tử

**Làm:** tạo `SqlBookingStore.cs` và `SqlApplyTransaction.cs`. `DecideAndApplyAsync` kiểm tra authority, index và RequestId trong transaction; dùng decision engine của Gia Quân trên trạng thái đã đọc trong chính transaction đó. Tạo event/ID một lần, lưu projection, log, result, LastAppliedIndex và HeadHash cùng một commit fully durable. Không chờ ACK hoặc gọi mạng khi transaction đang mở.

**Tự kiểm và bàn giao:** HOLD ghế 1 tạo đúng một reservation và một event/result; chèn lỗi trước commit thì không bảng nào giữ phần thay đổi dở. NO_CHANGE vẫn ghi event/result, không đổi VersionIndex của ghế. Local commit chỉ trả PersistedEvent cho coordinator, không tự trả FINAL cho client.

#### H3 Dùng chung lõi Apply cho backup và replay

**Làm:** bổ sung `AppendAndApplyAsync` và `ApplyReplayEntryAsync`: chung phần kiểm tra prefix/before-version và ghi transaction, nhưng kiểm tra quyền theo hai đường riêng. Append mới cần leader/epoch hiện hành; replay cần RecoveryContext đã được Control cấp trên node đóng gate. Giữ nguyên GUID, timestamp, ResultPayload và bytes do primary tạo.

**Tự kiểm và bàn giao:** cùng event gửi hai lần chỉ có một log/result; khác event ở cùng index phải conflict; thiếu index phải GAP. Replay lịch sử epoch cũ bằng context hợp lệ được phép, còn APPEND giả quyền phải bị từ chối. Giao Nguyên Chương receipt chỉ sau commit và mã lỗi rõ ràng.

#### H4 Cài đọc snapshot và xử lý commit chưa rõ

**Làm:** tạo `SqlSnapshotReader.cs`; đọc dữ liệu cùng AppliedIndex từ một snapshot SQL, lọc kết quả/reservation theo OwnerId đã xác thực. Coordinator kiểm tra Stable trước khi gọi và truyền expected index; sai mốc phải từ chối hoặc đọc lại theo luồng đã chốt, không ghép mốc cũ với dữ liệu mới. Thêm tra cứu log/result theo RequestId khi SQL báo lỗi sau commit.

**Tự kiểm và bàn giao:** inject lỗi sau commit nhưng trước return: đóng gate qua Control, xác minh log trước khi tiếp tục; không cấp EventId thứ hai. Kiểm tra T19 và T29 cùng Gia Quân. Chọn và ghi rõ isolation phù hợp, không dùng NOLOCK cho snapshot có thẩm quyền.

### Tuần 2 Làm H5 và H6

#### H5 Viết oracle và đối soát dữ liệu

**Làm:** trong `database/oracle/`, tạo truy vấn đếm ghế/vé, tìm ghế có nhiều HELD/ticket, kiểm tra SOLD khớp CONFIRMED và kiểm tra log/result/index. Phối hợp serializer chung để tính digest sắp theo khóa tại mốc K đang freeze. Viết rebuild projection từ seed và log hiện hành, giữ NodeId và thông tin riêng của máy.

**Tự kiểm và bàn giao:** sau HOLD → CONFIRM, oracle thấy một vé và cùng reservation/chủ; sau duplicate số hàng không tăng. SQL oracle kiểm tra dữ liệu cục bộ; I07/I10 về hai bản sao/quyền writer còn cần log và test nhiều node, không chứng minh được bằng một truy vấn SQL. Giao Đức An/Nguyên Chương PrefixReport và digest tại K.

#### H6 Ghép Host và bằng chứng tích hợp

**Làm:** nối các implementation vào `src/De6.Server.Host/` theo cấu hình, không hard-code một DB cho ba server. Ghi lệnh migration/seed và chạy server trong README. Khi thêm package SQL/test thật, cập nhật nguồn NuGet có chủ đích; `nuget-template.config` hiện chỉ phục vụ bộ khung không package.

**Tự kiểm và bàn giao:** PR của Huy phải có code Persistence, test nguyên tử/duplicate/SQL failure và output oracle. Khi ghép từng cổng, thêm lệnh test đã có vào script/CI cùng chủ test; không chỉ gom ảnh của người khác. Ghi rõ G1 chưa phải nghiệm thu FINAL; phối hợp G2 trên hai DB độc lập rồi G3 ba máy.

**Tình huống phải tự giải thích:** SQL đã commit HOLD nhưng không nhận ACK backup. Dữ liệu cục bộ có thể đã HELD; không trả FINAL, không cho đọc trạng thái đó, không reset DB để làm lại. Giao event đang chờ cho coordinator/replication và giữ chứng cứ.

**File báo cáo lý thuyết và liên hệ code:** [Phần của Huy](../report/01_huy_giao_tac.md). Thành viên tự viết phần này, dẫn PR/code/test của mình.

**Lý thuyết và phần bảo vệ:** ND11 slide 2–4, 6–8; sách mục 2.5 “Quản trị giao tác và điều khiển tương tranh” (tr. 74), 2.5.1 “Các giao tác” (tr. 75–76) và 2.5.3 “Các khóa” (tr. 76–77). Huy giải thích ACID, xung đột đọc/ghi và ranh giới transaction Apply; liên hệ I02–I06 với SQL constraints/isolation. Nêu giả thiết fully durable commit, các bước begin → kiểm tra → ghi log/result/projection/NodeState → commit và giới hạn: commit SQL cục bộ chưa đủ FINAL toàn cụm. Bảo vệ bằng trace transaction cùng APPEND_APPLY/ACK liên quan và demo T13/T19, chỉ ra oracle không có nửa Apply và cách xử lý outcome chưa rõ.

**Nghiệm thu:** chủ trì T01, T13, T19; đồng thực hiện T03–T09, T14–T16, T18, T20–T21, T25, T28–T30, T35. Xem oracle chi tiết trong [ma trận](../testing/TEST_MATRIX.md).

**Phụ thuộc/reviewer:** cần Contracts/Decision từ Gia Quân; authority/recovery context từ Đức An; ACK/replay từ Nguyên Chương. Gia Quân review tính nguyên tử/dedup; Đức An review metadata/recovery. Huy không tự duyệt phần tích hợp mình viết.

**Hoàn tất khi:** migration/seed chạy trên ba DB độc lập, oracle kiểm tra được toàn bộ quan hệ và prefix; không có nửa Apply, duplicate không ghi thêm, SQL ambiguous commit không trả FINAL sai. PR có lệnh chạy và record đúng các test được tuyên bố; build/smoke qua và reviewer chấp thuận.

## 3. Gia Quân — Domain/Coordinator và hợp đồng chung

**Phạm vi sở hữu:** `src/De6.Booking.Domain/`; file dự kiến `BookingDecisionEngine.cs`, `CommandCoordinator.cs`, `CommandIdentityValidator.cs`; điều phối `src/De6.Contracts/` cho DTO, enum, canonical encoder và fixture. Test tại `tests/unit/` và `tests/integration/` cho domain/coordinator. Nhánh: `feature/gia-quan-domain`.

| Đầu vào | Đầu ra chính xác |
| --- | --- |
| AuthenticatedCommand với OwnerId từ phiên; snapshot Stable; quyền hiện hành | `ICommandCoordinator.SubmitAsync`: CommandReply tách TransportStatus/BusinessResult |
| HOLD/CONFIRM/CANCEL, TripId/SeatNo/ReservationId, trạng thái ghế/reservation | Decision HELD_CREATED/SOLD_CONFIRMED/HOLD_CANCELLED/NO_CHANGE; BusinessResult v1 cố định |
| CommandIdentity và dữ liệu sự kiện | Canonical command/event/result bytes, CommandHash/EventHash fixtures dùng chung |
| LocalApplied và MatchingReceipt từ store/replication | StableIndex chỉ tăng khi receipt khớp; FINAL chỉ trong giới hạn Stable |

### Tuần 1 Làm Q1 đến Q4 và ghép sớm Q5

#### Q1 Chốt Contracts và fixture dùng chung

**Làm:** mở `src/De6.Contracts/ProtocolContracts.cs`; các record hiện là bản nháp, chưa đủ serializer/validation/API V12. Chốt với bốn bạn còn lại tên field, null, mã lỗi, authority và receipt trước khi tạo encoder. Tạo canonical encoder theo mục 7–8 V12; không dùng JSON tùy thứ tự hoặc culture máy.

**Tự kiểm và bàn giao:** một bộ input cố định phải cho cùng CommandHash, PayloadBytes, EventHash và ResultPayload trên mọi module. HOLD dùng dấu `-` ở field reservation của chuỗi hash; RequestId, IP và epoch không nằm trong CommandHash. Giao fixture byte-for-byte và mã kết quả cho Huy, Nguyên Chương, Văn Khoa; họ chạy cùng fixture trước merge.

#### Q2 Cài decision engine theo bảng nghiệp vụ

**Làm:** tạo `BookingDecisionEngine.cs`; nhận trạng thái ghế/reservation và command đã xác thực, trả decision có cấu trúc cùng BusinessResult. Cài đủ HOLD, CONFIRM, CANCEL, sai chủ, sai ghế, reservation thiếu/đã hủy/đã bán. OwnerId lấy từ phiên; không tin owner trong payload. Không tự hết hạn HELD và không hủy ticket SOLD.

**Tự kiểm và bàn giao:** viết test mỗi dòng bảng mục 7 V12. HOLD AVAILABLE → OK_HELD; CONFIRM đúng reservation → OK_SOLD; CONFIRM lại bằng RequestId mới → ALREADY_CONFIRMED kèm TicketId cũ. Các từ chối không làm thay đổi ghế và không lộ dữ liệu quầy khác. Giao Huy decision để áp dụng trong cùng SQL transaction.

#### Q3 Cài identity và dedup đúng ranh giới

**Làm:** tạo `CommandIdentityValidator.cs` và nối tra RequestResults trong store. Cùng DatasetId/RequestId/CommandHash thì dùng nguyên kết quả đã lưu khi event đã Stable; khác nội dung/chủ thì REQUEST_CONFLICT, giữ bản gốc. Lỗi nghiệp vụ hợp lệ như SEAT_BUSY phải được lưu/nhân bản bằng NO_CHANGE; lỗi frame/login/input bị từ chối trước nhận lệnh.

**Tự kiểm và bàn giao:** gửi đồng thời 5 lần cùng identity chỉ có một event/result; đổi SeatNo với cùng RequestId phải conflict. Nếu event còn LocalApplied chưa Stable, không dùng kết quả cục bộ để trả FINAL cho retry. Giao Văn Khoa cách phân biệt lỗi giao vận và kết quả nghiệp vụ.

#### Q4 Cài coordinator và hàng đợi

**Làm:** tạo `CommandCoordinator.cs`; một worker cấp grant, xử lý tuần tự cả mutation và đọc có thẩm quyền. Queue giới hạn 256; đầy thì RETRY_LATER trước khi bắt đầu. Grant của mutation giữ đến Stable hoặc cụm bị đóng gate; không nhận mutation kế trên projection chưa Stable. Đồng bộ việc grant/freeze với AuthorityGate của Đức An.

**Tự kiểm và bàn giao:** ba quầy HOLD ghế 1 bằng ba RequestId khác nhau chỉ một OK_HELD. Chặn replication của lệnh đầu: lệnh tiếp không được quyết định và GET_SEATS không thấy projection chưa Stable. ACK/heartbeat phải đi đường I/O riêng, không xếp vào worker đang chờ chính chúng.

### Tuần 2 Hoàn thiện Q5 và Q6

#### Q5 Ghép Apply rồi replica rồi FINAL

**Làm:** sau PersistedEvent của Huy, gọi `EnsureOneBackupAsync` của Nguyên Chương bằng đúng event. Chỉ MatchingReceipt hợp lệ mới tăng StableIndex và cho FINAL; không dùng timer ngủ hoặc trạng thái socket thay ACK. Hết hạn chưa đủ bằng chứng: UNKNOWN và BLOCKED_UNCERTAIN; tiếp tục đối soát event đó qua luồng Control.

**Tự kiểm và bàn giao:** test receipt sai EventId/hash/epoch không tăng StableIndex; một backup mất ACK nhưng backup kia hợp lệ vẫn có thể hoàn tất. Socket client mất sau bắt đầu không hủy SQL/replication; CancellationToken của client chỉ hủy chờ trả lời. Giao trace grant → LocalApplied → ACK → Stable → FINAL → release.

#### Q6 Cài đọc có thẩm quyền và test tổng hợp

**Làm:** ghép GET_SEATS, GET_RESULT, GET_MY_RESERVATIONS qua cùng coordinator. Khi chưa Stable, đợi có giới hạn rồi CLUSTER_NOT_READY; không đọc trực tiếp backup. Chỉ dùng snapshot/mốc của Huy; GET_RESULT chỉ trả cho đúng owner và FINAL trong tiền tố Stable. Test engine riêng trước, sau đó ghép SQL và hai node thật.

**Tự kiểm và bàn giao:** PR có test bảng nghiệp vụ, tương tranh, dedup, gate đọc và receipt sai; ghi trạng thái T02–T10/T29 theo bằng chứng thật. Mock hỗ trợ unit test nhưng không thay G2. Khi đổi Contracts phải cùng cập nhật fixture, bên dùng và mô tả giao thức.

**Tình huống phải tự giải thích:** A nhận SEAT_BUSY đã FINAL với RequestId R. Sau đó B hủy giữ ghế. A retry R vẫn phải nhận nguyên SEAT_BUSY; muốn giữ ghế bằng một thao tác mới sau khi R đã hoàn tất thì tạo RequestId mới. Không chạy lại nghiệp vụ cho retry để đổi kết quả cũ.

**File báo cáo lý thuyết và liên hệ code:** [Phần của Gia Quân](../report/02_gia_quan_loai_tru.md). Thành viên tự viết phần này, dẫn PR/code/test của mình.

**Lý thuyết và phần bảo vệ:** ND7 slide 2–3; sách mục 2.3.3.1 “Giải thuật tập trung” (tr. 56–57). Gia Quân giải thích giả thiết một coordinator hợp lệ, hàng đợi và Request → Grant → Release; ánh xạ grant/release vào worker của Domain, không suy một mutex cục bộ là fencing cả cụm. Nêu I08/I11, thứ tự quyết định trên trạng thái Stable, điểm giữ quyền khi chờ replication và giới hạn nghẽn/coordinator hỏng. Bảo vệ bằng trace tương tranh T02, LocalApplied → chờ ACK → Stable → FINAL ở T10/T29; chứng minh ACK I/O không bị xếp sau worker đang chờ nó.

**Nghiệm thu:** chủ trì T02–T10, T29; phối hợp T01, T11–T12, T19, T27, T30, T32–T33.

**Phụ thuộc/reviewer:** cần store nguyên tử của Huy, replica receipt của Nguyên Chương và authority gate của Đức An; cung cấp command/result cho Văn Khoa. Huy review transaction/dedup; Nguyên Chương review canonical bytes và đường ACK.

**Hoàn tất khi:** 10 ghế tuân I01–I05; một mutation chưa Stable; đọc đúng snapshot; retry không đổi result; không có FINAL trước primary+backup durable; cancellation chỉ bỏ chờ phản hồi. Có fixture chung, test và reviewer xác nhận.

## 4. Đức An — Bully và Cluster.Control

**Phạm vi sở hữu:** `src/De6.Cluster.Election/`, `src/De6.Cluster.Control/`; file dự kiến `BullyElectionService.cs`, `AuthorityGate.cs`, `RecoveryService.cs`, `RecoveryManifestStore.cs`; hướng dẫn fencing/promotion và test tại `tests/integration/`, `tests/fault-injection/`. Nhánh: `feature/duc-an-election`.

| Đầu vào | Đầu ra chính xác |
| --- | --- |
| Thành viên 1/2/3, heartbeat, ELECTION/ANSWER/COORDINATOR đã xác thực | Candidate và trace Bully; timeout chỉ tạo nghi ngờ, không tự cấp quyền ghi |
| Hai participant đã freeze và RunId | `CompareAsync`: PrefixReport hoặc Conflict, không chọn DB theo NodeId |
| Manifest, bằng chứng fence và quyền admin | `PromoteAsync`: Activated hoặc Blocked sau ghi/readback cả hai participant |
| Node RECOVERING, nguồn READY và RepairPlan khi cần | `RejoinAsync`: VerifiedAtK hoặc Blocked; authority gate thống nhất với coordinator |

### Tuần 1 Làm A1 đến A3

#### A1 Cài một cổng quyền phục vụ duy nhất

**Làm:** tạo `AuthorityGate.cs` trong Cluster.Control; tách ServingState/AcceptedLeader khỏi ElectionCandidateId. Mọi boot là RECOVERING, ServingEnabled=false, StableIndex chưa được tin. Freeze/promotion và grant coordinator dùng chung state machine hoặc đồng bộ tương đương để không mở quyền giữa lúc đối soát.

**Tự kiểm và bàn giao:** unit test boot/restart luôn đóng; candidate không gọi được nghiệp vụ ghi; BACKUP_READY không trả đọc cho quầy. Giao Gia Quân API kiểm tra/cấp grant/freeze, và Huy API đóng gate khi STORAGE_FAULT. Không cho Host hoặc UI tự gán PRIMARY_READY để bỏ qua Control.

#### A2 Cài Bully và timer có thể kiểm thử

**Làm:** tạo `BullyElectionService.cs`; node đủ điều kiện gửi ELECTION lên ID cao hơn, xử lý ANSWER, chờ COORDINATOR và bầu lại khi hết hạn. Dùng ElectionRound với GUID, khử lặp và bỏ epoch cũ. Theo V12: heartbeat 1 giây, nghi ngờ sau 3 lần thiếu; Answer 2 giây, Coordinator 5 giây, backoff 0–500 ms.

**Tự kiểm và bàn giao:** test bằng đồng hồ điều khiển được rồi chạy peer thật: mất ANSWER, mất COORDINATOR, hai node cùng bầu. RECOVERING không được trả ANSWER như ứng viên hợp lệ. Kết quả chỉ CANDIDATE; không ghi đè AcceptedLeader của primary đang khỏe và không tự mở bán.

#### A3 Viết đường quản trị và fencing thực

**Làm:** thiết kế lệnh quản trị riêng được xác thực, không cho cổng client gọi promotion. Tạo runbook chỉ rõ primary cũ nào cần dừng, cách dừng hẳn Host/ngăn tự khởi động hoặc tắt máy, và bằng chứng người vận hành phải ghi. Không coi mất ping hay tăng epoch là fencing.

**Tự kiểm và bàn giao:** mô phỏng chỉ mất heartbeat trong khi primary vẫn chạy: node mới giữ candidate/BLOCKED, không writer thứ hai. Thiếu quyền admin, thiếu fence hoặc thiếu hai participant hợp lệ phải từ chối. PR có code kiểm tra điều kiện và runbook, không chỉ mô tả thao tác tay.

### Tuần 2 Làm A4 đến A6

#### A4 Cài compare và promotion manifest

**Làm:** tạo `RecoveryService.cs` và `RecoveryManifestStore.cs`; theo đúng 8 bước mục 12 V12. Đóng gate, cố định một RunId, kiểm tra hai participant cùng dataset/schema/quyền cũ, đọc chain và projection/result. Chỉ chọn lịch sử dài hơn khi hai log tương thích tiền tố; replay tới cùng K/hash/digest, rồi ghi quyền mới/manifest bền vững ở cả hai.

**Tự kiểm và bàn giao:** candidate stale phải nhận dữ liệu từ survivor mới hơn; cùng index khác hash thì BLOCKED, không chọn theo ID/độ dài. Chỉ sau đọc lại hai bản cùng manifest mới đặt StableIndex=K và mở một PRIMARY_READY. Giao Nguyên Chương RecoveryContext đã được cài hợp lệ, không nhận RunId tự khai từ peer.

#### A5 Cài tiếp tục manifest và rejoin

**Làm:** nếu crash khi mới ghi một participant, boot vẫn đóng gate và chỉ tiếp tục đúng RunId/CandidateId/NewEpoch/K/hash đó; không tạo promotion cạnh tranh. Node cũ về RECOVERING: freeze nguồn tại K, dùng replay được ủy quyền; đuôi xung đột cần RepairPlan và archive trước rebuild. Không copy NodeId/credentials/private key hoặc quyền phục vụ.

**Tự kiểm và bàn giao:** crash ở từng bước manifest đều không tự mở quyền. Một participant mất trong run thì giữ BLOCKED. Rejoin xong phải kiểm tra hash/digest và quyền hiện hành trước BACKUP_READY; không tự trả writer về S3 chỉ vì S3 có ID lớn nhất.

#### A6 Ghép fault test và chứng cứ chuyển quyền

**Làm:** phối hợp mọi chủ module tạo kịch bản T22–T26/T33/T36, thêm test rejoin/stale candidate và restart cụm. Mỗi lần chuyển quyền ghi OldEpoch/NewEpoch, candidate, hai participant, RunId, K/hash/digest và bằng chứng fence; không ghi secret.

**Tự kiểm và bàn giao:** PR có code Election/Control, test timer/state machine và trace promotion thật. Runbook phải có cả điều kiện tiếp tục lẫn điều kiện dừng; người khác làm lại được mà không sửa DB tay. Các test lỗi SQL/replication/client do chủ phần đó cùng chạy, Đức An không phải viết toàn bộ kiểm thử thay nhóm.

**Tình huống phải tự giải thích:** S3 đang primary, mạng từ S1/S2 tới S3 bị đứt nhưng S3 còn chạy. S2 thắng Bully ở phía hai node chỉ là ứng viên. Chưa có fencing thực và đối soát hai participant thì không mở S2 làm writer.

**File báo cáo lý thuyết và liên hệ code:** [Phần của Đức An](../report/03_duc_an_bully.md). Thành viên tự viết phần này, dẫn PR/code/test của mình.

**Lý thuyết và phần bảo vệ:** ND7 slide 9–10; sách mục 2.3.5.1 “Các giải thuật bầu chọn truyền thống”, phần Bully (tr. 59). Đức An nêu giả thiết ID duy nhất, biết thành viên/địa chỉ và timeout chỉ gây nghi ngờ; mô tả gửi ELECTION lên ID cao hơn, nhận ANSWER, bầu lại/thông báo COORDINATOR. Phân biệt candidate Bully với quyền writer V12: I10/I12 còn cần fence, hai participant và manifest/readback. Bảo vệ trace hai node bầu đồng thời T24, mất heartbeat nhưng primary còn sống T22 và crash manifest T25; chỉ ra giới hạn Bully khi phân vùng và vì sao không tự promote.

**Nghiệm thu:** chủ trì T22–T26, T33, T36; phối hợp T18, T20–T21, T25, T27–T30, T35.

**Phụ thuộc/reviewer:** cần durable metadata/oracle của Huy, compare/replay và peer transport của Nguyên Chương, grant coordinator của Gia Quân. Nguyên Chương review election/transport; Huy review manifest/dữ liệu và điều kiện activation.

**Hoàn tất khi:** Bully chỉ chọn candidate; không promote bằng timeout, epoch hoặc ID; promotion có fence và hai bản dữ liệu/metadata khớp; restart đóng gate; mọi nhánh BLOCKED được test và log. Reviewer xác nhận trace và runbook thực thi được.

## 5. Nguyên Chương — Replication và Transport.Tcp

**Phạm vi sở hữu:** `src/De6.Cluster.Replication/`, `src/De6.Transport.Tcp/`; file dự kiến `ReplicaSender.cs`, `ReplicaReceiver.cs`, `ReplayService.cs`, `LengthPrefixedConnection.cs`, `TlsConnectionFactory.cs`; fixture giao thức và fault hooks trong `tests/`. Nhánh: `feature/nguyen-chuong-replication`.

| Đầu vào | Đầu ra chính xác |
| --- | --- |
| PersistedEvent nguyên bytes và replication deadline | `EnsureOneBackupAsync`: MatchingReceipt hoặc Uncertain; không tạo lại event |
| APPEND_APPLY từ peer đã xác thực | ACK chỉ sau durable Apply; receipt khớp node/authority/event/index/hash |
| Prefix report, mốc replay và RecoveryContext hợp lệ | Chuỗi entry nguyên bytes; catch-up/VerifiedAtK phối hợp Control |
| TCP byte stream/TLS session và envelope v1 | Frame length-prefix/JSON đúng giới hạn; lỗi frame đóng kết nối, không sửa nghiệp vụ |

### Tuần 1 Làm C1 đến C4

#### C1 Cài framing và kết nối TCP độc lập nghiệp vụ

**Làm:** tạo `LengthPrefixedConnection.cs`: 4 byte UInt32 big-endian N rồi N byte JSON UTF-8, 1 ≤ N ≤ 1 MiB. ReadExactly phải đọc đủ số byte hoặc báo EOF; một socket chỉ có một writer tuần tự. Chốt envelope v1, correlation và giới hạn queue với Gia Quân; ACK/heartbeat không đi qua worker nghiệp vụ đang chờ.

**Tự kiểm và bàn giao:** test một frame chia thành nhiều lần đọc, hai frame ghép một lần đọc, EOF giữa header/body, N=0 và N vượt giới hạn. Chỉ lỗi kết nối/protocol, không tự sinh kết quả bán vé. Giao Văn Khoa API gửi/nhận bất đồng bộ và cách thông báo disconnect.

#### C2 Cài TLS và phân quyền kết nối

**Làm:** tạo `TlsConnectionFactory.cs`; client xác minh server theo trust/pin cấu hình demo; peer certificate ánh xạ đúng NodeId đã cấu hình. Không dùng callback luôn trả true. Tách handler client, peer và admin, không cho một field SenderId/OwnerId trong JSON tự tạo quyền.

**Tự kiểm và bàn giao:** chứng thư không hợp lệ, giả NodeId hoặc quầy gọi APPEND/promotion phải bị từ chối trước Apply. Provision credential/certificate theo hướng dẫn ngoài Git; không commit private key hoặc mật khẩu. Giao Đức An peer identity đã xác thực, giao Văn Khoa lỗi TLS rõ ràng.

#### C3 Cài receiver và ACK sau durable commit

**Làm:** tạo `ReplicaReceiver.cs`; kiểm tra dataset/schema/protocol/quyền peer trước gọi `AppendAndApplyAsync` của Huy. Chỉ tạo ACK_APPLIED từ receipt sau commit. So index, previous hash, before-version, EventId/hash/payload; duplicate giống hệt ACK lại, GAP báo mốc cần, conflict đóng gate/cách ly.

**Tự kiểm và bàn giao:** chèn dừng trước commit không được có ACK thành công; gửi lại đúng event không thêm hàng. Khác bytes tại cùng index không được overwrite. Lưu và truyền nguyên PayloadBytes/ResultPayload, không parse rồi serialize lại bằng tùy chọn riêng.

#### C4 Cài sender đủ một backup hợp lệ

**Làm:** tạo `ReplicaSender.cs`, triển khai `EnsureOneBackupAsync` nhận PersistedEvent đã lưu. Gửi đúng event tới các backup hợp lệ, kiểm tra receipt đủ NodeId/AuthorityEpoch/EventEpoch/LogIndex/EventId/EventHash. Dùng deadline replication 5 giây mặc định V12, ghi rõ giá trị khi test; không tạo event thay thế khi đổi backup.

**Tự kiểm và bàn giao:** backup thứ nhất mất ACK, backup thứ hai ACK đúng thì trả MatchingReceipt. ACK sai hash/epoch/event hoặc từ node ngoài membership không đạt. Hết deadline chưa đủ bằng chứng trả Uncertain cho coordinator, không tự gọi FINAL hoặc tự bỏ event đã LocalApplied.

### Tuần 2 Làm C5 và C6

#### C5 Cài catch up và replay được cấp quyền

**Làm:** tạo `ReplayService.cs`; khi GAP, đồng bộ prefix theo đúng mốc. Recovery dùng REPLAY_ENTRY với RecoveryContext đã được Control cài trên node freeze/RECOVERING; giữ epoch/writer/id/hash lịch sử. Nhận/kiểm tra dữ liệu mạng ngoài transaction; từng Apply cục bộ có transaction, không giữ SQL lock để đợi TCP.

**Tự kiểm và bàn giao:** replay thiếu context/sai source/sai RunId/mốc phải từ chối. Tại K đã freeze, cả nguồn/đích có cùng HeadHash và digest dữ liệu/result; K cũ không đọc trực tiếp từ projection hiện tại nếu chưa rebuild tới K. Conflict của survivor trong promotion phải dừng; chỉ repair node rejoin bằng kế hoạch admin.

#### C6 Thêm fault hook và trace nhiều node

**Làm:** tạo điểm lỗi có cấu hình cho trước/sau send, sau backup commit trước ACK, frame/socket và replay; chỉ bật trong môi trường test. Log correlation, RequestId, EventId, index/hash/epoch và mốc durable; không log credential. Phối hợp hai rồi ba server với Gia Quân/Huy.

**Tự kiểm và bàn giao:** PR có test framing, duplicate/GAP/conflict, mất một ACK và ACK giả/cũ. Giao trace đủ chứng minh LocalApplied → backup durable → ACK → Stable/FINAL, kèm record các T được tuyên bố. Test bằng fake store chỉ kiểm tra luồng; durable acceptance cần SQL thật.

**Tình huống phải tự giải thích:** backup đã lưu event nhưng ACK bị mất. Primary gửi lại đúng bytes/identity của event. Backup nhận ra duplicate, không Apply lần hai, rồi ACK lại; không tạo một event mới để né timeout.

**File báo cáo lý thuyết và liên hệ code:** [Phần của Nguyên Chương](../report/04_nguyen_chuong_nhan_ban.md). Thành viên tự viết phần này, dẫn PR/code/test của mình.

**Lý thuyết và phần bảo vệ:** ND14 slide 5–7, 13, 15; ND12 slide 2–3, 6, 8–9, 11–14. Sách mục 2.8.5.2 “Các giao thức dựa trên bản chính” (tr. 106–107) và 2.6.6 “Phục hồi” (tr. 88, gồm giới thiệu/điểm kiểm tra). Nguyên Chương giải thích một thứ tự ghi, primary-backup đồng bộ và phục hồi từ log/prefix; liên hệ I07/I09/I12. Trace phải tách local Apply, APPEND_APPLY, backup durable Apply, ACK và Stable/FINAL; demo T11/T20/T28 cho duplicate/GAP/replay. Nêu giả thiết một lỗi từ ba bản đồng bộ, giới hạn dừng khi conflict/thiếu chứng cứ; không gọi V12 là thuật toán đồng thuận hay dùng checkpoint trong sách để tuyên bố code đã có snapshot recovery.

**Nghiệm thu:** chủ trì T11–T12, T15, T20–T21, T27–T28, T30–T31; phối hợp T10, T13–T19, T23–T24, T32–T33.

**Phụ thuộc/reviewer:** Huy cung cấp Apply/receipt durable, Gia Quân cung cấp canonical/event và deadline, Đức An cấp authority/recovery context; Văn Khoa dùng TCP API. Đức An review quyền/peer/replay; Huy review thời điểm ACK và durable evidence.

**Hoàn tất khi:** giữ nguyên bytes trên mạng và đĩa; ACK đúng event sau commit; duplicate không Apply lần hai; GAP replay đúng; conflict không âm thầm sửa lịch sử; I/O không kẹt coordinator. Có test framing, fault evidence và reviewer duyệt.

## 6. Văn Khoa — Client.Sdk và WinForms

**Phạm vi sở hữu:** `src/De6.Client.Sdk/`, `src/De6.Client.WinForms/`; file dự kiến `ClientPendingStore.cs`, `BookingClient.cs`, `EndpointRouter.cs`, `MainForm.cs`; cấu hình `config/local/c1.json`–`c3.json` và mẫu client LAN phối hợp Huy; kịch bản demo/test client. Nhánh: `feature/van-khoa-client`.

| Đầu vào | Đầu ra chính xác |
| --- | --- |
| DatasetId, OwnerId từ phiên, RequestId và canonical command | Pending bền vững trước gửi: atomic replace + flush, file lock riêng một tiến trình |
| Ba endpoint, trạng thái PRIMARY_READY, TLS identity | Kết nối primary hợp lệ; hint chỉ định tuyến, không cấp quyền |
| FINAL/UNKNOWN/NOT_FOUND và kết quả GET_RESULT | Resolve cùng identity/ResultPayload; không cấp RequestId mới khi timeout/reconnect |
| Snapshot có AppliedIndex và reservation của chủ quầy | 10 ghế có nhãn AVAILABLE/HELD/SOLD; pending/UNKNOWN tách trạng thái xác nhận |

### Tuần 1 Làm K1 đến K3 và ghép UI cơ bản của K5

#### K1 Cài SDK trước khi mở thao tác trên UI

**Làm:** tạo `BookingClient.cs`, tách transport/routing/pending khỏi Form. Nhận DTO/result từ Gia Quân và API TCP/TLS của Nguyên Chương. Chốt cách LOGIN tạo owner từ phiên; quầy không gửi owner tùy ý để chiếm reservation. Không đưa SQL connection string hoặc truy vấn SQL vào SDK/UI.

**Tự kiểm và bàn giao:** test SDK với transport giả có điều khiển để kiểm tra các nhánh phản hồi; Form vẫn disabled nếu backend/gate chưa sẵn sàng. Mock không tạo bằng chứng hệ thống đã bán vé. Giao Gia Quân danh sách thông báo người dùng cho FINAL, UNKNOWN, CLUSTER_NOT_READY và lỗi validation.

#### K2 Cài pending bền vững trước lần gửi đầu

**Làm:** tạo `ClientPendingStore.cs`; một store riêng theo client/dataset/owner, giữ khóa độc quyền cho một tiến trình. Ghi RequestId và canonical command bằng file tạm, flush, atomic replace trước khi send; xử lý crash từng mốc. Nếu lưu thất bại, báo chưa gửi và không gửi qua mạng. Khi nhận FINAL, lưu kết quả bền vững trước khi xóa/đánh dấu pending đã giải quyết.

**Tự kiểm và bàn giao:** kill client sau ghi trước send, sau send trước reply, sau nhận reply trước lưu; restart phải còn đủ identity để resolve an toàn. Store cũ khác dataset/owner hoặc instance thứ hai dùng chung store phải bị từ chối rõ ràng. Không tự xóa pending lỗi để làm UI trống.

#### K3 Cài router ba endpoint và đăng nhập lại

**Làm:** tạo `EndpointRouter.cs`; thử endpoint từ cấu hình, xác minh TLS, kiểm tra node PRIMARY_READY và đăng nhập lại khi đổi server. Leader hint chỉ giúp chọn endpoint, không mở quyền server. Khi đổi kết nối/epoch, bỏ response của socket cũ và tra lại ở primary hiện hành.

**Tự kiểm và bàn giao:** mất server hiện tại thì UI báo mất kết nối/đang xác minh, không tự khẳng định thất bại. Không chặn UI thread bằng network wait hoặc Thread.Sleep. Giao Nguyên Chương trace kết nối/correlation để kiểm tra response lạc phiên.

### Tuần 2 Làm K4 đến K6 và hoàn thiện UI

#### K4 Cài resolve và retry giữ nguyên identity

**Làm:** trên timeout/EOF/UNKNOWN, giữ pending và dùng GET_RESULT sau khi nối primary READY; NOT_FOUND chỉ cho phép retry cùng RequestId và cùng canonical command. Không tạo RequestId mới do failover. Phân biệt retry thao tác đang chờ với một thao tác mới do người dùng thực hiện sau kết quả cuối.

**Tự kiểm và bàn giao:** mất response OK_SOLD rồi restart phải tìm lại cùng TicketId/ReservationId; không thêm vé. GET_RESULT của owner khác không tiết lộ result. UI không tự tạo đường retry mới khiến một click thành nhiều RequestId khi chưa rõ kết quả.

#### K5 Cài Form có trạng thái xác nhận rõ

**Làm:** hoàn thiện `MainForm.cs` hoặc thay `TemplateForm.cs` có chủ đích: 10 ghế có cả chữ và màu AVAILABLE/HELD/SOLD; chọn ghế, giữ, xác nhận/hủy reservation và danh sách giữ của quầy hiện tại. Dữ liệu ghế lấy từ snapshot có AppliedIndex; đang gửi/UNKNOWN là trạng thái lệnh riêng, cache phải ghi dữ liệu cũ.

**Tự kiểm và bàn giao:** không tô SOLD/thông báo bán thành công chỉ vì send xong; chỉ FINAL có thẩm quyền mới xác nhận. SOLD không có nút hủy vé; HELD không tự hết hạn. Khóa thao tác trùng trong lúc một lệnh đang resolve, cho người dùng thấy cần chờ/xác minh thay vì bấm bán lại.

#### K6 Chạy ba quầy và record demo

**Làm:** dùng C1/A ở M4, C2/B và C3/C ở M5, mỗi tiến trình một cấu hình/store/phiên. Chạy các luồng cùng ghế, sai chủ, client restart và server failover với từng chủ backend. Đo latency bằng đồng hồ đơn điệu ngay client; ghi số mẫu, median/p95/max và điều kiện chạy.

**Tự kiểm và bàn giao:** PR có code SDK, pending và UI, test store/retry cùng record T17/T34; thêm các T phối hợp khi đã chạy thật. Cung cấp hướng dẫn mở C2/C3 trên cùng máy và minh chứng hai thư mục pending riêng. Mỗi bạn backend vẫn phải tự cung cấp log/oracle phần mình, Văn Khoa chỉ ghép demo.

**Tình huống phải tự giải thích:** đã bấm CONFIRM nhưng cửa sổ bị tắt trước khi thấy vé. Mở lại cùng quầy và dataset, đọc pending, reconnect rồi GET_RESULT/retry cùng identity; không nhấn một CONFIRM mới bằng RequestId khác để xử lý lệnh cũ.

**File báo cáo lý thuyết và liên hệ code:** [Phần của Văn Khoa](../report/05_van_khoa_truyen_thong.md). Thành viên tự viết phần này, dẫn PR/code/test của mình.

**Lý thuyết và phần bảo vệ:** ND2 slide 11–12, 16–17, 23–25; ND12 phần lỗi truyền thông/phục hồi (slide 2–3, 6, 8–9). Sách mục 2.1.2 “Gọi thủ tục xa” (tr. 25–29), 2.2.1 “Tên, định danh và địa chỉ” (tr. 34), 2.6.3 “Truyền thông khách/chủ tin cậy” và 2.6.3.2 “Các tình huống lỗi trong gọi thủ tục từ xa” (tr. 82–84). Văn Khoa phân biệt RequestId/NodeId với socket/IP, giải thích lost request/lost reply, retry và idempotency; minh họa RPC về mặt request/response, không tuyên bố dự án dùng framework RPC. Trace persist identity → send → mất response → UNKNOWN → reconnect → GET_RESULT/retry giữ identity, liên hệ I05/I07. Demo T17/T34 và nêu giới hạn: TCP không quyết định nghiệp vụ đã xảy ra hay chưa, timeout/NOT_FOUND không cho phép đổi RequestId, cache không xác nhận bán thành công.

**Nghiệm thu:** chủ trì T17, T32, T34; phối hợp T02–T09, T16, T29, T31, T33, T35.

**Phụ thuộc/reviewer:** cần backend/coordinator và result của Gia Quân; TCP/TLS của Nguyên Chương; gate/recovery của Đức An. Gia Quân review identity/nghiệp vụ/UI; Nguyên Chương review reconnect/socket/TLS.

**Hoàn tất khi:** pending sống qua crash, không replay chéo dataset/owner/store; không đổi RequestId do mất phản hồi; ba quầy độc lập trên hai máy; UI chỉ báo thành công từ FINAL có thẩm quyền. Có bằng chứng restart/reconnect và reviewer duyệt.

## 7. Cổng tích hợp G0–G5

| Cổng | Nội dung và phụ thuộc | Điều kiện qua cổng | Điều phối/review |
| --- | --- | --- | --- |
| G0 | Chốt DTO/canonical bytes/schema, ID, gate và V12 | Fixture chung; chữ ký API/ownership thống nhất; không lẫn V11 | Huy + cả năm người |
| G1 | Domain + SQL + coordinator một server, sau G0 | Unit/local integration, atomic Apply và snapshot có oracle; không tuyên bố chịu lỗi/FINAL ba bản sao | Gia Quân + Huy |
| G2 | Primary và ít nhất một backup thật, sau G1 | Event/result durable hai nơi trước FINAL; dedup, ACK và đọc Stable đúng; chạy lại end-to-end | Nguyên Chương + Gia Quân |
| G3 | Ba server độc lập, sau G2 | Crash, stale candidate, catch-up/rejoin; dữ liệu/hash tại K và FINAL được giữ | Huy + Nguyên Chương + Đức An |
| G4 | Bully + fencing/manifest + TCP/SQL faults + client restart, sau G3 | Không hai writer; manifest tiếp tục đúng RunId; pending resolve; giới hạn một lỗi rõ ràng | Đức An + Văn Khoa, reviewer chéo |
| G5 | Số liệu, báo cáo, đối chiếu bài giảng và video, sau G4 | T01–T36 có record/giới hạn trung thực; demo tái lập, release review | Huy tổng hợp; mỗi chủ module ký phần mình |

Không tự đánh dấu cổng hoàn tất từ việc build thành công. G0–G5 là thứ tự phụ thuộc, không phải ngày hẹn. Nếu thay hợp đồng, quay lại fixture và test chịu ảnh hưởng trước khi tiếp tục.

## 8. Quy trình bàn giao

1. Từ `develop` tạo nhánh feature đúng tên trong bảng; xem [Git Workflow](Git_Workflow.md), bao gồm điều kiện trưởng nhóm push `develop` lần đầu.
2. PR vào `develop` ghi phạm vi/file sửa, input/output đổi, V12/test IDs, dependency và reviewer; không ghi T01–T36 PASS nếu thiếu record.
3. Chạy `scripts/build.ps1`, `scripts/test.ps1` và startup `-Check` liên quan; các bước này chỉ xác nhận bộ khung. Khi có implementation, chạy thêm test/SQL oracle thực.
4. Đính kèm [record kiểm thử](../testing/TEST_RECORD_TEMPLATE.md), log đã loại secrets, hash/SQL oracle và cách tái lập; cập nhật [ma trận](../testing/TEST_MATRIX.md).
5. Reviewer kiểm tra code và bằng chứng, chủ module xử lý nhận xét; Huy ghép theo cổng, chạy lại test tích hợp sau merge.
6. Chỉ phát hành qua PR `develop` → `main` khi G5 đạt và nhóm đồng ý; `main` chỉ dành bản phát hành, không làm nhánh làm việc hằng ngày.

**Checklist chung:** hợp đồng/fixture cập nhật; không chứa secret, SDK/bin/obj hay dữ liệu pending; build/smoke đạt; test nghiệp vụ có record hoặc ghi rõ chưa chạy; failure path đóng gate đúng; reviewer đã duyệt; README/runbook phản ánh lệnh thực tế.

Mỗi người tự trình bày luồng chức năng đã code, chỉ ra PR/file và chạy lại test của mình khi bàn giao. Huy vẫn trực tiếp cài SqlBookingStore/Apply; Văn Khoa vẫn trực tiếp cài SDK/WinForms/pending, song song với nhiệm vụ tích hợp hoặc tổng hợp demo.


## 9. Cách bắt đầu và phối hợp để không bị chờ nhau

### Việc đầu tiên của cả năm người

1. Đọc README, mục 6–10 và 23 V12, rồi đọc mục chuyên môn được dẫn trong phần mình. Chạy `scripts/test.ps1` và startup `-Check` để biết bộ khung ban đầu; các lệnh này chưa kiểm tra SQL/bán vé.
2. Tạo nhánh feature từ develop theo Git Workflow. Mở module được giao và ghi trong PR đầu tiên file sẽ tạo/sửa, API nhận/gửi, fixture và test sẽ thực hiện. Không sửa đồng thời Contracts ở các nhánh mà không báo Gia Quân.
3. Cùng chốt G0 bằng bộ fixture: command HOLD hợp lệ, kết quả OK_HELD, một event cố định, một receipt đúng và các receipt sai; thêm snapshot/prefix/digest tại một K cố định. Các GUID/thời gian trong fixture phải cố định và khớp nhau.
4. Mỗi người làm được phần riêng bằng test có điều khiển: Huy dùng SQL cục bộ; Gia Quân dùng fake store/sender; Đức An dùng fake clock/metadata; Nguyên Chương dùng loopback/fake store rồi SQL thật; Văn Khoa dùng transport giả và file tạm. Fake phải được ghi là fake; khi ghép cổng phải thay bằng implementation thật.

### Những thứ cần giao trực tiếp cho bạn khác

| Bên giao | Gói bàn giao | Bên nhận tự kiểm |
| --- | --- | --- |
| Gia Quân | DTO, canonical encoder, input và expected bytes/hash/result | Cả nhóm chạy fixture giống nhau; không copy enum riêng |
| Huy | Store/Apply, snapshot, receipt sau commit, oracle và hướng dẫn SQL | Gia Quân/Nguyên Chương gọi API với test DB; Đức An đối soát K |
| Đức An | AuthorityGate, authenticated peer rules, RecoveryContext và runbook | Gia Quân không grant lúc freeze; Nguyên Chương không replay thiếu context |
| Nguyên Chương | TCP/TLS API, replica sender/receiver, trace ACK và fault hooks | Văn Khoa test reconnect; Gia Quân test FINAL sau receipt; Huy kiểm tra durable |
| Văn Khoa | SDK/pending, client config, trace ba quầy và demo UI | Gia Quân kiểm tra identity/result; Nguyên Chương kiểm tra socket/TLS |

Khi một API chưa xong, gửi ngay chữ ký và fixture đã thống nhất để bên kia test bằng fake. Không để một bạn tự đoán dữ liệu rồi cả nhóm sửa lại khi ghép. Thay đổi hợp đồng phải ghi rõ phần nào đổi và những test/consumer nào cần cập nhật.

## 10. Một luồng xuyên suốt để ghép đúng hướng

### HOLD ghế 1 của quầy A

1. Văn Khoa tạo identity cho một thao tác mới, lưu pending bền vững, gửi HOLD. RequestId ký hiệu R1 và EventId ký hiệu E1 dưới đây chỉ để mô tả; trong chương trình đều là GUID thật.
2. Transport của Nguyên Chương xác thực TLS/phiên, kiểm tra frame/envelope. Owner A được server lấy từ phiên; Gia Quân kiểm tra command và grant qua gate của Đức An.
3. Huy mở transaction, tra R1, đọc ghế 1 và dùng decision của Gia Quân. Nếu AVAILABLE, lưu HELD/ReservationId, event E1, ResultPayload OK_HELD và mốc mới cùng commit. Đây mới là LocalApplied.
4. Nguyên Chương gửi nguyên E1; một backup ghi xong transaction mới trả receipt. Gia Quân kiểm tra receipt đúng E1/hash/epoch rồi tăng StableIndex và trả FINAL. Văn Khoa lưu kết quả rồi cập nhật UI xác nhận.
5. Nếu A mất response, retry R1 phải trả cùng result; nếu B gửi một HOLD mới khi ghế còn HELD, nhận SEAT_BUSY được lưu bằng NO_CHANGE và nhân bản. Sau CONFIRM đúng chủ, chỉ có một ticket và ghế SOLD.

### Dừng đúng khi chưa có bằng chứng

- Có local commit nhưng không đủ ACK: UNKNOWN/BLOCKED_UNCERTAIN; không mutation tiếp, không đọc projection chưa Stable, không cấp event mới thay thế.
- Backup đã commit nhưng mất ACK: gửi lại nguyên event; duplicate ACK lại, không Apply lần hai.
- Primary hỏng: Bully chọn candidate; chỉ mở writer mới sau fencing thực, so cả hai survivor và manifest/readback đúng. Timeout hoặc ID cao không đủ.
- Thiếu hai participant, prefix conflict, quyền/dataset không khớp: giữ BLOCKED và chứng cứ. Không sửa DB, bỏ log hay tắt kiểm tra để demo chạy tiếp.

## 11. Tự kiểm và nộp PR theo cách dễ làm lại

### Tổ chức test khi bắt đầu triển khai

Các thư mục `tests/unit/`, `tests/integration/`, `tests/fault-injection/` hiện là vị trí dự kiến. Mỗi chủ module phải tạo project test thực có `.csproj` hoặc đưa test vào project đã thống nhất, thêm vào solution và ghi chính xác lệnh chạy. Không đặt file test rời rồi gọi đó là đã chạy. Nếu chọn framework cần package, cập nhật cấu hình restore cùng Huy; hiện harness bootstrap không cần package.

Với mỗi hành vi mới: viết ca kiểm tra đầu vào/kết quả mong đợi, xác nhận ca đó bắt được lỗi, cài phần tối thiểu, chạy lại rồi ghép test với module thật. `scripts/test.ps1` hiện chỉ chạy smoke; khi project test nghiệp vụ có thật, chủ test cùng Huy phải bổ sung lệnh vào script/CI và giữ exit code khác 0 khi lỗi. Không dùng riêng `dotnet test` hiện tại để kết luận toàn bộ nghiệp vụ đã được kiểm tra.

### Mỗi PR cần có những gì

1. Nêu module/file đã thực sự cài và hành vi mới. Liệt kê API/fixture/schema thay đổi và người nhận bị ảnh hưởng; dẫn mục V12, cổng G và mã T liên quan.
2. Ghi lệnh test đúng với project đã tạo, kết quả/exit code, môi trường SQL/node và cấu hình lỗi. Chạy `scripts/test.ps1 -Configuration Release` và startup `-Check` liên quan để tránh làm hỏng bộ khung; đây là kiểm tra bổ sung.
3. Với test nhiều node, lưu record theo `docs/testing/TEST_RECORD_TEMPLATE.md`: commit SHA, DatasetId, request/event/index/hash/epoch, log cần thiết và SQL oracle. Ảnh UI chỉ hỗ trợ, không thay chứng cứ durable/không bán trùng.
4. Chỉ đánh dấu PASS những mã đã chạy đủ oracle, ghi FAIL/CHƯA CHẠY cho phần còn lại; không đổi toàn ma trận vì build xanh. Nhờ reviewer đúng người, sửa nhận xét rồi Huy phối hợp ghép theo cổng.
5. Cập nhật file báo cáo riêng: giải thích phần sách bằng lời của mình, trỏ đúng code/PR, kèm một trace/test thật và giới hạn. Mỗi người phải bảo vệ được cả lý thuyết lẫn phần mình code.

### Phạm vi cần giữ trong đợt 2 tuần

Tập trung một chuyến 10 ghế, ba server/SQL độc lập, ba quầy, ba cơ chế đã chốt và các điều kiện an toàn V12. Làm luồng HOLD/CONFIRM/CANCEL có kiểm chứng trước, rồi ghép ba node, chuyển quyền/rejoin và các lỗi theo G0–G5. Giao diện rõ trạng thái và đủ thao tác là ưu tiên; trang trí, tính năng thanh toán, đăng ký người dùng, nhiều chuyến, microservice hay thuật toán khác chưa thuộc bản cơ sở. Không bỏ pending, gate đọc, replica durable hoặc fencing để đổi lấy thêm màn hình.

Hai tuần là thời lượng nhóm dự kiến, không phải bằng chứng hoàn tất. Nếu hết thời gian mà một cổng chưa đạt, phải báo trạng thái thực và giới hạn còn thiếu; không ghi hệ thống chịu lỗi/đạt CLO chỉ từ build hoặc mô hình. CLO chính thức của đúng lớp vẫn cần đề cương/rubric giảng viên; các mục sách/bài giảng ở đây là đối chiếu học thuật, không thay xác nhận đó.

## 12. Giải thích nhanh những từ dùng trong phân công

| Từ | Hiểu và dùng trong dự án này |
| --- | --- |
| Contract và fixture | Dạng dữ liệu/API chung và mẫu đầu vào kèm kết quả chuẩn để hai module tự đối chiếu |
| Coordinator và grant | Một nơi sắp thứ tự thao tác; grant là quyền vào vùng xử lý, không phải quyền SQL cấp cho client |
| LocalApplied | Một node đã commit event, result và dữ liệu; chưa chắc đủ điều kiện trả FINAL |
| Stable và FINAL | Primary đã xác minh cùng event bền vững ở primary và ít nhất một backup; FINAL mới xác nhận kết quả cho client |
| UNKNOWN và pending | Chưa biết kết quả; client giữ identity bền vững để tra/retry, không hiểu là thất bại chắc chắn |
| Candidate và fencing | Người được Bully chọn và việc cách ly writer cũ thực sự; candidate chưa được tự mở ghi |
| Prefix và digest tại K | So lịch sử có cùng tiền tố và dữ liệu/result tại một mốc đã freeze; không chỉ so số hàng |
| RecoveryContext và manifest | Quyền replay giới hạn của một run và hồ sơ chuyển quyền bền vững; đều không tự phát sinh từ field peer gửi |
| Oracle và record | Điều kiện kiểm tra tính đúng và bằng chứng chạy thực; không phải số PASS tự điền |
