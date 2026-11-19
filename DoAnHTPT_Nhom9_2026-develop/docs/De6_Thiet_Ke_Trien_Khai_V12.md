# Thiết kế triển khai mô phỏng bán vé tàu 10 ghế

Phiên bản V12 ngày 14 tháng 11 năm 2026

Đề 6 học phần Các hệ thống phân tán PTIT cơ sở TP.HCM. Căn cứ đề bài ưu tiên là bản HTPT 26-27 theo lựa chọn của người học. Trọng tâm kiến thức là các bài giảng ThS. Lê Ngọc Bảo. Nhóm hiện có bốn thành viên làm việc trên năm máy: ba server lưu ba bản sao SQL độc lập và ba tiến trình quầy trên hai máy còn lại.

**Quyết định kiến trúc:** điều phối tập trung các thao tác nghiệp vụ, bầu ứng viên bằng Bully và nhân bản primary-backup đồng bộ. Một kết quả chỉ được trả FINAL sau khi cùng sự kiện và kết quả đã được lưu bền vững ở primary cùng ít nhất một backup. Chuyển quyền có kiểm soát sau khi cách ly writer cũ và đối soát hai bản sao. Không tự promote chỉ vì timeout.

V12 thay thế luồng PREPARED/COMMITTED của V11 bằng một sự kiện quyết định bất biến và một transaction Apply cục bộ trên mỗi máy. Trong tài liệu này, SQL commit cục bộ, đủ hai bản sao và phản hồi cho client là ba mốc khác nhau. Hợp đồng, giả thiết và điều kiện nghiệm thu bên dưới là phần bắt buộc khi triển khai.

## 1 Yêu cầu và phạm vi

Đề 26-27 yêu cầu mô phỏng bán vé tàu cao tốc 10 ghế và có bản sao/cụm dữ liệu. Nhân bản là yêu cầu của bản đề này. Cụm “mọi tình huống” được cụ thể hóa bằng ma trận lỗi và phép thử; không diễn giải thành bảo đảm tiếp tục bán với mọi tổ hợp hỏng hóc.

| Nội dung | Quyết định V12 |
| --- | --- |
| Dữ liệu nghiệp vụ | Một chuyến TripId=1, đúng 10 ghế SeatNo=1..10 |
| Quầy | C1, C2, C3 độc lập; tài khoản A, B, C; pending riêng |
| Lệnh ghi | HOLD, CONFIRM, CANCEL giữ ghế; không hủy vé đã SOLD |
| Thanh toán | Mô phỏng bằng CONFIRM; không giao dịch tiền thật |
| Kiến trúc | S1, S2, S3 cùng binary Server.Host; mỗi máy SQL riêng |
| Truyền thông | TCP có TLS, khung length-prefix và JSON có phiên bản |
| Cài đặt dự kiến | C# .NET 10, WinForms, SQL Server 2022; khóa phiên bản SDK/package khi bắt đầu code |
| Phục hồi | Log toàn bộ, replay và chuyển primary có kiểm soát |
| Hoãn | Nhiều chuyến, TTL giữ ghế, đa primary, thanh toán, Redis/Kafka, Raft/Paxos, sharding |

Không coi .NET, TCP hay ACID là thuật toán phân tán thứ tư. Cũng không tuyên bố môn học chỉ cho phép đúng ba thuật toán: nhóm chủ động chọn ba cơ chế đủ phục vụ đề 6.

## 2 Căn cứ bài giảng và kết quả cần chứng minh

| Nội dung học | Vị trí trong slide thầy | Minh chứng của đồ án |
| --- | --- | --- |
| HTPT và kiến trúc khách/chủ | ND1 slide 3, 23-24 | Các máy độc lập trao đổi mạng nhưng cung cấp một dịch vụ bán vé |
| Tiến trình, luồng, dispatcher/worker | ND10 slide 2, 7-8 | I/O và heartbeat độc lập với worker vùng găng |
| Truyền thông và định danh | ND2 slide 11-12, 16-17, 23-25 | RequestId không đổi khi retry; NodeId khác địa chỉ IP/cổng |
| Loại trừ tập trung | ND7 slide 2-3 | Coordinator cấp lượt duy nhất cho worker xử lý yêu cầu từ xa |
| Bầu chọn Bully | ND7 slide 9-10 | Trace ELECTION, ANSWER, COORDINATOR và các nhánh timeout |
| Giao tác và tương tranh | ND11 slide 2-4, 6-8 | Log, ghế, reservation, vé và kết quả cùng transaction |
| Primary-backup và nhất quán | ND14 slide 5-7, 13, 15 | Một thứ tự cập nhật, đủ bản sao trước FINAL, không đọc backup |
| Chịu lỗi và phục hồi | ND12 slide 2-3, 6, 8-9, 11-14 | Crash ở các mốc, replay, phát hiện thiếu dữ liệu và cách ly writer |
| Xác thực và quyền | ND13 slide 4-5, 8 | Chủ reservation từ phiên; peer chỉ từ thành viên xác thực |

Đây là ma trận đối chiếu nội dung, không phải bảng mã CLO chính thức. Đề cương INT1405 công khai của PTIT hỗ trợ phạm vi môn nhưng chưa xác định CLO/rubric của lớp thầy Bảo. Khi có đề cương đúng lớp, gắn mã CLO vào chính các minh chứng này, không tự đặt tỷ trọng điểm.

## 3 Lựa chọn và giả thiết kiến trúc

Ba hướng được cân nhắc: giữ giao thức hai dấu PREPARED/COMMITTED của V11; primary-backup đồng bộ có log và phục hồi có kiểm soát; hoặc tự chuyển quyền bằng một cơ chế đồng thuận đầy đủ. Người soạn chọn hướng thứ hai để giảm số pha phải tự chứng minh và bám primary-based trong ND14. Hướng thứ ba làm tăng đáng kể phạm vi. Giữ hai dấu ở hướng thứ nhất không tự tạo tính đúng nếu thiếu quy tắc phục hồi.

Các giả thiết bắt buộc:

1. Thành viên cố định S1=1, S2=2, S3=3, không thêm/bớt thành viên khi chạy.
2. Một đợt lỗi bắt đầu từ ba bản sao cùng lịch sử và đã đồng bộ. Mức cam kết là tối đa một node không sẵn sàng trong đợt đó. Node mới bật nhưng chưa đồng bộ vẫn tính là không sẵn sàng. Cam kết chịu lỗi được khôi phục sau khi đủ ba node đã đồng bộ lại.
3. Có thể mất kết nối, trễ, lặp bản tin và mất phản hồi. Timeout chỉ tạo nghi ngờ. Không có tự promote trong phân vùng.
4. Không xét Byzantine, người quản trị độc hại, sửa DB trực tiếp, phần cứng báo ghi bền vững sai hoặc mất đồng thời hai bản dữ liệu cần thiết.
5. SQL commit dùng fully durable transaction. Không bật delayed durability. Không có tác động ngoài hệ thống như trừ tiền hoặc in vé vật lý trong Apply.
6. Chỉ một người vận hành tin cậy có quyền quản trị chuyển quyền. Primary cũ phải bị dừng/cách ly vật lý trước khi mở primary mới. Mọi lần boot đều đóng quyền phục vụ.
7. Nếu giả thiết không kiểm chứng được, dừng phục vụ nghiệp vụ và giữ chứng cứ. An toàn khi dừng không được báo là dịch vụ vẫn bán bình thường.

Không cam kết chịu lỗi thứ hai khi node thứ nhất chưa được phục hồi đồng bộ; không quảng cáo chịu N-1 lỗi, zero downtime hoặc tự failover an toàn trong mọi phân vùng.

## 4 Triển khai vật lý và topology

