using EvangelionERPV2.AiModule.Application.Configs;
using EvangelionERPV2.AiModule.Application.Services;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace EvangelionERPV2.AiModule.Test.Ai
{
    /// <summary>
    /// The synthesis seam. Two things matter here and nothing else does: a server that misbehaves
    /// becomes a typed failure rather than an exception reaching a controller, and the audio comes
    /// back as a stream that was never read — because a byte array is how a cache gets added later.
    /// </summary>
    public class AiVoiceServiceTests
    {
        /// <summary>
        /// Stands in for a rendered sentence. Deliberately larger than the service's emptiness
        /// probe: a 14-byte "mp3" is not a small sentence, it is a broken one.
        /// </summary>
        private static readonly string RenderedSentence = string.Concat(Enumerable.Repeat("0123456789", 400));

        [Fact]
        public async Task Synthesizing_ReturnsTheAudioAndItsContentType()
        {
            var service = BuildService(_ => Audio(HttpStatusCode.OK, "audio/mpeg", RenderedSentence));

            var result = await service.SynthesizeAsync("Você tem 4 parcelas vencidas.");

            Assert.True(result.Success);
            Assert.Equal("audio/mpeg", result.ContentType);

            await using var stream = result.Audio!;
            Assert.Equal(RenderedSentence, await new StreamReader(stream).ReadToEndAsync());
        }

        [Fact]
        public async Task Synthesizing_SendsTheVoiceAndFormatTheSettingsAskFor()
        {
            string? body = null;
            var service = BuildService(request =>
            {
                body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return Audio(HttpStatusCode.OK, "audio/mpeg", RenderedSentence);
            });

            await service.SynthesizeAsync("Bom dia.");

            using var payload = JsonDocument.Parse(body!);
            Assert.Equal("tts-1", payload.RootElement.GetProperty("model").GetString());
            Assert.Equal("pt_BR-jeff-medium", payload.RootElement.GetProperty("voice").GetString());
            Assert.Equal("mp3", payload.RootElement.GetProperty("response_format").GetString());
            Assert.Equal("Bom dia.", payload.RootElement.GetProperty("input").GetString());
        }

        [Fact]
        public async Task Synthesizing_SendsTheFigureAsWords_NotAsDigits()
        {
            string? body = null;
            var service = BuildService(request =>
            {
                body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return Audio(HttpStatusCode.OK, "audio/mpeg", RenderedSentence);
            });

            await service.SynthesizeAsync("Você tem 4 parcelas vencidas, somando R$ 9.226,24.");

            using var payload = JsonDocument.Parse(body!);
            var input = payload.RootElement.GetProperty("input").GetString();

            // Piper reads what it is given: "R$ 9.226,24" comes out as symbols and digits, which is
            // the one thing the listener called in to hear.
            Assert.Contains("nove mil duzentos e vinte e seis reais e vinte e quatro centavos", input);
            Assert.DoesNotContain("R$", input);
        }

        [Fact]
        public async Task Synthesizing_WhenTheServerAnswers200WithNoAudio_IsAFailure()
        {
            // Observed on 2026-09-14: Piper answers 200 with a bare 45-byte ID3 header when it cannot
            // load the voice. Reporting that as success plays silence and the client never falls back
            // to the device's voice — the exact failure this whole path exists to remove.
            var service = BuildService(_ => Audio(HttpStatusCode.OK, "audio/mpeg", new string('\0', 45)));

            var result = await service.SynthesizeAsync("Bom dia.");

            Assert.False(result.Success);
            Assert.Null(result.Audio);
        }

        [Fact]
        public async Task Synthesizing_HandsOverEveryByte_IncludingTheOnesItLookedAt()
        {
            // The emptiness check reads the first bytes, so this pins that it puts them back: a
            // sentence missing its first 256 bytes is a sentence that starts mid-word.
            var body = string.Concat(Enumerable.Repeat("0123456789", 100));
            var service = BuildService(_ => Audio(HttpStatusCode.OK, "audio/mpeg", body));

            var result = await service.SynthesizeAsync("Bom dia.");

            await using var stream = result.Audio!;
            Assert.Equal(body, await new StreamReader(stream).ReadToEndAsync());
        }

        [Fact]
        public async Task Synthesizing_WhenTheServerRefuses_IsATypedFailureNotAnException()
        {
            var service = BuildService(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("boom"),
            });

            var result = await service.SynthesizeAsync("Bom dia.");

            Assert.False(result.Success);
            Assert.Null(result.Audio);
            Assert.NotNull(result.Error);
        }

        [Fact]
        public async Task Synthesizing_WhenTheServerIsUnreachable_IsATypedFailureNotAnException()
        {
            var service = BuildService(_ => throw new HttpRequestException("connection refused"));

            var result = await service.SynthesizeAsync("Bom dia.");

            Assert.False(result.Success);
            Assert.Contains("indisponível", result.Error);
        }

        [Fact]
        public async Task Synthesizing_WhenItTimesOut_IsATypedFailureNotAnException()
        {
            // HttpClient surfaces its own timeout as TaskCanceledException with no cancellation of
            // ours behind it. Letting that through would 500 the endpoint instead of 503-ing it.
            var service = BuildService(_ => throw new TaskCanceledException("timeout"));

            var result = await service.SynthesizeAsync("Bom dia.");

            Assert.False(result.Success);
            Assert.NotNull(result.Error);
        }

        [Fact]
        public async Task Synthesizing_WhenTheCallerCancels_Throws()
        {
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();

            var service = BuildService(_ => Audio(HttpStatusCode.OK, "audio/mpeg", RenderedSentence));

            // A listener who hung up is not a failure to report — it is a turn nobody is waiting for.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => service.SynthesizeAsync("Bom dia.", cancellation.Token));
        }

        [Fact]
        public async Task Synthesizing_WhenVoiceIsDisabled_NeverReachesTheServer()
        {
            var called = false;
            var service = BuildService(
                _ => { called = true; return Audio(HttpStatusCode.OK, "audio/mpeg", RenderedSentence); },
                voiceEnabled: false);

            var result = await service.SynthesizeAsync("Bom dia.");

            Assert.False(result.Success);
            Assert.False(called);
        }

        [Fact]
        public async Task Synthesizing_Nothing_NeverReachesTheServer()
        {
            var called = false;
            var service = BuildService(_ =>
            {
                called = true;
                return Audio(HttpStatusCode.OK, "audio/mpeg", RenderedSentence);
            });

            var result = await service.SynthesizeAsync("   ");

            Assert.False(result.Success);
            Assert.False(called);
        }

        [Fact]
        public async Task Synthesizing_DoesNotReadTheAudioBeforeHandingItOver()
        {
            // The whole point of the path: the answer is still upstream when the controller gets the
            // stream. The emptiness probe reads the first bytes and nothing more — a service that
            // pulled the whole sentence here is one refactor away from caching it.
            var body = new WatchedStream(RenderedSentence);
            var content = new StreamContent(body);
            content.Headers.ContentType = MediaTypeHeaderValue.Parse("audio/mpeg");

            var service = BuildService(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = content });

            var result = await service.SynthesizeAsync("Bom dia.");

            Assert.True(result.Success);
            Assert.True(body.BytesRead <= 256, $"read {body.BytesRead} bytes before handing over");
            Assert.True(body.BytesRead < RenderedSentence.Length);

            await using var stream = result.Audio!;
            var delivered = await new StreamReader(stream).ReadToEndAsync();

            // And every byte still arrives, probe included.
            Assert.Equal(RenderedSentence, delivered);
        }

        [Fact]
        public async Task DisposingTheAudio_AlsoReleasesTheUpstreamResponse()
        {
            // The stream outlives the method that created it, so it carries the response with it.
            // Without that the connection leaks on every sentence the assistant speaks.
            var body = new WatchedStream(RenderedSentence);
            var content = new StreamContent(body);
            content.Headers.ContentType = MediaTypeHeaderValue.Parse("audio/mpeg");

            var service = BuildService(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = content });

            var result = await service.SynthesizeAsync("Bom dia.");
            await result.Audio!.DisposeAsync();

            Assert.True(body.WasDisposed);
        }

        private static HttpResponseMessage Audio(HttpStatusCode status, string contentType, string body)
        {
            var content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(body)));
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);

            return new HttpResponseMessage(status) { Content = content };
        }

        private static AiVoiceService BuildService(
            Func<HttpRequestMessage, HttpResponseMessage> handler, bool voiceEnabled = true)
        {
            var client = new HttpClient(new StubHandler(handler))
            {
                BaseAddress = new Uri("http://piper.local"),
            };

            var settings = Options.Create(new AiSettings
            {
                VoiceEnabled = voiceEnabled,
                VoiceModel = "tts-1",
                VoiceName = "pt_BR-jeff-medium",
                VoiceFormat = "mp3",
            });

            return new AiVoiceService(client, settings);
        }

        /// <summary>
        /// A body that records whether anybody pulled the bytes, and whether it was disposed.
        /// Deliberately a <see cref="Stream"/> behind <see cref="StreamContent"/> rather than a
        /// custom <see cref="HttpContent"/>: the base class's own <c>ReadAsStreamAsync</c> buffers
        /// into memory first, so a custom content would report "read" no matter what the service
        /// did, and the test would prove nothing.
        /// </summary>
        private sealed class WatchedStream : MemoryStream
        {
            public WatchedStream(string body) : base(Encoding.UTF8.GetBytes(body))
            {
            }

            public int BytesRead { get; private set; }

            public bool WasDisposed { get; private set; }

            /// <summary>
            /// Counted here only. <see cref="MemoryStream.ReadAsync(Memory{byte}, CancellationToken)"/>
            /// forwards to this overload for an array-backed buffer, so counting in both overrides
            /// tallies every byte twice — which is how this test first "found" a read that never
            /// happened.
            /// </summary>
            public override int Read(byte[] buffer, int offset, int count)
            {
                var read = base.Read(buffer, offset, count);
                BytesRead += read;

                return read;
            }

            protected override void Dispose(bool disposing)
            {
                WasDisposed = true;
                base.Dispose(disposing);
            }
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

            public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();

                return Task.FromResult(_handler(request));
            }
        }
    }
}
