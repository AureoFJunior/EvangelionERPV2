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
    /// <remarks>
    /// The audio is never materialised here. The response is read headers-first and its body handed
    /// back as an open stream, so the bytes travel from Piper to the caller's response and exist
    /// nowhere else — no byte array, no temp file, no cache. Only the character count and the
    /// latency are ever logged.
    /// </remarks>
    public sealed class AiVoiceService : IAiVoiceService
    {
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

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/audio/speech")
                {
                    Content = JsonContent.Create(new
                    {
                        model = _settings.VoiceModel,
                        input = text,
                        voice = _settings.VoiceName,
                        response_format = _settings.VoiceFormat,
                    }),
                };

                // Headers first: the point of this whole path is that the body is still on the wire
                // when we hand it over, rather than sitting in memory waiting to be cached.
                response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);

                if (!response.IsSuccessStatusCode)
                {
                    Log.Logger.Warning("Speech synthesis refused. Status={Status}", (int)response.StatusCode);
                    response.Dispose();

                    return AiVoiceResult.Failed("Não consegui gerar o áudio da resposta.");
                }

                var audio = await response.Content.ReadAsStreamAsync(cancellation);
                var contentType = response.Content.Headers.ContentType?.MediaType ?? DefaultContentType();

                stopwatch.Stop();

                Log.Logger.Information(
                    "Speech synthesis ready. Characters={Characters} LatencyMs={LatencyMs}",
                    text.Length,
                    stopwatch.ElapsedMilliseconds);

                // The stream outlives this method, so the response has to as well — disposing it here
                // would close the connection under the caller mid-sentence.
                return AiVoiceResult.Ok(
                    new ResponseOwnedStream(audio, response), contentType, stopwatch.ElapsedMilliseconds);
            }
            // Only the caller hanging up is a cancellation. HttpClient reports its *own* timeout as a
            // TaskCanceledException too, with no token of ours behind it — rethrowing that would 500
            // the endpoint over a slow Piper instead of answering 503 and letting the client fall
            // back to the device's voice.
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                response?.Dispose();
                throw;
            }
            catch (Exception ex)
            {
                response?.Dispose();

                // Never the message: it carries the request, and the request is the answer's text.
                Log.Logger.Error("Speech synthesis failed. ErrorType={ErrorType}", ex.GetType().Name);

                return AiVoiceResult.Failed("O serviço de voz está indisponível.");
            }
        }

        private string DefaultContentType() =>
            _settings.VoiceFormat.Equals("wav", StringComparison.OrdinalIgnoreCase)
                ? "audio/wav"
                : "audio/mpeg";

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
