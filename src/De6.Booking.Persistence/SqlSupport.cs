using Microsoft.Data.SqlClient;

namespace De6.Booking.Persistence;

internal static class SqlSupport
{
    internal static SqlCommand Command(SqlConnection connection, SqlTransaction? transaction, string sql,
        params (string Name, object? Value)[] parameters)
    {
        var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 15 };
        foreach (var (name, value) in parameters)
        {
            // DateTime inference uses SQL datetime and rounds fractions before reaching datetime2(7).
            var parameter=value is DateTime time
                ? new SqlParameter(name,System.Data.SqlDbType.DateTime2) {Scale=7,Value=time}
                : new SqlParameter(name,value??DBNull.Value);
            command.Parameters.Add(parameter);
        }
        return command;
    }

    internal static async Task<int> Execute(SqlConnection connection, SqlTransaction transaction, string sql,
        CancellationToken token, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(connection, transaction, sql, parameters);
        return await command.ExecuteNonQueryAsync(token);
    }

    internal static async Task<object?> Scalar(SqlConnection connection, SqlTransaction? transaction, string sql,
        CancellationToken token, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(connection, transaction, sql, parameters);
        return await command.ExecuteScalarAsync(token);
    }

    internal static async Task RequireDurable(SqlConnection connection, CancellationToken token)
    {
        var mode = Convert.ToInt32(await Scalar(connection, null,
            "SELECT delayed_durability FROM sys.databases WHERE database_id=DB_ID()", token));
        if (mode == 2) throw new InvalidDataException("FORCED delayed durability is incompatible with V12");
    }

    internal static SqlConnection Connection(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.InitialCatalog) ||
            new[] { "master", "model", "msdb", "tempdb" }.Contains(builder.InitialCatalog, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("An explicit non-system application database is required");
        return new SqlConnection(builder.ConnectionString);
    }
}
