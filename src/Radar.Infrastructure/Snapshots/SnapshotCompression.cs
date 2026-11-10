using System.IO.Compression;

namespace Radar.Infrastructure.Snapshots
{
    /// <summary>
    /// Brotli for stored snapshots: JSON specs shrink roughly 10:1, which matters on a 500 MB free database.
    /// "Optimal" is a fast, good-enough level; the maximum level is many times slower for a few % more.
    /// </summary>
    internal static class SnapshotCompression
    {
        public static byte[] Compress(byte[] data)
        {
            using var output = new MemoryStream(data.Length / 8);
            using (var brotli = new BrotliStream(output, CompressionLevel.Optimal))
            {
                brotli.Write(data);
            }

            return output.ToArray();
        }

        public static byte[] Decompress(byte[] compressed)
        {
            using var input = new MemoryStream(compressed);
            using var brotli = new BrotliStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream(compressed.Length * 10);
            brotli.CopyTo(output);
            return output.ToArray();
        }
    }
}
