using De6.Contracts;
using Microsoft.Data.SqlClient;

namespace De6.Booking.Persistence;

/// <summary>Maps validated node configuration; does not open, seed, grant authority or activate SQL.</summary>
public static class SqlPersistenceComposition
{
    public static SqlStoreOptions CreateStoreOptions(ServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errors=ConfigurationValidator.Validate(options);
        if(errors.Count!=0) throw new ArgumentException(string.Join(Environment.NewLine,errors),nameof(options));
        var sql=options.Sql ?? throw new ArgumentException("SQL configuration is required.",nameof(options));
        var connection=new SqlConnectionStringBuilder
        {
            DataSource=sql.Server,InitialCatalog=sql.Database,IntegratedSecurity=true,
            Encrypt=true,TrustServerCertificate=true,ConnectTimeout=sql.ConnectTimeoutSeconds,
            PersistSecurityInfo=false,ApplicationName="De6.Server.Host",Pooling=false
        };
        return new(connection.ConnectionString,options.DatasetId,options.NodeId);
    }

    // The owners must supply these adapters explicitly. No default-open implementation lives here.
    public static SqlBookingStore CreateStore(ServerOptions options,IStoreAccessGate gate,IBookingDecisionEngine decisionEngine)
    {
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(decisionEngine);
        return new(CreateStoreOptions(options),gate,decisionEngine);
    }
}
