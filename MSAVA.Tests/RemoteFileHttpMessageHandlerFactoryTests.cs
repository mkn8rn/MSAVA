using System.Net;
using MSAVA_BLL.Services.Files;

namespace MSAVA_App.Tests;

public class RemoteFileHttpMessageHandlerFactoryTests
{
    [Test]
    public void Create_DisablesAutoRedirectForRemoteFileIngestion()
    {
        using var handler = RemoteFileHttpMessageHandlerFactory.Create();
        var socketsHandler = handler.Should().BeOfType<SocketsHttpHandler>().Subject;

        socketsHandler.AllowAutoRedirect.Should().BeFalse();
        socketsHandler.UseProxy.Should().BeFalse();
    }

    [Test]
    public async Task SendAsync_RejectsUnsafeResolvedHostBeforeOpeningConnection()
    {
        bool connectorCalled = false;
        string? resolvedHost = null;
        using var handler = RemoteFileHttpMessageHandlerFactory.Create(
            (host, _) =>
            {
                resolvedHost = host;
                return Task.FromResult(new[] { IPAddress.Parse("127.0.0.1") });
            },
            (_, _) =>
            {
                connectorCalled = true;
                throw new InvalidOperationException("Connection should not be opened.");
            });
        using var client = new HttpClient(handler);

        Func<Task> act = async () => await client.GetAsync("http://files.example.test/sample.txt");

        Exception exception = (await act.Should().ThrowAsync<Exception>()).Which;
        ArgumentException? argumentException = exception as ArgumentException
            ?? exception.InnerException as ArgumentException;

        argumentException.Should().NotBeNull();
        argumentException!.Message.Should().Contain(
            "FileUrl host resolves to an address that is not allowed for server-side ingestion.");
        resolvedHost.Should().Be("files.example.test");
        connectorCalled.Should().BeFalse();
    }

    [TestCase("http://127.0.0.1/sample.txt")]
    [TestCase("http://2130706433/sample.txt")]
    [TestCase("http://[::ffff:127.0.0.1]/sample.txt")]
    public async Task EnsureResolvedHostIsAllowedAsync_RejectsUnsafeLiteralWithoutResolvingDns(
        string fileUrl)
    {
        bool resolverCalled = false;
        var uri = new Uri(fileUrl);

        Func<Task> act = () => RemoteFileHostPolicy.EnsureResolvedHostIsAllowedAsync(
            uri,
            (_, _) =>
            {
                resolverCalled = true;
                return Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") });
            },
            "FileUrl",
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("FileUrl host is not allowed for server-side ingestion.*");
        resolverCalled.Should().BeFalse();
    }

    [Test]
    public async Task EnsureResolvedHostIsAllowedAsync_AllowsPublicLiteralWithoutResolvingDns()
    {
        bool resolverCalled = false;
        var uri = new Uri("http://93.184.216.34/sample.txt");

        await RemoteFileHostPolicy.EnsureResolvedHostIsAllowedAsync(
            uri,
            (_, _) =>
            {
                resolverCalled = true;
                return Task.FromResult(Array.Empty<IPAddress>());
            },
            "FileUrl",
            CancellationToken.None);

        resolverCalled.Should().BeFalse();
    }
}
