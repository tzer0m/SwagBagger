using SwagBagger.Models;
using System.Net;
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
            int[] categories = configuration.GetSection("Prowlarr:Categories").Get<int[]>() ?? throw new InvalidOperationException("Prowlarr:Categories is not configured.");

            // Build the category filter from the configured category codes, restricting results to the allowed categories only
            string categoryParams = string.Join("&", categories.Select(category => $"categories={category}"));

            // Build and send the search request against Prowlarr's search endpoint
            string encodedQuery = Uri.EscapeDataString(query);
            using HttpRequestMessage request = new(HttpMethod.Get, $"{baseUrl}/api/v1/search?query={encodedQuery}&type=search&{categoryParams}");
            request.Headers.Add("X-Api-Key", apiKey);
            HttpResponseMessage response = await HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            // Deserialize the response and order results by seeders descending, so the best candidates appear first, capped at 100 entries
            string json = await response.Content.ReadAsStringAsync();
            List<TorrentSearchResult>? results = JsonSerializer.Deserialize<List<TorrentSearchResult>>(json, SerializerOptions);
            return results?.OrderByDescending(result => result.Seeders).Take(100).ToList() ?? [];
        }

        /// <summary>
        /// Resolves a Prowlarr-proxied download link to a real magnet URI, if the link redirects to one.
        /// </summary>
        /// <param name="prowlarrDownloadUrl">The download URL returned by Prowlarr for a search result.</param>
        /// <returns>The resolved magnet URI if the link redirects to one; otherwise null.</returns>
        public async Task<string?> ResolveMagnetAsync(string prowlarrDownloadUrl)
        {
            // Create a new HTTP client that does not follow redirects.
            HttpClient noRedirectClient = httpClientFactory.CreateClient("NoRedirect");
            using HttpRequestMessage request = new(HttpMethod.Get, prowlarrDownloadUrl);
            using HttpResponseMessage response = await noRedirectClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            // If the response is a redirect, check if the Location header points to a magnet URI and return it; otherwise return null.
            if (response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found or HttpStatusCode.MovedPermanently)
            {
                string? location = response.Headers.Location?.ToString();
                return location is not null && location.StartsWith("magnet:") ? location : null;
            }
            return null;
        }
    }
}