using EvangelionERPV2.Web.Security;
using System.Text;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// The cap on an uploaded utterance. It has to hold while the bytes stream past, because the
    /// voice endpoint forwards the upload to the transcription server as it arrives — there is no
    /// buffer to inspect afterwards, on purpose: a buffer is a recording.
    /// </summary>
    public class BoundedReadStreamTests
    {
        [Fact]
        public async Task ReadingUnderTheLimit_PassesEverythingThrough()
        {
            await using var bounded = new BoundedReadStream(Source(1024), limit: 4096);

            var copied = await CopyAsync(bounded);

            Assert.Equal(1024, copied);
            Assert.Equal(1024, bounded.BytesRead);
            Assert.False(bounded.ExceededLimit);
        }

        [Fact]
        public async Task ReadingPastTheLimit_StopsAndSaysSo()
        {
            await using var bounded = new BoundedReadStream(Source(8192), limit: 1000);

            await CopyAsync(bounded);

            Assert.True(bounded.ExceededLimit);
            // Truncated, not thrown: the caller turns this into 413, and a client's oversized clip
            // is not a server fault.
            Assert.True(bounded.BytesRead > 1000);
        }

        [Fact]
        public async Task OnceOverTheLimit_ItRefusesToKeepReading()
        {
            await using var bounded = new BoundedReadStream(Source(8192), limit: 10);

            await CopyAsync(bounded);
            var afterwards = await bounded.ReadAsync(new byte[64]);

            Assert.Equal(0, afterwards);
        }

        [Fact]
        public void ItIsForwardOnly()
        {
            using var bounded = new BoundedReadStream(Source(16), limit: 16);

            // Seeking would mean re-reading the audio, which means holding it.
            Assert.False(bounded.CanSeek);
            Assert.False(bounded.CanWrite);
            Assert.Throws<NotSupportedException>(() => bounded.Seek(0, SeekOrigin.Begin));
        }

        private static Stream Source(int bytes) =>
            new MemoryStream(Encoding.UTF8.GetBytes(new string('a', bytes)));

        private static async Task<int> CopyAsync(Stream stream)
        {
            var buffer = new byte[256];
            var total = 0;
            int read;

            while ((read = await stream.ReadAsync(buffer)) > 0)
                total += read;

            return total;
        }
    }
}