| Máy | Tiến trình | Lưu trữ |
| --- | --- | --- |
| M1 | Server.Host S1 | SQL DB1 chỉ S1 ghi |
| M2 | Server.Host S2 | SQL DB2 chỉ S2 ghi |
| M3 | Server.Host S3 | SQL DB3 chỉ S3 ghi |
| M4 | Client.WinForms C1 | Pending riêng của quầy A |
| M5 | Client.WinForms C2 và C3 | Hai thư mục pending và hai phiên đăng nhập riêng |

Các server có kết nối peer trực tiếp từng cặp S1-S2, S1-S3, S2-S3. Không chuyển tiếp qua S2 như một mắt xích bắt buộc. Client SDK chứa danh sách ba endpoint và thử kết nối lại; SDK là thư viện trong client, không phải máy thứ sáu.

Port client 9001 và peer 9100 trên từng IP. Cùng số port trên các máy khác nhau hợp lệ. SQL chỉ nhận kết nối cục bộ của Server.Host; client không có connection string SQL. Không đặt cả ba SQL trên cùng một máy rồi gọi đó là demo chịu lỗi ba máy.

Server phân tầng Contracts, Transport, Domain, Persistence, Replication, Election, ClusterControl và Host. Mọi thay đổi nghiệp vụ đi qua coordinator. Peer ACK và heartbeat được tiếp nhận bởi I/O riêng, không xếp sau worker đang chờ chính ACK đó.

## 5 Thuật ngữ và trạng thái chính xác

| Thuật ngữ | Nghĩa bắt buộc |
| --- | --- |
| LocalApplied | Một SQL transaction đã lưu event, kết quả và projection trên một node |
| StableIndex | Mốc primary đã kiểm chứng cùng tiền tố được lưu bền vững tại primary và ít nhất một backup |
| FINAL | Phản hồi nghiệp vụ có thẩm quyền cho một RequestId, chỉ khi event của nó không vượt StableIndex |
| UNKNOWN | Client chưa xác định được kết quả; không có nghĩa lệnh thất bại hoặc chưa xảy ra |
| LastAppliedIndex | Mốc projection/log cục bộ, lưu bền vững; có thể vượt StableIndex tối đa một entry trên primary |
| Epoch | Số phiên quyền ghi do quy trình quản trị cấp; không phải đồng hồ Lamport, không tự fence node khác |
| EventId | GUID duy nhất của sự kiện; khác RequestId và LogIndex |
| LogIndex | Vị trí trong lịch sử hiện hành, liên tục từ 1; không phải định danh sự kiện xuyên nhánh |

StableIndex và ServingEnabled là trạng thái vận hành trong RAM, khởi động lại không được tin giá trị cũ để tự mở bán. NodeState lưu LastAppliedIndex, HeadHash và quyền đã chấp nhận; trạng thái boot vẫn RECOVERING.

Role của node là RECOVERING, BACKUP_READY, ELECTION, CANDIDATE, PRIMARY_READY, MAINTENANCE, BLOCKED_UNCERTAIN, FENCED hoặc STORAGE_FAULT. PRIMARY_READY chỉ là quyền có thể phục vụ; mỗi thao tác vẫn phải kiểm tra gate. BACKUP_READY có nghĩa đã xác minh cùng lịch sử tại mốc đồng bộ, không có quyền trả dữ liệu nghiệp vụ cho client.

## 6 Bất biến và ngữ nghĩa dịch vụ

| ID | Bất biến |
| --- | --- |
| I01 | TripId=1, đúng 10 ghế và SeatNo trong 1..10 |
| I02 | Mỗi ghế tối đa một reservation HELD và tối đa một ticket |
| I03 | SOLD có đúng ticket và reservation CONFIRMED khớp ghế/chủ |
| I04 | AVAILABLE không có current reservation/owner; HELD có đúng reservation HELD |
| I05 | Một RequestId trong cùng DatasetId chỉ có một CommandHash và một ResultPayload trong lịch sử hiện hành |
| I06 | Event, projection, RequestResults và LastAppliedIndex cùng commit cục bộ; không tồn tại nửa Apply |
| I07 | FINAL chỉ sau primary và ít nhất một backup lưu cùng event/hash và cùng kết quả bền vững |
| I08 | Một primary xử lý tối đa một mutation chưa Stable; không thực hiện mutation kế trên trạng thái chưa Stable |
| I09 | Mỗi backup chỉ nhận entry kế tiếp đúng prefix, hoặc ACK lại entry giống hệt đã có |
| I10 | Chỉ một writer được mở sau quy trình chuyển quyền; thiếu fencing hoặc đối soát thì không mở |
| I11 | Không trả đọc có thẩm quyền từ trạng thái LocalApplied chưa Stable |
| I12 | Nút cũ không tự giành quyền, không đưa nhánh bị loại vào lịch sử mới |

Mọi mutation và đọc nghiệp vụ có thẩm quyền được coordinator tuần tự hóa. GET_SEATS, GET_RESULT, GET_MY_RESERVATIONS trả cùng một snapshot SQL và AppliedIndex tương ứng. Trong lúc mutation chưa Stable, đọc nghiệp vụ được đợi tối đa thời gian yêu cầu rồi trả CLUSTER_NOT_READY; không đọc projection đang chờ nhân bản. GET_CLUSTER_STATUS chỉ là thông tin quản trị, có nhãn LocalApplied/Stable khác nhau.

Mục tiêu quan sát là một lịch sử tuần tự ở primary; sau phản hồi FINAL, đọc có thẩm quyền sau đó phải thấy kết quả đó hoặc trạng thái phát triển hợp lệ của nó. Nếu báo cáo dùng thuật ngữ tuyến tính hóa, phải gắn nó với gate đọc, thứ tự coordinator và quy tắc phục hồi, không suy từ UNIQUE hoặc hash bằng nhau. Chỉ nghiệm thu mô hình nhất quán sau khi kiểm thử cả đọc và chuyển quyền.

## 7 Nghiệp vụ và kết quả cố định

OwnerId được lấy từ phiên đăng nhập, không tin OwnerId trong JSON. OwnerId đại diện quầy, không phải thông tin hành khách.

| Trạng thái/điều kiện | Lệnh | Quyết định |
| --- | --- | --- |
| Ghế AVAILABLE | HOLD | Tạo ReservationId; HELD; OK_HELD |
| Ghế HELD hoặc SOLD | HOLD với RequestId mới | SEAT_BUSY hoặc ALREADY_SOLD; không thay ghế |
| Reservation HELD đúng chủ và khớp ghế | CONFIRM | CONFIRMED, SOLD, tạo TicketId; OK_SOLD |
| Reservation đã CONFIRMED đúng chủ | CONFIRM với RequestId mới | ALREADY_CONFIRMED kèm TicketId cũ; không tạo vé mới |
| Reservation đã CANCELLED đúng chủ | CONFIRM với RequestId mới | RESERVATION_CANCELLED |
| Reservation HELD đúng chủ | CANCEL | CANCELLED, ghế AVAILABLE; OK_CANCELLED |
| Reservation đã CANCELLED đúng chủ | CANCEL với RequestId mới | ALREADY_CANCELLED |
| Reservation đã CONFIRMED | CANCEL | TICKET_NOT_CANCELLABLE |
| Reservation sai chủ hoặc không tồn tại | CONFIRM/CANCEL | NOT_OWNER hoặc RESERVATION_NOT_FOUND |
| Reservation đúng chủ nhưng khác SeatNo trong lệnh | CONFIRM/CANCEL | RESERVATION_SEAT_MISMATCH |
| Cùng RequestId và CommandHash | Retry | Trả nguyên ResultPayload cũ khi đủ điều kiện Stable |
| Cùng RequestId khác nội dung/chủ | Bất kỳ | REQUEST_CONFLICT; không ghi đè kết quả gốc |

Lỗi nghiệp vụ của lệnh đã xác thực cũng được ghi thành event không thay projection ghế và nhân bản như thành công, để retry không đổi từ SEAT_BUSY thành OK_HELD khi ghế trống về sau. Lỗi khung, đăng nhập, sai DatasetId hoặc đầu vào không hợp lệ bị từ chối trước khi nhận lệnh và không được gọi là kết quả nghiệp vụ đã nhân bản.

