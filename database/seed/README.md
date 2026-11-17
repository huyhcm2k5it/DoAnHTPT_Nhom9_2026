# Seed có kiểm soát

001_seed.sql được SqlDatabaseBootstrap gọi sau khi xác minh DB trống hoặc đúng dataset/node hiện hành. Các parameter @node, @dataset, @hash do runner cấp. Tạo một chuyến, 10 AVAILABLE seats, SeedHash và NodeState có AcceptedEpoch=0/AcceptedLeader=NULL. Chạy lại không xóa vé/log/giữ ghế đã có, không đổi DatasetId.

Không chạy seed thô để reset demo. Cấp quyền ban đầu là bước Control riêng, cần kiểm tra cả ba replica theo V12. Xem [runbook](../../docs/implementation/HUY_WEEK1_RUNBOOK.md).
