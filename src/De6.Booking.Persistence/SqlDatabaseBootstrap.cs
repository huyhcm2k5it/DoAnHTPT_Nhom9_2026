using System.Data;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;

namespace De6.Booking.Persistence;

public sealed class SqlDatabaseBootstrap(string connectionString)
{
    public async Task InitializeAsync(Guid datasetId, int nodeId, CancellationToken cancellation = default)
    {
        if (datasetId == Guid.Empty || nodeId is < 1 or > 3) throw new ArgumentException("Invalid dataset or NodeId");
        await using var connection = SqlSupport.Connection(connectionString);
        await connection.OpenAsync(cancellation);
        await SqlSupport.RequireDurable(connection, cancellation);
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("De6.Schema.001.sql")
            ?? throw new InvalidOperationException("Missing embedded migration 001");
        using var reader = new StreamReader(resource, Encoding.UTF8);
        var script = await reader.ReadToEndAsync(cancellation);
        using var seedResource=Assembly.GetExecutingAssembly().GetManifestResourceStream("De6.Seed.001.sql")
            ?? throw new InvalidOperationException("Missing embedded seed 001");
        using var seedReader=new StreamReader(seedResource,Encoding.UTF8);
        var seedScript=await seedReader.ReadToEndAsync(cancellation);
        var checksum = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(script+"\n--SEED--\n"+seedScript)));
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellation);
        await SqlSupport.Execute(connection, transaction,
            "DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource='De6.Schema',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; IF @r<0 THROW 51000,'Cannot acquire schema lock',1;", cancellation);
        var version = await SqlSupport.Scalar(connection, transaction,
            "SELECT CONVERT(int,value) FROM sys.extended_properties WHERE class=0 AND name=N'De6.SchemaVersion'", cancellation);
        if (version == null)
        {
            if (Convert.ToInt32(await SqlSupport.Scalar(connection, transaction,
                "SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped=0", cancellation)) != 0)
                throw new InvalidDataException("Refusing to initialize a database containing unrecognized tables");
            await SqlSupport.Execute(connection, transaction, script, cancellation);
            await SqlSupport.Execute(connection, transaction,
                "EXEC sys.sp_addextendedproperty @name=N'De6.SchemaVersion',@value=1; EXEC sys.sp_addextendedproperty @name=N'De6.SchemaChecksum',@value=@checksum;",
                cancellation, ("@checksum", checksum));
        }
        else
        {
            var oldChecksum = Convert.ToString(await SqlSupport.Scalar(connection, transaction,
                "SELECT CONVERT(nvarchar(64),value) FROM sys.extended_properties WHERE class=0 AND name=N'De6.SchemaChecksum'", cancellation));
            var tableCount = Convert.ToInt32(await SqlSupport.Scalar(connection, transaction,
                "SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID('dbo') AND name IN ('Trips','Seats','Reservations','Tickets','OperationLog','RequestResults','NodeState')", cancellation));
            if (Convert.ToInt32(version) != 1 || oldChecksum != checksum || tableCount != 7)
                throw new InvalidDataException("Migration version/checksum/schema mismatch; no automatic reset");
        }

        using (var command = SqlSupport.Command(connection, transaction,
            "SELECT DatasetId,NodeId,SchemaVersion FROM dbo.NodeState WITH (UPDLOCK,HOLDLOCK) WHERE RowId=1"))
        await using (var state = await command.ExecuteReaderAsync(cancellation))
        {
            if (await state.ReadAsync(cancellation))
            {
                if (state.GetGuid(0) != datasetId || state.GetInt32(1) != nodeId || state.GetInt32(2) != 1)
                    throw new InvalidDataException("DatasetId/NodeId/schema mismatch; existing data preserved");
            }
        }
        var rows = Convert.ToInt32(await SqlSupport.Scalar(connection, transaction, "SELECT COUNT(*) FROM dbo.NodeState", cancellation));
        if (rows == 0)
        {
            var existing = Convert.ToInt32(await SqlSupport.Scalar(connection, transaction,
                "SELECT (SELECT COUNT(*) FROM dbo.Trips)+(SELECT COUNT(*) FROM dbo.Seats)+(SELECT COUNT(*) FROM dbo.Reservations)+(SELECT COUNT(*) FROM dbo.Tickets)+(SELECT COUNT(*) FROM dbo.OperationLog)+(SELECT COUNT(*) FROM dbo.RequestResults)", cancellation));
            if (existing != 0) throw new InvalidDataException("Refusing to seed over existing data without NodeState");
            var seedHash = SHA256.HashData(Encoding.ASCII.GetBytes("PTIT-DE6-V12-SEED|" + datasetId.ToString("N")));
            await SqlSupport.Execute(connection, transaction,
                seedScript,
                cancellation, ("@node", nodeId), ("@dataset", datasetId), ("@hash", seedHash));
        }
        if (Convert.ToInt32(await SqlSupport.Scalar(connection, transaction,
            "SELECT COUNT(*) FROM dbo.Seats WHERE TripId=1 AND SeatNo BETWEEN 1 AND 10", cancellation)) != 10)
            throw new InvalidDataException("Seed integrity failure; existing data preserved");
        await transaction.CommitAsync(cancellation);
    }
}