Không hết hạn HELD tự động. Khi client chết, pending giúp truy lại kết quả; GET_MY_RESERVATIONS lấy các reservation của quầy để tiếp tục CONFIRM hoặc CANCEL. Không reset DB để giải phóng ghế trong lúc demo.

BusinessResult v1 có đúng các field `code`, `tripId`, `seatNo`, `reservationId`, `ticketId` theo thứ tự này; field không dùng là null, không bỏ field. OK_HELD trả reservation mới; OK_SOLD và ALREADY_CONFIRMED trả cùng ticket tương ứng; OK_CANCELLED/ALREADY_CANCELLED trả reservation được yêu cầu. Các từ chối khác không tiết lộ dữ liệu của chủ khác. Epoch/LogIndex/CorrelationId nằm ở envelope, không làm thay đổi phần BusinessResult khi retry. CONFIRM/CANCEL luôn gửi TripId,SeatNo và ReservationId, cả ba phải khớp với reservation.

## 8 Định danh và hash dùng chung

DatasetId, RequestId, ReservationId, TicketId và EventId là GUID. DatasetId chỉ đổi khi reset cả cụm có kiểm soát, không đổi khi restart/failover. Epoch là Int64 dương. Mỗi epoch có đúng một writer được công nhận.

CommandHash là SHA-256 của UTF-8 chuỗi theo đúng thứ tự: `v1|DatasetIdN|OwnerId|Action|1|SeatNoDecimal|ReservationIdN-or--`. GUID dùng chữ thường định dạng N; Action là HOLD/CONFIRM/CANCEL; OwnerId chỉ nhận ký tự ASCII chữ, số, gạch dưới và gạch nối, dài 1..32. RequestId không nằm trong hash vì nó là khóa đối chiếu riêng; epoch/IP/connection cũng không nằm trong hash để retry qua failover giữ nguyên ý nghĩa.

`ReservationIdN-or--` ở biểu thức trên là vị trí giữ chỗ: với HOLD, giá trị thực ở field cuối là đúng một ký tự `-`; với CONFIRM/CANCEL, đó là 32 ký tự GUID định dạng N của reservation. Không hash nguyên tên placeholder và không đưa khoảng trắng quanh dấu phân cách.

Event chứa DatasetId, LogIndex, Epoch, WriterId, EventId, PreviousHash, RequestId, OwnerId, CommandHash, CommandData, Decision, các ID/timestamp do primary tạo, BeforeSeatVersion và ResultPayload. CommandData giữ Action,TripId,SeatNo,ReservationId để xác minh lại CommandHash. Decision là dữ liệu có cấu trúc, không phải chuỗi SQL để follower thực thi.

DecisionKind chỉ có HELD_CREATED, SOLD_CONFIRMED, HOLD_CANCELLED hoặc NO_CHANGE. Với thay đổi, Decision ghi đầy đủ SeatAfter (status/current reservation/current owner/version=LogIndex), ReservationAfter (ID,trip,seat,owner,status,CreatedUtc,UpdatedIndex) và TicketCreated nếu bán. CreatedUtc của reservation giữ nguyên khi xác nhận/hủy. NO_CHANGE không có patch nghiệp vụ, vẫn có BeforeSeatVersion và BusinessResult cố định. Follower kiểm tra before version và quan hệ patch, ghi đúng after image và result; không tự diễn giải lệnh thành quyết định mới. Timestamp dùng UTC định dạng ISO 8601 với 7 chữ số phần giây; SQL dùng datetime2(7). JSON/bytes chuẩn của các field lồng nhau thuộc Contracts v1 và có fixture kiểm thử byte-for-byte trước tích hợp.

Primary tạo payload JSON bằng bộ ghi canonical có thứ tự thuộc tính cố định của Contracts v1; lưu nguyên byte UTF-8 đó. EventHash = SHA-256(PreviousHash 32 byte nối PayloadBytes). SeedHash = SHA-256 của chuỗi ASCII `PTIT-DE6-V12-SEED|DatasetIdN`. Mọi node lưu/truyền lại nguyên byte payload; không parse rồi serialize tùy ý để so hash. ResultPayload bên trong event là kết quả nghiệp vụ cố định; envelope phản hồi có thể có CorrelationId/Epoch hiện tại khác nhau khi retry.

BeforeSeatVersion và kết quả chuyển trạng thái giúp phát hiện projection lệch. Follower xác minh tiền điều kiện và áp dụng Decision đã được primary quyết định, không tạo GUID/thời gian mới và không tự chạy lại nhánh nghiệp vụ để chọn kết quả khác.

## 9 Mô hình dữ liệu và transaction Apply

Giữ bảy bảng sau. Đây là hợp đồng schema để viết migration, không phải xác nhận đã chạy script SQL.

| Bảng | Khóa và thuộc tính chính | Ràng buộc |
| --- | --- | --- |
| Trips | TripId PK, TotalSeats | TripId=1, TotalSeats=10 |
| Seats | PK(TripId,SeatNo), Status, CurrentReservationId, CurrentOwnerId, VersionIndex | SeatNo 1..10; AVAILABLE có hai current field null, HELD/SOLD có cả hai |
| Reservations | ReservationId PK, TripId,SeatNo,OwnerId,Status, CreatedUtc, UpdatedIndex | FK ghế; UNIQUE(ReservationId,TripId,SeatNo,OwnerId); filtered UNIQUE(TripId,SeatNo) khi HELD |
| Tickets | TicketId PK, ReservationId,TripId,SeatNo,OwnerId,SoldUtc, CreatedIndex | UNIQUE(TripId,SeatNo), UNIQUE ReservationId; FK ghép tới reservation cùng ghế/chủ |
| OperationLog | LogIndex PK, EventId,Epoch,WriterId,RequestId, PreviousHash,EventHash,PayloadBytes | UNIQUE EventId; UNIQUE RequestId; đủ event để dựng lại projection |
| RequestResults | RequestId PK, OwnerId,CommandHash,LogIndex,ResultPayloadBytes | FK LogIndex; không sửa kết quả của RequestId đã có |
| NodeState | Một hàng cục bộ NodeId,DatasetId, AcceptedEpoch,AcceptedLeader,LastAppliedIndex,HeadHash,SchemaVersion | Không chứa cờ tự khởi động ServingEnabled |

Seats có FK ghép (CurrentReservationId,TripId,SeatNo,CurrentOwnerId) tới candidate key tương ứng của Reservations. Các quan hệ trạng thái xuyên bảng vẫn do Apply và oracle kiểm tra; CHECK đơn bảng không tự kiểm tra được SOLD khớp CONFIRMED.

Một transaction cục bộ có thứ tự: kiểm tra dataset/epoch/grant và mốc hiện hành; kiểm tra RequestId trùng; quyết định hoặc xác minh event; tạo/update reservation; update Seats; insert Tickets nếu CONFIRM; insert OperationLog và RequestResults; update LastAppliedIndex/HeadHash; kiểm tra các bất biến cần thiết; COMMIT fully durable. Thứ tự INSERT/UPDATE cụ thể phải thỏa FK, nhưng toàn bộ là một transaction. NO-OP nghiệp vụ vẫn thêm event/result và tăng LogIndex, VersionIndex của ghế chỉ đổi khi trạng thái ghế đổi.

Không giữ transaction/khóa SQL trong lúc chờ TCP. SQL failure hoặc outcome commit chưa rõ thì đóng gate và đọc lại log bằng RequestId trong phục hồi; không phát sinh event thứ hai để “thử lại cho chắc”. SQL UNIQUE là hàng rào cục bộ, không phải khóa toàn cụm.

## 10 Luồng ghi primary-backup đồng bộ

