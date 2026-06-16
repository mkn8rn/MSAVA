using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MSAVA_App.Services.Api;
using MSAVA_Shared.Models;

namespace MSAVA_App.Services.Files
{
    public class FileRetrievalService
    {
        private readonly ApiService _api;
        private readonly ILogger<FileRetrievalService> _logger;

        public FileRetrievalService(ApiService api, ILogger<FileRetrievalService> logger)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IReadOnlyList<SearchFileDataDTO>> GetAllFileMetadataAsync(CancellationToken cancellationToken = default)
        {
            var list = await _api.SendForAsync<List<SearchFileDataDTO>>(
                HttpMethod.Get,
                ApiService.Routes.FilesRetrieveMetaAll,
                AppJsonSerializerContext.Default.ListSearchFileDataDTO,
                cancellationToken: cancellationToken);

            if (list is null)
            {
                _logger.LogWarning("Files metadata request returned no data");
                return Array.Empty<SearchFileDataDTO>();
            }

            return list;
        }
    }
}
