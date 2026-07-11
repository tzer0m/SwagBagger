using System.Text.RegularExpressions;

namespace SwagBagger.Services
{
    /// <summary>
    /// Builds Plex-compliant destination folder paths for movies and TV shows from user-supplied form input.
    /// </summary>
    public partial class MediaPathBuilder(IConfiguration configuration)
    {
        /// <summary>
        /// Builds the destination folder for a movie, in the form "{root}/Movies/{Name} ({Year})".
        /// </summary>
        /// <param name="name">The movie's title, as entered on the form.</param>
        /// <param name="year">The movie's release year.</param>
        /// <returns>The full destination folder path.</returns>
        public string BuildMoviePath(string name, int year)
        {
            string root = configuration["Storage:DestinationPath"] ?? throw new InvalidOperationException("Storage:DestinationPath is not configured.");
            string sanitizedName = SanitizeForFileSystem(name);
            return $"{root}/Movies/{sanitizedName} ({year})";
        }

        /// <summary>
        /// Builds the destination folder for a TV show season, in the form "{root}/TV/{ShowName}/Season {SeasonNumber:D2}".
        /// </summary>
        /// <param name="showName">The show's title, as entered on the form.</param>
        /// <param name="seasonNumber">The season number, always rendered as two digits.</param>
        /// <returns>The full destination folder path.</returns>
        public string BuildTvPath(string showName, int seasonNumber)
        {
            string root = configuration["Storage:DestinationPath"] ?? throw new InvalidOperationException("Storage:DestinationPath is not configured.");
            string sanitizedShowName = SanitizeForFileSystem(showName);
            return $"{root}/TV/{sanitizedShowName}/Season {seasonNumber:D2}";
        }

        /// <summary>
        /// Strips characters that are illegal in Windows and Linux file names, and trims surrounding whitespace, so the result is safe to use as a folder name on any platform.
        /// </summary>
        /// <param name="input">The raw, user-supplied string.</param>
        /// <returns>A sanitized string safe for use as a folder name.</returns>
        private static string SanitizeForFileSystem(string input)
        {
            string cleaned = IllegalCharactersRegex().Replace(input, string.Empty);
            return cleaned.Trim();
        }

        /// <summary>
        /// Matches characters that are illegal in Windows and/or Linux file names.
        /// </summary>
        [GeneratedRegex("[<>:\"/\\\\|?*]")]
        private static partial Regex IllegalCharactersRegex();
    }
}