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
        var assembly = Assembly.GetExecutingAssembly();
        var versionOneScript = await ReadResource(assembly, "De6.Schema.001.sql", cancellation);
        var versionTwoScript = await ReadResource(assembly, "De6.Schema.002.sql", cancellation);
        var seedScript = await ReadResource(assembly, "De6.Seed.001.sql", cancellation);
        var versionOneChecksum = ComputeVersionOneChecksum(versionOneScript, seedScript);
        var currentChecksum = ComputeCurrentChecksum(versionOneScript, versionTwoScript, seedScript);

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellation);
        await SqlSupport.Execute(connection, transaction,
            "DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource='De6.Schema',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; IF @r<0 THROW 51000,'Cannot acquire schema lock',1;", cancellation);
        var versionValue = await SqlSupport.Scalar(connection, transaction,
            "SELECT CONVERT(int,value) FROM sys.extended_properties WHERE class=0 AND name=N'De6.SchemaVersion'", cancellation);

        if (versionValue is null or DBNull)
        {
            if (Convert.ToInt32(await SqlSupport.Scalar(connection, transaction,
                "SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped=0", cancellation)) != 0)
                throw new InvalidDataException("Refusing to initialize a database containing unrecognized tables");

            await SqlSupport.Execute(connection, transaction, versionOneScript, cancellation);
            var seedHash = SeedHash(datasetId);
            await SqlSupport.Execute(connection, transaction, seedScript, cancellation,
                ("@node", nodeId), ("@dataset", datasetId), ("@hash", seedHash));
            await SqlSupport.Execute(connection, transaction, versionTwoScript, cancellation);
            await SqlSupport.Execute(connection, transaction,
                "EXEC sys.sp_addextendedproperty @name=N'De6.SchemaVersion',@value=2; EXEC sys.sp_addextendedproperty @name=N'De6.SchemaChecksum',@value=@checksum;",
                cancellation, ("@checksum", currentChecksum));
        }
        else
        {
            var version = Convert.ToInt32(versionValue);
            var oldChecksum = Convert.ToString(await SqlSupport.Scalar(connection, transaction,
                "SELECT CONVERT(nvarchar(64),value) FROM sys.extended_properties WHERE class=0 AND name=N'De6.SchemaChecksum'", cancellation));
            var baseTableCount = Convert.ToInt32(await SqlSupport.Scalar(connection, transaction,
                "SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID('dbo') AND name IN ('Trips','Seats','Reservations','Tickets','OperationLog','RequestResults','NodeState','RecoveryManifest')", cancellation));

            if (version == 1)
            {
                if (oldChecksum != versionOneChecksum || baseTableCount != 7)
                    throw new InvalidDataException("Version-1 migration/checksum/schema mismatch; no automatic reset");
                await SqlSupport.Execute(connection, transaction, versionTwoScript, cancellation);
                await SqlSupport.Execute(connection, transaction,
                    "EXEC sys.sp_updateextendedproperty @name=N'De6.SchemaVersion',@value=2; EXEC sys.sp_updateextendedproperty @name=N'De6.SchemaChecksum',@value=@checksum;",
                    cancellation, ("@checksum", currentChecksum));
            }
            else if (version != 2 || oldChecksum != currentChecksum || baseTableCount != 8)
            {
                throw new InvalidDataException("Migration version/checksum/schema mismatch; no automatic reset");
            }
        }

        using (var command = SqlSupport.Command(connection, transaction,
            "SELECT DatasetId,NodeId,SchemaVersion FROM dbo.NodeState WITH (UPDLOCK,HOLDLOCK) WHERE RowId=1"))
        await using (var state = await command.ExecuteReaderAsync(cancellation))
        {
            if (!await state.ReadAsync(cancellation) || state.GetGuid(0) != datasetId ||
                state.GetInt32(1) != nodeId || state.GetInt32(2) != 2)
                throw new InvalidDataException("DatasetId/NodeId/schema mismatch; existing data preserved");
            if (await state.ReadAsync(cancellation))
                throw new InvalidDataException("NodeState must have exactly one row.");
        }
        if (Convert.ToInt32(await SqlSupport.Scalar(connection, transaction,
            "SELECT COUNT(*) FROM dbo.Seats WHERE TripId=1 AND SeatNo BETWEEN 1 AND 10", cancellation)) != 10)
            throw new InvalidDataException("Seed integrity failure; existing data preserved");
        await transaction.CommitAsync(cancellation);
    }

    private static async Task<string> ReadResource(Assembly assembly, string resourceName,
        CancellationToken cancellation)
    {
        using var resource = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded resource {resourceName}");
        using var reader = new StreamReader(resource, Encoding.UTF8);
        return await reader.ReadToEndAsync(cancellation);
    }

    private static byte[] SeedHash(Guid datasetId)
        => SHA256.HashData(Encoding.ASCII.GetBytes("PTIT-DE6-V12-SEED|" + datasetId.ToString("N")));

    private static string ComputeVersionOneChecksum(string migration, string seed)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(migration + "\n--SEED--\n" + seed)));

    private static string ComputeCurrentChecksum(string migrationOne, string migrationTwo, string seed)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            migrationOne + "\n--MIGRATION-002--\n" + migrationTwo + "\n--SEED--\n" + seed)));
}
