# Ma trận kiểm thử V12

Nguồn: [V12 mục 20](../De6_Thiet_Ke_Trien_Khai_V12.md). Đây là kế hoạch nghiệm thu của implementation tương lai. **T01–T36 đều CHƯA CHẠY** trong bộ khung; không suy PASS từ build, smoke, startup `-Check` hay kiểm chứng mô hình hữu hạn.

## Kiểm tra bộ khung

| Phạm vi | Lệnh/tiêu chí | Ý nghĩa |
| --- | --- | --- |
| Build | `scripts/build.ps1` trả 0 | Các project compile; chưa chứng minh booking |
| Smoke | `scripts/test.ps1` chạy assertion harness | Kiểm tra cấu hình sai/đúng và bootstrap không phục vụ; không phải T01 thực |
| Server check | `scripts/start-server.ps1 -NodeId 1` đến `3`, thêm `-Check` | Cấu hình hợp lệ, RECOVERING/ServingEnabled=false rồi thoát |
| Client check | `scripts/start-client.ps1 -ClientId 1` đến `3`, thêm `-Check` | Cấu hình hợp lệ rồi thoát, không mở UI |

Kết quả thực của những lệnh này được ghi trong báo cáo kiểm chứng lần bàn giao; bảng này không tự xác nhận đã chạy trên mọi máy. `dotnet test` không tự khám phá executable smoke harness.

## Nghiệm thu nghiệp vụ và chịu lỗi

Mỗi test cần [record](TEST_RECORD_TEMPLATE.md), commit SHA, DatasetId, epoch, IP/node, timeout, điểm lỗi, RequestId/EventId/hash, log ba node và SQL oracle. PASS phải có reviewer và đường dẫn record. Các chủ trì sau chịu trách nhiệm tập hợp bằng chứng, người phối hợp vẫn viết test module mình. Thứ tự G0–G5 theo [phân công](../team/Phan_Cong_Nhom9.md).

