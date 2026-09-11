using Microsoft.Net.Http.Headers;

namespace EvangelionERPV2.Web.Security
{
    public static class MultipartRequestHelper
    {
        private const int MaxBoundaryLength = 128;

        /// <summary>
        /// True when the request carries a multipart body, with its boundary. Used by the voice
        /// endpoint, which reads the body itself rather than letting model binding spool it to disk.
        /// </summary>
        public static bool IsMultipart(string? contentType, out string boundary)
        {
            boundary = string.Empty;

            if (string.IsNullOrWhiteSpace(contentType)
                || !contentType.Contains("multipart/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var parsedBoundary = HeaderUtilities.RemoveQuotes(
                MediaTypeHeaderValue.Parse(contentType).Boundary).Value;

            if (string.IsNullOrWhiteSpace(parsedBoundary) || parsedBoundary.Length > MaxBoundaryLength)
                return false;

            boundary = parsedBoundary;
            return true;
        }
    }

    /// <summary>
    /// Passes a stream through while counting, and stops dead at a byte limit. The voice endpoint
    /// forwards the upload to the transcription server as it arrives, so the cap cannot be a check
    /// on a buffer that was already filled — there is no buffer.
    /// </summary>
    public sealed class BoundedReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _limit;

        public BoundedReadStream(Stream inner, long limit)
        {
            _inner = inner;
            _limit = limit;
        }

        public long BytesRead { get; private set; }

        public bool ExceededLimit { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => BytesRead;
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (ExceededLimit)
                return 0;

            var read = await _inner.ReadAsync(buffer, cancellationToken);
            return Count(read);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (ExceededLimit)
                return 0;

            return Count(_inner.Read(buffer, offset, count));
        }

        private int Count(int read)
        {
            BytesRead += read;

            if (BytesRead <= _limit)
                return read;

            // Truncate rather than throw: the caller turns this into 413, and a half-read clip is
            // discarded anyway. Throwing here would surface as a 500 for a client's own mistake.
            ExceededLimit = true;
            return 0;
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
