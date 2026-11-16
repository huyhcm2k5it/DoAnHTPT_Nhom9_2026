# Oracle cục bộ và đối soát tại K

Huy phụ trách. 001_local_invariants.sql kiểm tra SQL cục bộ; SqlBookingStore.AuditLocalAsync xác minh hash chain/canonical event và đối chiếu projection/result với lịch sử. ProjectionDigest tạo SHA256 canonical, không chứa NodeId hoặc credential. Xem [runbook tuần 2](../../docs/implementation/HUY_WEEK2_RUNBOOK.md).

GetFrozenPrefixAsync/RebuildProjectionAsync cần maintenance lease thật và cùng K hiện hành. LocalApplied, StableIndex trong RAM và FINAL của client là các quan sát khác nhau. SQL một node không chứng minh I07/I10 hoặc nghiệm thu T01–T36. Không đánh PASS toàn cụm chỉ từ oracle local.
