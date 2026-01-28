using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

namespace MSAVA_BLL.Utils.Metadata;

/// <summary>
/// Image metadata extraction using SixLabors.ImageSharp.
/// Supports: PNG, JPG, JPEG, GIF, BMP, WebP, TIFF, TGA, PBM, PGM, PPM, ICO, DIB
/// </summary>
public static class ImageMetadataExtractor
{
    public static void Register(Dictionary<string, (Func<Stream, long, JsonDocument> Extractor, string ContentType)> map)
    {
        // ImageSharp supported raster formats only
        map["png"] = (Extract, "image/png");
        map["jpg"] = (Extract, "image/jpeg");
        map["jpeg"] = (Extract, "image/jpeg");
        map["gif"] = (Extract, "image/gif");
        map["bmp"] = (Extract, "image/bmp");
        map["webp"] = (Extract, "image/webp");
        map["tiff"] = (Extract, "image/tiff");
        map["tga"] = (Extract, "image/x-targa");
        map["pbm"] = (Extract, "image/x-portable-bitmap");
        map["pgm"] = (Extract, "image/x-portable-graymap");
        map["ppm"] = (Extract, "image/x-portable-pixmap");
        map["ico"] = (Extract, "image/x-icon");
        map["dib"] = (Extract, "image/bmp");
        
        // Note: HEIC, HEIF, PSD, EXR, JP2, XBM, XPM are NOT registered
        // They will fall through to InvalidMetadata
    }

    private static JsonDocument Extract(Stream stream, long size)
    {
        using var image = Image.Load(stream);
        var metadata = image.Metadata;

        string? cameraMake = null;
        string? cameraModel = null;
        string? dateTaken = null;
        int? isoSpeed = null;
        string? orientation = null;
        string? software = null;

        if (metadata.ExifProfile is not null)
        {
            if (metadata.ExifProfile.TryGetValue(ExifTag.Make, out var makeValue))
                cameraMake = makeValue.Value;
            if (metadata.ExifProfile.TryGetValue(ExifTag.Model, out var modelValue))
                cameraModel = modelValue.Value;
            if (metadata.ExifProfile.TryGetValue(ExifTag.DateTimeOriginal, out var dateValue))
                dateTaken = dateValue.Value;
            if (metadata.ExifProfile.TryGetValue(ExifTag.ISOSpeedRatings, out var isoValue) && isoValue.Value?.Length > 0)
                isoSpeed = (int?)isoValue.Value[0];
            if (metadata.ExifProfile.TryGetValue(ExifTag.Orientation, out var orientationValue))
                orientation = orientationValue.Value.ToString();
            if (metadata.ExifProfile.TryGetValue(ExifTag.Software, out var softwareValue))
                software = softwareValue.Value;
        }

        return MetadataExtractor.ToJsonDocument(new
        {
            Type = "Image",
            Valid = true,
            Width = image.Width,
            Height = image.Height,
            BitsPerPixel = image.PixelType.BitsPerPixel,
            HorizontalDpi = InvalidMetadata.OrInvalid(metadata.HorizontalResolution > 0 ? metadata.HorizontalResolution : null),
            VerticalDpi = InvalidMetadata.OrInvalid(metadata.VerticalResolution > 0 ? metadata.VerticalResolution : null),
            CameraMake = InvalidMetadata.OrInvalid(cameraMake),
            CameraModel = InvalidMetadata.OrInvalid(cameraModel),
            DateTaken = InvalidMetadata.OrInvalid(dateTaken),
            IsoSpeed = InvalidMetadata.OrInvalid(isoSpeed),
            Orientation = InvalidMetadata.OrInvalid(orientation),
            Software = InvalidMetadata.OrInvalid(software),
            Size = size
        });
    }
}