1. SDK ghi pending bền vững rồi gửi command. Server kiểm tra phiên, dataset và đầu vào.
2. Coordinator cấp một grant nội bộ cho worker. Đây là ánh xạ Request/Grant/Release của loại trừ tập trung: server thực thi vùng găng thay quầy. Không cấp SQL cho quầy. Grant giữ tới khi operation Stable hoặc cụm bị đóng gate.
3. Nếu RequestId đã có ở tiền tố Stable, trả ResultPayload cũ. Nếu nội dung khác, REQUEST_CONFLICT.
4. Primary chạy một SQL transaction DecideAndApply. Sau commit, LastAppliedIndex tăng một; StableIndex chưa tăng. Không công bố trạng thái mới cho đọc nghiệp vụ.
5. Primary gửi APPEND_APPLY nguyên event tới các backup hợp lệ. Backup ghi log, result và projection cùng SQL transaction; chỉ ACK_APPLIED sau commit bền vững. ACK chứa NodeId, AuthorityEpoch, EventEpoch, LogIndex, EventId và EventHash khớp request. Với ghi mới, hai epoch đều bằng epoch đang phục vụ.
6. Khi ít nhất một backup ACK hợp lệ, primary đặt StableIndex bằng LastAppliedIndex trong RAM. Đây là điều kiện công bố. Backup thứ ba có thể chậm và bắt kịp sau.
7. Trả FINAL với ResultPayload đã lưu, nhả grant, nhận thao tác tiếp. Client lưu kết quả rồi cập nhật trạng thái xác nhận trên UI.

Một mutation chỉ có đúng một event. Không thay payload khi đổi backup hoặc retry ACK. Nếu một backup chậm, thử backup còn lại với đúng event, đồng bộ prefix trước nếu cần. Chỉ cần một ACK backup hợp lệ; test chặn một ACK không mặc nhiên phải làm cả cụm dừng.

Nếu hết thời gian replication mà chưa có ACK, trả UNKNOWN khi còn kết nối và chuyển BLOCKED_UNCERTAIN. Không xử lý mutation hoặc đọc nghiệp vụ tiếp. Tiếp tục đối soát/nhân bản đúng event này; khi xác nhận được hai bản bền vững thì mở gate và client có thể GET_RESULT. Primary đang sống không được tùy ý bỏ event LocalApplied để tiếp tục lịch sử khác.

Client ngắt kết nối không hủy operation đã bắt đầu. CancellationToken của socket không được dùng để rollback hay bỏ replication sau LocalApplied. Lệnh còn nằm trong queue, chưa bắt đầu, có thể bị từ chối an toàn; SDK vẫn giữ RequestId để retry.

**Chỉnh đúng nhận thức:** Apply ở primary trước nhân bản không tự nó là lỗi. Lỗi là trả thành công/cho đọc hoặc quyết định lệnh tiếp quá sớm, hay phục hồi từ bản sao thiếu lịch sử. V12 cho phép local Apply trước replication nhưng khóa công bố và chỉ FINAL sau hai bản lưu bền vững. Không gọi SQL commit này là distributed commit/2PC.

## 11 Quy tắc APPEND và retry

| Tình huống follower | Hành vi |
| --- | --- |
| Sai certificate/WriterId/AcceptedLeader/Epoch | REJECT_AUTHORITY; không đổi SQL |
| Sai DatasetId/schema/protocol | REJECT_DATASET_OR_VERSION |
| Index=LastApplied+1, PreviousHash khớp, BeforeVersion khớp | Apply transaction và ACK_APPLIED |
| Index đã có và EventId/hash/payload đều giống | ACK_APPLIED lại; không Apply lần hai |
| Index đã có nhưng khác event/hash | PREFIX_CONFLICT; đóng node, không ghi đè |
| Index lớn hơn LastApplied+1 | GAP(expected=LastApplied+1); yêu cầu catch-up |
| Projection/owner/status trái bất biến | INVARIANT_VIOLATION; không ACK, cách ly |

Lặp bản tin, lặp ACK và ACK đến muộn chỉ được khớp với event đang chờ. Receipt của epoch trước hoặc EventId khác không làm tăng StableIndex. Peer connection thay đổi không thay đổi identity của event.

## 12 Bầu chọn Bully và chuyển quyền

Heartbeat mỗi 1 giây; nghi ngờ sau 3 heartbeat liên tiếp không nhận được. Tham số là lựa chọn demo, không phải bằng chứng crash. Timeout Answer 2 giây, timeout Coordinator 5 giây; có backoff ngẫu nhiên 0..500 ms khi khởi động lại vòng để tránh dồn bầu. Đo thời gian bằng đồng hồ đơn điệu.

Nút đủ điều kiện bầu là node đã được đồng bộ và admitted trong đợt hoạt động hiện tại. Node RECOVERING không trả ANSWER với tư cách ứng viên đủ điều kiện. Khi nhận ELECTION hợp lệ, gửi ANSWER, rồi khởi động/duy trì vòng bầu tới các ID lớn hơn. Không có ANSWER thì phát COORDINATOR; có ANSWER thì chờ COORDINATOR; hết thời gian chờ thì phát động lại. ElectionRound có InitiatorId và GUID để khử lặp; bỏ kết quả chỉ tới node đã biết không đủ điều kiện. Coordinator chỉ ghi CANDIDATE, không mở gate.

Lưu ElectionCandidateId/round riêng với ServingState và AcceptedLeader. COORDINATOR cập nhật kết quả bầu trong giai đoạn phục hồi, không ghi đè quyền phục vụ đang được công nhận và không tự demote/promote một PRIMARY_READY khỏe. Message bầu có AuthorityEpoch của đợt hiện hành; kết quả epoch cũ bị bỏ sau khi activation mới hoàn tất. Thay ServingState/quyền phục vụ chỉ qua ClusterControl và các điều kiện đóng gate hoặc chuyển quyền đã quy định.

Trong quiescent recovery, người vận hành chờ bầu hội tụ trên hai node sống đủ điều kiện; node ID lớn hơn là ứng viên. Nếu một node chưa đồng bộ/khác epoch, không tự coi nó là đủ điều kiện chỉ vì đang online.

Quy trình Promote bắt buộc:

1. Đóng gate nghiệp vụ ở hai node tham gia; chờ hoặc đánh dấu operation hiện tại chưa rõ. Tạo RecoveryRunId và snapshot chứng cứ log/metadata của cả hai.
2. Cách ly primary cũ bằng dừng hẳn Server.Host và ngăn tự khởi động, hoặc tắt máy. Ping timeout, một cờ gửi qua mạng hoặc epoch mới không thay thế bước này.
3. Kiểm tra hai node cùng DatasetId, schema và AcceptedEpoch/AcceptedLeader hiện hành. Nếu khác epoch/quyền không rõ, giữ BLOCKED; không chọn lịch sử bằng “node ID lớn hơn”. Trường hợp lỗi chồng khi chưa sửa xong đợt trước không thuộc mức cam kết một lỗi.
4. Đọc toàn bộ hash chain và kiểm tra projection/result của hai node. Hai lịch sử phải có quan hệ tiền tố. Nếu có cùng Index khác hash, giữ BLOCKED và điều tra; không chọn lịch sử dài hơn một cách mù quáng.
5. Chọn lịch sử dài hơn trong hai lịch sử tương thích, bao gồm event cuối có thể chưa từng được client nhận FINAL. Replay để cả hai node có cùng K, HeadHash và business/result digest. UNKNOWN có thể được hoàn tất thành công hoặc lỗi nghiệp vụ trong phục hồi.
6. Lập manifest gồm RunId, OldEpoch, NewEpoch=OldEpoch+1, CandidateId, hai ParticipantId, K, HeadHash và digest. Lưu manifest và AcceptedEpoch/Leader mới bền vững trên cả hai node sau khi dữ liệu đã khớp. Chưa ACK đủ hai node thì không mở gate. Nếu crash giữa bước này, xem quy tắc tiếp tục manifest bên dưới.
7. Ứng viên đọc lại xác nhận hai bản cùng manifest/epoch/prefix; đặt StableIndex=K, mở PRIMARY_READY ở đúng một node. Node kia BACKUP_READY. Mọi API công bố quyền phục vụ kiểm tra epoch/leader đã chấp nhận.
8. SDK reconnect, đăng nhập lại và resolve pending bằng cùng RequestId. Node cũ trở lại chỉ RECOVERING.

