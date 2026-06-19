using Microsoft.AspNetCore.Http;
using MSAVA_Shared.Models;

namespace MSAVA_BLL.Services.Files;

public class SaveFileFromFormFileDTO : IFileCreationMetadataRequest
{
    public required string FileName { get; set; }
    public required string FileExtension { get; set; }
    public required IFormFile FormFile { get; set; }
    public required Guid AccessGroupId { get; set; }
    public List<string>? Tags { get; set; } = [];
    public List<string>? Categories { get; set; } = [];
    public string? Description { get; set; } = string.Empty;
    public bool PublicViewing { get; set; }
    public bool PublicDownload { get; set; }
}
