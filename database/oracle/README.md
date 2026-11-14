# Read-only oracle to implement

Owner: Huy, reviewed with Gia Quân and Nguyên Chương. Implement reproducible read-only queries/checkers for V12 I01-I12 and T01-T36. Capture per-node DatasetId, schema/authority, LastAppliedIndex and HeadHash, full hash chain, seat/reservation/ticket relations, immutable RequestResults, canonical business/result digest and event identity.

Compare frozen replicas at the same K. LocalApplied, RAM StableIndex and a client FINAL are different observations. SQL CHECKSUM alone and matching seat colors are insufficient evidence. No completed oracle exists; never mark a test PASS without saved logs and query outputs.
