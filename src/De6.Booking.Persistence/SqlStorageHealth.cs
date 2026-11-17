using System.Data;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;

namespace De6.Booking.Persistence;

public sealed record SqlStorageHealthReport(int NodeId,Guid DatasetId,int SchemaVersion,long LastAppliedIndex,
    ReadOnlyMemory<byte> HeadHash,int DelayedDurability);

/// <summary>Read-only local startup evidence. This grants no lease, StableIndex or service authority.</summary>
public sealed class SqlStorageHealth
{
    public async Task<SqlStorageHealthReport> ProbeAsync(SqlStoreOptions options,CancellationToken cancellation=default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if(options.DatasetId==Guid.Empty || options.NodeId is <1 or >3) throw new ArgumentException("Invalid store identity.");
        cancellation.ThrowIfCancellationRequested();
        await using var connection=SqlSupport.Connection(options.ConnectionString);
        await connection.OpenAsync(cancellation);
        // This transaction contains SELECT only. Serializable keeps metadata/index/tail coherent.
        await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,cancellation);
        var delayed=await SqlSupport.Scalar(connection,transaction,
            "SELECT delayed_durability FROM sys.databases WHERE database_id=DB_ID()",cancellation);
        if(delayed is null or DBNull || Convert.ToInt32(delayed) is <0 or >1)
            throw new InvalidDataException("FORCED or unknown delayed durability is incompatible with V12.");
        var version=await SqlSupport.Scalar(connection,transaction,
            "SELECT CONVERT(int,value) FROM sys.extended_properties WHERE class=0 AND name=N'De6.SchemaVersion'",cancellation);
        var checksum=await SqlSupport.Scalar(connection,transaction,
            "SELECT CONVERT(nvarchar(64),value) FROM sys.extended_properties WHERE class=0 AND name=N'De6.SchemaChecksum'",cancellation);
        var tables=await SqlSupport.Scalar(connection,transaction,
            "SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID('dbo') AND name IN ('Trips','Seats','Reservations','Tickets','OperationLog','RequestResults','NodeState')",cancellation);
        if(version is null or DBNull || Convert.ToInt32(version)!=1 ||
            !string.Equals(Convert.ToString(checksum),await ExpectedSchemaChecksum(cancellation),StringComparison.Ordinal) || Convert.ToInt32(tables)!=7)
            throw new InvalidDataException("Existing SQL schema version/checksum/tables do not match; initialize or investigate separately.");

        SqlStorageHealthReport report;
        using(var command=SqlSupport.Command(connection,transaction,
            // Writers lock NodeState first with UPDLOCK, then visit OperationLog. Use the same
            // lock mode/order so this read-only probe cannot hold S(NodeState) while waiting on
            // a writer's log lock as that writer waits to upgrade NodeState to X.
            "SELECT RowId,NodeId,DatasetId,SchemaVersion,LastAppliedIndex,HeadHash FROM dbo.NodeState WITH(UPDLOCK,HOLDLOCK) WHERE RowId=1"))
        await using(var reader=await command.ExecuteReaderAsync(cancellation))
        {
            if(!await reader.ReadAsync(cancellation)) throw new InvalidDataException("NodeState is missing; probe does not seed databases.");
            if(reader.GetByte(0)!=1 || reader.GetInt32(1)!=options.NodeId || reader.GetGuid(2)!=options.DatasetId || reader.GetInt32(3)!=1)
                throw new InvalidDataException("NodeState DatasetId/NodeId/schema does not match configuration.");
            var index=reader.GetInt64(4); var hash=(byte[])reader[5];
            if(index<0 || hash.Length!=32) throw new InvalidDataException("Invalid local applied metadata.");
            report=new(options.NodeId,options.DatasetId,1,index,hash,Convert.ToInt32(delayed));
            if(await reader.ReadAsync(cancellation)) throw new InvalidDataException("NodeState must have exactly one row.");
        }
        using(var command=SqlSupport.Command(connection,transaction,
            "SELECT COUNT_BIG(*),COALESCE(MIN(LogIndex),0),COALESCE(MAX(LogIndex),0) FROM dbo.OperationLog"))
        await using(var reader=await command.ExecuteReaderAsync(cancellation))
        {
            await reader.ReadAsync(cancellation);
            if(reader.GetInt64(0)!=report.LastAppliedIndex || reader.GetInt64(1)!=(report.LastAppliedIndex==0?0:1) || reader.GetInt64(2)!=report.LastAppliedIndex)
                throw new InvalidDataException("LastAppliedIndex does not match a contiguous local log.");
        }
        var expectedHash=report.LastAppliedIndex==0
            ? SHA256.HashData(Encoding.ASCII.GetBytes("PTIT-DE6-V12-SEED|"+options.DatasetId.ToString("N")))
            : (byte[]?)await SqlSupport.Scalar(connection,transaction,
                "SELECT EventHash FROM dbo.OperationLog WHERE LogIndex=@index",cancellation,("@index",report.LastAppliedIndex));
        if(expectedHash is null || !report.HeadHash.Span.SequenceEqual(expectedHash))
            throw new InvalidDataException("NodeState head hash does not match the local log/seed.");
        // Disposing an uncommitted SELECT-only transaction releases locks without any durable mutation.
        return report;
    }

    private static async Task<string> ExpectedSchemaChecksum(CancellationToken cancellation)
    {
        var assembly=Assembly.GetExecutingAssembly();
        using var migration=assembly.GetManifestResourceStream("De6.Schema.001.sql") ?? throw new InvalidOperationException("Missing migration resource.");
        using var seed=assembly.GetManifestResourceStream("De6.Seed.001.sql") ?? throw new InvalidOperationException("Missing seed resource.");
        using var migrationReader=new StreamReader(migration,Encoding.UTF8);
        using var seedReader=new StreamReader(seed,Encoding.UTF8);
        var content=await migrationReader.ReadToEndAsync(cancellation)+"\n--SEED--\n"+await seedReader.ReadToEndAsync(cancellation);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    }
}
