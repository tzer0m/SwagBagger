using SwagBagger.Models;
using t0m.Ting;

namespace SwagBagger.Services
{
    /// <summary>
    /// Periodically polls qBittorrent for torrent status, exposing live updates for the UI and handling completed downloads by stopping seeding, moving files, and sending a Ting notification.
    /// </summary>
    /// <remarks>
    /// Creates a new monitor using the given services and configuration.
    /// </remarks>
    /// <param name="qBittorrentClient">Client used to query qBittorrent for torrent status.</param>
    /// <param name="tingClient">Client used to send completion notifications.</param>
    /// <param name="plexClient">Client used to interact with Plex.</param>
    /// <param name="logger">Logger for this service.</param>
    /// <param name="configuration">Configuration for the service.</param>
    public class TorrentMonitorService(QBittorrentClient qBittorrentClient, TingClient tingClient, PlexClient plexClient, ILogger<TorrentMonitorService> logger, IConfiguration configuration) : BackgroundService
    {
        /// <summary>
        /// Tracks hashes of torrents that have already been processed, so they are not handled more than once.
        /// </summary>
        private readonly HashSet<string> ProcessedHashes = [];

        /// <summary>
        /// Maps a torrent's hash to its pre-built Plex destination folder, registered at submission time by <see cref="RegisterDestination"/>.
        /// </summary>
        private readonly Dictionary<string, string> DestinationsByHash = [];

        /// <summary>
        /// Maps a torrent's hash to the display name entered on the submission form, so the UI can show it alongside qBittorrent's own torrent name.
        /// </summary>
        private readonly Dictionary<string, string> DisplayNamesByHash = [];

        /// <summary>
        /// Snapshot of torrents currently being moved, since qBittorrent no longer reports them once deleted. Keeps the row visible in the UI during the move.
        /// </summary>
        private readonly Dictionary<string, TorrentInfo> MovingSnapshots = [];

        /// <summary>
        /// Maps a torrent's hash to its current move progress, as a fraction between 0 and 1.
        /// </summary>
        private readonly Dictionary<string, double> MoveProgressByHash = [];

        /// <summary>
        /// The most recent snapshot of torrent status, updated on every poll. UI components can read this directly.
        /// </summary>
        public List<TorrentInfo> CurrentTorrents { get; private set; } = [];

        /// <summary>
        /// Raised whenever torrent status changes, so subscribed UI components can refresh.
        /// </summary>
        public event Action? StatusChanged;

        /// <summary>
        /// Registers the intended Plex destination folder and submitted display name for a torrent, keyed by its info hash, so it can be moved there once complete.
        /// </summary>
        /// <param name="hash">The torrent's info hash, as returned by qBittorrent when the magnet was added.</param>
        /// <param name="destinationPath">The pre-built Plex destination folder from <see cref="MediaPathBuilder"/>.</param>
        /// <param name="displayName">The name entered on the submission form.</param>
        public void RegisterDestination(string hash, string destinationPath, string displayName)
        {
            DestinationsByHash[hash] = destinationPath;
            DisplayNamesByHash[hash] = displayName;
        }

        /// <summary>
        /// Returns whether the given torrent is currently being moved to its destination.
        /// </summary>
        /// <param name="hash">The torrent's info hash.</param>
        public bool IsMoving(string hash) => MovingSnapshots.ContainsKey(hash);

        /// <summary>
        /// Returns the current move progress for the given torrent, as a fraction between 0 and 1, or 0 if not currently moving.
        /// </summary>
        /// <param name="hash">The torrent's info hash.</param>
        public double GetMoveProgress(string hash) => MoveProgressByHash.GetValueOrDefault(hash);

        /// <summary>
        /// Returns the display name entered on the submission form for the given torrent, or null if none was registered.
        /// </summary>
        /// <param name="hash">The torrent's info hash.</param>
        public string? GetDisplayName(string hash) => DisplayNamesByHash.GetValueOrDefault(hash);

