using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
namespace Strogo.SignedObserver
{
    public static class BoundedOutput
    {
        public static async Task<byte[]> ReadAsync(Process process, Stream input, int maximum)
        {
            if (maximum <= 0) throw new ArgumentOutOfRangeException(nameof(maximum));
            using (var output = new MemoryStream())
            {
                var buffer = new byte[8192];
                try
                {
                    while (true)
                    {
                        var length = await input.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                        if (length == 0) return output.ToArray();
                        if (output.Length + length > maximum) throw new InvalidDataException("SignedObserverOutputLimitExceeded");
                        output.Write(buffer, 0, length);
                    }
                }
                catch
                {
                    try { if (!process.HasExited) process.Kill(true); }
                    catch (InvalidOperationException) { }
                    throw;
                }
            }
        }
    }
}
