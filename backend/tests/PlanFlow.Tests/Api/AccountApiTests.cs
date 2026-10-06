using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using PlanFlow.Application.Common.Interfaces;
using PlanFlow.Domain.Enums;
using PlanFlow.Infrastructure.Persistence;
using PlanFlow.Tests.Integration.PostgresIntegration;

namespace PlanFlow.Tests.Api;

/// <summary>
/// Through the real HTTP pipeline: sign up, confirm the e-mail with the mailed code, then change the password while signed in.
/// Kept to four calls to the strict auth endpoints because they share a 5-per-minute limit for the whole test host.
/// </summary>
[Collection(PostgresCollection.Name)]
public class AccountApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AccountApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private record AuthResult(string AccessToken, string RefreshToken);

    private record Profile(string Email, bool EmailVerified);

    [Fact]
    public async Task SignUp_ConfirmEmail_ChangePassword()
    {
        var email = $"{Guid.NewGuid():N}@example.com";
        var client = _factory.CreateClient();

        var register = await client.PostAsJsonAsync("api/auth/register", new { email, password = "first-password-1", displayName = "Ann" });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        var auth = (await register.Content.ReadFromJsonAsync<AuthResult>(new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        Assert.False((await client.GetFromJsonAsync<Profile>("api/users/me", new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)))!.EmailVerified);

        string code;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlanFlowDbContext>();
            var welcome = db.EmailOutbox.Single(m => m.ToEmail == email && m.Event == NotificationEvent.Welcome);
            code = Regex.Match(welcome.TextBody, @"\b\d{6}\b").Value;
        }

        var verify = await client.PostAsJsonAsync("api/users/me/verify-email", new { code });
        Assert.Equal(HttpStatusCode.NoContent, verify.StatusCode);
        Assert.True((await client.GetFromJsonAsync<Profile>("api/users/me", new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)))!.EmailVerified);

        // A wrong current password is a 400 (not 401), so the app does not mistake it for an expired session.
        var wrong = await client.PostAsJsonAsync("api/users/me/change-password",
            new { currentPassword = "nope-nope-1", newPassword = "second-password-2", refreshToken = auth.RefreshToken });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        var changed = await client.PostAsJsonAsync("api/users/me/change-password",
            new { currentPassword = "first-password-1", newPassword = "second-password-2", refreshToken = auth.RefreshToken });
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

        using var check = _factory.Services.CreateScope();
        var checkDb = check.ServiceProvider.GetRequiredService<PlanFlowDbContext>();
        var hasher = check.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var user = checkDb.Users.Single(u => u.Email == email);
        Assert.True(hasher.Verify(user.PasswordHash, "second-password-2"));
        Assert.Contains(checkDb.EmailOutbox, m => m.ToEmail == email && m.Event == NotificationEvent.PasswordChanged);
        Assert.Single(checkDb.RefreshSessions.Where(s => s.UserId == user.Id)); // this device stays signed in
    }
}
