using Microsoft.AspNetCore.DataProtection;
using PlanFlow.Infrastructure.Security;
using Xunit;

namespace PlanFlow.Tests.Infrastructure.Security;

public class DataProtectionTokenEncryptionServiceTests
{
    private static DataProtectionTokenEncryptionService CreateService(IDataProtectionProvider? provider = null) =>
        new(provider ?? new EphemeralDataProtectionProvider());

    [Fact]
    public void Encrypt_ThenDecrypt_RoundTripsToOriginalPlaintext()
    {
        var provider = new EphemeralDataProtectionProvider();
        var service = CreateService(provider);
        const string plaintext = "ya29.a0Af-fake-google-access-token";

        var ciphertext = service.Encrypt(plaintext);
        var decrypted = service.Decrypt(ciphertext);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_NeverStoresPlaintextInTheCiphertext()
    {
        var service = CreateService();
        const string plaintext = "1//fake-refresh-token-secret";

        var ciphertext = service.Encrypt(plaintext);

        Assert.DoesNotContain(plaintext, ciphertext);
    }

    [Fact]
    public void Decrypt_TamperedCiphertext_ThrowsCryptographicException()
    {
        var service = CreateService();
        var ciphertext = service.Encrypt("some-token");
        var tampered = ciphertext[..^4] + "abcd";

        Assert.ThrowsAny<Exception>(() => service.Decrypt(tampered));
    }

    [Fact]
    public void Decrypt_CiphertextFromADifferentProtectorPurpose_ThrowsCryptographicException()
    {
        var provider = new EphemeralDataProtectionProvider();
        var otherPurposeCiphertext = provider.CreateProtector("SomeOtherFeature.v1").Protect("token");
        var service = CreateService(provider);

        Assert.ThrowsAny<Exception>(() => service.Decrypt(otherPurposeCiphertext));
    }
}
