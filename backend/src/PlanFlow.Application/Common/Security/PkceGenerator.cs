using System.Security.Cryptography;
using System.Text;

namespace PlanFlow.Application.Common.Security;

/// <summary>
/// PKCE (RFC 7636) code_verifier/code_challenge pair, S256 method. Prevents an intercepted
/// authorization code from being redeemed by anyone but the party that started the flow —
/// required for the Google OAuth2 connect flow (Phase 4A) since a mobile app cannot keep a
/// client secret confidential the way a server-side web app can.
/// </summary>
public static class PkceGenerator
{
    public static string GenerateCodeVerifier() =>
        Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static string DeriveCodeChallenge(string codeVerifier) =>
        Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));

    public static string GenerateState() =>
        Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
