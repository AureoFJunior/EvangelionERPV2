using EvangelionERPV2.AiModule.Application.Configs;
using EvangelionERPV2.AiModule.Application.Interface;
using Microsoft.Extensions.Options;
using Serilog;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace EvangelionERPV2.AiModule.Application.Services
{
    /// <summary>
    /// Speech-to-text against a local Whisper server, over the OpenAI-compatible
    /// <c>/v1/audio/transcriptions</c> shape that both faster-whisper-server and whisper.cpp speak.
    /// </summary>
    /// <remarks>
    /// The audio is streamed straight from the request body to the transcription server: it is
    /// never buffered to a file, never logged, and never returned. Only the text comes back.
    /// </remarks>
    public sealed class AiSpeechService : IAiSpeechService
    {
        private readonly HttpClient _client;
        private readonly AiSettings _settings;

        public AiSpeechService(HttpClient client, IOptions<AiSettings> settings)
        {
            _client = client;
            _settings = settings.Value;
        }

        public async Task<AiTranscriptionResult> TranscribeAsync(
            Stream audio,
            string contentType,
            string fileName,
            CancellationToken cancellation = default)
        {
            if (!_settings.SpeechEnabled)
                return AiTranscriptionResult.Failed("A transcrição de voz está desativada.");

            var stopwatch = Stopwatch.StartNew();

            try
            {
                using var content = new MultipartFormDataContent();
                using var file = new StreamContent(audio);
                file.Headers.ContentType = MediaTypeHeaderValue.Parse(
                    string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);

                content.Add(file, "file", fileName);
                content.Add(new StringContent(_settings.SpeechModel), "model");
                content.Add(new StringContent(_settings.SpeechLanguage), "language");
                // json, not verbose_json: we want the words and nothing else — no segments, no
                // timings, nothing that could be mistaken for a recording of the utterance.
                content.Add(new StringContent("json"), "response_format");

                using var response = await _client.PostAsync("/v1/audio/transcriptions", content, cancellation);

                if (!response.IsSuccessStatusCode)
                {
                    Log.Logger.Warning(
                        "Speech transcription refused. Status={Status}", (int)response.StatusCode);

                    return AiTranscriptionResult.Failed("Não consegui entender o áudio. Tente de novo.");
                }

                var payload = await response.Content.ReadAsStringAsync(cancellation);
                var text = ReadText(payload);

                stopwatch.Stop();

                return AiTranscriptionResult.Ok(
                    new AiTranscript(text, _settings.SpeechModel, stopwatch.ElapsedMilliseconds));
            }
            // Only the caller hanging up is a cancellation. HttpClient reports its own timeout the
            // same way, with no token of ours behind it, and rethrowing that 500s the endpoint over a
            // slow Whisper instead of answering 503.
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Never the message: it can carry the request, and the request is someone's voice.
                Log.Logger.Error("Speech transcription failed. ErrorType={ErrorType}", ex.GetType().Name);

                return AiTranscriptionResult.Failed("O serviço de voz está indisponível.");
            }
        }

        private static string ReadText(string payload)
        {
            try
            {
                using var document = JsonDocument.Parse(payload);

                return document.RootElement.TryGetProperty("text", out var text)
                    ? (text.GetString() ?? string.Empty).Trim()
                    : string.Empty;
            }
            catch (JsonException)
            {
                return string.Empty;
            }
        }
    }
}
