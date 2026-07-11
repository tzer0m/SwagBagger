namespace SwagBagger.Models
{
    /// <summary>
    /// Represents the response returned by Cloudflare's Turnstile siteverify endpoint.
    /// </summary>
    public record TurnstileVerifyResult
    {
        /// <summary>
        /// Indicates whether the Turnstile challenge was passed successfully.
        /// </summary>
        public bool Success { get; init; }
    }
}