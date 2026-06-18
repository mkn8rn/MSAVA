using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using MSAVA_API.Middleware;

namespace MSAVA_API.Tests;

public class SecurityHeadersMiddlewareTests
{
    [Test]
    public void Constructor_RejectsMissingNextDelegate()
    {
        Action act = () => _ = new SecurityHeadersMiddleware(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("next");
    }

    [Test]
    public async Task InvokeAsync_AddsSecurityHeadersWhenResponseStarts()
    {
        var (context, responseFeature) = CreateHttpContext();
        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);
        await responseFeature.FireOnStartingAsync();

        context.Response.Headers[SecurityHeadersMiddleware.ContentTypeOptionsHeader]
            .Should().ContainSingle().Which.Should().Be(SecurityHeadersMiddleware.ContentTypeOptionsValue);
        context.Response.Headers[SecurityHeadersMiddleware.FrameOptionsHeader]
            .Should().ContainSingle().Which.Should().Be(SecurityHeadersMiddleware.FrameOptionsValue);
        context.Response.Headers[SecurityHeadersMiddleware.ReferrerPolicyHeader]
            .Should().ContainSingle().Which.Should().Be(SecurityHeadersMiddleware.ReferrerPolicyValue);
        context.Response.Headers[SecurityHeadersMiddleware.PermissionsPolicyHeader]
            .Should().ContainSingle().Which.Should().Be(SecurityHeadersMiddleware.PermissionsPolicyValue);
    }

    [Test]
    public async Task InvokeAsync_EnforcesSecurityHeadersOverDownstreamValues()
    {
        var (context, responseFeature) = CreateHttpContext();
        var middleware = new SecurityHeadersMiddleware(httpContext =>
        {
            httpContext.Response.Headers[SecurityHeadersMiddleware.FrameOptionsHeader] = "SAMEORIGIN";
            httpContext.Response.Headers[SecurityHeadersMiddleware.ReferrerPolicyHeader] = "origin";
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);
        await responseFeature.FireOnStartingAsync();

        context.Response.Headers[SecurityHeadersMiddleware.FrameOptionsHeader]
            .Should().ContainSingle().Which.Should().Be(SecurityHeadersMiddleware.FrameOptionsValue);
        context.Response.Headers[SecurityHeadersMiddleware.ReferrerPolicyHeader]
            .Should().ContainSingle().Which.Should().Be(SecurityHeadersMiddleware.ReferrerPolicyValue);
    }

    private static (DefaultHttpContext Context, RecordingResponseFeature ResponseFeature) CreateHttpContext()
    {
        var context = new DefaultHttpContext();
        var responseFeature = new RecordingResponseFeature();
        context.Features.Set<IHttpResponseFeature>(responseFeature);
        return (context, responseFeature);
    }

    private sealed class RecordingResponseFeature : IHttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _startingCallbacks = [];

        public int StatusCode { get; set; } = StatusCodes.Status200OK;

        public string? ReasonPhrase { get; set; }

        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        public Stream Body { get; set; } = new MemoryStream();

        public bool HasStarted { get; private set; }

        public Task FireOnStartingAsync()
        {
            HasStarted = true;

            return Task.WhenAll(_startingCallbacks
                .Select(callback => callback.Callback(callback.State)));
        }

        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }

        public void OnStarting(Func<object, Task> callback, object state)
        {
            _startingCallbacks.Add((callback, state));
        }
    }
}