Manifest dở dang: gate của tất cả node boot lại luôn đóng. Nếu chưa hoàn tất bước ghi quyền trên cả hai participant, người vận hành chỉ được **tiếp tục chính RecoveryRunId đã lưu**, với cùng CandidateId, K/hash và cùng NewEpoch; đối chiếu hai bản rồi hoàn thành bản còn thiếu. Không tạo một promotion cạnh tranh, không tự giảm epoch, không cho ghi trong thời gian này. Nếu không lấy đủ hai participant hoặc metadata trái nhau, BLOCKED để phục hồi thủ công đầy đủ. Một người vận hành và việc không cho mở quyền ngoài API quản trị là giả thiết của bản cơ sở.

Epoch không tự fence node vật lý; toàn bộ mức an toàn chuyển quyền phụ thuộc cả fencing, dữ liệu, manifest và một cổng kích hoạt. Không tự động chuyển writer về S3 khi S3 trở lại. Chỉ admit S3 sau catch-up; mọi chuyển vai trò chủ động tiếp theo cũng thực hiện quy trình dừng ghi/cách ly writer cũ.

## 13 Crash ở từng mốc và dữ liệu chưa rõ

| Mốc crash primary | Dữ liệu có thể có | Quy tắc |
| --- | --- | --- |
| Trước local SQL commit | Chưa có event ở đâu | Retry cùng RequestId có thể quyết định lại |
| Sau local commit, chưa backup nào Apply | Chỉ máy cũ có event | Không có FINAL; hai survivor có thể không chứa event này |
| Backup Apply xong, ACK mất | Event có ở primary và backup | Hai survivor giữ event nếu có trong một log; replicate lại rồi công bố |
| Primary nhận ACK, chưa trả FINAL | Event đã đủ hai bản | Giữ event; client UNKNOWN tra lại kết quả |
| Sau FINAL, response tới client hoặc bị mất | Event và kết quả đã đủ hai bản | Không được mất/đổi kết quả sau một node crash |

Với ba node và một node crash, cặp hai survivor giao với mọi cặp hai node đã lưu một FINAL. Bởi vậy, xem cả hai survivor sẽ giữ được mọi FINAL nếu log không bị sửa và hai lịch sử tương thích. Không có cơ sở này khi chỉ khảo sát DB của candidate hoặc đã mất hai node chưa phục hồi.

Sự kiện chỉ có trên primary cũ và chưa từng đủ điều kiện FINAL có thể bị loại khi hai survivor tạo lịch sử mới. Khi node cũ rejoin, nhánh đó phải được lưu vào kho lưu trữ phục hồi rồi thay bằng lịch sử hiện hành; không mang qua replay tự động. Nếu LogIndex bị dùng lại trong nhánh mới, Epoch, EventId và hash khác phân biệt hai sự kiện. Khóa đối chiếu ACK luôn gồm epoch/event/hash, không chỉ LogIndex. RequestId của lệnh chưa có trong lịch sử được giữ được phép retry cùng ID và quyết định lại; vì chưa FINAL nên không có hai kết quả đã xác nhận mâu thuẫn.

## 14 Rejoin và đối soát

Với 10 ghế, chọn catch-up trong khoảng dừng mutation ngắn để dễ làm đúng. Primary hoàn tất event đang chờ nếu có, rồi chuyển MAINTENANCE; nếu không thể hoàn tất, giữ BLOCKED_UNCERTAIN. Chốt K=StableIndex=LastAppliedIndex.

Node trở lại gửi DatasetId, AcceptedEpoch, LastAppliedIndex và HeadHash. Node bị lệch dataset không được sửa lén bằng đổi DatasetId. Cùng lịch sử tiền tố thì gửi các event còn thiếu từ LastApplied+1. Nếu nhánh cũ có đuôi khác, cần RepairPlan do admin xác nhận, archive trước, rồi dựng lại từ seed và log hiện hành. Không auto overwrite log xung đột của node đang được dùng làm survivor trong promotion.

Transfer payload nhận và kiểm tra ngoài SQL transaction. Apply/rebuild cục bộ không chờ mạng trong transaction. Chỉ sao chép dữ liệu cụm; không copy NodeId, SQL credentials, private key hoặc quyền phục vụ. Sau replay phải so HeadHash và digest canonical của Seats, Reservations, Tickets, RequestResults cùng mốc K. Chỉ sau đó lưu AcceptedEpoch/Leader hiện hành và admit BACKUP_READY. Primary mở lại khi dữ liệu của chính nó Stable và vẫn có một backup hợp lệ.

**Quyền replay khác quyền ghi mới:** APPEND_APPLY bình thường chỉ nhận event mới do AcceptedLeader ở đúng AcceptedEpoch phát hành. REPLAY_ENTRY đi qua API riêng với RecoveryContext, được admin cài trước trên node đang freeze/RECOVERING: RunId, DatasetId, SourceNodeId được xác thực, TargetNodeId, TargetEpoch/Leader, base index/hash, K/head hash và loại Join hoặc Promotion. Context chỉ cấp quyền nhận lịch sử đã chốt; không cấp ServingEnabled và không nhận mutation từ client. Vì vậy node có AcceptedEpoch cũ vẫn nhận được replay được ủy quyền, trước khi AcceptedEpoch được cập nhật ở cuối. Event lịch sử giữ nguyên Epoch/WriterId/EventId/hash; kiểm tra quyền trên envelope/context hiện hành, không buộc event cũ mang epoch của nguồn mới. Context bị thu hồi sau hoàn tất hoặc hủy run. Thiếu context hoặc sai nguồn/mốc thì từ chối, không nới điều kiện của APPEND_APPLY.

Digest sắp theo khóa, chuẩn hóa null, GUID và thời gian; định nghĩa serializer chung trong Contracts. Không dùng CHECKSUM SQL ngắn làm bằng chứng duy nhất. Không có ReadSnapshot(K) lịch sử trên bảng hiện tại: muốn K cũ thì dựng projection tạm từ seed+log tới K. Không gọi phép so ở mốc tĩnh là thuật toán snapshot toàn cục ND6.

## 15 Giao thức và quyền truy cập

Mỗi frame gồm UInt32 big-endian N rồi N byte JSON UTF-8; 1 <= N <= 1 MiB. ReadExactly phải xử lý đọc thiếu, nhiều frame trong một lần đọc và EOF. Một socket có một writer tuần tự. Giới hạn client queue 256 yêu cầu; khi đầy trả RETRY_LATER trước khi nhận xử lý. Client command timeout 10 giây; replication timeout mỗi đợt 5 giây, vẫn có thể tiếp tục đối soát sau khi trả UNKNOWN. Các số này là cấu hình mặc định demo và phải ghi trong log test.

| Message | Mục đích và dữ liệu bắt buộc |
| --- | --- |
| LOGIN | Tài khoản quầy, tạo phiên trên node mới |
| HOLD/CONFIRM/CANCEL | DatasetId, RequestId, TripId, SeatNo, ReservationId khi cần; Owner từ phiên |
| GET_SEATS/GET_MY_RESERVATIONS | Đọc snapshot có thẩm quyền kèm Epoch/AppliedIndex |
| GET_RESULT | RequestId; FINAL chỉ từ tiền tố Stable, NOT_FOUND chỉ tại primary READY |
| GET_CLUSTER_STATUS | Vai trò, SQL health, LocalApplied, Stable, follower lag; không dùng như kết quả bán vé |
| APPEND_APPLY/ACK_APPLIED | Quyền peer hiện hành, event nguyên byte; ACK đúng event/hash sau SQL commit |
| REPLAY_ENTRY | RecoveryContext đã được admin cài trên node đóng gate; event lịch sử theo thứ tự, không sửa epoch cũ |
| ELECTION/ANSWER/COORDINATOR | SenderId, round, dataset và trạng thái đủ điều kiện; không cấp quyền ghi |

