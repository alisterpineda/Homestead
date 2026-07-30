using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using Google;
using Google.Apis.Drive.v3;
using Google.Apis.Http;
using Google.Apis.Services;
using Google.Apis.Upload;
using Homestead.WebApi.Domain.Backup;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace Homestead.WebApi.Infrastructure.Backup.GoogleDrive;

/// <summary>
/// Mirrors documents into a personal Google account's My Drive, under one app-managed root folder path
/// (<c>Homestead/Backup</c> by default).
/// </summary>
/// <remarks>
/// <para>
/// The first destination that is not a filesystem, and everything unusual here follows from one fact: a
/// Drive file's identity is its <b>id</b>, and its name is only metadata. Drive legally holds duplicate
/// names in one folder, <c>name =</c> queries are case-insensitive, and listing is eventually consistent
/// — so names never address anything. Writes go to the persisted handle; the single name lookup in the
/// design is crash recovery.
/// </para>
/// <para>
/// <b><c>files.delete</c> is never called.</b> It permanently deletes bypassing the trash, and on a
/// folder it takes every descendant the user owns — including files this app's <c>drive.file</c> scope
/// cannot even see. Every delete here, file or folder, is <c>files.update {trashed: true}</c>, and
/// Drive's ~30-day trash window is the recovery net under every other mitigation in this class.
/// </para>
/// </remarks>
public sealed class GoogleDriveBackupTarget : IBackupTarget, IBackupTargetCache, IDisposable
{
    /// <summary>Drive's folder MIME type — a folder is just a file with this type and no content.</summary>
    private const string FolderMimeType = "application/vnd.google-apps.folder";

    /// <summary>The My Drive root, as an alias Drive accepts anywhere a parent id is expected.</summary>
    private const string MyDriveRoot = "root";

    /// <summary>
    /// Two segments rather than one flat folder: the top segment is an app-owned namespace, so a future
    /// non-backup Drive context becomes <c>Homestead/Exports</c> instead of squatting a second top-level
    /// folder. Applied at first creation only — see <see cref="_resolvedRootPath"/>.
    /// </summary>
    private const string DefaultRootFolderPath = "Homestead/Backup";

    /// <summary>
    /// Upload chunk size. Must be a multiple of 256 KB (Drive's rule); 8 MB is large enough that a
    /// library of scans is one chunk each, and small enough that a deadline lands between chunks rather
    /// than stranding tens of megabytes of re-upload.
    /// </summary>
    private const int ChunkSize = 8 * 1024 * 1024;

    /// <summary>Fields asked of every upload response — the verification payload.</summary>
    private const string UploadFields = "id,sha256Checksum,md5Checksum";

    private const int MaxRateLimitAttempts = 5;

    // Sized for a residential uplink against the processor's five-minute per-job timeout: ~64 MB needs
    // roughly 2 Mbps sustained to fit, leaving room for backoff and the 30-second safety margin. The
    // source kind's own caps (100 files / 512 MB) were sized against local disk and are wildly wrong here.
    private const int DriveMaxFilesPerBatch = 25;
    private const long DriveMaxBytesPerBatch = 64L * 1024 * 1024;
    private const int DriveMaxOpsPerMetadataBatch = 50;

    public const string OutOfSpaceMessage =
        "Google Drive is out of storage space — mirror deletes free space only after Google's trash is emptied.";

    private readonly BackupTargetOptions _options;
    private readonly GoogleDriveTokenManager _tokenManager;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GoogleDriveBackupTarget> _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Lazy<DriveService> _drive;

    /// <summary>
    /// The <c>(path → folder id)</c> map, in memory. Authoritative copy lives in
    /// <c>backup.target_folders</c>; this is the read-through cache that makes a steady-state run cost
    /// zero folder calls.
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _folderIds = new(StringComparer.Ordinal);

    /// <summary>
    /// Serializes every folder resolution for this target. Concurrent find-or-create is precisely the
    /// race that mints twin year-folders and splits the mirror permanently, so folder work is
    /// single-flight by construction rather than by hope.
    /// </summary>
    private readonly SemaphoreSlim _folderGate = new(1, 1);

    // Volatile because InvalidateCache clears it from a request thread while a run may be reading it
    // under the gate — the write has to be visible, not merely eventually visible.
    private volatile bool _mapLoaded;

    /// <summary>
    /// The root path as actually created, once anything exists there. Pinned: it wins over config for the
    /// life of the mirror, because <c>DocumentBackupState</c> rows hold root-relative paths plus Drive
    /// file ids — honouring a changed config would mint a new empty root while every file stayed
    /// physically in the old tree, and mirror reporting would converge while being silently wrong.
    /// </summary>
    /// <remarks>Volatile for the same reason <c>_mapLoaded</c> is; cleared alongside it.</remarks>
    private volatile string? _resolvedRootPath;

    public GoogleDriveBackupTarget(
        string key,
        BackupTargetOptions options,
        GoogleDriveTokenManager tokenManager,
        IServiceScopeFactory scopeFactory,
        ILogger<GoogleDriveBackupTarget> logger,
        Google.Apis.Http.IHttpClientFactory? httpClientFactory = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        Key = key;
        _options = options;
        _tokenManager = tokenManager;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _delay = delay ?? Task.Delay;

        // Config is static, so the config-shape verdict never changes — computed once, exactly as the
        // local target does. Whether a GRANT exists and still works is database state and belongs to
        // IBackupTargetGate.
        ConfigurationError = BackupConfiguration.Validate(key, options, new Storage.FileStorageOptions());

        // Lazy: constructing a DriveService on an unconfigured target would be wasted work, and every
        // target in config is constructed at startup whether or not anyone ever runs it.
        _drive = new Lazy<DriveService>(() => CreateDriveService(httpClientFactory));
    }

