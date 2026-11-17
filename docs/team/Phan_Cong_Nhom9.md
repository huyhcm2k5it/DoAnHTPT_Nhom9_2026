# Phân công triển khai đồ án nhóm 9

## Mục tiêu trong hai tuần

Nhóm hoàn thiện bản cơ sở bán vé một chuyến, 10 ghế, ba server và ba quầy theo V12. Bốn thành viên hiện tại đều trực tiếp viết code, kiểm thử, review và trình bày một phần lý thuyết gắn với module mình làm. Công việc replication và TCP được chia vào bốn module để không phụ thuộc vào một đầu mối duy nhất.

| Thành viên | Tuần 1 | Tuần 2 |
| --- | --- | --- |
| Huy | H1–H4: schema/seed, SQL Apply nguyên tử, receiver cho backup, snapshot; chốt API lưu và nhận event với Gia Quân. | H5–H6: oracle, rebuild/replay phía SQL, xử lý lỗi commit chưa rõ, ghép Host và kiểm chứng G1/G2. |
| Gia Quân | Q1–Q4: Contracts/fixture, decision, dedup, coordinator; thiết kế và bắt đầu sender dựa trên API Huy bàn giao. | Q5–Q6: gửi event nguyên bytes, xác minh receipt, Stable/FINAL, đọc có thẩm quyền và kiểm thử hai node. |
| Đức An | A1–A3: boot đóng, Bully, gate/fencing và xác thực quyền peer phía server; chốt API `RecoveryContext`. | A4–A6: manifest, compare/promotion, cấp context replay và kiểm thử rejoin/fault cùng Huy. |
| Văn Khoa | K1–K3: framing TCP, xác minh TLS phía client, SDK/login và pending store; cung cấp API kết nối cho Host. | K4–K6: route/reconnect, `GET_RESULT`, WinForms, khôi phục pending và demo ba quầy. |
| Cả nhóm | Chốt G0 bằng fixture chung; ghép `HOLD → Apply SQL → backup Apply → ACK` trên một primary và một backup. | Hoàn thiện G2 rồi tiếp tục G3–G5 theo phụ thuộc; chạy test, lưu record, hoàn thiện lý thuyết và demo. |

Cuối mỗi tuần, mỗi người bàn giao code, lệnh kiểm thử và record thực tế, ghi rõ phần đã đạt, lỗi còn lại và phụ thuộc. Không chia lịch theo ngày. T01–T36 chỉ được đánh dấu PASS khi có đủ oracle và record.

Nguồn kỹ thuật là [thiết kế V12](../De6_Thiet_Ke_Trien_Khai_V12.md), đặc biệt mục 6–17 và 20–23. Hai tuần là kế hoạch làm việc, không phải bằng chứng hệ thống đã hoàn tất hoặc đạt CLO.

## Trách nhiệm và cách ghép

| Thành viên | Module và phần được giao | Bàn giao chính | Reviewer |
| --- | --- | --- | --- |
| Huy | `Booking.Persistence`, database, SQL receiver/Apply và tích hợp Host | Apply nguyên tử, receipt sau commit, snapshot, oracle và hướng dẫn SQL | Gia Quân; Đức An review recovery |
| Gia Quân | `Booking.Domain`, Contracts và replica sender/coordinator | Fixture canonical, decision, dedup, `EnsureOneBackupAsync`, Stable/FINAL | Huy; Đức An review quyền |
| Đức An | `Cluster.Election`, `Cluster.Control` và peer authorization | Gate, fencing, manifest, compare/promotion, `RecoveryContext` | Gia Quân; Huy review metadata/SQL |
| Văn Khoa | `Client.Sdk`, `Client.WinForms`, framing TCP và kết nối client | Frame channel, TLS client, pending bền vững, retry và UI | Gia Quân; Đức An review xác thực |

Quyền trách nhiệm ở các ranh giới được chia rõ. Gia Quân chốt bytes và fixture chung. Huy lưu event và kết quả nguyên bản rồi chỉ xác nhận receipt sau commit SQL. Đức An xác thực peer, gate và context phục hồi. Văn Khoa cung cấp kết nối TCP/TLS phía client. Gia Quân gửi nguyên event; Huy triển khai receiver gọi Apply. Các module không tự đặt OwnerId hoặc NodeId dựa trên JSON từ bên gọi.

## Lý thuyết gắn với code

