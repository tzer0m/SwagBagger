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
    /// <param name="logger">Logger for this service.</param>
    /// <param name="configuration">Configuration for the service.</param>
    public class TorrentMonitorService(QBittorrentClient qBittorrentClient, TingClient tingClient, ILogger<TorrentMonitorService> logger, IConfiguration configuration) : BackgroundService
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
        /// Tracks hashes of torrents currently being moved to their destination, so the UI can show a "Moving" status.
        /// </summary>
        private readonly HashSet<string> MovingHashes = [];

        /// <summary>
        /// The most recent snapshot of torrent status, updated on every poll. UI components can read this directly.
        /// </summary>
        public List<TorrentInfo> CurrentTorrents { get; private set; } = [];

        /// <summary>
        /// Raised whenever torrent status changes, so subscribed UI components can refresh.
        /// </summary>
        public event Action? StatusChanged;

        /// <summary>
        /// Registers the intended Plex destination folder for a torrent, keyed by its info hash, so it can be moved there once complete.
        /// </summary>
        /// <param name="hash">The torrent's info hash, as returned by qBittorrent when the magnet was added.</param>
        /// <param name="destinationPath">The pre-built Plex destination folder from <see cref="MediaPathBuilder"/>.</param>
        public void RegisterDestination(string hash, string destinationPath)
        {
            DestinationsByHash[hash] = destinationPath;
        }

        /// <summary>
        /// Returns whether the given torrent is currently being moved to its destination.
        /// </summary>
        /// <param name="hash">The torrent's info hash.</param>
        public bool IsMoving(string hash) => MovingHashes.Contains(hash);

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
                bool isComplete = torrent.Progress >= 1.0;
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
            try
            {
                // Remove the torrent from qBittorrent immediately to stop seeding, without touching the downloaded files
                await qBittorrentClient.DeleteTorrentAsync(torrent.Hash);

                // Check if a destination has been registered for this torrent
                if (!DestinationsByHash.TryGetValue(torrent.Hash, out string? destinationFolder))
                {
                    logger.LogWarning("Completed torrent {Name} has no registered destination, leaving files in place.", torrent.Name);
                    return;
                }

                // Mark the torrent as moving so the UI can reflect it immediately, rather than waiting for the next poll
                string containerDownloadsPath = configuration["QBittorrent:ContainerDownloadsPath"] ?? "/downloads";
                string hostDownloadsPath = configuration["QBittorrent:HostDownloadsPath"] ?? throw new InvalidOperationException("QBittorrent:HostDownloadsPath is not configured.");
                string translatedSavePath = torrent.SavePath.Replace(containerDownloadsPath, hostDownloadsPath).Replace('/', '\\');
                string sourcePath = Path.Combine(translatedSavePath, torrent.Name);
                string targetPath = Path.Combine(destinationFolder, torrent.Name);
                MovingHashes.Add(torrent.Hash);
                StatusChanged?.Invoke();

                // Copy the files to the registered Plex destination, then delete the local source once the copy has succeeded
                if (Directory.Exists(sourcePath))
                {
                    CopyDirectory(sourcePath, targetPath);
                    Directory.Delete(sourcePath, recursive: true);
                }
                else if (File.Exists(sourcePath))
                {
                    File.Copy(sourcePath, targetPath, overwrite: true);
                    File.Delete(sourcePath);
                }
                else
                {
                    logger.LogWarning("Completed torrent {Name} not found at expected path {SourcePath}.", torrent.Name, sourcePath);
                    return;
                }
                logger.LogInformation("Moved completed torrent {Name} to {TargetPath}.", torrent.Name, targetPath);
                await tingClient.SendAsync("Download complete", $"{torrent.Name} has finished downloading and been moved.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to move completed torrent {Name}.", torrent.Name);
                await tingClient.SendAsync("Download move failed", $"{torrent.Name} finished but could not be moved: {ex.Message}");
            }
            finally
            {
                MovingHashes.Remove(torrent.Hash);
                StatusChanged?.Invoke();
            }
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