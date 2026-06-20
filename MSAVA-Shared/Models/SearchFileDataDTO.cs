using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace MSAVA_Shared.Models;

public class SearchFileDataDTO
{
    [Display(Order = 1, Name = "FilePath")]
    public string FilePath { get; set; } = string.Empty;

    [Display(Order = 0, Name = "Name")]
    public string? Name { get; set; }

    [Display(Order = 3, Name = "Description")]
    public string? Description { get; set; }

    [Display(Order = 4, Name = "DataId")]
    public Guid DataId { get; set; }

    [Display(Order = 5, Name = "RefId")]
    public Guid RefId { get; set; }

    [Display(Order = 6, Name = "MimeType")]
    public string MimeType { get; set; } = string.Empty;

    [Display(Order = 7, Name = "FileExtension")]
    public string FileExtension { get; set; } = string.Empty;

    // Hide array details; replaced by TagsCount/CategoriesCount
    [Display(AutoGenerateField = false)]
    public string[]? Tags { get; set; }

    [Display(AutoGenerateField = false)]
    public string[]? Categories { get; set; }

    [Display(Name = "Tags", Order = 8)]
    public int TagsCount => Tags?.Length ?? 0;

    [Display(Name = "Categories", Order = 9)]
    public int CategoriesCount => Categories?.Length ?? 0;

    [Display(Order = 10, Name = "SizeInBytes")]
    public ulong SizeInBytes { get; set; }

    [Display(Order = 11, Name = "Checksum")]
    public string? Checksum { get; set; }

    // Hide raw JSON
    [Display(AutoGenerateField = false)]
    public JsonDocument? Metadata { get; set; }

    [Display(Order = 12, Name = "PublicViewing")]
    public bool PublicViewing { get; set; }

    [Display(Order = 13, Name = "DownloadCount")]
    public uint DownloadCount { get; set; }

    [Display(Order = 14, Name = "SavedAt")]
    public DateTime SavedAt { get; set; }

    [Display(AutoGenerateField = false)]
    public DateTime LastModifiedAt { get; set; }

    [Display(AutoGenerateField = false)]
    public Guid LastModifiedById { get; set; }
}