Mỗi người tự viết phần giải thích bằng lời của mình. Bài bảo vệ nêu giả thiết, bước thuật toán, bất biến, giới hạn và trace có đối chiếu với code/test. Trang dưới đây là trang in trong bản sách nhóm đã đối chiếu; nội dung thiết kế và giới hạn triển khai theo V12.

| Người | Lý thuyết và tài liệu | Code gắn với lý thuyết | Trace/demo |
| --- | --- | --- | --- |
| Huy | ACID, tương tranh; sách mục 2.5, 2.5.1, 2.5.3, tr. 74–77; ND11 | Transaction Apply, log/result/projection và SQL oracle | Một transaction không để lại nửa Apply; T13, T19 |
| Gia Quân | Loại trừ tập trung mục 2.3.3.1, tr. 56–57; primary-backup mục 2.8.5.2, tr. 106–107; ND7, ND14 | Coordinator, grant/release, gửi event và kiểm receipt | `LocalApplied → backup commit → ACK → Stable → FINAL`; T02, T10–T12, T29 |
| Đức An | Bully mục 2.3.5.1, tr. 59; phục hồi mục 2.6.6, tr. 88; ND7, ND12 | Election, fencing, manifest, RecoveryContext và rejoin | `ELECTION → ANSWER → COORDINATOR`, sau đó đối chiếu hai participant; T22, T24–T25, T28 |
| Văn Khoa | RPC mục 2.1.2, tr. 25–29; định danh mục 2.2.1, tr. 34; khách/chủ tin cậy mục 2.6.3, tr. 82–84; ND2/ND12 | Framing, SDK, pending, reconnect và WinForms | Lưu identity → gửi → UNKNOWN → `GET_RESULT` cùng RequestId; T17, T31, T34 |

Sách nền tảng không thay giao thức V12. Nhóm phân biệt rõ mục 2.6.6 là Phục hồi, mục 2.6.5 là Cam kết phân tán. Cả bốn người đều hiểu coordinator, Bully và primary-backup.

## Huy — SQL/Persistence và receiver

**Tuần 1**

1. **H1, schema và seed.** Tạo migration có phiên bản cho bảy bảng theo V12 mục 9, seed chuyến 1 và 10 ghế AVAILABLE. Giữ DatasetId chung, NodeId riêng. Kiểm khóa ghép, uniqueness và seed chạy lặp an toàn. Bàn giao sơ đồ và kiểu dữ liệu cho Gia Quân.
2. **H2, Apply nguyên tử.** Cài `SqlBookingStore` và `SqlApplyTransaction`. Trong một transaction kiểm authority, index, RequestId và áp dụng decision. Ghi projection, log, ResultPayload và NodeState cùng commit bền vững. Không gọi mạng khi đang giữ transaction.
3. **H3, receiver backup.** Nhận `APPEND_APPLY` từ peer đã được Đức An xác thực. Kiểm dataset, prefix, index, bytes và before-version rồi gọi `AppendAndApplyAsync`. Chỉ trả receipt/ACK sau commit. Duplicate cùng bytes chỉ ACK lại, event khác cùng index là conflict, thiếu index là GAP.
4. **H4, snapshot và kết quả.** Đọc seats, reservations, result cùng `AppliedIndex` trong snapshot thống nhất. Lọc theo OwnerId từ phiên. Khi commit outcome chưa rõ, tra log/request trước khi cho phép thử lại.

**Tuần 2**

5. **H5, oracle và replay phía lưu trữ.** Kiểm invariants ghế, reservation, ticket, log/result/index. Thực hiện API replay nhận `RecoveryContext` hợp lệ do Đức An cấp, lưu nguyên payload bytes và metadata lịch sử. Rebuild từ seed/log trong trạng thái đóng gate, giữ NodeId riêng.
6. **H6, Host và bàn giao.** Nối ba cấu hình SQL độc lập vào Host, ghi lệnh migration/seed, test SQL lỗi và record. Phối hợp Gia Quân kiểm G1/G2, với Đức An kiểm manifest và điểm phục hồi.

Kiểm thử trọng tâm là seed lặp, một HOLD chỉ sinh một event, lỗi trước commit không để lại thay đổi dở, duplicate không thêm hàng, GAP/conflict bị chặn, replay không tự cấp quyền. G1/G2 chưa chứng minh chịu lỗi ba node.

## Gia Quân — Domain, Contracts và sender

