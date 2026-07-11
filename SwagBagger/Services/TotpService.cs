using OtpNet;

namespace SwagBagger.Services
{
    /// <summary>
    /// Handles generation and validation of Time-based One-Time Passwords (TOTP) for 2FA login.
    /// </summary>
    public class TotpService
    {
        /// <summary>
        /// Generates a random 20-byte secret key, encoded as Base32 so it can be entered into authenticator apps.
        /// </summary>
        /// <returns>A Base32-encoded secret string.</returns>
        public string GenerateSecret()
        {
            byte[] secretKey = KeyGeneration.GenerateRandomKey(20);
            return Base32Encoding.ToString(secretKey);
        }

        /// <summary>
        /// Builds the otpauth:// URI used to generate a QR code for enrolling in an authenticator app.
        /// </summary>
        /// <param name="secret">The Base32-encoded secret.</param>
        /// <param name="accountName">The account name shown in the authenticator app (e.g. username or email).</param>
        /// <param name="issuer">The issuer name shown in the authenticator app (e.g. "SwagBagger").</param>
        /// <returns>An otpauth:// provisioning URI.</returns>
        public string GetProvisioningUri(string secret, string accountName, string issuer)
        {
            return string.Format("otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6&period=30", Uri.EscapeDataString(issuer), Uri.EscapeDataString(accountName), secret);
        }

        /// <summary>
        /// Validates a submitted 6-digit code against the stored secret, allowing +/- 60 seconds of clock drift.
        /// </summary>
        /// <param name="secret">The Base32-encoded secret to validate against.</param>
        /// <param name="code">The 6-digit code entered by the user.</param>
        /// <returns>True if the code is valid within the verification window.</returns>
        public bool ValidateCode(string secret, string code)
        {
            byte[] secretBytes = Base32Encoding.ToBytes(secret);
            Totp totp = new(secretBytes);
            return totp.VerifyTotp(code, out _, new VerificationWindow(2, 2));
        }
    }
}