        /// <summary>
        /// Returns torrents currently being moved, merged with the live qBittorrent list, so rows stay visible after being removed from qBittorrent.
        /// </summary>
        public IEnumerable<TorrentInfo> GetDisplayTorrents()
        {
            IEnumerable<string> currentHashes = CurrentTorrents.Select(t => t.Hash);
            IEnumerable<TorrentInfo> movingOnly = MovingSnapshots.Values.Where(t => !currentHashes.Contains(t.Hash));
            return CurrentTorrents.Concat(movingOnly);
        }

        /// <summary>
        /// Runs the polling loop for the lifetime of the application, checking qBittorrent every 15 seconds.
        /// </summary>
        /// <param name="stoppingToken">Signals when the host is shutting down.</param>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await qBittorrentClient.LoginAsync();
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await PollAsync();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error while polling qBittorrent.");
                }
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }

        /// <summary>
        /// Polls qBittorrent for current torrent status, updates <see cref="CurrentTorrents"/>, handles any newly completed torrents, and raises <see cref="StatusChanged"/>.
        /// </summary>
        private async Task PollAsync()
        {
            // Get the current torrent status from qBittorrent
            List<TorrentInfo>? torrents = await qBittorrentClient.GetTorrentsAsync();
            if (torrents is null)
            {
                return;
            }
            CurrentTorrents = torrents;

            // Checks for any newly completed torrents and hands them off to be processed in the background, so a large move does not stall this poll loop
            foreach (TorrentInfo torrent in torrents)
            {
                bool isComplete = torrent.State is "stalledUP" or "uploading" or "forcedUP";
                bool alreadyProcessed = ProcessedHashes.Contains(torrent.Hash);
                if (isComplete && !alreadyProcessed)
                {
                    ProcessedHashes.Add(torrent.Hash);
                    _ = HandleCompletedTorrentAsync(torrent);
                }
            }

            // Notify any subscribers that the status has changed
            StatusChanged?.Invoke();
        }

        /// <summary>
        /// Stops seeding a completed torrent immediately, then moves its files to the registered Plex destination and cleans up the source.
        /// </summary>
        /// <param name="torrent">The completed torrent to handle.</param>
        private async Task HandleCompletedTorrentAsync(TorrentInfo torrent)
        {
            // Get torrent display name
            string displayName = DisplayNamesByHash.GetValueOrDefault(torrent.Hash, torrent.Name);

            try
            {
                // Remove the torrent from qBittorrent immediately to stop seeding, without touching the downloaded files
                await qBittorrentClient.DeleteTorrentAsync(torrent.Hash);

                // Check if a destination has been registered for this torrent
                if (!DestinationsByHash.TryGetValue(torrent.Hash, out string? destinationFolder))
                {
                    logger.LogWarning("Completed torrent {Name} has no registered destination, leaving files in place.", displayName);
                    await tingClient.SendAsync("Download move failed", $"{displayName} finished but had no registered destination.");
                    return;
                }

                // Snapshot the torrent so it stays visible in the UI now that qBittorrent no longer reports it, and mark it as moving
                string containerDownloadsPath = configuration["QBittorrent:ContainerDownloadsPath"] ?? "/downloads";
                string hostDownloadsPath = configuration["QBittorrent:HostDownloadsPath"] ?? throw new InvalidOperationException("QBittorrent:HostDownloadsPath is not configured.");
                string translatedSourcePath = torrent.ContentPath.Replace(containerDownloadsPath, hostDownloadsPath);
                string targetPath = Path.Combine(destinationFolder, Path.GetFileName(translatedSourcePath));
                MovingSnapshots[torrent.Hash] = torrent;
                MoveProgressByHash[torrent.Hash] = 0;
                StatusChanged?.Invoke();

                // Copy the files to the registered Plex destination, tracking progress against the torrent's known size, then delete the local source once the copy has succeeded
                Directory.CreateDirectory(destinationFolder);
                using CancellationTokenSource progressCts = new();
                Task progressTask = TrackMoveProgressAsync(torrent.Hash, targetPath, torrent.Size, progressCts.Token);
                try
                {
                    if (Directory.Exists(translatedSourcePath))
                    {
                        CopyDirectory(translatedSourcePath, targetPath);
                        Directory.Delete(translatedSourcePath, recursive: true);
                    }
                    else if (File.Exists(translatedSourcePath))
                    {
                        File.Copy(translatedSourcePath, targetPath, overwrite: true);
                        File.Delete(translatedSourcePath);
                    }
                    else
                    {
                        logger.LogWarning("Completed torrent {Name} not found at expected path {SourcePath}.", displayName, translatedSourcePath);
                        await tingClient.SendAsync("Download move failed", $"{displayName} finished but its files could not be found at the expected path.");
                        return;
                    }
                }
                finally
                {
                    progressCts.Cancel();
                    try
                    {
                        await progressTask;
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected when the progress tracking loop is cancelled after the copy finishes
                    }
                }

                MoveProgressByHash[torrent.Hash] = 1;
                logger.LogInformation("Moved completed torrent {Name} to {TargetPath}.", displayName, targetPath);

                // Trigger a Plex library scan for whichever library the file was moved into
                if (destinationFolder.Contains("/Movies/"))
                {
                    await plexClient.RefreshMoviesAsync();
                }
                else if (destinationFolder.Contains("/TV/"))
                {
                    await plexClient.RefreshTvAsync();
                }
                await tingClient.SendAsync("Download complete", $"{displayName} has finished downloading and been moved.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to move completed torrent {Name}.", displayName);
                await tingClient.SendAsync("Download move failed", $"{displayName} finished but could not be moved: {ex.Message}");
            }
            finally
            {
                MovingSnapshots.Remove(torrent.Hash);
                MoveProgressByHash.Remove(torrent.Hash);
                StatusChanged?.Invoke();
            }
        }

        /// <summary>
        /// Periodically checks the destination path's size on disk and updates the move progress for the given torrent, until cancelled.
        /// </summary>
        /// <param name="hash">The torrent's info hash.</param>
        /// <param name="targetPath">The destination path being written to.</param>
        /// <param name="totalSize">The total expected size of the torrent's content, in bytes.</param>
        /// <param name="cancellationToken">Signals when the copy has finished and progress tracking should stop.</param>
        private async Task TrackMoveProgressAsync(string hash, string targetPath, long totalSize, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Check the size of the destination path on disk and update the move progress for the given torrent
                try
                {
                    long currentSize = GetPathSize(targetPath);
                    double progress = totalSize > 0 ? Math.Min(1.0, (double)currentSize / totalSize) : 0;
                    MoveProgressByHash[hash] = progress;
                    StatusChanged?.Invoke();
                }
                catch (IOException)
                {
                    // The target may not exist yet on the very first check, or a file may be mid-write - ignore and retry on the next tick
                }

                // Wait for a short interval before checking again, but allow cancellation to break the wait early
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Returns the total size in bytes of a file, or the combined size of all files within a directory tree.
        /// </summary>
        /// <param name="path">The file or directory path to measure.</param>
        private static long GetPathSize(string path)
        {
            if (Directory.Exists(path))
            {
                return Directory.GetFiles(path, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length);
            }
            if (File.Exists(path))
            {
                return new FileInfo(path).Length;
            }
            return 0;
        }

        /// <summary>
        /// Recursively copies a directory and all of its contents to a new location.
        /// </summary>
        /// <param name="sourceDir">The source directory to copy from.</param>
        /// <param name="targetDir">The destination directory to copy into.</param>
        private static void CopyDirectory(string sourceDir, string targetDir)
        {
            // Ensure the target directory exists before copying into it
            Directory.CreateDirectory(targetDir);

            // Copy every file in the current directory
            foreach (string filePath in Directory.GetFiles(sourceDir))
            {
                string targetFilePath = Path.Combine(targetDir, Path.GetFileName(filePath));
                File.Copy(filePath, targetFilePath, overwrite: true);
            }

            // Recurse into each subdirectory
            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                string targetSubDir = Path.Combine(targetDir, Path.GetFileName(subDir));
                CopyDirectory(subDir, targetSubDir);
            }
        }
    }
}