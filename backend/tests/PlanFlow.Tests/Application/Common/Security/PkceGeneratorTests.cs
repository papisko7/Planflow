using System.Security.Cryptography;
using System.Text;
using PlanFlow.Application.Common.Security;
using Xunit;

namespace PlanFlow.Tests.Application.Common.Security;

public class PkceGeneratorTests
{
    [Fact]
    public void GenerateCodeVerifier_ReturnsAUrlSafeHighEntropyValue()
    {
        var verifier = PkceGenerator.GenerateCodeVerifier();

        Assert.True(verifier.Length >= 43, "RFC 7636 requires a code_verifier of at least 43 characters.");
        Assert.DoesNotContain('+', verifier);
        Assert.DoesNotContain('/', verifier);
        Assert.DoesNotContain('=', verifier);
    }

    [Fact]
    public void GenerateCodeVerifier_IsDifferentEveryCall()
    {
        var first = PkceGenerator.GenerateCodeVerifier();
        var second = PkceGenerator.GenerateCodeVerifier();

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void DeriveCodeChallenge_MatchesTheS256MethodDefinedByRfc7636()
    {
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";

        var challenge = PkceGenerator.DeriveCodeChallenge(verifier);

        var expectedHash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        var expectedChallenge = Convert.ToBase64String(expectedHash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(expectedChallenge, challenge);
    }

    [Fact]
    public void DeriveCodeChallenge_IsDeterministicForTheSameVerifier()
    {
        var verifier = PkceGenerator.GenerateCodeVerifier();

        var first = PkceGenerator.DeriveCodeChallenge(verifier);
        var second = PkceGenerator.DeriveCodeChallenge(verifier);

        Assert.Equal(first, second);
    }

    [Fact]
    public void GenerateState_IsDifferentEveryCall()
    {
        var first = PkceGenerator.GenerateState();
        var second = PkceGenerator.GenerateState();

        Assert.NotEqual(first, second);
    }
}
