namespace MSAVA_BLL.Utils.Metadata;

/// <summary>
/// Sentinel values for invalid/missing metadata fields.
/// Use these constants to detect missing metadata in services.
/// </summary>
public static class InvalidMetadata
{
    // String sentinel - use for titles, authors, etc.
    public const string String = "[INVALID]";
    
    // Numeric sentinels
    public const int Int = -1;
    public const long Long = -1L;
    public const double Double = -1.0;
    
    // Duration sentinel (for audio/video)
    public const double DurationSeconds = -1.0;
    
    // Dimension sentinels (for images/video)
    public const int Width = -1;
    public const int Height = -1;
    
    // Audio sentinels
    public const int Bitrate = -1;
    public const int SampleRate = -1;
    public const int Channels = -1;
    
    // Document sentinels
    public const int PageCount = -1;
    public const int WordCount = -1;
    public const int LineCount = -1;

    /// <summary>
    /// Checks if a string value is invalid/missing.
    /// </summary>
    public static bool IsInvalid(string? value) => 
        string.IsNullOrEmpty(value) || value == String;

    /// <summary>
    /// Checks if an integer value is invalid/missing.
    /// </summary>
    public static bool IsInvalid(int? value) => 
        !value.HasValue || value.Value < 0;

    /// <summary>
    /// Checks if a double value is invalid/missing.
    /// </summary>
    public static bool IsInvalid(double? value) => 
        !value.HasValue || value.Value < 0;

    /// <summary>
    /// Returns the value or the invalid sentinel if null/empty.
    /// </summary>
    public static string OrInvalid(string? value) => 
        string.IsNullOrWhiteSpace(value) ? String : value;

    /// <summary>
    /// Returns the value or the invalid sentinel if null/zero/negative.
    /// </summary>
    public static int OrInvalid(int? value) => 
        value.HasValue && value.Value > 0 ? value.Value : Int;

    /// <summary>
    /// Returns the value or the invalid sentinel if null/zero/negative.
    /// </summary>
    public static double OrInvalid(double? value) => 
        value.HasValue && value.Value > 0 ? value.Value : Double;
}
