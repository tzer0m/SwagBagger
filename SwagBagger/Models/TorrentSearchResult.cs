namespace SwagBagger.Models
{
    /// <summary>
    /// Represents a single torrent result returned by a Prowlarr search.
    /// </summary>
    public class TorrentSearchResult
    {
        /// <summary>
        /// The title of the torrent as reported by the indexer.
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// The size of the torrent's content, in bytes.
        /// </summary>
        public long Size { get; set; }

        /// <summary>
        /// The number of seeders currently reported for this torrent.
        /// </summary>
        public int Seeders { get; set; }

        /// <summary>
        /// The number of leechers currently reported for this torrent.
        /// </summary>
        public int Leechers { get; set; }

        /// <summary>
        /// The name of the indexer that returned this result.
        /// </summary>
        public string Indexer { get; set; } = string.Empty;

        /// <summary>
        /// The magnet link used to add this torrent, if available.
        /// </summary>
        public string? MagnetUrl { get; set; }

        /// <summary>
        /// The direct download link used to fetch this torrent's .torrent file, if available.
        /// </summary>
        public string? DownloadUrl { get; set; }
    }
}