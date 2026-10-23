using Windows.Graphics.Imaging;
using Windows.Storage;

namespace UsageMon;

public static class BackgroundImageStore
{
    public static async Task<string> ImportAsync(StorageFile file, string destinationFolder)
    {
        using (var stream = await file.OpenReadAsync())
        {
            var decoder = await BitmapDecoder.CreateAsync(stream);
            if (decoder.DecoderInformation.CodecId != BitmapDecoder.PngDecoderId)
                throw new InvalidDataException("Please choose a PNG image.");
        }
        Directory.CreateDirectory(destinationFolder);
        string copy = Path.Combine(destinationFolder, Guid.NewGuid().ToString("N") + ".png");
        File.Copy(file.Path, copy);
        return copy;
    }
}