**Tuần 1**

1. **Q1, Contracts và fixture chung.** Chốt schema, null, enum, mã lỗi, command hash, event bytes, result bytes và receipt với Huy, Đức An, Văn Khoa. Mỗi fixture dùng ID/thời gian cố định và có expected bytes/hash để mọi module kiểm giống nhau.
2. **Q2, decision engine.** Cài HOLD, CONFIRM, CANCEL, sai chủ/ghế, reservation thiếu/đã hủy/đã bán. Không tự hết hạn HELD hoặc hủy ticket SOLD. Giao decision và fixture cho Huy.
3. **Q3, identity/dedup.** Cùng RequestId và command hash trả lại nguyên result đã Stable. Cùng RequestId nhưng khác nội dung/chủ trả REQUEST_CONFLICT. LocalApplied chưa Stable không được trả FINAL.
4. **Q4, coordinator.** Worker tuần tự mutation, queue giới hạn 256, đồng bộ grant/freeze với gate của Đức An. Không cho mutation sau dựa trên projection chưa Stable. Giữ đường ACK riêng để tránh worker chờ chính nó.

**Tuần 2**

5. **Q5, replica sender.** Gửi đúng `PersistedEvent` của Huy cho backup, không tạo event thay thế khi timeout. Kiểm đủ NodeId, epoch, LogIndex, EventId và EventHash trong receipt. Chỉ receipt hợp lệ mới tăng Stable và cho FINAL.
6. **Q6, đọc và tích hợp.** Nối `GET_SEATS`, `GET_RESULT`, `GET_MY_RESERVATIONS` qua coordinator. Test tương tranh, duplicate, receipt sai, client cancel khi đang chờ và freeze giữa request. Cùng Huy chạy end-to-end trên hai DB.

Kiểm thử trọng tâm là ba quầy cùng giữ một ghế chỉ có một OK_HELD, năm request giống nhau chỉ có một event, ACK sai không tăng StableIndex, đọc không thấy projection LocalApplied. Demo chính là grant → Apply → backup ACK → Stable → FINAL → release.

## Đức An — Bully, Control và quyền peer

**Tuần 1**

1. **A1, khởi động an toàn.** Server boot/restart ở RECOVERING, gate đóng, không nhận mutation trước khi Control xác minh trạng thái.
2. **A2, Bully.** Cài ELECTION/ANSWER/COORDINATOR, timeout và khử lặp ElectionRound. Candidate chỉ là ứng viên, chưa có quyền ghi.
3. **A3, authority và peer identity.** Cấp/freeze grant thống nhất với Gia Quân. Ánh xạ chứng thư peer sang NodeId cấu hình và từ chối peer/client gọi sai handler. Chốt API xác thực với Văn Khoa và Huy.

**Tuần 2**

4. **A4, manifest và fencing.** So sánh hai participant ở cùng prefix/hash/digest. Chỉ promotion sau fence và ghi/readback manifest; thiếu participant hoặc conflict thì giữ gate đóng.
5. **A5, quyền replay.** Cấp `RecoveryContext` có RunId, nguồn/đích, target epoch và mốc prefix. Huy chỉ replay khi context hợp lệ được cài trên node đóng gate.
6. **A6, rejoin và lỗi.** Kiểm node cũ có đuôi khác, lỗi giữa recovery, restart và stale candidate. Phối hợp Huy/ Gia Quân chạy record T20–T25, T28 và G3.

Kiểm thử trọng tâm là candidate không tự mở gate, peer giả NodeId bị chặn trước Apply, không promotion khi manifest/readback không khớp, rejoin không sao chép NodeId hay quyền phục vụ. Bảo vệ giới hạn Bully khi phân vùng.

## Văn Khoa — Client, framing TCP và kết nối

**Tuần 1**

1. **K1, framing và TCP.** Cài length-prefix 4 byte UInt32 big-endian, body UTF-8 từ 1 byte đến 1 MiB. Đọc đủ frame qua nhiều lần nhận, xử lý EOF, serialize ghi trên một socket. Cung cấp interface async cho Host/SDK.
2. **K2, TLS phía client và login.** Client xác minh chứng thư server theo trust/pin cấu hình, gắn OwnerId từ phiên login. Không nhận OwnerId do payload quầy tự khai. Đức An phụ trách xác thực peer phía server.
3. **K3, pending bền vững.** Ghi DatasetId, OwnerId, RequestId và command trước khi gửi bằng temp-file, flush và atomic replace. Một tiến trình giữ lock riêng cho mỗi client store.

