#if ANDROID
using ABitmap = global::Android.Graphics.Bitmap;
using ABitmapFactory = global::Android.Graphics.BitmapFactory;
using AExifInterface = global::Android.Media.ExifInterface;
using AMatrix = global::Android.Graphics.Matrix;
#endif

namespace SAFETY_STEPS.Services;

/// <summary>
/// Shrinks photos before they are stored as base64 in the Realtime Database.
/// A raw phone photo is several MB, and every list that reads a report or user
/// node downloads it in full, so photos are resized and re-encoded as JPEG.
/// </summary>
public static class ImageHelper
{
    public const int IncidentPhotoMaxSize = 1280;
    public const int ProfilePhotoMaxSize = 512;

    public static async Task<byte[]> LoadCompressedJpegAsync(FileResult file, int maxDimension, int quality = 75)
    {
        byte[] original;
        using (var src = await file.OpenReadAsync())
        using (var ms = new MemoryStream())
        {
            await src.CopyToAsync(ms);
            original = ms.ToArray();
        }

#if ANDROID
        try
        {
            return await Task.Run(() => CompressAndroid(original, maxDimension, quality));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ImageHelper] Compression failed, using original: {ex.Message}");
        }
#endif
        return original;
    }

#if ANDROID
    private static byte[] CompressAndroid(byte[] data, int maxDimension, int quality)
    {
        var bounds = new ABitmapFactory.Options { InJustDecodeBounds = true };
        ABitmapFactory.DecodeByteArray(data, 0, data.Length, bounds);
        if (bounds.OutWidth <= 0 || bounds.OutHeight <= 0) return data;

        // Decode at a power-of-two reduction first to keep memory use low.
        int sample = 1;
        while (Math.Max(bounds.OutWidth, bounds.OutHeight) / (sample * 2) >= maxDimension)
            sample *= 2;

        using var decoded = ABitmapFactory.DecodeByteArray(data, 0, data.Length,
            new ABitmapFactory.Options { InSampleSize = sample });
        if (decoded == null) return data;

        using var matrix = new AMatrix();
        float scale = Math.Min(1f, (float)maxDimension / Math.Max(decoded.Width, decoded.Height));
        if (scale < 1f) matrix.PostScale(scale, scale);

        // Re-encoding drops EXIF, so bake the camera's rotation into the pixels.
        int rotation = ReadExifRotation(data);
        if (rotation != 0) matrix.PostRotate(rotation);

        var output = matrix.IsIdentity
            ? decoded
            : ABitmap.CreateBitmap(decoded, 0, 0, decoded.Width, decoded.Height, matrix, true);

        try
        {
            using var outStream = new MemoryStream();
            output.Compress(ABitmap.CompressFormat.Jpeg!, quality, outStream);
            return outStream.ToArray();
        }
        finally
        {
            if (!ReferenceEquals(output, decoded))
            {
                output.Recycle();
                output.Dispose();
            }
            decoded.Recycle();
        }
    }

    private static int ReadExifRotation(byte[] data)
    {
        // ExifInterface(Stream) needs API 24; the file-path overload works on API 23.
        var path = Path.Combine(FileSystem.CacheDirectory, $"exif_{Guid.NewGuid():N}.jpg");
        try
        {
            File.WriteAllBytes(path, data);
            var exif = new AExifInterface(path);
            return exif.GetAttributeInt(AExifInterface.TagOrientation, 1) switch
            {
                6 => 90,
                3 => 180,
                8 => 270,
                _ => 0
            };
        }
        catch
        {
            return 0;
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }
#endif
}