    public string Key { get; }
    public BackupTargetKind? Kind => _options.Kind;
    public bool IsAvailable => ConfigurationError is null;
    public string? ConfigurationError { get; }

    /// <summary>What config asks for, which is only consulted while nothing has been created yet.</summary>
    public string? Location => DescribeLocation(null);

    /// <inheritdoc />
    /// <remarks>
    /// The persisted root wins, so the card names the folder the mirror actually lives in rather than the
    /// one config currently asks for. Null-when-unconfigured is an interface contract: there is no
    /// meaningful "where" for a target that cannot run.
    /// </remarks>
    public string? DescribeLocation(string? resolvedRoot)
    {
        if (ConfigurationError is not null)
            return null;

        // Blank falls back rather than rendering a nameless folder, for the same reason a blank config
        // value does — the caller reads the column straight off the row and cannot vouch for it.
        var root = string.IsNullOrWhiteSpace(resolvedRoot) ? EffectiveRootFolderPath : resolvedRoot.Trim();

        return $"Google Drive / {root}";
    }

    private string EffectiveRootFolderPath =>
        string.IsNullOrWhiteSpace(_options.RootFolderPath)
            ? DefaultRootFolderPath
            : _options.RootFolderPath.Trim();

    // 255 bytes per name and a deep tree, same as the conservative default — but the payoff Phase 1
    // declared is real here: Drive returns a checksum on the write response, so a copy can be verified
    // without downloading it back.
    public BackupTargetCapabilities Capabilities => new(
        MaxSegmentLength: 255,
        MaxPathLength: 4096,
        SupportsMove: true,
        SupportsContentHash: true,
        DefersFolderPrune: true);

    public BackupBatchCapOverrides BatchCapOverrides => new(
        _options.MaxFilesPerBatch ?? DriveMaxFilesPerBatch,
        _options.MaxBytesPerBatch ?? DriveMaxBytesPerBatch,
        _options.MaxOpsPerMetadataBatch ?? DriveMaxOpsPerMetadataBatch);

    // --- Writes ---------------------------------------------------------------------------------------

