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
}
