# Review thiết kế đồ án bán vé tàu 10 ghế theo học phần HTPT PTIT

Đã có [thiết kế triển khai V12](<C:/Users/ADMIN/Downloads/DoAnHTPT_Nhom9_2026/docs/De6_Thiet_Ke_Trien_Khai_V12.md>) sau review kiến trúc bổ sung. Bản review này lưu các nhận xét đối với V11; dùng V12 làm đặc tả triển khai hiện hành. Đặc biệt, Apply tại primary trước replication không tự nó sai nếu khóa công bố và chỉ FINAL sau đủ bản bền vững; không áp dụng máy móc yêu cầu giữ PREPARED/COMMITTED của V11 vào giao thức V12.

Ngày đối chiếu: 14/11/2026. Đối tượng: bản thiết kế V11 của đề 6, giảng viên được người học xác định là ThS. Lê Ngọc Bảo, PTIT cơ sở TP.HCM.

Căn cứ đề bài chính: `Danh sach do an mon hoc HTPT 26-27.doc`, theo lựa chọn ưu tiên của người học. Người học chưa xác nhận chính xác phiên bản lớp đang dùng; việc chọn bản 26-27 là căn cứ làm review, không phải xác nhận từ giảng viên.

**Kết luận: nên giữ hướng kiến trúc, nhưng V11 cần sửa trước khi dùng làm đặc tả thống nhất để chia code hoặc bảo vệ.** Ba cơ chế chính có căn cứ trong slide của thầy. Các phần còn thiếu chủ yếu là xác định đúng phiên bản đề, chứng minh cách cài thuật toán, khép kín quy tắc phục hồi và bổ sung minh chứng. Chưa có căn cứ xác nhận đạt CLO chính thức của lớp hay xác nhận phần mềm chạy đúng.

Review này phân biệt ba loại nhận xét: sai lệch xác định được ngay trong văn bản; đặc tả còn thiếu có thể dẫn đến các cách cài khác nhau; và khuyến nghị thu gọn để làm được trong phạm vi môn học. Các tình huống phản ví dụ dưới đây là phân tích thiết kế, chưa phải lỗi đã tái hiện trên chương trình của nhóm.

## 1 Căn cứ và mức độ đã kiểm tra

Đã đọc toàn bộ phần văn bản và 18 bảng của V11, xem hai hình nhúng. Đã đối chiếu các slide địa phương của thầy, trọng tâm ND1, ND2, ND7, ND10, ND11, ND12, ND13 và ND14; trích xuất bài giảng Nguyễn Xuân Anh 2014 và kiểm tra các mục về loại trừ tương hỗ, bầu chọn, cam kết và nhân bản. Đã so sánh nội dung đề 6 trong hai file danh sách đồ án ở Downloads.

Nguồn web được đọc ở mức đề cương, README, tài liệu phát triển và trang giới thiệu dự án. Chưa clone, build hay chạy độc lập các repo GitHub. Chưa có source đồ án, SQL đi kèm hoặc kết quả chạy ba máy để kiểm thử thực tế. Không suy từ README sang kết luận chương trình đã được chứng minh đúng.

Đã thử dựng trang Word bằng renderer đóng gói nhưng môi trường thiếu `soffice.exe`. Vì vậy, review không xác nhận phân trang hoặc bố cục toàn bộ DOCX, không sử dụng số trang Word chưa được kiểm chứng. Vị trí góp ý được chỉ bằng số mục của V11. File V11 gốc được giữ nguyên.

## 2 Phát hiện cần sửa đầu tiên là phiên bản đề bài

Hai nguồn địa phương có nội dung khác nhau:

| Nguồn | Nội dung liên quan đề 6 | Hệ quả |
| --- | --- | --- |
| `Danh sach do an mon hoc.doc` | Mô phỏng bán vé tàu cao tốc với 10 số ghế | Khớp với cách V11 mô tả bản đề cũ |
| `Danh sach do an mon hoc HTPT 26-27.doc` | Ngoài 10 ghế còn yêu cầu xây dựng mô hình bản sao dữ liệu hoặc cụm dữ liệu để hệ thống hoạt động trong các tình huống | Nhân bản/cụm dữ liệu đã nằm trong dòng đề 6 của bản này |

**Đối chiếu theo bản đề 26-27 đã được người học chọn làm căn cứ chính, mục Tóm tắt điều hành, 1.1 và nguồn [ĐỀ] của V11 đang ghi sai xuất xứ yêu cầu.** Cần ghi nhân bản/cụm dữ liệu là yêu cầu trong đề 6, thay cho cách gọi đây chỉ là yêu cầu người học bổ sung. Bản đề cũ được giữ để giải thích sự khác biệt giữa hai phiên bản.

Câu sửa đề xuất:

> Đề 6 trong danh sách đồ án HTPT 26-27 yêu cầu mô phỏng bán vé tàu cao tốc 10 ghế và xây dựng mô hình bản sao hoặc cụm dữ liệu. Nhóm lựa chọn ba máy chủ lưu bản sao độc lập và ba tiến trình quầy để hiện thực yêu cầu này. Báo cáo liệt kê rõ các tình huống lỗi được kiểm thử và những giới hạn còn lại.

Không chuyển cụm “mọi tình huống” thành cam kết kỹ thuật tuyệt đối. Cần diễn giải bằng ma trận: lỗi một server, lỗi SQL, mất phản hồi, backup chậm/cũ, mất liên lạc và restart. Với tình huống không thể tiếp tục bán an toàn, trình bày rõ cách phát hiện, dừng ghi và phục hồi. **Dừng an toàn thể hiện xử lý lỗi nhưng chưa chứng minh tiếp tục cung cấp đầy đủ dịch vụ.** Nếu yêu cầu chấm bài là tự chuyển server không có thao tác người vận hành, V11 hiện chưa đáp ứng yêu cầu đó.

