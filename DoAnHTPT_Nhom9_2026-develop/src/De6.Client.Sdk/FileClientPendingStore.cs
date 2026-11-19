
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using De6.Contracts;

namespace De6.Client.Sdk;

public sealed class FileClientPendingStore : IClientPendingStore
{
    private const int StoreVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly Guid _datasetId;
    private readonly string _ownerId;
    private readonly string _dataPath;
    private readonly FileStream _lockStream;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    private bool _disposed;

    private sealed class StoreDocument
    {
        public int Version { get; set; } = StoreVersion;
        public Guid DatasetId { get; set; }
        public string OwnerId { get; set; } = "";
        public List<PendingEntry> Pending { get; set; } = [];
        public List<ResolvedEntry> Resolved { get; set; } = [];
    }

    private sealed class PendingEntry
    {
        public CommandIdentity Identity { get; set; } = default!;
        public string CanonicalCommandBase64 { get; set; } = "";
        public DateTime SavedUtc { get; set; }
    }

    private sealed class ResolvedEntry
    {
        public Guid RequestId { get; set; }
        public string ResultPayloadBase64 { get; set; } = "";
        public DateTime ResolvedUtc { get; set; }
    }

    public FileClientPendingStore(
        string rootDirectory,
        Guid datasetId,
        string ownerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        if (datasetId == Guid.Empty)
            throw new ArgumentException(
                "DatasetId cannot be empty.",
                nameof(datasetId));

        BookingCanonical.ValidateOwner(ownerId);

        _datasetId = datasetId;
        _ownerId = ownerId;

        string ownerHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(ownerId)));

        string clientDirectory = Path.Combine(
            Path.GetFullPath(rootDirectory),
            datasetId.ToString("N"),
            ownerHash);

        Directory.CreateDirectory(clientDirectory);

        _dataPath = Path.Combine(
            clientDirectory,
            "pending.json");

        string lockPath = Path.Combine(
            clientDirectory,
            "store.lock");

        try
        {
            _lockStream = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                1,
                FileOptions.WriteThrough);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                "Pending store is already in use.",
                ex);
        }

        try
        {
            if (File.Exists(_dataPath))
            {
                LoadDocumentAsync(
                    CancellationToken.None).GetAwaiter().GetResult();
            }
            else
            {
                var document = new StoreDocument
                {
                    DatasetId = _datasetId,
                    OwnerId = _ownerId
                };

                PersistDocumentAsync(
                    document,
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        }
        catch
        {
            _lockStream.Dispose();
            throw;
        }
    }

    public async ValueTask SaveBeforeSendAsync(
        CommandIdentity command,
        ReadOnlyMemory<byte> canonicalCommand,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        ValidateIdentity(command);

        if (canonicalCommand.IsEmpty)
            throw new ArgumentException(
                "Canonical command cannot be empty.",
                nameof(canonicalCommand));

        await _mutex.WaitAsync(cancellationToken);

        try
        {
            ThrowIfDisposed();

            StoreDocument document =
                await LoadDocumentAsync(cancellationToken);

            PendingEntry? existing = document.Pending
                .FirstOrDefault(x =>
                    x.Identity.RequestId == command.RequestId);

            if (existing is not null)
            {
                byte[] existingBytes =
                    Convert.FromBase64String(
                        existing.CanonicalCommandBase64);

                bool sameIdentity = existing.Identity == command;

                bool sameBytes = existingBytes
                    .AsSpan()
                    .SequenceEqual(canonicalCommand.Span);

                if (!sameIdentity || !sameBytes)
                    throw new InvalidOperationException(
                        "RequestId already exists with different command.");

                return;
            }

            bool alreadyResolved = document.Resolved
                .Any(x => x.RequestId == command.RequestId);

            if (alreadyResolved)
                throw new InvalidOperationException(
                    "RequestId has already been resolved.");

            document.Pending.Add(new PendingEntry
            {
                Identity = command,
                CanonicalCommandBase64 =
                    Convert.ToBase64String(canonicalCommand.Span),
                SavedUtc = DateTime.UtcNow
            });

            await PersistDocumentAsync(
                document,
                cancellationToken);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async ValueTask<IReadOnlyList<CommandIdentity>>
        ReadUnresolvedAsync(CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);

        try
        {
            ThrowIfDisposed();

            StoreDocument document =
                await LoadDocumentAsync(cancellationToken);

            return document.Pending
                .Select(x => x.Identity)
                .ToArray();
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async ValueTask<IReadOnlyList<PendingCommand>> ReadUnresolvedCommandsAsync(CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            StoreDocument document = await LoadDocumentAsync(cancellationToken);
            return document.Pending
                .Select(x => new PendingCommand(
                    x.Identity,
                    Convert.FromBase64String(x.CanonicalCommandBase64),
                    x.SavedUtc))
                .ToArray();
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async ValueTask<IReadOnlyList<ResolvedCommand>> ReadResolvedAsync(CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            StoreDocument document = await LoadDocumentAsync(cancellationToken);
            return document.Resolved
                .Select(x => new ResolvedCommand(
                    x.RequestId,
                    Convert.FromBase64String(x.ResultPayloadBase64),
                    x.ResolvedUtc))
                .ToArray();
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async ValueTask SaveResolvedAsync(
        Guid requestId,
        ReadOnlyMemory<byte> resultPayload,
        CancellationToken cancellationToken)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException(
                "RequestId cannot be empty.",
                nameof(requestId));

        if (resultPayload.IsEmpty)
            throw new ArgumentException(
                "Result payload cannot be empty.",
                nameof(resultPayload));

        await _mutex.WaitAsync(cancellationToken);

        try
        {
            ThrowIfDisposed();

            StoreDocument document =
                await LoadDocumentAsync(cancellationToken);

            ResolvedEntry? existingResolved = document.Resolved
                .FirstOrDefault(x => x.RequestId == requestId);

            if (existingResolved is not null)
            {
                byte[] existingPayload =
                    Convert.FromBase64String(
                        existingResolved.ResultPayloadBase64);

                if (!existingPayload
                    .AsSpan()
                    .SequenceEqual(resultPayload.Span))
                {
                    throw new InvalidOperationException(
                        "RequestId already has a different resolved result.");
                }

                return;
            }

            PendingEntry? pending = document.Pending
                .FirstOrDefault(x =>
                    x.Identity.RequestId == requestId);

            if (pending is null)
                throw new InvalidOperationException(
                    "RequestId does not exist in pending store.");

            document.Resolved.Add(new ResolvedEntry
            {
                RequestId = requestId,
                ResultPayloadBase64 =
                    Convert.ToBase64String(resultPayload.Span),
                ResolvedUtc = DateTime.UtcNow
            });

            document.Pending.Remove(pending);

            await PersistDocumentAsync(
                document,
                cancellationToken);
        }
        finally
        {
            _mutex.Release();
        }
    }

    private void ValidateIdentity(CommandIdentity command)
    {
        if (command.DatasetId != _datasetId)
            throw new InvalidOperationException(
                "DatasetId does not match this store.");

        if (!StringComparer.Ordinal.Equals(
            command.OwnerId,
            _ownerId))
        {
            throw new InvalidOperationException(
                "OwnerId does not match this store.");
        }

        if (command.RequestId == Guid.Empty)
            throw new ArgumentException(
                "RequestId cannot be empty.");

        ArgumentNullException.ThrowIfNull(command.Command);

        string expectedHash =
            CanonicalProtocol.ComputeCommandHashString(
                command.DatasetId,
                command.OwnerId,
                command.Command.Action,
                command.Command.TripId,
                command.Command.SeatNo,
                command.Command.ReservationId);

        if (!StringComparer.OrdinalIgnoreCase.Equals(
            expectedHash,
            command.CommandHash))
        {
            throw new InvalidOperationException(
                "CommandHash does not match command identity.");
        }
    }

    private async Task<StoreDocument> LoadDocumentAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_dataPath))
        {
            return new StoreDocument
            {
                DatasetId = _datasetId,
                OwnerId = _ownerId
            };
        }

        await using var stream = new FileStream(
            _dataPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous |
            FileOptions.SequentialScan);

        StoreDocument? document =
            await JsonSerializer.DeserializeAsync<StoreDocument>(
                stream,
                JsonOptions,
                cancellationToken);

        if (document is null)
            throw new InvalidDataException(
                "Pending store is invalid.");

        if (document.Version != StoreVersion)
            throw new InvalidDataException(
                "Unsupported pending store version.");

        if (document.DatasetId != _datasetId)
            throw new InvalidDataException(
                "DatasetId does not match pending store.");

        if (!StringComparer.Ordinal.Equals(
            document.OwnerId,
            _ownerId))
        {
            throw new InvalidDataException(
                "OwnerId does not match pending store.");
        }

        document.Pending ??= [];
        document.Resolved ??= [];

        if (document.Pending.Select(x => x.Identity?.RequestId).Distinct().Count() != document.Pending.Count)
            throw new InvalidDataException("Pending store contains duplicate RequestId values.");

        if (document.Resolved.Select(x => x.RequestId).Distinct().Count() != document.Resolved.Count)
            throw new InvalidDataException("Pending store contains duplicate resolved RequestId values.");

        if (document.Pending.Any(x => document.Resolved.Any(y => y.RequestId == x.Identity.RequestId)))
            throw new InvalidDataException("A RequestId cannot be both pending and resolved.");

        foreach (PendingEntry entry in document.Pending)
        {
            if (entry.Identity is null ||
                entry.Identity.DatasetId != _datasetId ||
                entry.Identity.OwnerId != _ownerId)
            {
                throw new InvalidDataException(
                    "Pending identity is invalid.");
            }

            ValidateIdentity(entry.Identity);

            if (string.IsNullOrWhiteSpace(
                entry.CanonicalCommandBase64))
            {
                throw new InvalidDataException(
                    "Canonical command is missing.");
            }

            Convert.FromBase64String(
                entry.CanonicalCommandBase64);
        }

        foreach (ResolvedEntry entry in document.Resolved)
        {
            if (entry.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(entry.ResultPayloadBase64))
                throw new InvalidDataException("Resolved entry is invalid.");
            Convert.FromBase64String(entry.ResultPayloadBase64);
        }

        return document;
    }

    private async Task PersistDocumentAsync(
        StoreDocument document,
        CancellationToken cancellationToken)
    {
        string temporaryPath = _dataPath + "." +
            Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous |
                FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    document,
                    JsonOptions,
                    cancellationToken);

                await stream.FlushAsync(cancellationToken);
                stream.Flush(true);
            }

            if (File.Exists(_dataPath))
            {
                File.Replace(
                    temporaryPath,
                    _dataPath,
                    null,
                    true);
            }
            else
            {
                File.Move(
                    temporaryPath,
                    _dataPath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _lockStream.Dispose();
            _mutex.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}
