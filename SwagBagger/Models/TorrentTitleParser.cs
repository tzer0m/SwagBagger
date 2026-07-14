using System.Text.RegularExpressions;

namespace SwagBagger.Services
{
    /// <summary>
    /// Extracts a best-effort title, year, and season number from a torrent's release name, following common scene-release naming conventions.
    /// </summary>
    public partial class TorrentTitleParser
    {
        /// <summary>
        /// Matches a season marker such as "S01E02" or "Season 1", capturing the season number.
        /// </summary>
        [GeneratedRegex(@"\bS(?<season>\d{1,2})E\d{1,3}\b|\bSeason[.\s](?<season>\d{1,2})\b", RegexOptions.IgnoreCase)]
        private static partial Regex SeasonPattern();

        /// <summary>
        /// Matches a four-digit release year between 1900 and 2099.
        /// </summary>
        [GeneratedRegex(@"\b(19\d{2}|20\d{2})\b")]
        private static partial Regex YearPattern();

        /// <summary>
        /// Determines whether the given title looks like a TV release and, if so, extracts the season number.
        /// </summary>
        /// <param name="title">The torrent's release title.</param>
        /// <param name="season">The extracted season number, or 1 if a season marker was found but no number could be parsed.</param>
        /// <returns>True if a season marker was found in the title; otherwise false.</returns>
        public bool TryParseSeason(string title, out int season)
        {
            Match match = SeasonPattern().Match(title);
            if (match.Success && int.TryParse(match.Groups["season"].Value, out season))
            {
                return true;
            }
            season = 1;
            return match.Success;
        }

        /// <summary>
        /// Extracts a release year from the given title, if present.
        /// </summary>
        /// <param name="title">The torrent's release title.</param>
        /// <param name="year">The extracted year, or 0 if none was found.</param>
        /// <returns>True if a year was found; otherwise false.</returns>
        public bool TryParseYear(string title, out int year)
        {
            Match match = YearPattern().Match(title);
            if (match.Success && int.TryParse(match.Value, out year))
            {
                return true;
            }
            year = 0;
            return false;
        }

        /// <summary>
        /// Extracts a cleaned-up display name from a release title by cutting off at the first season marker or year, and replacing separator characters with spaces.
        /// </summary>
        /// <param name="title">The torrent's release title.</param>
        /// <returns>A best-effort clean title, or the original title if no cut point could be found.</returns>
        public string ParseName(string title)
        {
            Match seasonMatch = SeasonPattern().Match(title);
            Match yearMatch = YearPattern().Match(title);

            int cutIndex = title.Length;
            if (seasonMatch.Success)
            {
                cutIndex = Math.Min(cutIndex, seasonMatch.Index);
            }
            if (yearMatch.Success)
            {
                cutIndex = Math.Min(cutIndex, yearMatch.Index);
            }

            string cleaned = title[..cutIndex].Replace('.', ' ').Replace('_', ' ').Trim(' ', '-');
            return cleaned.Length > 0 ? cleaned : title;
        }
    }
}