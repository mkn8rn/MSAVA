using Microsoft.AspNetCore.Mvc;
using MSAVA_API.Controllers;
using MSAVA_BLL.Services.Files;
using MSAVA_BLL.Services.Interfaces;
using MSAVA_Shared.Models;

namespace MSAVA_API.Tests;

public class FilesCheckControllerTests
{
    [Test]
    public async Task CheckHash_ReturnsBadRequestWhenServiceReturnsValidationError()
    {
        var service = new RecordingDeduplicationService
        {
            SingleResult = HashCheckResult.Failed("", HashCheckRequestPolicy.RequiredMessage)
        };
        var controller = new FilesCheckController(service);

        var response = await controller.CheckHash(null);

        var badRequest = response.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeEquivalentTo(HashCheckResult.Failed("", HashCheckRequestPolicy.RequiredMessage));
        service.SingleRequest.Should().BeNull();
        service.SingleCallCount.Should().Be(0);
    }

    [Test]
    public async Task CheckHash_ReturnsOkForValidRequestAndPassesCancellationToken()
    {
        var request = CreateRequest(1);
        var service = new RecordingDeduplicationService
        {
            SingleResult = HashCheckResult.NotFound(request.ContentHashHex)
        };
        var controller = new FilesCheckController(service);
        using var cancellationTokenSource = new CancellationTokenSource();

        var response = await controller.CheckHash(request, cancellationTokenSource.Token);

        var ok = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(service.SingleResult);
        service.SingleRequest.Should().BeSameAs(request);
        service.SingleCancellationToken.Should().Be(cancellationTokenSource.Token);
        service.SingleCallCount.Should().Be(1);
    }

    [Test]
    public async Task CheckHashBatch_ReturnsBadRequestForNullBatchWithoutCallingService()
    {
        var service = new RecordingDeduplicationService();
        var controller = new FilesCheckController(service);

        var response = await controller.CheckHashBatch(null);

        var badRequest = response.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeEquivalentTo(new[]
        {
            HashCheckResult.Failed("", HashCheckBatchPolicy.RequiredMessage)
        });
        service.BatchRequest.Should().BeNull();
    }

    [Test]
    public async Task CheckHashBatch_ReturnsBadRequestForOversizedBatchWithoutCallingService()
    {
        var requests = Enumerable.Range(0, HashCheckBatchPolicy.MaximumRequestCount + 1)
            .Select(index => CreateRequest(index))
            .ToList();
        var service = new RecordingDeduplicationService();
        var controller = new FilesCheckController(service);

        var response = await controller.CheckHashBatch(requests);

        var badRequest = response.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeEquivalentTo(new[]
        {
            HashCheckResult.Failed("", HashCheckBatchPolicy.MaximumRequestCountMessage)
        });
        service.BatchRequest.Should().BeNull();
    }

    [Test]
    public async Task CheckHashBatch_ReturnsOkWhenServiceReturnsSuccessfulResults()
    {
        var request = CreateRequest(1);
        var results = new List<HashCheckResult>
        {
            HashCheckResult.NotFound(request.ContentHashHex)
        };
        var service = new RecordingDeduplicationService
        {
            BatchResults = results
        };
        var controller = new FilesCheckController(service);

        var response = await controller.CheckHashBatch([request]);

        var ok = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(results);
        service.BatchRequest.Should().ContainSingle().Which.Should().Be(request);
    }

    [Test]
    public async Task CheckHashBatch_ReturnsOkWhenValidBatchContainsItemErrors()
    {
        var successRequest = CreateRequest(1);
        var failedRequest = CreateRequest(2);
        var results = new List<HashCheckResult>
        {
            HashCheckResult.NotFound(successRequest.ContentHashHex),
            HashCheckResult.Failed(failedRequest.ContentHashHex, "FileExtension 'exe' is not supported.")
        };
        var service = new RecordingDeduplicationService
        {
            BatchResults = results
        };
        var controller = new FilesCheckController(service);

        var response = await controller.CheckHashBatch([successRequest, failedRequest]);

        var ok = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(results);
        service.BatchRequest.Should().Equal(successRequest, failedRequest);
    }

    private static HashCheckRequest CreateRequest(int index)
    {
        return new HashCheckRequest
        {
            ContentHashHex = index.ToString("x64"),
            FileExtension = "txt",
            AccessGroupId = Guid.NewGuid(),
            FileName = $"sample-{index}",
            PublicViewing = false,
            PublicDownload = false
        };
    }

    private sealed class RecordingDeduplicationService : IFileDeduplicationService
    {
        public HashCheckRequest? SingleRequest { get; private set; }
        public List<HashCheckRequest>? BatchRequest { get; private set; }
        public int SingleCallCount { get; private set; }
        public CancellationToken SingleCancellationToken { get; private set; }
        public HashCheckResult SingleResult { get; set; } = HashCheckResult.NotFound(new string('0', 64));
        public List<HashCheckResult> BatchResults { get; set; } = [];

        public Task<HashCheckResult> CheckAndGetReferenceAsync(
            HashCheckRequest? request,
            CancellationToken cancellationToken = default)
        {
            SingleCallCount++;
            SingleRequest = request;
            SingleCancellationToken = cancellationToken;
            return Task.FromResult(SingleResult);
        }

        public Task<List<HashCheckResult>> CheckAndGetReferenceBatchAsync(
            List<HashCheckRequest>? requests,
            CancellationToken cancellationToken = default)
        {
            BatchRequest = requests;
            return Task.FromResult(BatchResults);
        }
    }
}