    public async Task<BackupPutResult> PutAsync(
        BackupObjectRef target, Stream content, BackupPutContext context, CancellationToken ct)
    {
        EnsureConfigured();

        var (folderPath, name) = SplitPath(target.RelativePath);

        // A replace goes straight to the stored id. No lookup, no ambiguity — this is the steady state.
        if (target.Handle is { Length: > 0 } handle)
            return await UploadAsync(handle, parentId: null, name, content, context, ct);

        // No handle: either a first copy, or the crash window — a previous run wrote the file and died
        // before its state row landed. The second case is why this looks before it creates: without the
        // lookup, every crash would leave a permanent duplicate that nothing ever notices, because the
        // destination is never enumerated.
        var parentId = await ResolveFolderIdAsync(folderPath, ct);
        var existingId = await FindDeterministicMatchAsync(parentId, name, ct);

        try
        {
            return await UploadAsync(
                existingId, existingId is null ? parentId : null, name, content, context, ct);
        }
        // Only when this was a fresh create. A 404 while RESUMING means the session URI died (Drive
        // expires them after about a week), which says nothing about the parent — and dropping a
        // perfectly good folder mapping on that evidence would be a self-inflicted wound.
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound
                                            && existingId is null
                                            && context.ResumeSessionUri is null or { Length: 0 })
        {
            // The mapped parent folder is gone — deleted out of band, or trashed by a prune that raced
            // this run. Drop the row so the next run's ensure step rebuilds it, and fail this one action
            // rather than silently creating the file at the Drive root.
            await ForgetFolderAsync(folderPath, ct);

            throw new InvalidOperationException(
                $"The backup folder '{folderPath}' no longer exists on target '{Key}'. It will be " +
                "re-created on the next run.", ex);
        }
    }

    public async Task<string> MoveAsync(BackupObjectRef from, string toRelativePath, CancellationToken ct)
    {
        EnsureConfigured();

        // Without an id there is nothing to patch. Reported as a missing file so the batch handler's
        // self-heal contract kicks in: it drops the state row and the next run re-copies from scratch.
        if (from.Handle is not { Length: > 0 } fileId)
            throw new FileNotFoundException(
                $"Backup move for '{from.RelativePath}' on target '{Key}' has no stored Drive file id.",
                from.RelativePath);

        var (toFolder, toName) = SplitPath(toRelativePath);
        var newParentId = await ResolveFolderIdAsync(toFolder, ct);
        var oldParentId = await ResolveOldParentIdAsync(from, fileId, ct);

        var patch = new DriveFile
        {
            Name = toName,
            // Asserted on every id-addressed update. Normally a no-op; a resurrection when the user has
            // tidied the mirror into their trash. Without it the update SUCCEEDS against a trashed file
            // and the card shows a green, converged mirror thirty days from being purged.
            Trashed = false,
        };

        var request = _drive.Value.Files.Update(patch, fileId);
        request.Fields = "id";
        request.AddParents = newParentId;

        if (oldParentId is not null && oldParentId != newParentId)
            request.RemoveParents = oldParentId;

        try
        {
            var updated = await ExecuteAsync(() => request.ExecuteAsync(ct), ct);
            return updated.Id;
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            // Either the file or the destination folder is gone. Drop the folder row in case it was the
            // folder — the next run's ensure step repairs the map — and report the file as missing so the
            // batch drops its state row and re-copies.
            await ForgetFolderAsync(toFolder, ct);

            throw new FileNotFoundException(
                $"Backup source '{from.RelativePath}' is missing from target '{Key}'.", from.RelativePath);
        }
    }

    public async Task DeleteAsync(BackupObjectRef target, CancellationToken ct)
    {
        EnsureConfigured();

        if (target.Handle is not { Length: > 0 } fileId)
        {
            // Idempotent by contract, and there is deliberately no name-based fallback: a delete resolved
            // by name could trash the wrong file, and the one-way doctrine means nothing here trusts the
            // destination's own listing over the state row.
            _logger.LogWarning(
                "Backup delete for '{Path}' on target {TargetKey} has no stored Drive file id; nothing to trash",
                target.RelativePath, Key);
            return;
        }

        var request = _drive.Value.Files.Update(new DriveFile { Trashed = true }, fileId);
        request.Fields = "id";

        try
        {
            await ExecuteAsync(() => request.ExecuteAsync(ct), ct);
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            // Already gone. Not an error — the contract is idempotence.
        }
    }

    // --- Folders --------------------------------------------------------------------------------------

    public async Task EnsureFoldersAsync(IReadOnlyCollection<string> folderPaths, CancellationToken ct)
    {
        EnsureConfigured();

        await _folderGate.WaitAsync(ct);
        try
        {
            await LoadMapAsync(ct);

            foreach (var path in ExpandWithAncestors(folderPaths))
                await EnsureLockedAsync(path, ct);
        }
        finally
        {
            _folderGate.Release();
        }
    }

    public async Task PruneEmptyFoldersAsync(IReadOnlyCollection<string> folderPaths, CancellationToken ct)
    {
        EnsureConfigured();

        await _folderGate.WaitAsync(ct);
        try
        {
            await LoadMapAsync(ct);

            // Deepest first, so trashing a leaf makes its parent a candidate in the same pass.
            var candidates = folderPaths
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct(StringComparer.Ordinal)
                .OrderByDescending(Depth)
                .ThenBy(p => p, StringComparer.Ordinal)
                .ToList();

            var visited = new HashSet<string>(StringComparer.Ordinal);

            foreach (var candidate in candidates)
                await PruneUpwardsAsync(candidate, visited, ct);
        }
        finally
        {
            _folderGate.Release();
        }
    }

    private async Task PruneUpwardsAsync(string path, HashSet<string> visited, CancellationToken ct)
    {
        var current = path;

        // Stops at the kind root (a single segment, e.g. "Documents") and never touches the target root
        // itself — the same rule the local target's walk-up follows.
        while (current.Contains('/') && visited.Add(current))
        {
            if (!_folderIds.TryGetValue(current, out var folderId))
                return;

            bool empty;
            try
            {
                var list = _drive.Value.Files.List();
                list.Q = $"'{EscapeQueryValue(folderId)}' in parents and trashed = false";
                list.Fields = "files(id)";
                list.PageSize = 1;

                var response = await ExecuteAsync(() => list.ExecuteAsync(ct), ct);
                empty = response.Files is null or { Count: 0 };

                if (empty)
                {
                    var trash = _drive.Value.Files.Update(new DriveFile { Trashed = true }, folderId);
                    trash.Fields = "id";
                    await ExecuteAsync(() => trash.ExecuteAsync(ct), ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException
                                       and not GoogleDriveAuthException
                                       and not BackupTargetOutOfSpaceException)
            {
                // Best-effort by design, exactly as the local target's prune is: a leftover empty folder
                // is cosmetic, and failing the batch over one would strand real work.
                _logger.LogInformation(ex,
                    "Could not prune backup folder '{Path}' on target {TargetKey}", current, Key);
                return;
            }

            if (!empty)
                return;

            await ForgetFolderAsync(current, ct);
            current = ParentPath(current);
        }
    }

    /// <summary>Resolves one folder id, ensuring it on demand if the map has no answer.</summary>
    private async Task<string> ResolveFolderIdAsync(string path, CancellationToken ct)
    {
        if (_folderIds.TryGetValue(path, out var cached))
            return cached;

        // A miss here means the ensure step never saw this path (a crash gap, or a folder deleted out of
        // band). Single-flight through the same gate the ensure step uses, so an on-demand create can
        // never race a concurrent one into twin folders.
        await _folderGate.WaitAsync(ct);
        try
        {
            await LoadMapAsync(ct);

            if (_folderIds.TryGetValue(path, out cached))
                return cached;

            foreach (var ancestor in ExpandWithAncestors([path]))
                await EnsureLockedAsync(ancestor, ct);

            return _folderIds[path];
        }
        finally
        {
            _folderGate.Release();
        }
    }

    /// <summary>Find-or-create one folder, assuming the gate is held and every ancestor is already mapped.</summary>
    private async Task EnsureLockedAsync(string path, CancellationToken ct)
    {
        if (_folderIds.ContainsKey(path))
            return;

        if (path.Length == 0)
        {
            await EnsureRootLockedAsync(ct);
            return;
        }

        var parentId = _folderIds[ParentPath(path)];
        var name = path[(path.LastIndexOf('/') + 1)..];

        var folder = await FindFolderAsync(parentId, name, ct)
                     ?? await CreateFolderAsync(parentId, name, ct);

        _folderIds[path] = folder.Id;
        await RememberFolderAsync(path, folder.Id, ct);
    }

    /// <summary>
    /// Find-or-creates the whole root chain (<c>Homestead/Backup</c>) and maps its last segment as the
    /// target root.
    /// </summary>
    private async Task EnsureRootLockedAsync(CancellationToken ct)
    {
        // Reached only when the root is unmapped, and that is enough: a mapped root always has its path
        // recorded, because RememberRootAsync writes both rows in ONE transaction. There is deliberately no
        // repair path for "mapped but unrecorded" — no code path can produce it (the account switch clears
        // both together; a disconnect clears neither the pin nor the link), and the one thing that could —
        // a mirror created before this column existed — is out of scope by standing permission from the
        // repo owner. If that invariant is ever broken, the symptom is a card that reports the configured
        // path for a mirror that lives elsewhere; fix the writer, not this reader.
        //
        // The path as actually created wins over config once anything exists there: DocumentBackupState
        // rows hold root-relative paths plus Drive file ids, so relocating the root would leave the
        // mirror physically in the old tree while reporting converged.
        var path = _resolvedRootPath ?? EffectiveRootFolderPath;

        var parentId = MyDriveRoot;
        string? webViewLink = null;

        // Intermediates deliberately get NO map entry. Map keys stay strictly root-relative, which is what
        // keeps PruneUpwardsAsync's slash guard a structural guarantee that nothing above the effective
        // root can be trashed — the failure mode being the user's own top-level "Homestead". Cold cost is
        // one files.list per segment; the root resolves once per process and is then cached.
        foreach (var segment in path.Split('/'))
        {
            var folder = await FindFolderAsync(parentId, segment, ct)
                         ?? await CreateFolderAsync(parentId, segment, ct);

            parentId = folder.Id;
            webViewLink = folder.WebViewLink;
        }

        _folderIds[string.Empty] = parentId;
        _resolvedRootPath = path;

        await RememberRootAsync(path, parentId, webViewLink, ct);
    }

    /// <summary>
    /// A Drive folder as this class needs it: the id everything is addressed by, plus Drive's own URL for
    /// it so the admin card can link straight to the folder the app owns.
    /// </summary>
    /// <remarks>
    /// The link is requested for every folder rather than only the root: it is one extra field on a call
    /// already being made, and branching would cost more than it saves.
    /// </remarks>
    private readonly record struct DriveFolder(string Id, string? WebViewLink);

    private async Task<DriveFolder?> FindFolderAsync(string parentId, string name, CancellationToken ct)
    {
        var list = _drive.Value.Files.List();
        list.Q = $"name = '{EscapeQueryValue(name)}' and '{EscapeQueryValue(parentId)}' in parents " +
                 $"and mimeType = '{FolderMimeType}' and trashed = false";
        list.Fields = "files(id,webViewLink)";
        list.PageSize = 10;
        // Oldest wins, so two instances that already raced into twin folders converge on the same one
        // rather than alternating.
        list.OrderBy = "createdTime";

        var response = await ExecuteAsync(() => list.ExecuteAsync(ct), ct);

        return response.Files is { Count: > 0 } files
            ? new DriveFolder(files[0].Id, files[0].WebViewLink)
            : null;
    }

    private async Task<DriveFolder> CreateFolderAsync(string parentId, string name, CancellationToken ct)
    {
        var request = _drive.Value.Files.Create(new DriveFile
        {
            Name = name,
            MimeType = FolderMimeType,
            Parents = [parentId],
        });
        request.Fields = "id,webViewLink";

        var created = await ExecuteAsync(() => request.ExecuteAsync(ct), ct);
        return new DriveFolder(created.Id, created.WebViewLink);
    }

    // --- Uploads --------------------------------------------------------------------------------------

    private async Task<BackupPutResult> UploadAsync(
        string? fileId,
        string? parentId,
        string name,
        Stream content,
        BackupPutContext context,
        CancellationToken ct)
    {
        var metadata = new DriveFile { Name = name };

        ResumableUpload<DriveFile, DriveFile> upload;
        if (fileId is not null)
        {
            // Every id-addressed update asserts trashed:false — see MoveAsync for why this matters.
            metadata.Trashed = false;

            var update = _drive.Value.Files.Update(metadata, fileId, content, context.ContentType);
            update.Fields = UploadFields;
            // Never set KeepRevisionForever: unpinned prior revisions cost no quota and auto-purge, so
            // there is no revision cleanup to write. Pinned ones would cost quota forever.
            upload = update;
        }
        else
        {
            metadata.Parents = [parentId!];

            var create = _drive.Value.Files.Create(metadata, content, context.ContentType);
            create.Fields = UploadFields;
            upload = create;
        }

        upload.ChunkSize = ChunkSize;

        var result = await RunUploadAsync(upload, name, context, ct);

        if (result is not null)
            return result;

        var response = upload.ResponseBody
            ?? throw new InvalidOperationException(
                $"Google Drive accepted the upload of '{name}' but returned no file metadata.");

        await VerifyChecksumAsync(response, name, context, ct);

        return new BackupPutResult.Completed(response.Id);
    }

    /// <summary>
    /// Drives the resumable upload to completion, or parks it. Returns null when the upload finished.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The deadline is enforced with a linked token that is <i>ours</i>, so a budget expiry is
    /// distinguishable from the caller cancelling: the first parks a session and completes the batch
    /// green, the second is a real cancellation and propagates.
    /// </para>
    /// <para>
    /// Throttling is retried here rather than by the caller, and only along the two paths the SDK
    /// defines for it: <c>ResumeAsync</c> against an issued session (which re-queries the committed
    /// offset from the server before sending anything, so it cannot double-write), and a rewind-and-retry
    /// when the throttle landed before any session existed. Backoff consumes the deadline budget, which
    /// is the point — a throttled upload runs out of window and parks instead of failing.
    /// </para>
    /// </remarks>
    private async Task<BackupPutResult?> RunUploadAsync(
        ResumableUpload<DriveFile, DriveFile> upload,
        string name,
        BackupPutContext context,
        CancellationToken ct)
    {
        var sessionUri = context.ResumeSessionUri;
        var bytesSent = context.ResumeOffset;

        upload.UploadSessionData += data => sessionUri = data.UploadUri?.ToString() ?? sessionUri;
        upload.ProgressChanged += progress => bytesSent = Math.Max(bytesSent, progress.BytesSent);

        for (var attempt = 1; ; attempt++)
        {
            using var deadlineCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var budgetExpired = false;

            if (context.Deadline is { } deadline)
            {
                var remaining = deadline - DateTimeOffset.UtcNow;

                // Past the budget. Park if there is a session to park; otherwise report no progress and
                // let the caller re-plan the action into the continuation.
                if (remaining <= TimeSpan.Zero)
                    return Park(sessionUri, bytesSent, name);

                deadlineCts.CancelAfter(remaining);
            }

            IUploadProgress progress;
            try
            {
                progress = sessionUri is { Length: > 0 } resume
                    ? await upload.ResumeAsync(new Uri(resume), deadlineCts.Token)
                    : await upload.UploadAsync(deadlineCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                budgetExpired = true;
                progress = upload.GetProgress();
            }

            // Checked before the budget, not after: an upload that landed in the same instant the
            // deadline fired is finished, and parking it would throw away a completed transfer and
            // re-send the whole file in the continuation.
            if (progress.Status == UploadStatus.Completed)
                return null;

            // The SDK reports a cancelled upload as Failed-with-exception rather than throwing, so the
            // budget check has to look at both shapes.
            budgetExpired |= !ct.IsCancellationRequested
                             && progress.Exception is OperationCanceledException;

            if (budgetExpired)
                return Park(sessionUri, bytesSent, name);

            // A genuine failure. The SDK surfaces upload errors on the progress object rather than by
            // throwing, so out-of-space and throttling have to be classified from there.
            if (progress.Exception is not { } failure)
            {
                ct.ThrowIfCancellationRequested();

                throw new InvalidOperationException(
                    $"Google Drive upload finished in state {progress.Status} without a response.");
            }

            if (failure is not GoogleApiException google
                || !IsRateLimited(google)
                || attempt >= MaxRateLimitAttempts)
            {
                throw Reclassify(failure);
            }

            // Nothing was committed and no session exists, so the next attempt starts over — which means
            // the stream has to go back to where it started. A stream that cannot rewind cannot be
            // retried safely, so it isn't.
            if (sessionUri is null or { Length: 0 })
            {
                if (!upload.ContentStream.CanSeek)
                    throw Reclassify(failure);

                upload.ContentStream.Position = 0;
            }

            var wait = TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1));

            _logger.LogInformation(
                "Google Drive throttled the upload of '{Name}' on target {TargetKey} (attempt {Attempt}); " +
                "retrying in {Delay}", name, Key, attempt, wait);

            await _delay(wait, ct);
        }
    }

    private BackupPutResult Park(string? sessionUri, long bytesSent, string name)
    {
        if (sessionUri is not { Length: > 0 })
        {
            // The budget expired before Drive issued a session. Nothing to resume, but nothing was
            // written either — an empty session URI tells the caller to re-plan rather than record.
            return new BackupPutResult.Suspended(string.Empty, 0);
        }

        _logger.LogInformation(
            "Upload of '{Name}' to target {TargetKey} ran out of batch budget at {Bytes} bytes; parking " +
            "the session for the continuation batch", name, Key, bytesSent);

        return new BackupPutResult.Suspended(sessionUri, bytesSent);
    }

    /// <summary>
    /// Compares Drive's own checksum against the hash we already verified the source blob against.
    /// </summary>
    /// <remarks>
    /// This is the declared Phase 2 payoff of <c>SupportsContentHash</c>: the copy is verified from the
    /// write response, never by downloading it back. <c>sha256Checksum</c> is documented "if available",
    /// so MD5 is the fallback — computed in the same pre-write pass, not a second read. Both absent means
    /// trust-and-record: the pre-write hash check has already guarded the content.
    /// </remarks>
    private async Task VerifyChecksumAsync(
        DriveFile response, string name, BackupPutContext context, CancellationToken ct)
    {
        // SHA-256 when Drive reported one, else MD5, else neither — in which case there is nothing to
        // compare and the pre-write hash check has already guarded the content.
        bool? matches =
            Compare(response.Sha256Checksum, context.Sha256Hex)
            ?? Compare(response.Md5Checksum, context.Md5Hex);

        if (matches is not false)
            return;

        // Trash the bad copy BEFORE failing. Leaving it would let the next run's crash-window lookup find
        // corrupt bytes under the right name and adopt them as the mirror.
        try
        {
            var trash = _drive.Value.Files.Update(new DriveFile { Trashed = true }, response.Id);
            trash.Fields = "id";
            await trash.ExecuteAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not trash the corrupt upload of '{Name}' on target {TargetKey}", name, Key);
        }

        throw new InvalidOperationException(
            $"Google Drive reported a different checksum for '{name}' than the content that was sent. " +
            "The upload was trashed rather than recorded as mirrored.");
    }

    /// <summary>Null when either side is absent — "nothing to compare", not "they differ".</summary>
    private static bool? Compare(string? reported, string? expected) =>
        reported is { Length: > 0 } && expected is { Length: > 0 }
            ? string.Equals(reported, expected, StringComparison.OrdinalIgnoreCase)
            : null;

    // --- Crash-window lookup --------------------------------------------------------------------------

    private async Task<string?> FindDeterministicMatchAsync(string parentId, string name, CancellationToken ct)
    {
        var list = _drive.Value.Files.List();
        // Folders are excluded deliberately. A name match is not a type match, and adopting a folder as
        // "the file a previous run left here" would upload content onto a folder id — or, worse, trash it
        // as a surplus duplicate.
        list.Q = $"name = '{EscapeQueryValue(name)}' and '{EscapeQueryValue(parentId)}' in parents " +
                 $"and mimeType != '{FolderMimeType}' and trashed = false";
        list.Fields = "files(id,createdTime)";
        list.PageSize = 10;
        // Newest first: after a crash the newest match is the one the dead run wrote.
        list.OrderBy = "createdTime desc";

        var response = await ExecuteAsync(() => list.ExecuteAsync(ct), ct);

        if (response.Files is not { Count: > 0 } files)
            return null;

        // Drive allows duplicate names in a folder, so a crash loop could have left several. Keep the
        // newest and trash the rest rather than orphaning them — they are invisible otherwise, because
        // nothing ever enumerates the destination.
        foreach (var surplus in files.Skip(1))
        {
            try
            {
                var trash = _drive.Value.Files.Update(new DriveFile { Trashed = true }, surplus.Id);
                trash.Fields = "id";
                await trash.ExecuteAsync(ct);

                _logger.LogInformation(
                    "Trashed a duplicate of '{Name}' left behind in target {TargetKey} by an interrupted run",
                    name, Key);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Could not trash a duplicate of '{Name}' in target {TargetKey}", name, Key);
            }
        }

        return files[0].Id;
    }

    private async Task<string?> ResolveOldParentIdAsync(
        BackupObjectRef from, string fileId, CancellationToken ct)
    {
        // Free when the old folder is mapped, which it is for anything a previous run wrote.
        var (fromFolder, _) = SplitPath(from.RelativePath);
        if (_folderIds.TryGetValue(fromFolder, out var mapped))
            return mapped;

        // Otherwise ask. Drive now enforces a single parent, so an addParents without the matching
        // removeParents is rejected — this is not optional bookkeeping.
        try
        {
            var get = _drive.Value.Files.Get(fileId);
            get.Fields = "parents";

            var file = await ExecuteAsync(() => get.ExecuteAsync(ct), ct);
            return file.Parents is { Count: > 0 } parents ? string.Join(',', parents) : null;
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            throw new FileNotFoundException(
                $"Backup source '{from.RelativePath}' is missing from target '{Key}'.", from.RelativePath);
        }
    }

    // --- Folder map persistence -----------------------------------------------------------------------

    private async Task LoadMapAsync(CancellationToken ct)
    {
        if (_mapLoaded)
            return;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var rows = await db.BackupTargetFolders
            .AsNoTracking()
            .Where(f => f.TargetKey == Key)
            .Select(f => new { f.Path, f.RemoteFolderId })
            .ToListAsync(ct);

        foreach (var row in rows)
            _folderIds[row.Path] = row.RemoteFolderId;

        // The pin. Read here (and not seeded into _folderIds — the root's id has exactly one home, the
        // path-"" row already loaded above) so a reconnect re-finds the root along the path it was
        // actually created at rather than whatever config now says.
        var pinned = await db.BackupTargetStates
            .AsNoTracking()
            .Where(s => s.TargetKey == Key)
            .Select(s => s.RootFolderPath)
            .FirstOrDefaultAsync(ct);

        _resolvedRootPath = string.IsNullOrWhiteSpace(pinned) ? null : pinned;

        _mapLoaded = true;
    }

    /// <inheritdoc cref="IBackupTargetCache.InvalidateCache"/>
    /// <remarks>
    /// Called when the grant changes hands (disconnect, or a confirmed account switch), which is exactly
    /// when every id in the map stops resolving. Clearing only the database rows would leave this
    /// singleton handing out the old account's ids for the life of the process.
    /// </remarks>
    public void InvalidateCache()
    {
        _folderIds.Clear();
        _mapLoaded = false;
        // Cleared with the map, not independently: a stale path here would make a reconnect re-find the
        // root along the PREVIOUS account's layout. The row it was read from survives a disconnect on
        // purpose (it pins the surviving copy records) and is cleared only by an account switch.
        _resolvedRootPath = null;

        _logger.LogInformation(
            "Dropped the cached Drive folder map for target {TargetKey}; the next run rebuilds it", Key);
    }

    private async Task RememberFolderAsync(string path, string folderId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await UpsertFolderAsync(db, path, folderId, ct);

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Records the root <i>as actually created</i> — its id, the path, and Drive's own URL for the folder.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what makes the admin card's location true by construction: every run reads the path from
    /// here, and config is consulted only while nothing has been created yet.
    /// </para>
    /// <para>
    /// Both halves in <b>one</b> scope and one flush — the map row that addresses the folder and the state
    /// row the card renders from. Two flushes would be two connections and two round trips for one logical
    /// write, and a failure between them leaves the id mapped with no recorded path, which is precisely the
    /// state <see cref="AdoptMappedRootAsync"/> then has to spend a Drive call repairing. The id still has
    /// exactly one home: <c>backup.target_folders</c> under path key <c>""</c>. It is not duplicated onto
    /// the state row, whose clearing rules differ.
    /// </para>
    /// </remarks>
    private async Task RememberRootAsync(
        string path, string folderId, string? webViewLink, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTimeOffset.UtcNow;

        await UpsertFolderAsync(db, string.Empty, folderId, ct);

        var state = await db.BackupTargetStates.FirstOrDefaultAsync(s => s.TargetKey == Key, ct);

        if (state is null)
        {
            // Lazily, exactly as GoogleDriveGrantStore does: a target that has never been touched has no
            // row, and a first run against a target connected before this column existed is a first touch.
            state = new BackupTargetState
            {
                TargetKey = Key,
                IsEnabled = true,
                CreatedAt = now,
            };

            db.BackupTargetStates.Add(state);
        }

        state.RootFolderPath = path;
        // Never blanked by a response that simply did not carry one: a link already on file points at the
        // same folder, and losing it costs the only answer to "which of these is the app's".
        state.RootFolderWebViewLink = webViewLink ?? state.RootFolderWebViewLink;
        state.UpdatedAt = now;

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Stages the (path → folder id) row. The caller flushes, so several can share one round trip.</summary>
    private async Task UpsertFolderAsync(
        AppDbContext db, string path, string folderId, CancellationToken ct)
    {
        var existing = await db.BackupTargetFolders
            .FirstOrDefaultAsync(f => f.TargetKey == Key && f.Path == path, ct);

        if (existing is not null)
        {
            existing.RemoteFolderId = folderId;
            return;
        }

        db.BackupTargetFolders.Add(new BackupTargetFolder
        {
            Id = Guid.NewGuid(),
            TargetKey = Key,
            Path = path,
            RemoteFolderId = folderId,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    /// <summary>
    /// Drops a mapping, in memory and in the database. Both halves are needed: clearing only the cache
    /// would let the stale row come back on the next process start, and clearing only the row would leave
    /// this instance still handing out the dead id.
    /// </summary>
    private async Task ForgetFolderAsync(string path, CancellationToken ct)
    {
        if (path.Length == 0)
            return;

        _folderIds.TryRemove(path, out _);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // One row, through the change tracker rather than ExecuteDelete: there is no bulk win at this
        // size, and the tracked path keeps this usable from any provider.
        var row = await db.BackupTargetFolders
            .FirstOrDefaultAsync(f => f.TargetKey == Key && f.Path == path, ct);

        if (row is not null)
        {
            db.BackupTargetFolders.Remove(row);
            await db.SaveChangesAsync(ct);
        }

        _logger.LogInformation(
            "Dropped the Drive folder mapping for '{Path}' on target {TargetKey}; the next run's ensure " +
            "step will rebuild it", path, Key);
    }

    // --- Errors ---------------------------------------------------------------------------------------

    /// <summary>
    /// Runs one Drive call, classifying the three 403s that mean entirely different things.
    /// </summary>
    /// <remarks>
    /// Drive reports storage exhaustion, per-user throttling, and project throttling <b>all as HTTP
    /// 403</b>, distinguished only by a reason string — and <c>quotaExceeded</c>, confusingly, means
    /// <i>rate</i>. Getting this wrong is expensive in both directions: read a throttling storm as
    /// out-of-space and a transient blip skips a whole run's puts; read exhaustion as retryable and every
    /// put burns the full backoff ladder against a wall.
    /// </remarks>
    private async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (GoogleApiException ex) when (IsRateLimited(ex) && attempt < MaxRateLimitAttempts)
            {
                var wait = TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1));

                _logger.LogInformation(
                    "Google Drive throttled target {TargetKey} (attempt {Attempt}); retrying in {Delay}",
                    Key, attempt, wait);

                await _delay(wait, ct);
            }
            catch (GoogleApiException ex)
            {
                throw Reclassify(ex);
            }
        }
    }

    /// <summary>Maps a Google exception onto the typed outcomes the batch handler acts on.</summary>
    private static Exception Reclassify(Exception exception) => exception switch
    {
        GoogleApiException google when IsOutOfSpace(google) =>
            new BackupTargetOutOfSpaceException(OutOfSpaceMessage),

        GoogleApiException { HttpStatusCode: HttpStatusCode.Unauthorized } =>
            new GoogleDriveAuthException("unauthorized",
                "Google rejected the stored authorization — reconnect the backup target."),

        _ => exception
    };

    // Both lists are shared with the about.get probe, which classifies the same 403s from the raw JSON
    // rather than through the SDK: one of them reading a reason as transient while the other reads it as a
    // dead authorization is how a throttled minute would latch a working target as disconnected.
    private static bool IsOutOfSpace(GoogleApiException ex) =>
        HasReason(ex, GoogleDriveErrorReasons.OutOfSpace);

    private static bool IsRateLimited(GoogleApiException ex) =>
        ex.HttpStatusCode == HttpStatusCode.TooManyRequests
        || HasReason(ex, GoogleDriveErrorReasons.Throttling);

    private static bool HasReason(GoogleApiException ex, params string[] reasons) =>
        ex.Error?.Errors?.Any(e => reasons.Contains(e.Reason, StringComparer.Ordinal)) == true;

    // --- Plumbing -------------------------------------------------------------------------------------

    private DriveService CreateDriveService(Google.Apis.Http.IHttpClientFactory? httpClientFactory)
    {
        var initializer = new BaseClientService.Initializer
        {
            ApplicationName = "Homestead",
            HttpClientInitializer = new BearerTokenInitializer(this),
        };

        if (httpClientFactory is not null)
            initializer.HttpClientFactory = httpClientFactory;

        return new DriveService(initializer);
    }

    /// <summary>
    /// Injects a live access token per request, and retries a 401 exactly once after evicting the cache.
    /// </summary>
    /// <remarks>
    /// Per request rather than per service, because a service is a singleton and an access token lives an
    /// hour: a chunked upload started at minute 58 must not fail on its second chunk.
    /// </remarks>
    private sealed class BearerTokenInitializer(GoogleDriveBackupTarget owner)
        : IConfigurableHttpClientInitializer, IHttpExecuteInterceptor, IHttpUnsuccessfulResponseHandler
    {
        public void Initialize(ConfigurableHttpClient httpClient)
        {
            httpClient.MessageHandler.AddExecuteInterceptor(this);
            httpClient.MessageHandler.AddUnsuccessfulResponseHandler(this);
        }

        public async Task InterceptAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var token = await owner._tokenManager.GetAccessTokenAsync(
                owner.Key, owner._options.ClientId!, owner._options.ClientSecret!, ct);

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        public Task<bool> HandleResponseAsync(HandleUnsuccessfulResponseArgs args)
        {
            if (args.Response.StatusCode != HttpStatusCode.Unauthorized)
                return Task.FromResult(false);

            // Once. A second 401 after a fresh token is a dead grant, not a stale one, and looping would
            // just hammer the token endpoint on the way to the same answer.
            if (args.CurrentFailedTry > 1)
                return Task.FromResult(false);

            // Rejected, not merely stale: dropping the cache alone would let the next attempt read the
            // same (not-yet-expired) token straight off the row and fail identically.
            owner._tokenManager.InvalidateRejected(owner.Key);
            return Task.FromResult(args.SupportsRetry);
        }
    }

    private void EnsureConfigured()
    {
        if (ConfigurationError is not null)
            throw new InvalidOperationException($"Backup target '{Key}' is unavailable: {ConfigurationError}");
    }

    /// <summary>Splits a forward-slash relative path into (folder path, file name).</summary>
    private static (string FolderPath, string Name) SplitPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new InvalidOperationException("A backup path cannot be empty.");

        var index = relativePath.LastIndexOf('/');
        return index < 0
            ? (string.Empty, relativePath)
            : (relativePath[..index], relativePath[(index + 1)..]);
    }

    private static string ParentPath(string path)
    {
        var index = path.LastIndexOf('/');
        return index < 0 ? string.Empty : path[..index];
    }

    private static int Depth(string path) => path.Count(c => c == '/');

    /// <summary>
    /// Every folder that must exist for the given set, shallowest first — including the target root
    /// (the empty path), which is a map entry like any other.
    /// </summary>
    private static IEnumerable<string> ExpandWithAncestors(IEnumerable<string> paths)
    {
        var all = new HashSet<string>(StringComparer.Ordinal) { string.Empty };

        foreach (var path in paths)
        {
            var current = path;
            while (!string.IsNullOrEmpty(current))
            {
                all.Add(current);
                current = ParentPath(current);
            }
        }

        return all.OrderBy(Depth).ThenBy(p => p, StringComparer.Ordinal);
    }

    /// <summary>
    /// Escapes a value for a Drive query string. Backslash first, then the quote — reversing that order
    /// would double-escape the backslashes this method just added.
    /// </summary>
    private static string EscapeQueryValue(string value) =>
        value.Replace("\\", "\\\\").Replace("'", "\\'");

    public void Dispose()
    {
        if (_drive.IsValueCreated)
            _drive.Value.Dispose();

        _folderGate.Dispose();
    }
}
