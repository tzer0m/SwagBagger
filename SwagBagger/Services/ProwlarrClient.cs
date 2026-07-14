using SwagBagger.Models;
using System.Text.Json;

namespace SwagBagger.Services
{
    /// <summary>
    /// Wraps Prowlarr's search API for finding torrents across configured indexers.
    /// </summary>
    /// <remarks>
    /// Creates a new client using the given HTTP client factory and configuration.
    /// </remarks>
    /// <param name="httpClientFactory">Factory used to create the underlying HTTP client.</param>
    /// <param name="configuration">Application configuration, used to read Prowlarr connection settings.</param>
    public class ProwlarrClient(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        /// <summary>
        /// Http client used to communicate with the Prowlarr API.
        /// </summary>
        private readonly HttpClient HttpClient = httpClientFactory.CreateClient();

        /// <summary>
        /// Options used when deserializing Prowlarr API responses.
        /// </summary>
        private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };

        /// <summary>
        /// Searches all configured Prowlarr indexers for the given term and returns the combined results, ordered by seeders descending and capped at 100.
        /// </summary>
        /// <param name="query">The search term to look up.</param>
        /// <returns>A list of torrent search results ordered by seeders descending, containing at most 100 entries.</returns>
        public async Task<List<TorrentSearchResult>> SearchAsync(string query)
        {
            // Read Prowlarr connection settings from configuration
            string baseUrl = configuration["Prowlarr:BaseUrl"] ?? throw new InvalidOperationException("Prowlarr:BaseUrl is not configured.");
            string apiKey = configuration["Prowlarr:ApiKey"] ?? throw new InvalidOperationException("Prowlarr:ApiKey is not configured.");

            // Build and send the search request against Prowlarr's search endpoint
            string encodedQuery = Uri.EscapeDataString(query);
            using HttpRequestMessage request = new(HttpMethod.Get, $"{baseUrl}/api/v1/search?query={encodedQuery}&type=search");
            request.Headers.Add("X-Api-Key", apiKey);
            HttpResponseMessage response = await HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            // Deserialize the response and order results by seeders descending, so the best candidates appear first, capped at 100 entries
            string json = await response.Content.ReadAsStringAsync();
            List<TorrentSearchResult>? results = JsonSerializer.Deserialize<List<TorrentSearchResult>>(json, SerializerOptions);
            return results?.OrderByDescending(result => result.Seeders).Take(100).ToList() ?? [];
        }
    }
}