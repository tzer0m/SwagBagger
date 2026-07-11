using System.Text.RegularExpressions;

namespace SwagBagger.Services
{
    /// <summary>
    /// Extracts information from magnet links, such as the torrent's info hash.
    /// </summary>
    public partial class MagnetLinkParser
    {
        /// <summary>
        /// Extracts the info hash from a magnet link's "xt=urn:btih:" parameter.
        /// </summary>
        /// <param name="magnetLink">The full magnet link.</param>
        /// <returns>The info hash, lowercased, or null if the magnet link does not contain a valid hash.</returns>
        public string? ExtractHash(string magnetLink)
        {
            // Match the hex or base32 hash following "xt=urn:btih:" in the magnet link
            Match match = HashRegex().Match(magnetLink);

            return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
        }

        /// <summary>
        /// Matches the info hash portion of a magnet link's "xt=urn:btih:" parameter.
        /// </summary>
        [GeneratedRegex(@"xt=urn:btih:([a-zA-Z0-9]+)")]
        private static partial Regex HashRegex();
    }
}