| ID | Tình huống V12 | Oracle bắt buộc | Chủ trì/phối hợp | Trạng thái / record |
| --- | --- | --- | --- | --- |
| T01 | Bootstrap đúng/sai dataset | Ba bản seed đúng; dữ liệu cũ không bị reset âm thầm | Huy | CHƯA CHẠY / — |
| T02 | Ba quầy HOLD cùng ghế | Một OK_HELD; hai lỗi nghiệp vụ cố định | Gia Quân | CHƯA CHẠY / — |
| T03 | HOLD rồi CONFIRM | Một ticket, ghế SOLD, reservation CONFIRMED | Gia Quân + Huy | CHƯA CHẠY / — |
| T04 | HOLD rồi CANCEL rồi HOLD lại | Lịch sử giữ cũ CANCELLED; một giữ mới hợp lệ | Gia Quân + Huy | CHƯA CHẠY / — |
| T05 | CONFIRM/CANCEL sai chủ | Không đổi ghế/vé, kết quả cố định nếu lệnh hợp lệ | Gia Quân | CHƯA CHẠY / — |
| T06 | CONFIRM lại với ID cũ và ID mới | ID cũ cùng OK_SOLD; ID mới ALREADY_CONFIRMED cùng ticket | Gia Quân | CHƯA CHẠY / — |
| T07 | Retry đồng thời cùng RequestId | Một event/result, không thêm tác động | Gia Quân | CHƯA CHẠY / — |
| T08 | Cùng RequestId đổi nội dung/chủ | REQUEST_CONFLICT; không ghi đè | Gia Quân | CHƯA CHẠY / — |
| T09 | Bán đủ 10 ghế và yêu cầu dư | Tối đa 10 ticket, không bán lại SOLD | Gia Quân + Huy | CHƯA CHẠY / — |
| T10 | Local commit rồi mất replication | Không FINAL, không đọc trạng thái local; lệnh sau không chạy | Gia Quân + Huy | CHƯA CHẠY / — |
| T11 | Backup Apply rồi mất ACK | Retry APPEND không Apply lần hai; đủ chứng cứ thì trả cùng result | Huy + Gia Quân | CHƯA CHẠY / — |
| T12 | Chỉ chặn một ACK, backup khác ACK được | Có thể FINAL với ACK hợp lệ còn lại | Gia Quân | CHƯA CHẠY / — |
| T13 | Kill primary trước local SQL commit | Không có nửa transaction; retry cùng identity | Huy | CHƯA CHẠY / — |
| T14 | Kill primary sau local commit trước replica | Không mất FINAL trước đó; xử lý đuôi chưa FINAL đúng quy tắc | Huy + Gia Quân | CHƯA CHẠY / — |
| T15 | Kill primary sau backup Apply trước ACK | Survivor có event phải được giữ/nhân bản trước READY | Huy + Đức An | CHƯA CHẠY / — |
| T16 | Kill primary sau ACK trước response | GET_RESULT sau phục hồi trả đúng result | Huy + Văn Khoa | CHƯA CHẠY / — |
| T17 | Mất response FINAL và kill/restart client | Pending resolve cùng TicketId/ReservationId | Văn Khoa | CHƯA CHẠY / — |
| T18 | Candidate stale, survivor khác mới hơn | Candidate lấy lịch sử từ survivor, không chọn DB theo ID | Đức An + Huy | CHƯA CHẠY / — |
| T19 | SQL exception sau commit nhưng client thư viện báo lỗi | Dừng gate; tra RequestId, không tạo event thứ hai | Huy | CHƯA CHẠY / — |
| T20 | Backup crash và rejoin | Freeze, replay, hash/digest cùng K; NodeId không bị copy | Huy + Đức An | CHƯA CHẠY / — |
| T21 | Node cũ có đuôi khác ở LogIndex được dùng lại | Archive/repair; epoch/event/hash cũ bị từ chối | Huy + Đức An | CHƯA CHẠY / — |
| T22 | Mất heartbeat, old primary vẫn chạy | Không promote khi chưa fence | Đức An | CHƯA CHẠY / — |
| T23 | Partition với primary ở mỗi phía | Hành vi đúng hai dòng ma trận lỗi; không hai writer | Đức An + Gia Quân | CHƯA CHẠY / — |
| T24 | Hai node đồng thời bầu, mất ANSWER/COORDINATOR | Tái bầu, hội tụ khi hai node liên lạc ổn định; chưa tự mở gate | Đức An | CHƯA CHẠY / — |
| T25 | Crash trong ghi manifest ở participant thứ nhất | Cả hai đóng quyền; tiếp tục cùng RunId rồi mới mở | Đức An + Huy | CHƯA CHẠY / — |
| T26 | Chỉ còn một participant hợp lệ | Không mở quyền/FINAL ghi mới | Đức An | CHƯA CHẠY / — |
| T27 | ACK cũ/sai event/sai hash | Không tăng StableIndex | Gia Quân | CHƯA CHẠY / — |
| T28 | GAP, duplicate và prefix conflict | Replay đúng mốc; duplicate ACK; conflict đóng gate | Huy + Đức An | CHƯA CHẠY / — |
| T29 | GET_SEATS xen giữa LocalApplied và Stable | Không trả snapshot chưa công bố hoặc mốc sai | Gia Quân | CHƯA CHẠY / — |
| T30 | Đối soát K khi đang có yêu cầu mới | Freeze trước; dữ liệu và result cùng K | Huy + Đức An | CHƯA CHẠY / — |
| T31 | Frame chia/gộp, EOF, size ngoài giới hạn | Không lệch JSON/frame, đóng kết nối đúng, không sửa nghiệp vụ | Văn Khoa | CHƯA CHẠY / — |
| T32 | Queue đầy/socket mất sau bắt đầu operation | RETRY_LATER trước nhận; operation đã bắt đầu không bị bỏ | Văn Khoa + Gia Quân | CHƯA CHẠY / — |
| T33 | Giả peer/owner/leader hint | Không vượt xác thực hoặc đổi quyền ghi | Đức An + Văn Khoa | CHƯA CHẠY / — |
| T34 | Pending dataset cũ hoặc hai client dùng cùng store | Từ chối dataset, khóa store; không replay chéo | Văn Khoa | CHƯA CHẠY / — |
| T35 | Restart ba process với dữ liệu còn | Không tự mở bán; bảo toàn FINAL sau đối soát | Đức An + Huy + Văn Khoa | CHƯA CHẠY / — |
| T36 | Một node chưa sẵn sàng rồi thêm lỗi | Dừng đúng giới hạn, không báo chịu được hai lỗi | Đức An | CHƯA CHẠY / — |

## Điều kiện chạy và kết luận

G1 có thể kiểm tra domain/SQL cục bộ nhưng không đủ nghiệm thu FINAL theo cấu hình ba bản sao. G2 cần primary+backup thật để kiểm tra durable result/ACK. G3–G4 cần đủ ba server, ba DB độc lập, client thật và fault hooks đúng mốc; mọi đợt lỗi bắt đầu từ ba bản đã đồng bộ. Một node chưa rejoin xong rồi thêm lỗi phải dừng theo T36.

Record tách rõ LocalApplied, Stable và FINAL; chứng minh cùng event/hash/result ở hai nơi trước FINAL. T10/T29 không đạt nếu chỉ chứng minh không bán trùng mà vẫn lộ projection chưa Stable. T22/T23 không đạt nếu candidate tự mở ghi khi chưa fence. T18/T20/T21/T25/T30/T35 phải giữ prefix, kết quả đã FINAL, metadata/NodeId và manifest đúng quy tắc V12.

Dùng CHƯA CHẠY khi chưa thực hiện; BLOCKED khi thiếu tiền điều kiện/hạ tầng; FAIL khi quan sát sai oracle; PASS chỉ khi evidence đủ và được review. Khi đổi hợp đồng, SQL, authority hoặc framing, ghi phạm vi test cần chạy lại và record commit mới. Không thay lịch sử FAIL cũ bằng dòng PASS thiếu giải thích.