Envelope chung có ProtocolVersion=1, DatasetId, MessageId, CorrelationId, SenderId và Payload; RequestId ở các message liên quan. Peer envelope có AuthorityEpoch và SenderId được đối chiếu NodeState cho ghi mới, hoặc RecoveryContext cho replay. Epoch/WriterId bên trong event là thông tin lịch sử của event, không được gán lại bằng authority của nguồn replay. AppliedReceipt có cả AuthorityEpoch và EventEpoch để không trộn hai nghĩa.

Ba tài khoản A/B/C được provision trong cấu hình server nhất quán, không cần hệ thống đăng ký tài khoản. Dùng thư viện xác thực mật khẩu tiêu chuẩn; không lưu password rõ trong source hoặc log. Phiên đăng nhập có thể mất khi failover, nhưng OwnerId ổn định. Quyết định chọn thư viện/hash cụ thể được chốt khi triển khai và không tự viết thuật toán mật mã.

TLS server được client xác minh bằng chứng thư được tin cậy/pin trong cấu hình demo; peer dùng chứng thư riêng ánh xạ đúng NodeId của ba thành viên. Không dùng callback luôn trả true. Thông tin leader trong JSON không thay xác thực kết nối. Chặn admin và peer RPC trên cổng quầy. Không log mật khẩu, token phiên, connection string hoặc private key.

## 16 Hợp đồng module cho lập trình

Các tên dưới đây là hợp đồng C# dự kiến, không phải source đã build. Contracts là nơi duy nhất định nghĩa DTO, enum, canonical encoding và kiểu ID.

| Module/API | Input | Output và trách nhiệm |
| --- | --- | --- |
| ICommandCoordinator.SubmitAsync | AuthenticatedCommand, CancellationToken của chờ trả lời | CommandReply; cấp một grant, không hủy operation đã LocalApplied vì socket mất |
| IBookingStore.DecideAndApplyAsync | CommandIdentity, AcceptedAuthority, expected next index | PersistedEvent hoặc ExistingResult; transaction nguyên tử; chỉ primary gọi |
| IBookingStore.AppendAndApplyAsync | EventBytes, AuthenticatedPeerAuthority | AppliedReceipt hoặc GAP/CONFLICT; chỉ event mới đúng quyền hiện hành |
| IBookingStore.ApplyReplayEntryAsync | EventBytes, RecoveryContext | AppliedReceipt hoặc Conflict; cho lịch sử cũ giữ nguyên metadata, node đang đóng gate |
| IReplicaSender.EnsureOneBackupAsync | PersistedEvent, replication deadline | MatchingReceipt hoặc Uncertain; không tạo event mới |
| IBookingStore.ReadStableSnapshotAsync | expected AppliedIndex, loại đọc và owner | Snapshot và mốc từ cùng transaction; coordinator đã kiểm tra gate |
| IRecoveryService.CompareAsync | Hai participant đã freeze, RunId | PrefixReport hoặc Conflict; không tự chọn bằng NodeId |
| IRecoveryService.PromoteAsync | Manifest, bằng chứng fence, quyền admin | Activated hoặc Blocked; phải ghi/readback cả hai participant |
| IRecoveryService.RejoinAsync | Node recovering, source READY, RepairPlan khi cần | VerifiedAtK hoặc Blocked |
| IClientPendingStore | DatasetId,OwnerId,RequestId,canonical command | Persist trước gửi; atomic replace + flush; resolve cùng identity |

AuthenticatedCommand là command đã ràng buộc OwnerId từ phiên. CommandIdentity là DatasetId+RequestId+OwnerId+CommandHash và payload lệnh. PersistedEvent giữ nguyên PayloadBytes/EventHash/ResultPayload. AppliedReceipt chứa NodeId,AuthorityEpoch,EventEpoch,LogIndex,EventId,EventHash và RecoveryRunId nullable. Snapshot có DatasetId,Epoch,AppliedIndex và dữ liệu đọc. CommandReply tách TransportStatus khỏi BusinessResult, không dùng một enum OK cho cả hai.

RecoveryContext là quyền cục bộ có hạn theo RunId được mô tả ở mục 14; không suy ra từ field RunId do peer tự khai. ApplyReplayEntryAsync và AppendAndApplyAsync dùng chung lõi Apply transaction và kiểm tra prefix, nhưng có hai nhánh kiểm tra thẩm quyền riêng.

IAuthorityGate có quyền trong RAM và metadata đã chấp nhận; chỉ ClusterControl sửa. Admin transition và coordinator grant được đồng bộ bằng cùng một state machine, không để một thread mở phục vụ trong khi thread khác đang freeze. Read/replication network I/O có thể chạy riêng, nhưng mọi Apply nghiệp vụ trên một node phải có thứ tự và transaction. Dùng một mutex cục bộ không thay fencing toàn cụm.

## 17 Bootstrap và giao diện client

Bootstrap dữ liệu rỗng chỉ khi cả ba máy được kiểm tra là dataset mới: seed cùng DatasetId, K=0 và SeedHash; NodeId lấy riêng từ cấu hình. Bully chọn S3 trong ba node hợp lệ. Người vận hành ghi activation epoch 1/leader S3 trên cả ba, xác minh lại seed và metadata rồi mở S3. Một node có log sẵn không được coi là dataset mới chỉ vì UI đang trống.

Restart một process luôn bắt đầu RECOVERING. Nếu đó là primary, không tự lấy lại quyền. Restart cả cụm phải đóng gate cả ba, xem dữ liệu và manifest, rồi chạy phục hồi có kiểm soát; không reset seed hoặc DatasetId. Nếu có manifest dở dang, tiếp tục manifest theo mục 12 trước khi cấp quyền mới. Metadata không tương thích hoặc thiếu hai participant hợp lệ thì giữ BLOCKED.

UI hiển thị 10 ghế với nhãn AVAILABLE/HELD/SOLD bên cạnh màu. Tách biểu diễn “đang gửi/UNKNOWN” của lệnh khỏi trạng thái ghế đã xác nhận. Có danh sách giữ ghế của chính quầy. GET_RESULT chỉ trả ResultPayload cho đúng OwnerId từ phiên; tài khoản khác không được đọc vé/kết quả của người khác.

Mỗi client có thư mục dữ liệu riêng, một tiến trình giữ khóa file để không có hai instance cùng ghi một pending store. Trước gửi, ghi canonical command và identity bằng atomic replace và flush xuống đĩa. Khi restart, đăng nhập lại cùng OwnerId và resolve các pending. Không cấp RequestId mới khi timeout, đổi endpoint hoặc nhận NOT_FOUND. NOT_FOUND từ primary READY chỉ là không có trong lịch sử đang được giữ; retry cùng RequestId là cách tiếp tục an toàn.

SDK thử ba endpoint, xác thực server và chỉ gửi nghiệp vụ tới node báo PRIMARY_READY. Leader hint chỉ là gợi ý định tuyến. Bỏ response từ socket cũ khi đã chuyển endpoint/epoch và tra kết quả lại ở primary hiện hành. Cache chỉ để hiển thị “dữ liệu cũ”; không dùng để xác nhận bán thành công.

Thiếu backup đã biết trước khi thực hiện local transaction: không bắt đầu mutation mới, trả CLUSTER_NOT_READY. Backup có thể hỏng sau preflight; khi đó áp dụng nhánh UNKNOWN của mục 10. Nếu cả hai peer không sẵn sàng, đóng gate nghiệp vụ cho đến đối soát, không tự hạ xuống chế độ một server.

