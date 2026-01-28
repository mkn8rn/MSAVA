namespace MSAVA_INF.Models;

/// <summary>
/// Supported file extension types. Only includes formats with actual metadata extraction support.
/// Unsupported formats should use Unknown and will have InvalidMetadata returned.
/// </summary>
public enum FileExtensionType : short // PostgreSQL doesn't support ushort
{
    Unknown = 0,

    // Documents (1-99)
    _TXT = 1,
    _PDF = 2,
    _JSON = 3,
    _DOCX = 4,
    _CSV = 5,
    _XLSX = 6,
    _PPTX = 7,
    _RTF = 8,
    _HTML = 9,
    _XML = 10,
    _MD = 11,
    _ODT = 12,
    _ODS = 13,
    _ODP = 14,
    _XLS = 15,    // Legacy Excel - supported via NPOI
    _LOG = 16,
    _YAML = 17,
    _INI = 18,
    _TSV = 19,
    _HTM = 20,
    _YML = 21,
    _MARKDOWN = 22,

    // Raster images - ImageSharp supported (1001-1099)
    _PNG = 1001,
    _JPG = 1002,
    _JPEG = 1003,
    _GIF = 1004,
    _BMP = 1005,
    _WEBP = 1006,
    _TIFF = 1007,
    _TGA = 1008,
    _PBM = 1009,
    _PGM = 1010,
    _PPM = 1011,
    _ICO = 1012,
    _DIB = 1013,

    // Vector images - text-based formats we can parse (2001-2099)
    _SVG = 2001,
    _SVGZ = 2002,
    _EPS = 2003,
    _AI = 2004,     // AI files are EPS-based
    _DXF = 2005,    // AutoCAD text format
    _SWF = 2006,    // Flash - basic header parsing

    // Audio - TagLib supported (3001-3099)
    _MP3 = 3001,
    _FLAC = 3002,
    _WAV = 3003,
    _OGG = 3004,
    _M4A = 3005,
    _AAC = 3006,
    _WMA = 3007,
    _AIFF = 3008,
    _OPUS = 3009,
    _APE = 3010,
    _MPC = 3011,
    _WV = 3012,
    _DSF = 3013,
    _AU = 3014,

    // Video - TagLib supported (4001-4099)
    _MP4 = 4001,
    _MKV = 4002,
    _AVI = 4003,
    _MOV = 4004,
    _WEBM = 4005,
    _WMV = 4006,
    _FLV = 4007,
    _MPG = 4008,
    _MPEG = 4009,
    _3GP = 4010,
    _3G2 = 4011,
    _OGV = 4012,
    _ASF = 4013,
    _M4V = 4014,
    _F4V = 4015,
    _TS = 4016,     // Transport stream - basic detection
    _MTS = 4017,
    _M2TS = 4018,
}
