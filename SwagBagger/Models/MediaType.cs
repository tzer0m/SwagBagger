namespace SwagBagger.Models
{
    /// <summary>
    /// Identifies whether a submitted torrent is a movie or a TV show, determining which destination path format is used.
    /// </summary>
    public enum MediaType
    {
        /// <summary>
        /// A movie, stored under Movies/{Name} ({Year}).
        /// </summary>
        Movie,

        /// <summary>
        /// A TV show season, stored under TV/{ShowName}/Season {SeasonNumber:D2}.
        /// </summary>
        Tv
    }
}