## 3 Đối chiếu với môn học và CLO

Đề cương [INT1405 trên website PTIT](https://clc.ptit.edu.vn/wp-content/uploads/2023/02/DISTRIBUTED-SYSTEMS.pdf) xác nhận học phần 3 tín chỉ, có các nội dung kiến trúc, tiến trình, truyền thông, định danh, đồng bộ, nhất quán/nhân bản, chịu lỗi và bảo mật. Tài liệu do CNTT1 biên soạn, không có bảng mã CLO của lớp thầy Lê Ngọc Bảo. Chỉ dùng làm căn cứ tham chiếu phạm vi; không lấy tỷ lệ điểm trong tài liệu đó áp cho lớp TP.HCM.

URL chương trình đào tạo [W1] mà V11 dẫn không truy xuất được trong phiên kiểm tra này. Đề cương công khai ở trên là nguồn kiểm tra bổ sung, không phải bằng chứng đã kiểm chứng được mọi nội dung tại URL [W1]. Lỗi truy xuất hiện tại cũng chưa đủ để kết luận liên kết đó đã hỏng vĩnh viễn.

V11 làm đúng khi gọi MT01–MT07 là mã đối chiếu nội bộ. Tuy nhiên, bảng hiện thiên về liệt kê công nghệ. Để chứng minh kết quả học tập, mỗi dòng cần có hành động sinh viên thực hiện được, bằng chứng và điều kiện đánh giá. Bảng dưới đây là **đối chiếu đề xuất**, chưa phải CLO chính thức.

| Kết quả cần chứng minh | Căn cứ trực tiếp trong slide thầy | V11 hiện có | Minh chứng nên bổ sung |
| --- | --- | --- | --- |
| Giải thích vì sao đây là HTPT và phân biệt mô hình logic/vật lý | ND1 slide 3 và 23–24; ND10 slide 2, 7–8 | Sơ đồ 5 máy, 3 SQL riêng | Chỉ ra tiến trình, không gian nhớ, kênh mạng và dịch vụ chung; phân biệt 5 máy với 6 tiến trình ứng dụng |
| Thiết kế trao đổi thông điệp và định danh yêu cầu | ND2 slide 2, 11–12, 16–17, 23–25 | TCP/JSON, RequestId, NodeId | Trace một yêu cầu từ SDK đến server; tách định danh nút khỏi IP/cổng; test chia/gộp frame |
| Phân tích và cài điều phối vùng găng | ND7 slide 2–3 | FIFO và một mutation worker | Ánh xạ Request/Grant/Release; chỉ rõ ai thực thi vùng găng; log không có hai lượt cùng vào |
| Thực hiện bầu chọn và giải thích điều kiện áp dụng | ND7 slide 9–10 | Election/Answer/Coordinator và candidate | Trace nhiều nút đồng thời bầu; timeout chờ Coordinator; phân biệt kết quả bầu với quyền phục vụ |
| Bảo vệ giao tác và tránh xung đột | ND11 slide 2–4, 6–8 | ACID, UNIQUE, các bất biến | Một transaction Apply đủ các bảng; giải thích vì sao SQL cục bộ không đồng nghĩa cam kết toàn cụm |
| Lựa chọn mô hình nhất quán và nhân bản | ND14 slide 5–7, 13, 15 | Primary-based push và replay | Chốt ngữ nghĩa đọc/ghi; trình bày lịch sử thao tác; so sánh các bản sao đúng cùng mốc |
| Phân tích lỗi và thực hiện phục hồi | ND12 slide 2–3, 6, 8–9, 11–14 | Crash checkpoints, log, fencing | Chứng minh dữ liệu đã FINAL được giữ; giải quyết đuôi chưa hoàn tất; đo thời gian phục hồi |
| Thực hiện xác thực và kiểm soát quyền phù hợp | ND13 slide 4–5, 8 | OwnerId, peer authentication được đề xuất | Sai chủ bị từ chối; bản tin peer giả bị từ chối; nêu đúng phần nào đã cài |

Không cần đưa toàn bộ chương trình học vào một đề tài. Đề 6 có thể tập trung vào tương tranh, nhân bản và chịu lỗi; không cần thêm thuật toán đồng hồ, di trú mã, Web Service hoặc SOA chỉ để có tên mọi chương. Tương tự, có nội dung bảo mật trong môn không có nghĩa phải triển khai một hệ thống định danh doanh nghiệp.

**Cần phân biệt ba mức:** có liên hệ với nội dung môn; có thiết kế minh họa nội dung đó; có minh chứng thực hiện đạt yêu cầu. V11 hiện mạnh nhất ở mức thứ hai. Việc xác nhận CLO cụ thể vẫn cần đề cương/rubric đúng lớp và sản phẩm thực nghiệm.

## 4 Ba cơ chế có phù hợp bài giảng không

### Loại trừ tương hỗ tập trung

Phù hợp về ý tưởng. ND7 slide 3 mô tả yêu cầu vào/ra vùng găng qua server khóa, được cấp quyền rồi trả quyền. Bài giảng Nguyễn Xuân Anh, mục 2.3.3.1, nêu Request, Grant, Release.

V11 mục 4.1 đang đưa toàn bộ thao tác về primary để primary tuần tự thực thi. Cách này có thể bảo vệ vùng găng, nhưng cần gọi đúng là **điều phối tập trung với server thực thi thay client** nếu giữ cách cài hiện tại. Một hàng đợi nội bộ chưa tự chứng minh đã hiện thực nguyên dạng giao thức cấp quyền cho các tiến trình từ xa.

Không cần mở quyền SQL cho client hay thêm vòng xin khóa qua mạng chỉ để tạo đủ tên bản tin. Cần sơ đồ ánh xạ: yêu cầu của quầy vào hàng đợi; coordinator cấp lượt cho worker; worker giữ quyền trong suốt quyết định và hoàn tất thao tác; coordinator giải phóng lượt. Nếu thầy yêu cầu nguyên dạng client nhận Grant rồi thực thi vùng găng, phải đáp ứng yêu cầu đó và tách quyền truy cập dữ liệu cho an toàn.

### Bully

ND7 slide 9–10 có đúng thuật toán và ba loại thông điệp V11 dùng. Việc phân biệt bầu chọn với cho phép ghi là hợp lý.

Tuy nhiên, mục 4.2 phải đủ các nhánh: nhận Election thì trả Answer và phát động bầu với nút cao hơn; nhận Answer thì đợi Coordinator; hết thời gian đợi Coordinator phải có hành động; xử lý hai vòng bầu đồng thời và thông điệp cũ. Đây là hoàn thiện cách cài Bully, không phải thêm một thuật toán bầu khác.

Khi S3 trở lại, nên quy định S3 ở trạng thái RECOVERING, chưa thuộc tập đủ điều kiện phục vụ/bầu chọn. Sau khi đồng bộ, nếu hệ thống chủ động trì hoãn chuyển vai trò để tránh gián đoạn, ghi đó là chính sách vận hành của nhóm. Không mô tả như thể Bully chuẩn mặc nhiên cho phép bỏ qua mọi nút ID cao hơn đã đủ điều kiện.

### Nhân bản dựa trên bản chính

ND14 slide 15 nêu mọi cập nhật qua bản chính rồi lan truyền đến bản sao; ND12 slide 6 liên hệ bản chính, sao lưu và bầu chọn khi primary hỏng. Ba SQL độc lập và các backup nhận log phù hợp hướng này.

PREPARED/COMMITTED, ACK bền vững và replay là cách nhóm cụ thể hóa cơ chế. **Việc chỉ đặt tên là “kỹ thuật hỗ trợ” không làm giảm nghĩa vụ mô tả và chứng minh các pha đó.** Đồng thời, ND12 slide 11 đã có cam kết phân tán và nhắc hai pha/ba pha. Vì vậy, không nên tuyên bố mọi khái niệm cam kết nhiều pha đều ngoài môn. Giao thức V11 cũng chưa đủ cơ sở để tự gọi là cài đặt 2PC chuẩn.

Trong các nguồn đã kiểm tra, chưa thấy yêu cầu của thầy bắt buộc “đúng ba thuật toán duy nhất”. Câu trả lời bảo vệ ở mục 14.2 nên đổi thành: nhóm chủ động chọn ba cơ chế để giải quyết ba vấn đề của đề 6 và giữ quy mô khả thi.

## 5 Các điểm cần sửa theo mức ưu tiên

P0 dưới đây là điểm chặn việc coi V11 là đặc tả đã kín. P1 là phần phải làm rõ trước khi tích hợp/nghiệm thu; P2 là cải thiện trình bày hoặc khối lượng.

| ID | Mức | Vị trí V11 | Nhận xét | Việc sửa cụ thể |
| --- | --- | --- | --- | --- |
| R01 | P0 theo căn cứ đề đã chọn | Tóm tắt, 1.1, 16 | Gán yêu cầu nhân bản cho phần bổ sung, trái với bản đề 26-27 | Dẫn bản đề 26-27, sửa xuất xứ yêu cầu |
| R02 | P0 | 4.1 so với 8.2 | Trả UNKNOWN rồi giải phóng lượt mâu thuẫn với chặn lệnh sau đến khi kiểm chứng | Tách trạng thái phản hồi cho client và trạng thái giải quyết nội bộ |
| R03 | P0 | 7.1, 8.1–8.4, 9.2 | Dấu COMMITTED cục bộ và mức được lưu trên hai máy chưa phân biệt rõ | Định nghĩa local marker, điều kiện FINAL và quy tắc phục hồi tương ứng |
| R04 | P0 | 8.4, 9.2, 10.1 | Chưa có quy tắc hoàn chỉnh xử lý PREPARED bị bỏ, SequenceNo và máy cũ quay lại | Chốt lịch sử được giữ, cách lưu dấu loại bỏ và ngăn thông điệp cũ |
| R05 | P0 | 9.2, 11.1, 13.1 | Epoch/fencing và quyền gửi COMMIT chưa thành hợp đồng đủ cụ thể | Ràng buộc peer với primary hiện hành, epoch được chấp nhận và thứ tự chuyển quyền |
| R06 | P1 | 4.1 | Hàng đợi được gọi là thuật toán trong slide nhưng thiếu ánh xạ giao thức | Bổ sung vai trò, vùng găng, Request/Grant/Release và log minh họa |
| R07 | P1 | 4.2, 9 | Bully mới minh họa trường hợp S3 chết, S2 thắng | Bổ sung các nhánh timeout, cạnh tranh bầu và điều kiện rejoin |
| R08 | P1 | 4.3, 6.3, 10 | Chưa chốt mô hình nhất quán quan sát được bởi client | Quy định primary reads, mốc Applied, thứ tự yêu cầu và giới hạn khi chuyển quyền |
| R09 | P1 | 10.1, 13.1 | REPLAY_FROM(lastPrepared/lastCommitted) có thể bỏ qua dấu commit còn thiếu | Dùng tiền tố đã xác minh; nêu rõ replay có bao gồm mốc bắt đầu không |
| R10 | P1 | 7.2, 10.1, 13.1 | ReadSnapshot(K) chưa có cách lấy lịch sử đúng mốc K | Đối soát khi dừng mutation và các bản sao cùng K, hoặc dựng lại từ log |
| R11 | P1 | 7.1–7.3 | Ràng buộc trong bảng chưa chứng minh được quan hệ xuyên bảng | Bổ sung DDL và Apply transaction, kiểm tra trạng thái/owner/seat nhất quán |
| R12 | P1 | 7.3, 15 | Nói có SQL đi kèm nhưng chưa tìm thấy trong hồ sơ hiện có | Đính kèm đúng script hoặc sửa thành sản phẩm dự kiến |
| R13 | P1 | 13.2 | G1 một server nhưng nghiệm thu T01–T10 có yêu cầu cụm và durable backup ACK | Tách test domain một server khỏi nghiệm thu phân tán; không nới điều kiện FINAL âm thầm |
| R14 | P1 | 12 | Test plan còn thiếu các nhánh phản ví dụ quan trọng | Bổ sung các test ở phần 8 của review |
| R15 | P1 | 2, 14.2 | Chưa có CLO/rubric đúng lớp nhưng có câu chữ dễ gợi đã khớp hoàn toàn | Giữ ma trận nội bộ, thêm bằng chứng và trạng thái xác minh |
| R16 | P2 | 13 | TV5 gánh UI, SDK, TCP, harness và bằng chứng | Chuyển phần transport/harness cho các chủ module liên quan |
| R17 | P2 | Hai hình và nhiều mục kết luận | Hình thứ nhất dễ khiến hiểu S1–S3 chỉ nối qua S2; hình thứ hai có chữ tách giữa từ | Nêu rõ peer logical full mesh nếu dùng Bully trực tiếp; sửa nhãn hình |

### R02 UNKNOWN không được tự mở lượt mutation tiếp theo

Tại mục 4.1, luồng ghi “trả FINAL hoặc UNKNOWN; giải phóng quyền xử lý”. Tại bước 5 mục 8.2, mất ACK phải “chặn lệnh sau” đến khi kiểm chứng. Hai cách cài này có thể cho hành vi khác nhau.

Ví dụ lệnh A giữ ghế 05 đã đến backup nhưng primary chưa xác định hoàn tất. Nếu giải phóng lượt rồi cho lệnh B quyết định dựa trên projection chưa Apply A, B có thể dùng trạng thái không đúng tiền tố cần được giữ. Kết quả cụ thể tùy cách cài, nhưng đặc tả hiện chưa loại trừ khả năng đó.

Câu sửa đề xuất:

> UNKNOWN là kết quả giao tiếp cho client khi chưa xác định được kết quả. Nếu mutation hiện tại chưa được giải quyết thành quyết định có thể phục hồi hoặc loại bỏ theo quy trình, worker không nhận mutation tiếp theo. Việc client ngắt kết nối hoặc chưa lưu được phản hồi không tự thay đổi quyết định đã cam kết của server.

Sau khi server đã hoàn tất an toàn, không giữ toàn bộ hàng đợi chỉ để đợi client xác nhận đã lưu FINAL. Kết quả đã lưu cho phép client tra lại.

### R03 COMMITTED cục bộ chưa đồng nghĩa đã có hai bản bền vững

Bước 5 mục 8.2 đánh COMMITTED ở primary trước khi gửi sang backup. Như vậy, tồn tại thời điểm chỉ primary có marker COMMITTED, trong khi bảng 8.1 mô tả COMMITTED đã có trên primary và ít nhất một backup.

Cần tách tối thiểu:

- Marker cục bộ cho một entry và payload cụ thể.
- Điều kiện trả FINAL: marker đã lưu bền vững ở primary và ít nhất một backup; Apply tại primary thành công.
- Quy tắc phục hồi: tập dữ liệu nào được xem, entry nào bắt buộc giữ và trường hợp nào phải dừng đối soát.

Không nhất thiết thêm bảng mới. Có thể giữ marker hiện tại và mô tả rõ mức chứng cứ tại coordinator. Nhưng không được dùng một tên trạng thái để ngụ ý cả hai mức đã được chứng minh.

Trong mô hình ba nút cố định, chịu một nút crash, hai nút còn lại có giao với cặp lưu bền vững của mọi lệnh đã FINAL. Đây là cơ sở lập luận giữ kết quả đã xác nhận, **với điều kiện phải khảo sát cả hai nút sống và giữ đúng lịch sử**, không chỉ đọc database của ứng viên có ID cao hơn. Lập luận này không biến Bully thành giao thức đồng thuận.

Với entry chưa từng trả FINAL, có thể phải hoàn tất nó trong quá trình phục hồi nếu có quyết định hợp lệ cần giữ. UNKNOWN không có nghĩa là lệnh chắc chắn chưa xảy ra.

### R04 Đuôi PREPARED phải được giải quyết bằng quy tắc tái hiện được

Giả sử S3 và S1 có PREPARED tại seq 8, S2 mới tới seq 7, rồi S3 chết. Sau khi fence S3, S2 được chọn. Câu “cách ly để quyết định bỏ” chưa trả lời: seq 8 giữ lại hay tái sử dụng, RequestId cũ trả kết quả nào, marker COMMIT đến muộn bị xử lý thế nào, và S3 trở lại có được mang entry cũ vào không.

V11 vừa yêu cầu SequenceNo liên tục vừa chưa có trạng thái/biên bản dữ liệu mô tả các entry bị loại. Nếu mỗi module tự chọn một cách xử lý, replay và UNIQUE RequestId dễ xung đột.

Đề xuất trong phạm vi bài: phục hồi khi cụm dừng mutation, fence primary cũ, xác định một tiền tố được giữ, lưu lại phần đuôi bị loại để kiểm toán, chốt quy tắc đánh số và epoch của lịch sử hiện hành. Không xóa dữ liệu đã FINAL; không tái sử dụng một định danh sự kiện khiến thông điệp cũ có thể được chấp nhận nhầm. Có thể chọn quy ước phù hợp schema, nhưng phải mô tả và kiểm thử nó trước khi chia code.

### R05 Epoch chỉ có tác dụng khi phía nhận kiểm tra quyền

IAuthorityGate ở phía primary chưa đủ nếu backup vẫn nhận bản tin có SenderId tùy ý hoặc COMMIT từ một epoch đã kết thúc. Cần hợp đồng: cùng ClusterId/DatasetId; đúng thành viên được xác thực; đúng primary và epoch đã được công nhận; đúng SequenceNo và payload; bản tin lặp hợp lệ thì trả cùng ACK.

Trong demo có điều khiển thủ công, việc tắt hẳn tiến trình hoặc máy cũ có thể là biện pháp fencing vật lý. Cần chặn tự khởi động lại thành writer; mọi lần boot quay về RECOVERING như V11 đã nêu. Nút cũ chỉ rejoin sau đối soát, không nhận quyền từ cờ ServingEnabled lưu trước crash.

Thao tác PROMOTE phải có kiểm tra điều kiện và bản ghi vận hành, tránh hai người cùng bật hai ứng viên. Không cần thêm một dịch vụ điều phối mới để đạt điều này trong mô hình demo đã giới hạn.

### R08 Đọc dữ liệu cần ngữ nghĩa rõ ràng

Khuyến nghị giữ mọi đọc có thẩm quyền tại PRIMARY_READY. Yêu cầu GET_SEATS phải trả trạng thái ghế và AppliedSequence từ cùng một ảnh chụp đọc nhất quán, không đọc ghế trước rồi lấy mốc mới sau một mutation khác.

Mức công bố dễ bảo vệ là thứ tự mutation thống nhất tại primary, backup áp dụng cùng thứ tự, đọc có thẩm quyền thấy dữ liệu đã Apply. Sau failover phải khôi phục tiền tố được giữ trước khi mở đọc/ghi. Nếu muốn khẳng định nhất quán tuyến tính, cần xác định điểm tuyến tính hóa và chứng minh cả thao tác đọc, yêu cầu chồng lấn, UNKNOWN và chuyển quyền. Không chỉ dựa vào “một writer” hoặc ba hash bằng nhau.

Nhất quán ở giao diện client cũng khác với độ trễ cập nhật hình ghế: ba quầy có thể hiển thị mốc khác nhau trong lúc polling mà server vẫn ngăn đặt trùng. UI phải hiển thị hoặc xử lý dữ liệu cũ đúng cách.

### R09 và R10 Replay và đối soát cùng mốc

Một backup có LastPrepared=12, LastCommitted=10, LastApplied=9. Nếu chỉ xin các entry sau 12, nó có thể không nhận marker còn thiếu cho 11–12. Nếu chỉ replay sau LastCommitted, còn phải áp dụng entry 10 đã có tại chỗ. API cần tách việc kiểm tra log, bổ sung marker và Apply projection.

Đối soát “cùng mốc K” là đúng, nhưng bảng Seats chỉ có trạng thái hiện tại và UpdatedSeq không tạo ra khả năng đọc lại quá khứ. Lọc `UpdatedSeq <= K` không khôi phục trạng thái trước một lần cập nhật mới hơn K.

Với 10 ghế, cách đơn giản là tạm dừng mutation, đưa các bản sao tới cùng mốc K, đọc transaction nhất quán, chuẩn hóa trường/khóa rồi tính hash. Nếu cần so lịch sử ở K cũ, dựng projection tạm từ seed và log được giữ tới K. Không cần thêm hệ thống snapshot phân tán vào bản cơ sở. So hash ở mốc tĩnh cũng không đồng nghĩa đã cài thuật toán chụp trạng thái toàn cục của ND6.

### R11 và R12 SQL cần kiểm chứng đúng mức

Thiết kế UNIQUE ghế đã bán và filtered unique index cho HELD là hợp lý. Microsoft xác nhận [filtered index có thể có thuộc tính UNIQUE](https://learn.microsoft.com/en-us/sql/relational-databases/indexes/create-filtered-indexes?view=sql-server-ver16). Tuy nhiên, các index riêng lẻ chưa bảo đảm tất cả quan hệ giữa Seats, Reservations và Tickets.

Apply cần cập nhật nguyên tử trạng thái reservation, ghế, vé nếu có, RequestResults và LastApplied. Phải kiểm tra reservation khớp TripId/SeatNo/OwnerId, không tồn tại HELD mới trên ghế SOLD, và retry Apply không tạo thêm kết quả. Follower áp dụng quyết định đã ghi trong event; không sinh ID hoặc tự quyết định nghiệp vụ lại dựa trên trạng thái đã lệch.

SQL ACK phải dựa trên commit có độ bền đầy đủ. [Microsoft phân biệt fully durable với delayed durable commit](https://learn.microsoft.com/en-us/sql/relational-databases/logs/control-transaction-durability?view=sql-server-ver16): thành công của loại delayed chưa chứng minh log đã ghi xuống đĩa. Cấu hình thực nghiệm cần tương thích với cam kết durable ACK.

Chưa tìm thấy `De6_SQL_Server_Schema_Tham_Khao.sql` trong hồ sơ đã kiểm tra. Vì vậy, chưa thể xác nhận câu mục 7.3 nói script tạo đủ ràng buộc là đúng trong thực thi; chưa thể kết luận script có lỗi SQL. Đây là thiếu minh chứng, không phải bằng chứng script sai.

## 6 Tham khảo GitHub và Studocu sao cho đúng môn

| Nguồn đã đọc | Giá trị sử dụng | Phần không được suy diễn hoặc bê nguyên |
| --- | --- | --- |
| [Phunguy65 train-ticket-booking-system](https://github.com/Phunguy65/train-ticket-booking-system/blob/main/DEVELOPMENT.vi.md) | Rất gần công nghệ và nghiệp vụ: quầy WinForms, TCP, backend, SQL, 10 ghế | Tài liệu kiến trúc mô tả một backend và một DB; không dùng để chứng minh chịu lỗi bằng ba bản sao |
| [ohbriansung Distributed Event Purchase System](https://github.com/ohbriansung/Distributed_Event_Purchase_System) | Tham khảo cách mô tả Bully, UUID, replica và test đồng thời | README đưa tuyên bố chịu N−1 lỗi; chưa có kiểm chứng độc lập để áp cho đồ án. Mô tả ghi rồi nhân bản không thay cho phân tích crash ở giữa |
| [RailSync của Bologna](https://apice.unibo.it/bin/view/Courseproject/RailSync) | Học cách tổ chức test tự động, acceptance test và demo lỗi dịch vụ | Không cần đưa MEVN, Redis, Mongo replica, NGINX và toàn bộ microservice vào đề 10 ghế |
| [Saumyarup ticket-booking](https://github.com/Saumyarup-100/ticket-booking) | Tham khảo transaction giữ ghế và tình huống nhiều người tranh cùng ghế | PostgreSQL và khóa hàng không tự tạo thành thiết kế replication/failover của V11; QR/email/waitlist không cần thiết |
| [gitgik Designing Ticketmaster](https://github.com/gitgik/distributed-system-design/blob/master/designing_ticketmaster.md) | Đọc để nhận diện điểm tranh chấp trên một ghế | Đây là tài liệu system design quy mô lớn; không dùng như bằng chứng source hoặc benchmark PTIT |
| [Báo cáo nhóm 19 trên Studocu](https://www.studocu.vn/vn/document/hoc-vien-cong-nghe-buu-chinh-vien-thong/quan-tri-ban-hang-va-phan-phoi-san-pham/nhom-19-bao-cao-he-thong-phan-tan/43794360) | Bìa ghi thầy Lê Ngọc Bảo; tham khảo cách đi từ bài toán đến thuật toán và phân tích hệ thống | Đề tài đồng bộ thời gian, không phải bán vé; không chứng minh thiết kế V11 được thầy chấp thuận |
| [Báo cáo cảm biến PTIT trên Studocu](https://www.studocu.vn/vn/document/hoc-vien-cong-nghe-buu-chinh-vien-thong/cac-he-thong-phan-tan-ths-xuan-anh/bao-cao-cuoi-ky-mon-he-thong-phan-tan-ptit-thu-thap-du-lieu-cam-bien/163523241) | Tham khảo cách tách cơ sở lý thuyết, thiết kế và thực nghiệm | Bìa ghi Đỗ Trung Tuấn dù nhãn trang thuộc học phần mang tên khác; phải đọc nội dung bìa, không suy giảng viên từ nhãn Studocu |
| [Bài kiểm tra gắn tên thầy Bảo trên Studocu](https://www.studocu.vn/vn/document/hoc-vien-cong-nghe-buu-chinh-vien-thong/cac-he-thong-phan-tan-ths-xuan-anh/cau-bt-kiem-tra-gk-thay-le-ngoc-bao-ptithcm/113482533) | Là ví dụ sinh viên chia sẻ liên quan chi phí truyền thông | Không phải đề cương/CLO/rubric chính thức |

Bản [bài giảng Nguyễn Xuân Anh trên Studocu](https://www.studocu.vn/vn/document/hoc-vien-cong-nghe-buu-chinh-vien-thong/cac-he-thong-phan-tan-ths-xuan-anh/bai-giang-he-thong-phan-tan-pt-it-1405/133868236) cũng có sẵn ở máy dưới dạng PDF. Slide ND1 của thầy liệt kê tài liệu này, nên đây là căn cứ học thuật gần môn hơn các bài system design tổng quát.

Một nguồn Studocu về bán vé máy bay cũng được tìm thấy nhưng nội dung bìa là môn Cơ sở dữ liệu phân tán, giảng viên Phan Thị Hà. Không dùng cấu trúc phân mảnh dữ liệu/chi nhánh của tài liệu đó để đặt thêm yêu cầu cho đồ án HTPT của thầy Bảo.

Khi tham khảo mã thật sau này, nên ghi repository, commit, file/mục và phần nhóm tự thay đổi. Không cần tăng số nguồn để làm tài liệu trông có vẻ đầy đủ; cần làm rõ mỗi nguồn hỗ trợ quyết định nào.

## 7 Phạm vi nên giữ để không vượt môn

**Nên giữ làm bản cơ sở:** một chuyến, 10 ghế; ba quầy là ba tiến trình; ba server và ba SQL độc lập; primary duy nhất; HOLD/CONFIRM/CANCEL; Bully; primary-backup; RequestId chống lặp; log và replay; phục hồi khi một server crash theo mô hình công bố.

**Cần giải thích nhưng không nhất thiết mở rộng thành hệ thống lớn:** các pha commit, epoch vận hành, việc cách ly writer cũ, quyền của owner, kiểm tra peer, TCP framing và cách đo lỗi. Đây là chi tiết khiến mô hình chạy đúng, không phải thành tích thuật toán bổ sung.

**Nên hoãn:** Raft/Paxos, đa primary, Redis lock, Kafka, Kubernetes, sharding, nhiều chuyến/nhiều tàu, thanh toán thật, QR/email, tự hết hạn giữ ghế và snapshot động nếu chưa cần. Không có căn cứ buộc phải thêm các phần này để đạt mục tiêu đề 6.

HOLD là lựa chọn nghiệp vụ của nhóm, không phải yêu cầu tối thiểu thấy trong dòng đề. Giữ HOLD/CONFIRM/CANCEL thì cần công bố: khi quầy chết, ghế giữ không tự hết hạn; quầy khởi động lại có thể phục hồi reservation; có quy trình hủy giữ khi cần. Không mặc định phải đồng bộ đồng hồ phân tán chỉ để có timeout UI.

Một worker tuần tự xử lý mutation hợp lý cho 10 ghế. Vẫn cần để TCP, heartbeat và xử lý ACK chạy độc lập; nếu worker đang chờ ACK mà chặn luôn đường nhận ACK hoặc heartbeat thì kiến trúc sẽ tự gây treo. Đây là điểm tích hợp cần chú ý theo ND10 dispatcher/worker.

Chọn .NET/SQL theo công cụ thực tế trên năm máy và khả năng cả nhóm build được. Phiên bản nền tảng không phải CLO, cũng không thay cho việc giải thích thuật toán.

## 8 Kiểm thử cần bổ sung và sửa cách nghiệm thu

V11 có 30 test dự kiến là điểm tốt. Cần thêm hoặc làm rõ các tình huống sau; mọi dòng vẫn là kế hoạch cho đến khi có log chạy thật.

| Test đề xuất | Cách gây tình huống | Điều cần chứng minh |
| --- | --- | --- |
| X01 Ứng viên có dữ liệu cũ | S3 đã FINAL một lệnh với S1; S2 chưa có; tắt S3, bầu S2 | S2 lấy lịch sử từ S1 trước READY; không chọn dữ liệu theo ID |
| X02 Backup commit nhưng ACK mất | Cho S1 lưu marker COMMITTED rồi chặn ACK, sau đó tắt S3 | Phục hồi quyết định hợp lệ; cùng RequestId không sinh kết quả khác |
| X03 Primary mới chỉ commit cục bộ | Tắt S3 trước khi backup có marker | Không nhầm local marker với điều kiện đã FINAL; xử lý UNKNOWN theo quy tắc công bố |
| X04 Đuôi PREPARED rồi rejoin | S3/S1 có entry chưa hoàn tất, S2 được chọn; bật lại S3 | Không giữ hai lịch sử; không chấp nhận marker muộn của lịch sử bị loại |
| X05 UNKNOWN rồi lệnh kế tiếp | Chặn hoàn tất A; gửi B tranh cùng ghế | Không quyết định B trên projection không tương ứng tiền tố được giữ |
| X06 Hai yêu cầu promote | Hai phiên quản trị yêu cầu mở quyền gần nhau | Tối đa một server được phục vụ sau chuyển quyền |
| X07 Bầu cử chồng lấn | S1/S2 cùng phát Election; làm mất Coordinator một vòng | Thực hiện lại đúng nhánh, hội tụ khi điều kiện mạng cho phép |
| X08 Bản tin epoch cũ | Trì hoãn PREPARE/COMMIT rồi đưa tới sau chuyển quyền | Follower từ chối bản tin không còn thẩm quyền |
| X09 Đọc xen Apply | Gửi GET_SEATS trong lúc CONFIRM hoàn tất | Snapshot và AppliedSequence khớp, không có tổ hợp xuyên bảng nửa cũ nửa mới |
| X10 Catch-up thiếu marker | Backup có PREPARED xa hơn COMMITTED và Applied | Replay bổ sung đúng phần thiếu, không nhảy qua do dùng LastPrepared |
| X11 Retry hai phía ranh giới | Gửi cùng RequestId đồng thời, trước và sau failover | Một kết quả quyết định bền vững; phân biệt cùng ID khác payload |
| X12 Tải hết ghế | Nhiều lệnh cho cả 10 ghế và thêm lệnh khi hết | Tổng vé hợp lệ tối đa 10; không chỉ chứng minh riêng ghế 05 |

T13 “chặn ACK PREPARED” phải chỉ rõ chặn ACK nào. Nếu chỉ chặn một backup mà backup còn lại ACK hợp lệ thì theo chính sách ít nhất một backup, hệ thống vẫn có thể tiếp tục. Không đặt kỳ vọng UNKNOWN cho mọi trường hợp chặn một ACK bất kỳ.

T20 phân vùng cần tách hai trường hợp: primary ở nhánh có hai nút và vẫn có ACK; primary bị cô lập một mình. Chính sách cấm tự promote chưa đồng nghĩa toàn bộ cụm phải dừng ở mọi kiểu phân vùng. Chọn chính sách có thể giải thích được và áp dụng thống nhất trong bảng lỗi, code và test.

G1 chỉ có một server nên chưa thể nghiệm thu FINAL theo điều kiện hai bản lưu bền vững. Có thể kiểm tra domain/SQL tại G1, sau đó chạy lại test đầu cuối khi có replica thật; hoặc cho hai server tham gia ngay. Nếu dùng mock/dev mode, gắn nhãn rõ và không lấy kết quả đó làm bằng chứng chịu lỗi của cấu hình demo.

Đo latency bằng đồng hồ đơn điệu trong một client; không lấy timestamp hai máy chưa đồng bộ trừ trực tiếp. Mỗi nhóm số liệu phải nêu số mẫu, điều kiện tải và cách reset dữ liệu. Failover có thao tác người vận hành cần báo riêng thời gian phát hiện, chờ thao tác, đối soát và reconnect.

## 9 Điều chỉnh phân công để nhóm thực hiện được

Giữ phân công năm thành viên của V11 nhưng giảm tải TV5. TV1 làm schema/Apply/oracle; TV2 làm domain và coordinator; TV3 làm Bully, trạng thái nút và promotion; TV4 làm replication/recovery đồng thời phụ trách framing peer hoặc fault hooks; TV5 tập trung WinForms, SDK pending/reconnect và trình diễn. Mỗi chủ module tự cung cấp log/test cho phần của mình.

TV2 và TV4 phải thống nhất lúc nào worker được nhận lệnh tiếp. TV1 và TV4 phải thống nhất ngữ nghĩa Prepare/Commit/Apply/Replay. TV3 và TV4 phải cùng chịu trách nhiệm quy tắc prefix, epoch và rejoin. Các cặp này là ranh giới rủi ro chính, quan trọng hơn việc chỉ chia đều số folder.

Để tránh học thuộc một đoạn, mỗi thành viên nên giải thích được: vì sao không bán trùng; Bully làm gì và không chứng minh được gì; lúc nào được trả thành công; khi nào phải ngừng bán; máy cũ trở lại bằng cách nào. Đây là bằng chứng hiểu hệ thống, không phải một bảng tỷ trọng CLO tự đặt.

## 10 Các đoạn nên thay trong V11

**Thay kết luận khớp học phần:**

> Thiết kế có căn cứ trong các nội dung ND7, ND11, ND12 và ND14 của học phần, được hỗ trợ bởi ND1, ND2, ND10 và ND13. Bảng đối chiếu trong báo cáo dùng để truy vết nội dung học với thiết kế và thực nghiệm. Việc xác nhận đạt các CLO chính thức cần căn cứ đề cương/rubric áp dụng cho lớp và kết quả chạy có minh chứng.

**Thay câu trả lời về ba thuật toán:**

> Nhóm chọn loại trừ tập trung để quản lý thao tác cạnh tranh, Bully để bầu điều phối và nhân bản theo bản chính để lưu dữ liệu dự phòng. Các cơ chế này đã có trong bài giảng và đủ tạo trọng tâm cho đề 6. Nhóm giới hạn mô hình lỗi và phục hồi có kiểm soát để bảo đảm cách cài có thể giải thích, kiểm thử và hoàn thành.

**Thay mức cam kết chịu lỗi:**

> Trong cấu hình ba server cố định, nhóm kiểm thử chịu một server crash khi hai server còn lại liên lạc được, dữ liệu bền vững còn đọc được và writer cũ đã được cách ly. Lệnh chỉ được xác nhận thành công sau khi đạt điều kiện ghi bền vững và Apply quy định. Khi không xác minh được quyền ghi hoặc lịch sử, hệ thống báo chưa sẵn sàng và thực hiện quy trình phục hồi. Thời gian gián đoạn được đo, không công bố hoạt động liên tục trong mọi lỗi.

## 11 Điều kiện để gọi bản thiết kế đã sẵn sàng bảo vệ

1. Dẫn bản đề 26-27 làm căn cứ chính theo lựa chọn của người học; sửa các câu về xuất xứ yêu cầu nhân bản. Nếu giảng viên xác nhận phiên bản khác, đối chiếu lại phần yêu cầu tương ứng.
2. Bổ sung ánh xạ thuật toán tập trung và các nhánh Bully còn thiếu.
3. Thống nhất UNKNOWN, marker cục bộ, điều kiện FINAL, xử lý đuôi và quyền theo epoch.
4. Cung cấp DDL/Apply thật và quy trình replay/đối soát khả thi với schema.
5. Chạy demo ba server thật; giữ log/SQL chứng minh các tình huống lỗi cốt lõi.
6. Điền kết quả thực tế vào test plan, không biến 30 dòng dự kiến thành 30 PASS.
7. Gắn nguồn ngay tại các nhận định; bổ sung CLO/rubric của lớp khi có.

Đánh giá hiện tại: **phù hợp định hướng môn và có thể tiếp tục phát triển; chưa đủ căn cứ gọi là hoàn chỉnh hoặc đạt CLO chính thức.** Không cần tăng số công nghệ. Cần làm cho quan hệ giữa đề bài, slide, cách cài và bằng chứng chạy trở nên rõ và nhất quán.

## 12 Danh mục nguồn địa phương để truy vết

- [Bản V11 được review](<C:/Users/ADMIN/Downloads/De6_Thiet_Ke_Chi_Tiet_PTIT_3_Thuat_Toan_Review_GitHub_V11.docx>)
- [Danh sách đề HTPT 26-27](<C:/Users/ADMIN/Downloads/Danh sach do an mon hoc HTPT 26-27.doc>)
- [Danh sách đề bản cũ](<C:/Users/ADMIN/Downloads/Danh sach do an mon hoc.doc>)
- [ND1 Tổng quan](<C:/Users/ADMIN/Downloads/Noi dung 1 - Tong quan.pptx>)
- [ND2 Truyền thông và định danh](<C:/Users/ADMIN/Downloads/Noi dung 2 - truyen thong dinh danh.pptx>)
- [ND7 Loại trừ tương hỗ và bầu cử](<C:/Users/ADMIN/Downloads/Noi dung 7 -  Loai tru tuong ho- giai thuat bau cu.pptx>)
- [ND10 Tiến trình](<C:/Users/ADMIN/Downloads/Noi dung 10 - Tien trinh.pptx>)
- [ND11 Giao tác và tương tranh](<C:/Users/ADMIN/Downloads/Noi dung 11 - quan tri giao dich va dieu khien tuong tranh.pptx>)
- [ND12 Phục hồi và chịu lỗi](<C:/Users/ADMIN/Downloads/Noi dung 12 - phuc hoi va chiu loi.pptx>)
- [ND13 Bảo mật](<C:/Users/ADMIN/Downloads/Noi dung 13 - Bao mat.pptx>)
- [ND14 Nhất quán và nhân bản](<C:/Users/ADMIN/Downloads/Noi dung 14 - Nhát quan va nhan ban.pptx>)
- Bài giảng Hệ thống phân tán, Nguyễn Xuân Anh, 2014, bản PDF 148 trang tại Downloads. Mục 2.3.3.1 ở trang PDF 57; mục 2.3.5.1 ở trang PDF 60; mục 2.6.5.1 ở trang PDF 88; mục 2.8.5.2 ở trang PDF 107–108. Số này là thứ tự trang trong PDF, không phải số trang của V11.
