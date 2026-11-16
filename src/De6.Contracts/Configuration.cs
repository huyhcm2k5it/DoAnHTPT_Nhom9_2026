using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace De6.Contracts;

public sealed record NodeEndpoint(int NodeId, string Host, int ClientPort, int PeerPort);
// SQL credentials and arbitrary connection-string keys are deliberately not configuration fields.
public sealed record SqlOptions(string Server, string Database, int ConnectTimeoutSeconds = 5);
public sealed record ServerOptions(int NodeId, Guid DatasetId, NodeEndpoint[] Nodes, string DataDirectory, SqlOptions? Sql = null);
public sealed record ClientOptions(int ClientId, string OwnerId, Guid DatasetId, NodeEndpoint[] Servers, string DataDirectory);

public static class ConfigurationValidator
{
    public static IReadOnlyList<string> Validate(ServerOptions options)
    {
        var errors = ValidateCommon(options.DatasetId, options.Nodes);
        if (options.NodeId is < 1 or > 3) errors.Add("NodeId must be 1, 2 or 3.");
        ValidateDirectory(options.DataDirectory, $"data/servers/s{options.NodeId}", errors);
        if (options.Sql is not null)
        {
            // Shared memory has no remote endpoint or SQL alias/DNS ambiguity (V12 section 4).
            if (options.Sql.Server is null || !Regex.IsMatch(options.Sql.Server,
                @"\Alpc:(?:\.|\(local\))(?:\\[A-Za-z0-9_]{1,128})?\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                errors.Add("Sql.Server must be a local shared-memory endpoint such as 'lpc:.\\SQLEXPRESS'.");
            if (options.Sql.Database is null || !Regex.IsMatch(options.Sql.Database,
                @"\A(?:de_6|De6_[A-Za-z0-9_]{1,124})\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                errors.Add("Sql.Database must be de_6 or a De6_ application database (at most 128 ASCII characters).");
            if (options.Sql.ConnectTimeoutSeconds is < 1 or > 30)
                errors.Add("Sql.ConnectTimeoutSeconds must be 1..30.");
        }
        return errors;
    }

    public static IReadOnlyList<string> Validate(ClientOptions options)
    {
        var errors = ValidateCommon(options.DatasetId, options.Servers);
        if (options.ClientId is < 1 or > 3) errors.Add("ClientId must be 1, 2 or 3.");
        if (string.IsNullOrEmpty(options.OwnerId) || !Regex.IsMatch(options.OwnerId, "\\A[A-Za-z0-9_-]{1,32}\\z"))
            errors.Add("OwnerId must contain 1..32 ASCII letters, digits, underscores or hyphens.");
        if (options.ClientId is >= 1 and <= 3 && options.OwnerId != ((char)('A' + options.ClientId - 1)).ToString())
            errors.Add("Template clients C1/C2/C3 must use owner A/B/C respectively.");
        ValidateDirectory(options.DataDirectory, $"data/clients/c{options.ClientId}", errors);
        return errors;
    }

    private static List<string> ValidateCommon(Guid datasetId, NodeEndpoint[]? nodes)
    {
        List<string> errors = [];
        if (datasetId == Guid.Empty) errors.Add("A nonempty shared DatasetId is required.");
        if (nodes is null || nodes.Length != 3 || nodes.Any(node => node is null) ||
            !nodes.Select(node => node.NodeId).Order().SequenceEqual(new[] { 1, 2, 3 }))
        {
            errors.Add("Membership must contain each of NodeId 1, 2, 3 exactly once.");
            return errors;
        }
        HashSet<string> endpoints = new(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
        {
            // Literal IPs avoid DNS aliases masking endpoint collisions in the template.
            if (!IPAddress.TryParse(node.Host, out var address))
                errors.Add($"S{node.NodeId}: Host must be an explicit IP address.");
            foreach (var port in new[] { node.ClientPort, node.PeerPort })
            {
                if (port is < 1 or > 65535) errors.Add($"S{node.NodeId}: port must be 1..65535.");
                var key = $"{address?.MapToIPv6()}|{port}";
                if (!endpoints.Add(key)) errors.Add($"Duplicate configured endpoint: {node.Host}:{port}.");
            }
        }
        return errors;
    }

    private static void ValidateDirectory(string? directory, string expected, List<string> errors)
    {
        // Exact repository-relative stores prevent two demo clients sharing pending data.
        // Future configurable stores must also acquire an exclusive process lock (V12 T34).
        if (string.IsNullOrWhiteSpace(directory) || directory.Replace('\\', '/') != expected)
            errors.Add($"DataDirectory must be '{expected}' relative to repository root.");
    }
}

public static class ConfigurationFile
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };

    public static ServerOptions ReadServer(string path)
    {
        var options = JsonSerializer.Deserialize<ServerOptions>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("Server configuration is empty.");
        RequireValid(ConfigurationValidator.Validate(options));
        return options;
    }

    public static ClientOptions ReadClient(string path)
    {
        var options = JsonSerializer.Deserialize<ClientOptions>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("Client configuration is empty.");
        RequireValid(ConfigurationValidator.Validate(options));
        return options;
    }

    private static void RequireValid(IReadOnlyList<string> errors)
    {
        if (errors.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors));
    }
}
