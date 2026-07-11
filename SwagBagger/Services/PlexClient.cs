namespace SwagBagger.Services
{
    /// <summary>
    /// Triggers Plex library scans via Plex's HTTP API after new content has been moved into the library.
    /// </summary>
    /// <remarks>
    /// Creates a new client using the given HTTP client factory and configuration.
    /// </remarks>
    /// <param name="httpClientFactory">Factory used to create the underlying HTTP client.</param>
    /// <param name="configuration">Application configuration, used to read Plex connection settings.</param>
    public class PlexClient(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        /// <summary>
        /// Http client used to communicate with the Plex Media Server API.
        /// </summary>
        private readonly HttpClient HttpClient = httpClientFactory.CreateClient();

        /// <summary>
        /// Triggers a scan of the Movies library section.
        /// </summary>
        public Task RefreshMoviesAsync()
        {
            string sectionKey = configuration["Plex:MovieSectionKey"] ?? throw new InvalidOperationException("Plex:MovieSectionKey is not configured.");
            return RefreshSectionAsync(sectionKey);
        }

        /// <summary>
        /// Triggers a scan of the TV library section.
        /// </summary>
        public Task RefreshTvAsync()
        {
            string sectionKey = configuration["Plex:TvSectionKey"] ?? throw new InvalidOperationException("Plex:TvSectionKey is not configured.");
            return RefreshSectionAsync(sectionKey);
        }

        /// <summary>
        /// Sends a refresh request for the given Plex library section key.
        /// </summary>
        /// <param name="sectionKey">The Plex library section key to refresh.</param>
        private async Task RefreshSectionAsync(string sectionKey)
        {
            // Read Plex connection settings from configuration
            string baseUrl = configuration["Plex:BaseUrl"] ?? throw new InvalidOperationException("Plex:BaseUrl is not configured.");
            string token = configuration["Plex:Token"] ?? throw new InvalidOperationException("Plex:Token is not configured.");

            // Send the refresh request - Plex triggers the scan asynchronously and returns immediately
            HttpResponseMessage response = await HttpClient.GetAsync($"{baseUrl}/library/sections/{sectionKey}/refresh?X-Plex-Token={token}");
            response.EnsureSuccessStatusCode();
        }
    }
}