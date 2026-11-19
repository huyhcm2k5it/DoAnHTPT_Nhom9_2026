-- Version 2 adds durable recovery-run readback. Migration is applied transactionally
-- by SqlDatabaseBootstrap after verifying the immutable version-1 checksum.
ALTER TABLE dbo.NodeState DROP CONSTRAINT CK_NodeState_Metadata;
UPDATE dbo.NodeState SET SchemaVersion=2 WHERE RowId=1;
ALTER TABLE dbo.NodeState WITH CHECK ADD CONSTRAINT CK_NodeState_Metadata CHECK (
    LastAppliedIndex>=0 AND SchemaVersion=2 AND DatasetId<>'00000000-0000-0000-0000-000000000000'
);
ALTER TABLE dbo.NodeState CHECK CONSTRAINT CK_NodeState_Metadata;

CREATE TABLE dbo.RecoveryManifest (
    RunId uniqueidentifier NOT NULL CONSTRAINT PK_RecoveryManifest PRIMARY KEY,
    DatasetId uniqueidentifier NOT NULL,
    Kind varchar(10) COLLATE Latin1_General_100_BIN2 NOT NULL,
    PreviousEpoch bigint NULL,
    PreviousLeaderId int NULL,
    NewEpoch bigint NOT NULL,
    NewLeaderId int NOT NULL,
    ParticipantCount tinyint NOT NULL,
    Participant1 int NOT NULL,
    Participant2 int NOT NULL,
    Participant3 int NULL,
    SchemaVersion int NOT NULL,
    TargetIndex bigint NOT NULL,
    HeadHash binary(32) NOT NULL,
    BusinessDigest binary(32) NOT NULL,
    FenceEvidenceReference nvarchar(128) NULL,
    FenceOperatorId nvarchar(32) NULL,
    ManifestHash binary(32) NOT NULL,
    Status varchar(10) COLLATE Latin1_General_100_BIN2 NOT NULL,
    IsActive bit NOT NULL,
    CreatedUtc datetime2(7) NOT NULL,
    CompletedUtc datetime2(7) NULL,
    CONSTRAINT CK_RecoveryManifest_Identity CHECK (
        RunId<>'00000000-0000-0000-0000-000000000000' AND
        DatasetId<>'00000000-0000-0000-0000-000000000000' AND
        Participant1 BETWEEN 1 AND 3 AND Participant2 BETWEEN 1 AND 3 AND
        Participant1<>Participant2 AND NewEpoch>0 AND NewLeaderId BETWEEN 1 AND 3 AND
        SchemaVersion=2 AND TargetIndex>=0
    ),
    CONSTRAINT CK_RecoveryManifest_State CHECK (
        (Status='STAGED' AND IsActive=1 AND CompletedUtc IS NULL) OR
        (Status='COMPLETED' AND IsActive=0 AND CompletedUtc IS NOT NULL)
    ),
    CONSTRAINT CK_RecoveryManifest_Kind CHECK (
        (Kind='BOOTSTRAP' AND PreviousEpoch IS NULL AND PreviousLeaderId IS NULL AND
            NewEpoch=1 AND NewLeaderId=3 AND ParticipantCount=3 AND
            Participant1=1 AND Participant2=2 AND Participant3=3 AND TargetIndex=0 AND
            FenceEvidenceReference IS NULL AND FenceOperatorId IS NULL) OR
        (Kind='PROMOTION' AND PreviousEpoch>0 AND PreviousLeaderId BETWEEN 1 AND 3 AND
            NewEpoch=PreviousEpoch+1 AND NewLeaderId<>PreviousLeaderId AND ParticipantCount=2 AND
            Participant3 IS NULL AND NewLeaderId IN (Participant1,Participant2) AND
            PreviousLeaderId NOT IN (Participant1,Participant2) AND
            FenceEvidenceReference IS NOT NULL AND LEN(FenceEvidenceReference)>0 AND
            LEN(FenceEvidenceReference)<=128 AND
            FenceOperatorId IS NOT NULL AND LEN(FenceOperatorId)>0 AND LEN(FenceOperatorId)<=32) OR
        (Kind='REJOIN' AND
            ((PreviousEpoch IS NULL AND PreviousLeaderId IS NULL) OR
             (PreviousEpoch>0 AND PreviousLeaderId BETWEEN 1 AND 3 AND
                (NewEpoch>PreviousEpoch OR (NewEpoch=PreviousEpoch AND NewLeaderId=PreviousLeaderId)))) AND
            ParticipantCount=2 AND Participant3 IS NULL AND
            NewLeaderId IN (Participant1,Participant2) AND
            FenceEvidenceReference IS NULL AND FenceOperatorId IS NULL)
    )
);

CREATE UNIQUE INDEX UX_RecoveryManifest_OneActiveRun
    ON dbo.RecoveryManifest(IsActive) WHERE IsActive=1;