**Tuần 2**

4. **K4, route và reconnect.** Kết nối node PRIMARY_READY từ endpoint đã cấu hình, xử lý disconnect và correlation. Đổi endpoint không tạo RequestId mới.
5. **K5, resolve kết quả.** Sau timeout/UNKNOWN, giữ pending rồi gọi `GET_RESULT`. NOT_FOUND chỉ cho retry cùng RequestId/canonical command. Ghi kết quả FINAL bền vững trước khi xóa pending.
6. **K6, UI và demo.** Hiển thị 10 ghế với AVAILABLE/HELD/SOLD và trạng thái pending/UNKNOWN tách riêng. Chạy ba quầy, restart client, chứng minh mỗi quầy có config/login/pending riêng; ghi trace và số liệu.

Kiểm thử trọng tâm là frame chia/ghép, EOF/header lỗi, chứng thư không hợp lệ, crash trước/sau gửi, response đến trễ và reconnect. UI chỉ xác nhận thành công khi nhận FINAL hợp lệ.

## Ma trận bàn giao replication và transport

| Hạng mục | Chủ trì | Bên phối hợp | Đầu ra cần bàn giao |
| --- | --- | --- | --- |
| TCP framing và TLS client | Văn Khoa | Đức An | Frame channel, server verification, lỗi protocol đóng kết nối |
| Xác thực peer và quyền APPEND/REPLAY | Đức An | Văn Khoa, Huy | NodeId đã ánh xạ chứng thư, policy handler, RecoveryContext |
| Receiver và durable ACK | Huy | Đức An, Gia Quân | Apply backup commit trước receipt, duplicate/GAP/conflict |
| Sender và matching receipt | Gia Quân | Huy, Văn Khoa | Gửi nguyên event, receipt đúng mới Stable/FINAL |
| Catch-up/replay | Huy phần lưu, Đức An cấp quyền | Gia Quân | Replay nguyên bytes tại K, cùng HeadHash/digest |
| Fault hooks/trace | Chủ module của điểm lỗi | Cả nhóm | Mất ACK, stale receipt, restart, frame lỗi và record T liên quan |

## Cổng ghép và cách bàn giao

| Cổng | Kết quả cần có | Người chịu trách nhiệm |
| --- | --- | --- |
| G0 | Fixture bytes/schema, chữ ký API và authority thống nhất | Gia Quân chủ trì, cả bốn review |
| G1 | Domain + SQL + coordinator một server, Apply và snapshot có oracle | Gia Quân + Huy |
| G2 | Primary và backup thật, event/result bền vững trước FINAL | Gia Quân + Huy; Đức An xác minh quyền |
| G3 | Ba server, crash, stale candidate và rejoin | Đức An + Huy + Gia Quân |
| G4 | Bully, fencing, manifest, lỗi TCP/SQL và client restart | Đức An + Văn Khoa |
| G5 | Ma trận T có record, báo cáo lý thuyết và demo được review | Cả nhóm |

Mỗi PR nêu module/file, hợp đồng ảnh hưởng, cổng G, mã test T, lệnh chạy và trạng thái chưa đạt. Test fake phải ghi rõ là fake; không dùng build/smoke để tuyên bố durable Apply, FINAL hoặc chịu lỗi. Huy điều phối thứ tự tích hợp; chủ module tự viết và chạy test của mình. Phát hành qua PR `develop` → `main` chỉ sau G5.

### Luồng HOLD mẫu

1. Văn Khoa tạo RequestId, ghi pending rồi gửi HOLD qua TLS. Server lấy OwnerId từ phiên.
2. Đức An xác minh peer/gate, Gia Quân kiểm identity và quyết định theo trạng thái ổn định.
3. Huy transaction ghi reservation, event, result và index rồi commit. Đây là LocalApplied, chưa phải FINAL.
4. Gia Quân gửi đúng event. Huy ở backup Apply và commit rồi mới trả receipt. Gia Quân đối chiếu receipt, cập nhật Stable và trả FINAL.
5. Nếu response mất, Văn Khoa truy vấn lại cùng RequestId. Duplicate không tạo event thứ hai. Nếu thiếu ACK, cụm trả UNKNOWN và giữ gate theo quy tắc V12.
