using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;
using PlanFlow.Infrastructure.Email;

namespace PlanFlow.Tests.Infrastructure.Email;

/// <summary>
/// Talks to a tiny in-process SMTP server, so the real MailKit code path (connect, envelope, message body) is exercised
/// without needing Mailpit or an internet connection.
/// </summary>
public class SmtpEmailSenderTests
{
    /// <summary>Accepts one connection, answers like an SMTP server and records the message data.</summary>
    private sealed class FakeSmtpServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly Task _serving;

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public List<string> Commands { get; } = new();
        public string Data { get; private set; } = string.Empty;

        public FakeSmtpServer()
        {
            _listener.Start();
            _serving = Task.Run(ServeOneAsync);
        }

        private async Task ServeOneAsync()
        {
            using var client = await _listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\r\n", AutoFlush = true };

            await writer.WriteLineAsync("220 fake.local ready");
            while (await reader.ReadLineAsync() is { } line)
            {
                Commands.Add(line);
                var command = line.ToUpperInvariant();
                if (command.StartsWith("EHLO") || command.StartsWith("HELO"))
                {
                    await writer.WriteLineAsync("250 fake.local");
                }
                else if (command.StartsWith("DATA"))
                {
                    await writer.WriteLineAsync("354 go ahead");
                    var data = new StringBuilder();
                    while (await reader.ReadLineAsync() is { } dataLine && dataLine != ".")
                    {
                        data.AppendLine(dataLine);
                    }

                    Data = data.ToString();
                    await writer.WriteLineAsync("250 queued");
                }
                else if (command.StartsWith("QUIT"))
                {
                    await writer.WriteLineAsync("221 bye");
                    return;
                }
                else
                {
                    await writer.WriteLineAsync("250 ok");
                }
            }
        }

        public Task WaitAsync() => _serving.WaitAsync(TimeSpan.FromSeconds(10));

        public void Dispose() => _listener.Stop();
    }

    [Fact]
    public async Task SendAsync_DeliversTheMessageToTheSmtpServer()
    {
        using var server = new FakeSmtpServer();
        var sender = new SmtpEmailSender(Options.Create(new EmailOptions
        {
            Host = "127.0.0.1", Port = server.Port, FromAddress = "noreply@planflow.test", FromName = "PlanFlow"
        }));

        await sender.SendAsync("ann@example.com", "Welcome to PlanFlow", "<p>Hello Ann</p>", "Hello Ann", CancellationToken.None);
        await server.WaitAsync();

        Assert.Contains(server.Commands, c => c.StartsWith("MAIL FROM:<noreply@planflow.test>", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(server.Commands, c => c.StartsWith("RCPT TO:<ann@example.com>", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Subject: Welcome to PlanFlow", server.Data);
        Assert.Contains("To: ann@example.com", server.Data);
        Assert.Contains("multipart/alternative", server.Data); // both the HTML and the plain-text version were sent
    }

    [Fact]
    public async Task SendAsync_ServerNotReachable_Throws_SoTheOutboxCanRetry()
    {
        var unusedPort = ((IPEndPoint)new Func<EndPoint>(() =>
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var endpoint = probe.LocalEndpoint;
            probe.Stop();
            return endpoint;
        })()).Port;
        var sender = new SmtpEmailSender(Options.Create(new EmailOptions { Host = "127.0.0.1", Port = unusedPort }));

        await Assert.ThrowsAnyAsync<Exception>(() =>
            sender.SendAsync("ann@example.com", "s", "<p>h</p>", "h", CancellationToken.None));
    }
}
