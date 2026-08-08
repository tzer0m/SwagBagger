namespace SwagBagger.Services
{
    /// <summary>
    /// Triggers Jellyfin library scans via Jellyfin's HTTP API after new content has been moved into the library.
    /// </summary>
    /// <remarks>
    /// Creates a new client using the given HTTP client factory and configuration.
    /// </remarks>
    /// <param name="httpClientFactory">Factory used to create the underlying HTTP client.</param>
    /// <param name="configuration">Application configuration, used to read Jellyfin connection settings.</param>
    public class JellyfinClient(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        /// <summary>
        /// Http client used to communicate with the Jellyfin server API.
        /// </summary>
        private readonly HttpClient HttpClient = httpClientFactory.CreateClient();

        /// <summary>
        /// Triggers a scan of the Movies library.
        /// </summary>
        /// <returns>Task result.</returns>
        public Task RefreshMoviesAsync()
        {
            string libraryId = configuration["Jellyfin:MovieLibraryId"] ?? throw new InvalidOperationException("Jellyfin:MovieLibraryId is not configured.");
            return RefreshLibraryAsync(libraryId);
        }

        /// <summary>
        /// Triggers a scan of the TV library.
        /// </summary>
        /// <returns>Task result.</returns>
        public Task RefreshTvAsync()
        {
            string libraryId = configuration["Jellyfin:TvLibraryId"] ?? throw new InvalidOperationException("Jellyfin:TvLibraryId is not configured.");
            return RefreshLibraryAsync(libraryId);
        }

        /// <summary>
        /// Sends a refresh request for the given Jellyfin library item id.
        /// </summary>
        /// <param name="libraryId">The Jellyfin library item id to refresh.</param>
        /// <returns>Task result.</returns>
        private async Task RefreshLibraryAsync(string libraryId)
        {
            // Read Jellyfin connection settings from configuration
            string baseUrl = configuration["Jellyfin:BaseUrl"] ?? throw new InvalidOperationException("Jellyfin:BaseUrl is not configured.");
            string apiKey = configuration["Jellyfin:ApiKey"] ?? throw new InvalidOperationException("Jellyfin:ApiKey is not configured.");

            // Send the refresh request
            using HttpRequestMessage request = new(HttpMethod.Post, $"{baseUrl}/Items/{libraryId}/Refresh?Recursive=true&ImageRefreshMode=Default&MetadataRefreshMode=Default&ReplaceAllMetadata=false&ReplaceAllImages=false");
            request.Headers.Add("X-Emby-Token", apiKey);
            HttpResponseMessage response = await HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }
    }
}