# V12 acceptance evidence

Copy one record per T01-T36 from [V12 section 20](../../docs/De6_Thiet_Ke_Trien_Khai_V12.md). Initial status is NOT RUN. Startup configuration smoke checks are not a substitute for any full business acceptance case.

- Case ID/title and invariant(s):
- Status: NOT RUN / FAIL / PASS, reviewer:
- Commit SHA, SDK/package/schema/protocol versions:
- DatasetId, accepted epoch/leader, RecoveryRunId if applicable:
- Physical topology, server IP/NodeId and client owner/store paths:
- Timeouts, queue limits, load, and exact commands:
- Initial SQL/prefix state on each node:
- Fault trigger and exact event/commit/ACK/response boundary:
- RequestId, EventId, LogIndex, both epochs, hashes/result bytes:
- Timestamped logs from all three nodes and client, monotonic timings:
- Read-only SQL oracle output and canonical digest at the SAME frozen K:
- Expected versus observed LocalApplied, StableIndex and FINAL separately:
- Fencing evidence and manifest/readback if promotion occurred:
- Reproduction steps, artifact locations/checksums, redactions:
- Limitations, unexplained output, review and rerun notes:

Mark PASS only after the case-specific oracle matches saved evidence. Distinguish one unavailable node from a second fault before rejoin. A finite model result does not prove the C#/SQL implementation.
