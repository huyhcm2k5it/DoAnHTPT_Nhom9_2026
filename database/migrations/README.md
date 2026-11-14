# Ordered migrations to implement

Owner: Huy. Add numbered, reviewed SQL migrations with explicit prerequisites, schema version, transaction/error handling and backup/recovery instructions. Test a fresh database and the documented supported upgrade path on independent test databases before team use.

No migration runner or executable migration is implemented. Never silently drop/reset an existing dataset to make startup pass. Disable delayed durability and prove the transaction boundaries with integration tests before claiming LocalApplied.
