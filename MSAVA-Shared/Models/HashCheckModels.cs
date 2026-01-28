namespace MSAVA_Shared.Models;

/// <summary>
/// Request to check if a file hash already exists and get/create a reference.
/// </summary>
public record HashCheckRequest
{
    /// <summary>
    /// The SHA-256 hash of the file content as a hex string (64 characters).
    /// </summary>
    public required string ContentHashHex { get; init; }
    
    /// <summary>
    /// The file extension (without dot).
    /// </summary>
    public required string FileExtension { get; init; }
    
    /// <summary>
    /// Access group for the new reference (if one needs to be created).
    /// </summary>
    public Guid? AccessGroupId { get; init; }
    
    /// <summary>
    /// File name for the new reference (if one needs to be created).
    /// </summary>
    public string? FileName { get; init; }
    
    /// <summary>
    /// Description for the new reference (if one needs to be created).
    /// </summary>
    public string? Description { get; init; }
    
    /// <summary>
    /// Tags for the new reference (if one needs to be created).
    /// </summary>
    public List<string>? Tags { get; init; }
    
    /// <summary>
    /// Categories for the new reference (if one needs to be created).
    /// </summary>
    public List<string>? Categories { get; init; }
    
    /// <summary>
    /// Whether the file should be publicly viewable.
    /// </summary>
    public bool PublicViewing { get; init; }
    
    /// <summary>
    /// Whether the file should be publicly downloadable.
    /// </summary>
    public bool PublicDownload { get; init; }
}

/// <summary>
/// Result of hash check and reference creation.
/// </summary>
public record HashCheckResult
{
    /// <summary>
    /// Whether a file with this hash exists on the server.
    /// </summary>
    public required bool FileExists { get; init; }
    
    /// <summary>
    /// The reference ID the user can use.
    /// - If user already had access: their existing reference
    /// - If file exists but user had no access: newly created reference
    /// - If file doesn't exist: null (user should upload)
    /// </summary>
    public Guid? ReferenceId { get; init; }
    
    /// <summary>
    /// Whether a new reference was created for this request.
    /// </summary>
    public bool NewReferenceCreated { get; init; }
    
    /// <summary>
    /// The hash that was checked (echoed back for client verification).
    /// </summary>
    public required string ContentHashHex { get; init; }
    
    /// <summary>
    /// Indicates if upload is required.
    /// </summary>
    public bool UploadRequired => !FileExists;
    
    /// <summary>
    /// Message for the client.
    /// </summary>
    public string? Message { get; init; }
    
    /// <summary>
    /// Error message if the operation failed.
    /// </summary>
    public string? Error { get; init; }
    
    /// <summary>
    /// Creates a result for when the user already has access.
    /// </summary>
    public static HashCheckResult ExistingAccess(string hashHex, Guid refId) => new()
    {
        FileExists = true,
        ReferenceId = refId,
        NewReferenceCreated = false,
        ContentHashHex = hashHex,
        Message = "You already have access to this file."
    };
    
    /// <summary>
    /// Creates a result for when a new reference was created.
    /// </summary>
    public static HashCheckResult NewReference(string hashHex, Guid refId) => new()
    {
        FileExists = true,
        ReferenceId = refId,
        NewReferenceCreated = true,
        ContentHashHex = hashHex,
        Message = "File exists. A new reference has been created for you."
    };
    
    /// <summary>
    /// Creates a result indicating the file does not exist.
    /// </summary>
    public static HashCheckResult NotFound(string hashHex) => new()
    {
        FileExists = false,
        ReferenceId = null,
        NewReferenceCreated = false,
        ContentHashHex = hashHex,
        Message = "File not found. Please proceed with upload."
    };
    
    /// <summary>
    /// Creates a result for an error condition.
    /// </summary>
    public static HashCheckResult Failed(string hashHex, string error) => new()
    {
        FileExists = false,
        ReferenceId = null,
        NewReferenceCreated = false,
        ContentHashHex = hashHex,
        Error = error
    };
}