## 18 Ma trận lỗi và lời hứa chính xác

| Tình huống | Hành vi V12 | Cam kết |
| --- | --- | --- |
| Một backup dừng, primary và backup kia liên lạc | Ghi với ACK backup còn lại; nếu thiếu prefix thì catch-up | Tiếp tục bán sau khi đạt điều kiện Stable |
| Primary crash thật, hai backup hợp lệ còn | Bully candidate; fence; union tiền tố tương thích; activation | Phục hồi sau gián đoạn có thao tác vận hành |
| Heartbeat timeout nhưng không fence được | Candidate không mở ghi | Dừng chuyển quyền, không hứa tiếp tục bán |
| Phân vùng 1-2, primary ở phía hai node | Có thể tiếp tục nếu backup đó hợp lệ và ACK được | Không tự mở writer ở phía một node |
| Phân vùng 1-2, primary ở phía một node | Không đủ backup ACK; UNKNOWN/BLOCKED | Nhánh hai node không promote trước fencing |
| SQL primary lỗi/commit outcome không rõ | STORAGE_FAULT, dừng công bố, xác minh log | Không suy TCP sống nghĩa là phục vụ đúng |
| ACK/response mất | Giữ identity, gửi lại event hoặc GET_RESULT | Không nhân đôi tác động của RequestId được giữ |
| Peer hợp lệ có prefix/hash conflict | Đóng gate cụm để điều tra, giữ chứng cứ | Không chọn bên nào đúng theo ID hay độ dài |
| Node cũ quay lại | RECOVERING, freeze/catch-up/repair có kiểm soát | Không giành quyền hoặc phát lại đuôi cũ |
| Một node chưa rejoin xong rồi node khác hỏng | Thiếu hai node hợp lệ; BLOCKED | Ngoài đợt chịu một lỗi đã định nghĩa |
| Cả ba restart, ổ còn | Boot đóng quyền; đối soát manifest và log | Có thể phục hồi có kiểm soát, không hứa tự khởi động lại dịch vụ |
| Hai bản dữ liệu cần thiết cùng mất | Dừng, giữ dữ liệu còn lại | Không cam kết giữ mọi FINAL |

## 19 Lập luận tính đúng trong mô hình đã chốt

**Không bán trùng:** coordinator chỉ quyết định một mutation tại một thời điểm trên projection đã Stable. HOLD chỉ tạo giữ ghế khi AVAILABLE, CONFIRM chỉ chuyển reservation hợp lệ, SOLD không bán lại. Transaction giữ mọi quan hệ nhất quán và UNIQUE ticket/ghế chặn lỗi cục bộ. Follower áp dụng cùng Decision theo thứ tự. Sau chuyển quyền, mọi quyết định mới dựa trên tiền tố đã đối soát, không dựa riêng vào ID node.

**Không mất kết quả đã FINAL khi một node crash:** tại thời điểm trả FINAL, event và ResultPayload tồn tại trên hai node. Trong ba node, hai survivor sau một crash chứa ít nhất một bản của event đó. Vì hai log phải tương thích và recovery chọn tiền tố dài hơn rồi lưu ở cả hai trước READY, event và kết quả ấy không bị bỏ. Điều này dựa trên lưu trữ bền vững và xem đủ hai survivor; không dựa vào Bully.

**Retry không nhân đôi:** event/result mang RequestId và CommandHash cùng transaction; primary serialize kiểm tra trùng. Follower duplicate chỉ ACK. Recovery giữ nguyên ResultPayload của lịch sử được giữ; client retry cùng identity. Lệnh chưa từng FINAL và bị bỏ khỏi nhánh cũ có thể được quyết định lại, nhưng không có hai kết quả đã FINAL khác nhau theo lập luận trên.

**Một writer sau chuyển quyền:** primary cũ bị fence vật lý, mọi boot đóng gate, chỉ candidate trong manifest được cấp quyền sau hai bản metadata/dữ liệu khớp. Lập luận phụ thuộc quy trình quản trị tin cậy và không áp cho hai người tự mở quyền ngoài hệ thống. Epoch chỉ giúp từ chối bản tin cũ, không thay bước fence.

**Không lộ trạng thái chưa ổn định:** đọc nghiệp vụ qua cùng coordinator và chỉ khi StableIndex=LastAppliedIndex; transaction đọc lấy dữ liệu và mốc cùng nhau. Backup không phục vụ đọc. Thông tin quản trị local được phân biệt rõ trên UI.

Các lập luận là kiểm tra thuật toán trong mô hình đã nêu. Chương trình thực còn phải kiểm tra migration, SQL isolation, serializer, I/O, cancellation và quyền admin bằng test tích hợp/fault injection. Mô hình hữu hạn, nếu có, không chứng minh chương trình C# hay SQL đã đúng.

## 20 Kế hoạch kiểm thử và điều kiện đạt

| ID | Tình huống | Oracle bắt buộc |
| --- | --- | --- |
| T01 | Bootstrap đúng/sai dataset | Ba bản seed đúng; dữ liệu cũ không bị reset âm thầm |
| T02 | Ba quầy HOLD cùng ghế | Một OK_HELD; hai lỗi nghiệp vụ cố định |
| T03 | HOLD rồi CONFIRM | Một ticket, ghế SOLD, reservation CONFIRMED |
| T04 | HOLD rồi CANCEL rồi HOLD lại | Lịch sử giữ cũ CANCELLED; một giữ mới hợp lệ |
| T05 | CONFIRM/CANCEL sai chủ | Không đổi ghế/vé, kết quả cố định nếu lệnh hợp lệ |
| T06 | CONFIRM lại với ID cũ và ID mới | ID cũ cùng OK_SOLD; ID mới ALREADY_CONFIRMED cùng ticket |
| T07 | Retry đồng thời cùng RequestId | Một event/result, không thêm tác động |
| T08 | Cùng RequestId đổi nội dung/chủ | REQUEST_CONFLICT; không ghi đè |
| T09 | Bán đủ 10 ghế và yêu cầu dư | Tối đa 10 ticket, không bán lại SOLD |
| T10 | Local commit rồi mất replication | Không FINAL, không đọc trạng thái local; lệnh sau không chạy |
| T11 | Backup Apply rồi mất ACK | Retry APPEND không Apply lần hai; đủ chứng cứ thì trả cùng result |
| T12 | Chỉ chặn một ACK, backup khác ACK được | Có thể FINAL với ACK hợp lệ còn lại |
| T13 | Kill primary trước local SQL commit | Không có nửa transaction; retry cùng identity |
| T14 | Kill primary sau local commit trước replica | Không mất FINAL trước đó; xử lý đuôi chưa FINAL đúng quy tắc |
| T15 | Kill primary sau backup Apply trước ACK | Survivor có event phải được giữ/nhân bản trước READY |
| T16 | Kill primary sau ACK trước response | GET_RESULT sau phục hồi trả đúng result |
| T17 | Mất response FINAL và kill/restart client | Pending resolve cùng TicketId/ReservationId |
| T18 | Candidate stale, survivor khác mới hơn | Candidate lấy lịch sử từ survivor, không chọn DB theo ID |
| T19 | SQL exception sau commit nhưng client thư viện báo lỗi | Dừng gate; tra RequestId, không tạo event thứ hai |
| T20 | Backup crash và rejoin | Freeze, replay, hash/digest cùng K; NodeId không bị copy |
| T21 | Node cũ có đuôi khác ở LogIndex được dùng lại | Archive/repair; epoch/event/hash cũ bị từ chối |
| T22 | Mất heartbeat, old primary vẫn chạy | Không promote khi chưa fence |
| T23 | Partition với primary ở mỗi phía | Hành vi đúng hai dòng ma trận lỗi; không hai writer |
| T24 | Hai node đồng thời bầu, mất ANSWER/COORDINATOR | Tái bầu, hội tụ khi hai node liên lạc ổn định; chưa tự mở gate |
| T25 | Crash trong ghi manifest ở participant thứ nhất | Cả hai đóng quyền; tiếp tục cùng RunId rồi mới mở |
| T26 | Chỉ còn một participant hợp lệ | Không mở quyền/FINAL ghi mới |
| T27 | ACK cũ/sai event/sai hash | Không tăng StableIndex |
| T28 | GAP, duplicate và prefix conflict | Replay đúng mốc; duplicate ACK; conflict đóng gate |
| T29 | GET_SEATS xen giữa LocalApplied và Stable | Không trả snapshot chưa công bố hoặc mốc sai |
| T30 | Đối soát K khi đang có yêu cầu mới | Freeze trước; dữ liệu và result cùng K |
| T31 | Frame chia/gộp, EOF, size ngoài giới hạn | Không lệch JSON/frame, đóng kết nối đúng, không sửa nghiệp vụ |
| T32 | Queue đầy/socket mất sau bắt đầu operation | RETRY_LATER trước nhận; operation đã bắt đầu không bị bỏ |
| T33 | Giả peer/owner/leader hint | Không vượt xác thực hoặc đổi quyền ghi |
| T34 | Pending dataset cũ hoặc hai client dùng cùng store | Từ chối dataset, khóa store; không replay chéo |
| T35 | Restart ba process với dữ liệu còn | Không tự mở bán; bảo toàn FINAL sau đối soát |
| T36 | Một node chưa sẵn sàng rồi thêm lỗi | Dừng đúng giới hạn, không báo chịu được hai lỗi |

