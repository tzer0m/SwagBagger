using SwagBagger.Models;
using System.Text.Json;

namespace SwagBagger.Services
{
    /// <summary>
    /// Wraps qBittorrent's WebUI API for authenticating, adding magnet links, and polling torrent status.
    /// </summary>
    /// <remarks>
    /// Creates a new client using the given HTTP client factory and configuration.
    /// </remarks>
    /// <param name="httpClientFactory">Factory used to create the underlying HTTP client.</param>
    /// <param name="configuration">Application configuration, used to read qBittorrent connection settings.</param>
    public class QBittorrentClient(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        /// <summary>
        /// Http client used to communicate with the qBittorrent WebUI API.
        /// </summary>
        private readonly HttpClient HttpClient = httpClientFactory.CreateClient();

        /// <summary>
        /// Session cookie used for authenticated requests to the qBittorrent WebUI API.
        /// </summary>
        private string? SessionCookie;

        /// <summary>
        /// Authenticates against qBittorrent's WebUI API and stores the resulting session cookie for subsequent requests.
        /// </summary>
        public async Task LoginAsync()
        {
            // Read qBittorrent connection settings from configuration
            string baseUrl = configuration["QBittorrent:BaseUrl"] ?? throw new InvalidOperationException("QBittorrent:BaseUrl is not configured.");
            string username = configuration["QBittorrent:Username"] ?? throw new InvalidOperationException("QBittorrent:Username is not configured.");
            string password = configuration["QBittorrent:Password"] ?? throw new InvalidOperationException("QBittorrent:Password is not configured.");

            // Create the login request content
            Dictionary<string, string> loginPayload = new() { ["username"] = username, ["password"] = password };
            HttpResponseMessage response = await HttpClient.PostAsync($"{baseUrl}/api/v2/auth/login", new FormUrlEncodedContent(loginPayload));
            response.EnsureSuccessStatusCode();

            // Read the session cookie from the response headers
            if (response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies))
            {
                SessionCookie = cookies.FirstOrDefault(cookie => cookie.StartsWith("QBT_SID"))?.Split(';')[0];
            }

            // Throw an exception if the session cookie was not found
            if (SessionCookie is null)
            {
                throw new InvalidOperationException("qBittorrent login did not return a session cookie. Check the configured credentials.");
            }
        }

        /// <summary>
        /// Adds a magnet link to qBittorrent for download, tagged with the given category.
        /// </summary>
        /// <param name="magnetLink">The magnet link to add.</param>
        public async Task AddMagnetAsync(string magnetLink)
        {
            // Check if the session cookie is set, then read qBittorrent connection settings from configuration
            EnsureLoggedIn();
            string baseUrl = configuration["QBittorrent:BaseUrl"] ?? throw new InvalidOperationException("QBittorrent:BaseUrl is not configured.");

            // Create the add torrent request content
            Dictionary<string, string> payload = new() { ["urls"] = magnetLink };
            using HttpRequestMessage request = new(HttpMethod.Post, $"{baseUrl}/api/v2/torrents/add") { Content = new FormUrlEncodedContent(payload) };
            request.Headers.Add("Cookie", SessionCookie);

            // Send the request and ensure it was successful
            HttpResponseMessage response = await HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }

        /// <summary>
        /// Retrieves the current list of torrents and their status from qBittorrent.
        /// </summary>
        /// <returns>The raw JSON array of torrent info returned by qBittorrent.</returns>
        public async Task<List<TorrentInfo>?> GetTorrentsAsync()
        {
            // Check if the session cookie is set, then read qBittorrent connection settings from configuration
            EnsureLoggedIn();
            string baseUrl = configuration["QBittorrent:BaseUrl"] ?? throw new InvalidOperationException("QBittorrent:BaseUrl is not configured.");

            // Create the request to get the list of torrents
            using HttpRequestMessage request = new(HttpMethod.Get, $"{baseUrl}/api/v2/torrents/info");
            request.Headers.Add("Cookie", SessionCookie);

            // Send the request and ensure it was successful
            HttpResponseMessage response = await HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            // Read the response content as a JSON array
            string json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<TorrentInfo>>(json);
        }

        /// <summary>
        /// Removes a torrent from qBittorrent's tracking (stopping seeding), without deleting its downloaded files, so they can be moved separately afterward.
        /// </summary>
        /// <param name="hash">The torrent's info hash.</param>
        public async Task DeleteTorrentAsync(string hash)
        {
            // Check if the session cookie is set, then read qBittorrent connection settings from configuration
            EnsureLoggedIn();
            string baseUrl = configuration["QBittorrent:BaseUrl"] ?? throw new InvalidOperationException("QBittorrent:BaseUrl is not configured.");

            // Create the delete request content - deleteFiles=false removes only the torrent entry, leaving the downloaded files in place for a separate move step
            Dictionary<string, string> payload = new() { ["hashes"] = hash, ["deleteFiles"] = "false" };
            using HttpRequestMessage request = new(HttpMethod.Post, $"{baseUrl}/api/v2/torrents/delete") { Content = new FormUrlEncodedContent(payload) };
            request.Headers.Add("Cookie", SessionCookie);

            // Send the request and ensure it was successful
            HttpResponseMessage response = await HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }

        /// <summary>
        /// Throws if <see cref="LoginAsync"/> has not been called yet, since all other calls require an active session.
        /// </summary>
        private void EnsureLoggedIn()
        {
            if (SessionCookie is null)
            {
                throw new InvalidOperationException("QBittorrentClient is not logged in. Call LoginAsync first.");
            }
        }
    }
}