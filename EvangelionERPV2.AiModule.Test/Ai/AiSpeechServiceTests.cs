using EvangelionERPV2.AiModule.Application.Configs;
using EvangelionERPV2.AiModule.Application.Services;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text;

namespace EvangelionERPV2.AiModule.Test.Ai
{
    public class AiSpeechServiceTests
    {
        [Fact]
        public async Task Transcribing_ReturnsWhatWasSaid()
        {
            var service = BuildService(HttpStatusCode.OK, "{\"text\":\"  quanto eu tenho a receber?  \"}");

            var result = await service.TranscribeAsync(Audio(), "audio/webm", "utterance.webm");

            Assert.True(result.Success);
            Assert.Equal("quanto eu tenho a receber?", result.Transcript.Text);
            Assert.True(result.Transcript.HasSpeech);
        }

        [Fact]
        public async Task Transcribing_Silence_ComesBackWithoutSpeechRatherThanFailing()
        {
            // Whisper answers 200 with an empty string for a clip that is only room noise. That is
            // not an error — it is a turn the client must simply not send.
            var service = BuildService(HttpStatusCode.OK, "{\"text\":\"\"}");

            var result = await service.TranscribeAsync(Audio(), "audio/webm", "utterance.webm");

            Assert.True(result.Success);
            Assert.False(result.Transcript.HasSpeech);
        }

        [Fact]
        public async Task Transcribing_AsksForTheVadFilterAndTheVocabularyHint()
        {
            string? body = null;
            var service = BuildService(
                request =>
                {
                    body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                    return Respond(HttpStatusCode.OK, "{\"text\":\"oi\"}");
                });

            await service.TranscribeAsync(Audio(), "audio/webm", "utterance.webm");

            // Measured 2026-09-14 on noisy clips: without the prompt, this system's own words come
            // back as nonsense — "estoque crítico hoje" as "toque, que de cor".
            Assert.Contains("vad_filter", body);
            Assert.Contains("estoque crítico", body);
        }

        [Fact]
        public async Task Transcribing_WithTheVadFilterOff_DoesNotAskForIt()
        {
            string? body = null;
            var service = BuildService(
                request =>
                {
                    body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                    return Respond(HttpStatusCode.OK, "{\"text\":\"oi\"}");
                },
                vadFilter: false);

            await service.TranscribeAsync(Audio(), "audio/webm", "utterance.webm");

            Assert.DoesNotContain("vad_filter", body);
        }

        [Fact]
        public async Task Transcribing_ARepetitionLoop_ComesBackAsNothingSaid()
        {
            // What Whisper returns for silence: one phrase until it runs out of room. Passed on, it
            // becomes a question the person never asked and stays in their history as their words.
            var loop = string.Concat(Enumerable.Repeat("O que é que é ", 48));
            var service = BuildService(HttpStatusCode.OK, "{\"text\":\"" + loop + "\"}");

            var result = await service.TranscribeAsync(Audio(), "audio/webm", "utterance.webm");

            Assert.True(result.Success);
            Assert.False(result.Transcript.HasSpeech);
        }

        [Fact]
        public async Task Transcribing_WhenTheServerRefuses_IsATypedFailureNotAnException()
        {
            var service = BuildService(HttpStatusCode.BadRequest, "nope");

            var result = await service.TranscribeAsync(Audio(), "audio/webm", "utterance.webm");

            Assert.False(result.Success);
            Assert.NotNull(result.Error);
            Assert.False(result.Transcript.HasSpeech);
        }

        [Fact]
        public async Task Transcribing_WhenTheServerIsUnreachable_IsATypedFailureNotAnException()
        {
            var service = BuildService(_ => throw new HttpRequestException("connection refused"));

            var result = await service.TranscribeAsync(Audio(), "audio/webm", "utterance.webm");

            Assert.False(result.Success);
            Assert.Contains("indisponível", result.Error);
        }

        [Fact]
        public async Task Transcribing_WhenSpeechIsDisabled_NeverReachesTheServer()
        {
            var called = false;
            var service = BuildService(
                _ => { called = true; return Respond(HttpStatusCode.OK, "{\"text\":\"oi\"}"); },
                speechEnabled: false);

            var result = await service.TranscribeAsync(Audio(), "audio/webm", "utterance.webm");

            Assert.False(result.Success);
            Assert.False(called);
        }

        [Fact]
        public async Task Transcribing_SendsTheModelAndLanguageTheSettingsAskFor()
        {
            string? body = null;
            var service = BuildService(
                request =>
                {
                    body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                    return Respond(HttpStatusCode.OK, "{\"text\":\"oi\"}");
                });

            await service.TranscribeAsync(Audio(), "audio/webm", "utterance.webm");

            Assert.Contains("faster-whisper-small", body);
            Assert.Contains("pt", body);
            // json, never verbose_json: no segments, no timings, nothing resembling a recording.
            Assert.Contains("json", body);
            Assert.DoesNotContain("verbose_json", body);
        }

        private static Stream Audio() => new MemoryStream(Encoding.UTF8.GetBytes("not really audio"));

        private static HttpResponseMessage Respond(HttpStatusCode status, string payload) =>
            new(status) { Content = new StringContent(payload) };

        private static AiSpeechService BuildService(HttpStatusCode status, string payload) =>
            BuildService(_ => Respond(status, payload));

        private static AiSpeechService BuildService(
            Func<HttpRequestMessage, HttpResponseMessage> handler,
            bool speechEnabled = true,
            bool vadFilter = true)
        {
            var client = new HttpClient(new StubHandler(handler))
            {
                BaseAddress = new Uri("http://whisper.local"),
            };

            var settings = Options.Create(new AiSettings
            {
                SpeechEnabled = speechEnabled,
                SpeechModel = "Systran/faster-whisper-small",
                SpeechLanguage = "pt",
                SpeechVadFilter = vadFilter,
            });

            return new AiSpeechService(client, settings);
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

            public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(_handler(request));
        }
    }
}
