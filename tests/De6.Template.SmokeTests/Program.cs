using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using De6.Contracts;
using De6.Cluster.Control;

// No test framework/packages: this executable returns nonzero when any assertion fails.
// Breaks caught: accepting unsafe configuration or opening serving rights on boot.
var dataset = Guid.Parse("124a4f06-4ac1-4c12-8b27-202600000009");
NodeEndpoint[] nodes = [new(1, "127.0.0.1", 9001, 9101), new(2, "127.0.0.1", 9002, 9102), new(3, "127.0.0.1", 9003, 9103)];
var server = new ServerOptions(1, dataset, nodes, "data/servers/s1");
var client = new ClientOptions(1, "A", dataset, nodes, "data/clients/c1");
var failures = 0;
var count = 0;
Check("accept valid local server", () => ConfigurationValidator.Validate(server).Count == 0);
Check("accept valid local client", () => ConfigurationValidator.Validate(client).Count == 0);
Check("reject duplicate node IDs", () => ConfigurationValidator.Validate(server with { Nodes = [nodes[0], nodes[0], nodes[2]] }).Count > 0);
Check("reject duplicate endpoints", () => ConfigurationValidator.Validate(server with { Nodes = [nodes[0], nodes[1] with { ClientPort = 9001 }, nodes[2]] }).Count > 0);
Check("reject client/peer endpoint collision", () => ConfigurationValidator.Validate(server with { Nodes = [nodes[0] with { PeerPort = 9001 }, nodes[1], nodes[2]] }).Count > 0);
Check("reject port outside 1..65535", () => ConfigurationValidator.Validate(server with { Nodes = [nodes[0] with { ClientPort = 65536 }, nodes[1], nodes[2]] }).Count > 0);
Check("reject missing membership", () => ConfigurationValidator.Validate(server with { Nodes = [] }).Count > 0);
Check("reject unknown local node", () => ConfigurationValidator.Validate(server with { NodeId = 4 }).Count > 0);
Check("reject empty DatasetId", () => ConfigurationValidator.Validate(server with { DatasetId = Guid.Empty }).Count > 0);
Check("reject blank data directory", () => ConfigurationValidator.Validate(server with { DataDirectory = "" }).Count > 0);
Check("reject traversal data directory", () => ConfigurationValidator.Validate(client with { DataDirectory = "data/../shared" }).Count > 0);
Check("reject absolute data directory", () => ConfigurationValidator.Validate(client with { DataDirectory = "C:/shared" }).Count > 0);
Check("reject unsafe owner", () => ConfigurationValidator.Validate(client with { OwnerId = "A|B" }).Count > 0);
Check("reject missing owner", () => ConfigurationValidator.Validate(client with { OwnerId = "" }).Count > 0);
Check("reject owner/client mismatch", () => ConfigurationValidator.Validate(client with { OwnerId = "B" }).Count > 0);
Check("reject shared client directory", () => ConfigurationValidator.Validate(client with { DataDirectory = "data/clients/c2" }).Count > 0);
Check("reject shared server directory", () => ConfigurationValidator.Validate(server with { DataDirectory = "data/servers/s2" }).Count > 0);
Check("accept same ports on distinct LAN IPs", () => ConfigurationValidator.Validate(server with { Nodes = [new(1, "192.168.1.11", 9001, 9100), new(2, "192.168.1.12", 9001, 9100), new(3, "192.168.1.13", 9001, 9100)] }).Count == 0);
Check("bootstrap is RECOVERING", () => new BootstrapState().Role == "RECOVERING");
Check("bootstrap cannot serve", () => !new BootstrapState().ServingEnabled);
Check("bootstrap has no recovered StableIndex", () => new BootstrapState().StableIndex == 0);
Check("server rejects invalid config with clean exit 2", () => InvalidStartupExit("De6.Server.Host", "net10.0", server with { Nodes = [nodes[0], nodes[0], nodes[2]] }) == 2);
Check("client rejects invalid config with clean exit 2", () => InvalidStartupExit("De6.Client.WinForms", "net10.0-windows", client with { OwnerId = "" }) == 2);
Console.WriteLine($"{count - failures}/{count} assertions passed.");
return failures == 0 ? 0 : 1;

void Check(string name, Func<bool> assertion)
{
    count++;
    try
    {
        if (!assertion()) throw new InvalidOperationException("assertion returned false");
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
    }
}

int InvalidStartupExit<T>(string project, string framework, T invalidOptions)
{
    var root = new DirectoryInfo(AppContext.BaseDirectory);
    while (root is not null && !File.Exists(Path.Combine(root.FullName, "DoAnHTPT_Nhom9_2026.sln"))) root = root.Parent;
    if (root is null) throw new InvalidOperationException("Run the harness inside the repository after building the full solution.");
    var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
    var app = Path.Combine(root.FullName, "src", project, "bin", configuration, framework, project + ".dll");
    var dotnet = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "../../..", "dotnet.exe"));
    var temporary = Path.Combine(Path.GetTempPath(), "de6-invalid-" + Guid.NewGuid().ToString("N") + ".json");
    try
    {
        File.WriteAllText(temporary, JsonSerializer.Serialize(invalidOptions));
        var start = new ProcessStartInfo(dotnet) { RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in new[] { app, "--config", temporary, "--check" }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start configuration check.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException("Startup --check did not exit within 30 seconds.");
        }
        Task.WaitAll(output, error);
        if (process.ExitCode != 2) Console.Error.WriteLine($"Unexpected startup exit {process.ExitCode}: {error.Result}");
        return process.ExitCode;
    }
    finally { File.Delete(temporary); }
}