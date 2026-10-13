using EvangelionERPV2.AiModule.Application.Configs;
using EvangelionERPV2.AiModule.Application.Interface;
using Microsoft.Extensions.Options;
using Serilog;
using System.Diagnostics;
using System.Net.Http.Json;

namespace EvangelionERPV2.AiModule.Application.Services
{
    /// <summary>
    /// Text-to-speech against a local Piper server, over the OpenAI-compatible
    /// <c>/v1/audio/speech</c> shape.
    /// </summary>
    public sealed class AiVoiceService : IAiVoiceService
    {
        /// <summary>
        /// How much of the answer is looked at before it is handed over. Small on purpose: this is a
        /// sanity check on the first bytes, not a buffer of the audio.
        /// </summary>
        private const int SilenceProbeBytes = 256;

        /// <summary>
        /// Below this, whatever came back is a container with no sound in it. A bare ID3 header is
        /// 45 bytes; a real sentence is tens of kilobytes.
        /// </summary>
        private const int MinimumAudibleBytes = 128;

        private readonly HttpClient _client;
        private readonly AiSettings _settings;

        public AiVoiceService(HttpClient client, IOptions<AiSettings> settings)
        {
            _client = client;
            _settings = settings.Value;
        }

        public async Task<AiVoiceResult> SynthesizeAsync(string text, CancellationToken cancellation = default)
        {
            if (!_settings.VoiceEnabled)
                return AiVoiceResult.Failed("A voz do assistente está desativada.");

            if (string.IsNullOrWhiteSpace(text))
                return AiVoiceResult.Failed("Nada a falar.");

            var stopwatch = Stopwatch.StartNew();
            HttpResponseMessage? response = null;

            var spoken = AiSpokenText.Normalize(text);

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/audio/speech")
                {
                    Content = JsonContent.Create(new
                    {
                        model = _settings.VoiceModel,
                        input = spoken,
                        voice = _settings.VoiceName,
                        response_format = _settings.VoiceFormat,
                    }),
                };

                response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);

                if (!response.IsSuccessStatusCode)
                {
                    Log.Logger.Warning("Speech synthesis refused. Status={Status}", (int)response.StatusCode);
                    response.Dispose();

                    return AiVoiceResult.Failed("Não consegui gerar o áudio da resposta.");
                }

                var audio = await response.Content.ReadAsStreamAsync(cancellation);
                var contentType = response.Content.Headers.ContentType?.MediaType ?? DefaultContentType();

                var probe = new byte[SilenceProbeBytes];
                var probed = await audio.ReadAtLeastAsync(probe, probe.Length, throwOnEndOfStream: false, cancellation);

                if (probed < MinimumAudibleBytes)
                {
                    Log.Logger.Warning(
                        "Speech synthesis returned no audible audio. Bytes={Bytes} Voice={Voice}",
                        probed,
                        _settings.VoiceName);

                    await audio.DisposeAsync();
                    response.Dispose();

                    return AiVoiceResult.Failed("Não consegui gerar o áudio da resposta.");
                }

                stopwatch.Stop();

                Log.Logger.Information(
                    "Speech synthesis ready. Characters={Characters} LatencyMs={LatencyMs}",
                    text.Length,
                    stopwatch.ElapsedMilliseconds);

                return AiVoiceResult.Ok(
                    new ResponseOwnedStream(new PrefixedStream(probe, probed, audio), response),
                    contentType,
                    stopwatch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                response?.Dispose();
                throw;
            }
            catch (Exception ex)
            {
                response?.Dispose();

                Log.Logger.Error("Speech synthesis failed. ErrorType={ErrorType}", ex.GetType().Name);

                return AiVoiceResult.Failed("O serviço de voz está indisponível.");
            }
        }

        private string DefaultContentType() =>
            _settings.VoiceFormat.Equals("wav", StringComparison.OrdinalIgnoreCase)
                ? "audio/wav"
                : "audio/mpeg";

        /// <summary>
        /// The bytes already read, then the rest of the stream. Read-forward only, which is all the
        /// response body is.
        /// </summary>
        private sealed class PrefixedStream : Stream
        {
            private readonly byte[] _prefix;
            private readonly int _prefixLength;
            private readonly Stream _rest;
            private int _prefixPosition;

            public PrefixedStream(byte[] prefix, int prefixLength, Stream rest)
            {
                _prefix = prefix;
                _prefixLength = prefixLength;
                _rest = rest;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                var fromPrefix = ReadPrefix(buffer.AsSpan(offset, count));

                return fromPrefix > 0 ? fromPrefix : _rest.Read(buffer, offset, count);
            }

            public override async ValueTask<int> ReadAsync(
                Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                var fromPrefix = ReadPrefix(buffer.Span);

                return fromPrefix > 0 ? fromPrefix : await _rest.ReadAsync(buffer, cancellationToken);
            }

            public override Task<int> ReadAsync(
                byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
                ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

            private int ReadPrefix(Span<byte> destination)
            {
                var remaining = _prefixLength - _prefixPosition;
                if (remaining <= 0 || destination.Length == 0)
                    return 0;

                var count = Math.Min(remaining, destination.Length);
                _prefix.AsSpan(_prefixPosition, count).CopyTo(destination);
                _prefixPosition += count;

                return count;
            }

            public override void Flush() => _rest.Flush();

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    _rest.Dispose();

                base.Dispose(disposing);
            }

            public override async ValueTask DisposeAsync()
            {
                await _rest.DisposeAsync();
                await base.DisposeAsync();
            }
        }

        /// <summary>
        /// The body stream plus the response it belongs to, disposed together. Without this the
        /// caller would have to know it holds two things, and the connection would leak the day
        /// somebody forgot.
        /// </summary>
        private sealed class ResponseOwnedStream : Stream
        {
            private readonly Stream _inner;
            private readonly HttpResponseMessage _response;

            public ResponseOwnedStream(Stream inner, HttpResponseMessage response)
            {
                _inner = inner;
                _response = response;
            }

            public override bool CanRead => _inner.CanRead;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
                _inner.ReadAsync(buffer, offset, count, cancellationToken);

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
                _inner.ReadAsync(buffer, cancellationToken);

            public override void Flush() => _inner.Flush();

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _inner.Dispose();
                    _response.Dispose();
                }

                base.Dispose(disposing);
            }

            public override async ValueTask DisposeAsync()
            {
                await _inner.DisposeAsync();
                _response.Dispose();

                await base.DisposeAsync();
            }
        }
    }
}
