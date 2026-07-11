<#
.SYNOPSIS
    Generates a one-time TOTP secret for SwagBagger 2FA enrollment.
    Run this locally, once. Never expose this via the web app itself.
#>

# Generate 20 cryptographically random bytes (matches Otp.NET's KeyGeneration.GenerateRandomKey(20))
$randomBytes = New-Object byte[] 20
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$rng.GetBytes($randomBytes)

# Base32 encode the bytes (RFC 4648, no padding) - authenticator apps expect Base32, not Base64
function ConvertTo-Base32 {
    param([byte[]]$Bytes)

    $alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"
    $bits = ""
    foreach ($b in $Bytes) {
        $bits += [Convert]::ToString($b, 2).PadLeft(8, '0')
    }

    $output = ""
    for ($i = 0; $i -lt $bits.Length; $i += 5) {
        $chunk = $bits.Substring($i, [Math]::Min(5, $bits.Length - $i)).PadRight(5, '0')
        $output += $alphabet[[Convert]::ToInt32($chunk, 2)]
    }

    return $output
}

$secret = ConvertTo-Base32 -Bytes $randomBytes
$issuer = "SwagBagger"
$accountName = "tom"
$provisioningUri = "otpauth://totp/${issuer}:${accountName}?secret=$secret&issuer=$issuer&digits=6&period=30"

Write-Host ""
Write-Host "Secret (store this in config, e.g. user secrets or an env var):" -ForegroundColor Cyan
Write-Host $secret -ForegroundColor Yellow
Write-Host ""
Write-Host "Manual entry key for your authenticator app is the secret above."
Write-Host "If you'd rather scan a QR code, paste this URI into an OFFLINE QR generator you trust:" -ForegroundColor Cyan
Write-Host $provisioningUri
Write-Host ""
Write-Host "This script does not save or transmit the secret anywhere. Copy it now." -ForegroundColor Red