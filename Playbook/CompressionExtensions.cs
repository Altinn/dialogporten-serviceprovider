using System.Buffers.Text;
using System.IO.Compression;
using System.Text;

namespace Digdir.BDB.Dialogporten.ServiceProvider.Playbook;

public static class CompressionExtensions
{
    public static string Encode(this string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);

        using var memoryStream = new MemoryStream();
        using (var brotliStream = new BrotliStream(memoryStream, CompressionLevel.Optimal))
        {
            brotliStream.Write(bytes, 0, bytes.Length);
        }
        var aa = Encoding.UTF8.GetString(Base64Url.EncodeToUtf8(memoryStream.ToArray()));
        return aa;
    }

    // public static byte[] Compress(this byte[] input)
    // {
    //     using var memoryStream = new MemoryStream(input);
    //     using (var brotliStream = new BrotliStream(memoryStream, CompressionLevel.Optimal))
    //     {
    //         brotliStream.Write(input, 0, input.Length);
    //         brotliStream.
    //     }
    //
    //
    // }
    public static async Task<byte[]> DecompressBytesAsync(byte[] bytes, CancellationToken cancel = default(CancellationToken))
    {
        using (var inputStream = new MemoryStream(bytes))
        {
            using (var outputStream = new MemoryStream())
            {
                using (var compressionStream = new BrotliStream(inputStream, CompressionMode.Decompress))
                {
                    await compressionStream.CopyToAsync(outputStream, cancel);
                }
                return outputStream.ToArray();
            }
        }
    }
    public static async Task<byte[]> CompressBytesAsync(byte[] bytes, CancellationToken cancel = default(CancellationToken))
    {
        using var outputStream = new MemoryStream();
        await using (var compressionStream = new BrotliStream(outputStream, CompressionLevel.Optimal))
        {
            await compressionStream.WriteAsync(bytes, cancel);
        }
        return outputStream.ToArray();
    }
  
}
