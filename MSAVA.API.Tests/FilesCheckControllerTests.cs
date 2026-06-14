using Microsoft.AspNetCore.Mvc;
using MSAVA_API.Controllers;
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
            SingleResult = HashCheckResult.Failed("", "Hash check request is required.")
        };
        var controller = new FilesCheckController(service);

        var response = await controller.CheckHash(null);

        var badRequest = response.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeEquivalentTo(service.SingleResult);
        service.SingleRequest.Should().BeNull();
    }

    [Test]
    public async Task CheckHashBatch_DelegatesNullBatchToServiceAndReturnsBadRequest()
    {
        var service = new RecordingDeduplicationService
        {
            BatchResults =
            [
                HashCheckResult.Failed("", "Hash check batch request is required.")
            ]
        };
        var controller = new FilesCheckController(service);

        var response = await controller.CheckHashBatch(null);

        var badRequest = response.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeEquivalentTo(service.BatchResults);
        service.BatchRequest.Should().BeNull();
    }

    [Test]
    public async Task CheckHashBatch_DelegatesOversizedBatchToServiceAndReturnsBadRequest()
    {
        var requests = Enumerable.Range(0, 101)
            .Select(index => CreateRequest(index))
            .ToList();
        var service = new RecordingDeduplicationService
        {
            BatchResults =
            [
                HashCheckResult.Failed("", "Maximum 100 hashes per batch request.")
            ]
        };
        var controller = new FilesCheckController(service);

        var response = await controller.CheckHashBatch(requests);

        var badRequest = response.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.Value.Should().BeEquivalentTo(service.BatchResults);
        service.BatchRequest.Should().BeSameAs(requests);
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
        public HashCheckResult SingleResult { get; set; } = HashCheckResult.NotFound(new string('0', 64));
        public List<HashCheckResult> BatchResults { get; set; } = [];

        public Task<HashCheckResult> CheckAndGetReferenceAsync(
            HashCheckRequest? request,
            CancellationToken cancellationToken = default)
        {
            SingleRequest = request;
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
