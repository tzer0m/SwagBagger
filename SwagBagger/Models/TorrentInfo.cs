using System.Text.Json.Serialization;

namespace SwagBagger.Models
{
    /// <summary>
    /// Represents a single torrent's status as returned by qBittorrent's /api/v2/torrents/info endpoint.
    /// </summary>
    public record TorrentInfo
    {
        /// <summary>
        /// The torrent's unique info hash, used to identify it in subsequent API calls.
        /// </summary>
        [JsonPropertyName("hash")]
        public string Hash { get; init; } = string.Empty;

        /// <summary>
        /// The display name of the torrent.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// Download progress as a fraction between 0 and 1.
        /// </summary>
        [JsonPropertyName("progress")]
        public double Progress { get; init; }

        /// <summary>
        /// The current qBittorrent state string, e.g. "downloading", "uploading", "stalledUP", "error".
        /// </summary>
        [JsonPropertyName("state")]
        public string State { get; init; } = string.Empty;

        /// <summary>
        /// The absolute path where the torrent's files are being saved.
        /// </summary>
        [JsonPropertyName("save_path")]
        public string SavePath { get; init; } = string.Empty;

        /// <summary>
        /// Total size of the torrent's content, in bytes.
        /// </summary>
        [JsonPropertyName("size")]
        public long Size { get; init; }

        /// <summary>
        /// Number of seeds currently connected for this torrent.
        /// </summary>
        [JsonPropertyName("num_seeds")]
        public int NumSeeds { get; init; }

        /// <summary>
        /// Current download speed, in bytes per second.
        /// </summary>
        [JsonPropertyName("dlspeed")]
        public long DownloadSpeed { get; init; }
    }
}