Mỗi record gồm commit SHA, DatasetId, epoch, IP/node, cấu hình timeout, mốc gây lỗi, request/event/hash, log ba máy và SQL oracle. Chỉ PASS khi bằng chứng đúng. Kiểm tra LocalApplied, Stable và FINAL riêng, không chỉ nhìn màu ghế.

Đo latency từ một đồng hồ đơn điệu tại client; báo số mẫu, median/p95/max và điều kiện tải. Đo failover tách phát hiện, chờ người vận hành/fence, đối soát, activation và reconnect. Không lấy timestamp hai máy chưa đồng bộ trừ trực tiếp. Không biến số test dự kiến thành số test đã chạy.

## 21 Phân công và các mốc tích hợp

| Thành viên | Trách nhiệm | Điều kiện bàn giao |
| --- | --- | --- |
| TV1 | Schema, transaction Apply, oracle và rebuild projection | Chứng minh nguyên tử, FK/UNIQUE và SQL failure handling |
| TV2 | Domain, coordinator, grant/release, dedup và snapshot đọc | T02-T10, T29; không nhận lệnh mới khi chưa Stable |
| TV3 | Bully, authority gate, manifest/promotion và peer identity | T22-T27, T33; không tự mở quyền |
| TV4 | Replication, replay, hash, peer framing và fault hooks | T11-T21, T28, T30-T31 |
| TV5 | WinForms, SDK/pending/reconnect, tổng hợp demo | T17, T32-T35; mỗi chủ module tự cung cấp test/log phần mình |

G0 chốt DTO/canonical bytes/schema và các quy tắc V12. G1 kiểm tra domain/SQL/coordinator một server bằng unit hoặc local integration; chưa gọi đó là PASS chịu lỗi. G2 có ít nhất primary+backup thật để nghiệm thu FINAL/dedup; chạy lại test đầu cuối. G3 đủ ba server để nghiệm thu crash/candidate stale/rejoin. G4 ghép Bully với fencing/manifest, lỗi TCP/SQL và client restart. G5 hoàn thiện số liệu, đối chiếu nội dung học và video demo.

Không có chế độ một server âm thầm trả FINAL như cấu hình ba bản sao. Nếu làm dev mode riêng, phải gắn nhãn và không dùng kết quả đó cho nghiệm thu bản cơ sở.

## 22 Nguồn và quy tắc dùng tài liệu

Nguồn ưu tiên là [đề HTPT 26-27](<C:/Users/ADMIN/Downloads/Danh sach do an mon hoc HTPT 26-27.doc>), [ND7](<C:/Users/ADMIN/Downloads/Noi dung 7 -  Loai tru tuong ho- giai thuat bau cu.pptx>), [ND11](<C:/Users/ADMIN/Downloads/Noi dung 11 - quan tri giao dich va dieu khien tuong tranh.pptx>), [ND12](<C:/Users/ADMIN/Downloads/Noi dung 12 - phuc hoi va chiu loi.pptx>) và [ND14](<C:/Users/ADMIN/Downloads/Noi dung 14 - Nhát quan va nhan ban.pptx>). ND1, ND2, ND10, ND13 hỗ trợ kiến trúc, I/O, định danh và quyền. Bài giảng Nguyễn Xuân Anh 2014, mục 2.3.3.1, 2.3.5.1, 2.6.5 và 2.8.5.2 hỗ trợ giải thích thuật ngữ.

[Đề cương INT1405 PTIT](https://clc.ptit.edu.vn/wp-content/uploads/2023/02/DISTRIBUTED-SYSTEMS.pdf) chỉ được dùng đối chiếu phạm vi, không lấy làm rubric của lớp. [Trang sách Tanenbaum và van Steen bản 2](https://www.distributed-systems.net/index.php/books/ds2/) là nguồn học liệu liên quan được môn giới thiệu, không phải yêu cầu dùng công nghệ mới. [Microsoft về độ bền transaction](https://learn.microsoft.com/en-us/sql/relational-databases/logs/control-transaction-durability?view=sql-server-ver16) hỗ trợ lựa chọn fully durable commit; không chứng nhận giao thức tự viết đã đúng.

[GitHub Phunguy65](https://github.com/Phunguy65/train-ticket-booking-system/blob/main/DEVELOPMENT.vi.md) dùng tham khảo tổ chức C#/TCP/WinForms/SQL, không làm bằng chứng ba bản sao. [GitHub ohbriansung](https://github.com/ohbriansung/Distributed_Event_Purchase_System) gợi ý Bully, UUID và test, không kế thừa tuyên bố chịu N-1 lỗi. [RailSync Bologna](https://apice.unibo.it/bin/view/Courseproject/RailSync) gợi ý acceptance test; không đưa Redis/Mongo/microservice sang bản cơ sở.

[Báo cáo nhóm 19 trên Studocu](https://www.studocu.vn/vn/document/hoc-vien-cong-nghe-buu-chinh-vien-thong/quan-tri-ban-hang-va-phan-phoi-san-pham/nhom-19-bao-cao-he-thong-phan-tan/43794360) có tên thầy Bảo nhưng thuộc đồng bộ thời gian; chỉ học cách trình bày và phân tích. Không lấy báo cáo sinh viên làm CLO hoặc xác nhận được giảng viên chấp thuận. Phạm vi tham khảo GitHub/Studocu là tài liệu công khai, chưa build/test độc lập source của các repo.

## 23 Bảng khóa quyết định để tránh code lệch

Trước khi viết module, cả nhóm dùng chung các quyết định sau: một dataset; ba member cố định; một writer; một mutation chưa Stable; event bất biến; Apply transaction nguyên tử; ACK sau durable commit; đủ primary+backup mới FINAL; không đọc trạng thái chưa Stable; retry cùng RequestId; không tự promote; inspect hai survivor; manifest một RunId; node cũ RECOVERING; prefix conflict thì dừng.

Mọi đề xuất đổi một quyết định trên phải cập nhật thiết kế, hợp đồng và test liên quan trước khi merge. Không trộn REPLICATE_PREPARE/COMMIT của V11 với APPEND_APPLY của V12. Không sửa enum/protocol ở riêng một module. V12 là đặc tả để triển khai; mức nghiệm thu cuối cùng dựa trên code và bằng chứng chạy đúng các điều kiện đã nêu.
