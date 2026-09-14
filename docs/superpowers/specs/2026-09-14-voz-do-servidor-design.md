# A voz vem do servidor — design

**Status:** implemented on the backend 2026-09-14. Tasks 0–5 done. The voice is
`pt_BR-faber-medium`, chosen by listening (open question 1, answered). Verified end to end through
`POST /api/v1/ai/speak`: 7.05 s of audio for one sentence, empty and over-cap text refused with 400,
600 characters accepted, `no-store` on the response, no temp file in the container, and 429 at the
configured ceiling. Written 2026-09-14.

**Built differently:** the spec has the service hand back the response stream untouched. It now
reads the first 256 bytes before doing so, because Piper answers **200 with a 45-byte ID3 header and
no audio** when the voice file is missing — observed on 2026-09-14, and the response is chunked, so
there is no Content-Length to check instead. Passing that on would have played silence and looked
like success, and the client would never have fallen back: the exact failure this document was
written to remove. The probed bytes are put back in front of the stream; nothing else is buffered.
**Supersedes one decision in:** `2026-09-11-voz-no-agente-design.md` — *"The device speaks; the
server never renders audio"*, and the non-goal *"No server-side TTS"*.
**Leaves untouched:** everything else in that document. Transcription still runs locally, audio of
the **user** is still never stored, writes still need a tapped card, and voice still never confirms
one.

## Why the earlier decision is being overturned

It read well and it was wrong about one platform in a way that matters.

The argument was: the device already has a voice, so use it and there is no audio artifact to keep
out of logs, disk and backups. That holds on four platforms out of five:

| Platform | Where the voice comes from | State |
| --- | --- | --- |
| Windows | SAPI; pt-BR ships with the OS | works untouched |
| macOS | built-in voices | works untouched |
| Android (Expo) | Google TTS engine | works untouched |
| iOS (Expo) | `AVSpeechSynthesizer` | works untouched |
| **Linux desktop** | `speech-dispatcher` | **no voices at all unless installed** |

On a Linux machine without `speech-dispatcher`, `window.speechSynthesis` exists and reports **zero
voices**. The utterance is accepted, nothing is said, and the `onDone` callback never fires — which
parked the client's session in `speaking` forever, so it stopped listening and the conversation
ended after one turn. That specific failure is already fixed in FrontV2 (a watchdog, plus a
`hasVoice()` check), but the fix only makes the silence survivable. It does not make the assistant
talk.

That is the sharp edge. The blunt one matters more for the product: **the voice is different on
every machine.** Quality, accent, and whether pt-BR exists at all vary per device, so the same
question answered on a phone and on the counter PC sounds like two different products, and neither
is a voice anybody chose. "Install a package" is not an instruction to give a counter clerk.

So the server renders the audio. One voice, chosen once, identical on every platform, with no
per-machine setup.

## Decisions

### Piper behind an OpenAI-compatible HTTP server, beside Whisper

The transcription server is already `fedirz/faster-whisper-server`, reached over plain HTTP at
`/v1/audio/transcriptions`. The synthesis server mirrors it exactly: OpenAI's `/v1/audio/speech`
shape, a `voice` profile in Compose, one endpoint string in settings, no volumes for audio.

`ghcr.io/matatonic/openedai-speech-min` is the image — Piper only, no XTTS, **1.22 GB pulled and
verified available** on 2026-09-14. Piper runs on CPU at far faster than real time, which matters
because the GPU is already holding `qwen3:14b` at ~9 GB of 16 GB; a synthesis model fighting it
would turn a ten-second answer into forty.

The seam is an interface, as with transcription, so swapping Piper for a hosted voice later is one
registration — with the LGPD conversation that choice reopens.

### The device's voice stays, as a fallback

Server first, device second. Not because the device's voice is good, but because the alternative is
a voice mode with no sound when the container is down or `VoiceEnabled` is false, and because the
native apps already have a working voice that costs nothing to keep.

The client tries the endpoint once per session and remembers the answer; a 404, a 503 or a network
failure demotes it to the device for the rest of that session rather than paying for a failed
request on every sentence.

### The audio is streamed and never written down

This is what made the original non-goal defensible, and it is the part that has to be got right
rather than argued away.

Three places would store it by accident:

1. **Buffering the response.** Reading the synthesis response into a `byte[]` before returning it
   puts a whole answer's audio in memory and invites someone to add a cache later. The controller
   copies the upstream `Stream` to `Response.Body` and returns `FileStreamResult` / `PushStreamContent`
   shaped code — never `File(bytes, …)`, never a temp path.
2. **Response caching.** No `ResponseCache`, no output caching, no ETag on this route. An audio
   response cached on disk by middleware is exactly the artifact this is avoiding.
3. **The container.** No volume mount for audio; the model volume holds voices only.

And the two that must stay clean: Serilog logs the character count and the latency, never the text
and never the bytes; no entity anywhere gains an audio column.

**The LGPD posture is different from transcription, and weaker on purpose.** Input audio is the
user's own voice — biometric, a category this system otherwise never handles, which is why it is
destroyed in flight. Output audio is a machine reading text the database already stores. It carries
the same business data the text does and no new category of personal data. It is still not stored,
because there is no reason to store it and every reason not to, but the rule here is hygiene rather
than the hard constraint that shapes the upload path.

### One request per sentence, not per answer

The client already splits an answer into sentences so speaking can begin before the answer is
finished. That stays: each sentence is its own request, so the first word is heard while the rest
is still being synthesised.

This also bounds every request naturally. `MaxSpeechChars` (600) is a backstop for a sentence that
is really a paragraph, not the working limit.

### No cache, deliberately

The obvious optimisation — key the audio by the text hash and serve repeats from disk — is refused.
It reintroduces exactly the artifact this design is avoiding, to save a synthesis that costs Piper
a fraction of a second on CPU. If synthesis ever becomes the bottleneck, the answer is a second
Piper process, not a spool directory.

## Architecture

```
FrontV2 (Expo: web + native)
  answer text ──► toSentences() ──► for each sentence:
        │
        ├──► POST /api/v1/ai/speak { text }        ← audio/mpeg or audio/wav, streamed
        │         │
        │         └── on 404 / 503 / network error, once per session:
        │             fall back to the device voice (expo-speech / speechSynthesis)
        ▼
     play, then speak the next sentence
        │
        └── barge-in stops playback and abandons the queue

EvangelionERPV2.Web
  POST /api/v1/ai/speak
        │  [Authorize rbac:ai.chat]  [RequestSizeLimit]  rate limited
        │
        ├──► IAiVoiceService ──► Piper server (local, :9100) /v1/audio/speech
        │◄── Stream (audio/mpeg)
        ▼
     copied to Response.Body — not buffered, not cached, not written to disk
```

## Layer breakdown

Everything lands in the existing `Ai` module. **No new entity, no migration, no new permission** —
voice output reaches `ai.chat` like the rest of the assistant.

**`EvangelionERPV2.Ai.Application`**
- `Interface/IAiVoiceService.cs` — `Task<AiVoiceResult> SynthesizeAsync(string text, CancellationToken)`;
  `AiVoiceResult(Stream? Audio, string ContentType, long LatencyMs, string? Error)` with the same
  `Success`/`Failed` shape `AiTranscriptionResult` already uses, so the two read alike.
- `Services/AiVoiceService.cs` — posts `{ model, input, voice, response_format }` to
  `/v1/audio/speech`, returns the response stream **without reading it**. Maps a non-success status
  and a timeout to a typed failure rather than an exception.
- `Configs/AiSettings.cs` — the `Voz` region gains: `VoiceEnabled` (false), `VoiceEndpoint`
  (`http://localhost:9100`), `VoiceModel` (`tts-1`), `VoiceName` (`pt_BR-faber-medium`),
  `VoiceFormat` (`mp3`), `VoiceTimeoutSeconds` (20), `MaxSpeechChars` (600).
- `DI/AiIoC.cs` — `IAiVoiceService` on a named `HttpClient`, registered only when `VoiceEnabled`,
  exactly as `IAiSpeechService` is.

**`EvangelionERPV2.Web`**
- `Controllers/AiController.cs` — `POST ai/speak`, `[Authorize(Policy = "rbac:" + RbacPermissions.Ai.Chat)]`,
  503 when `!Enabled || !VoiceEnabled`, 400 on empty text or text over `MaxSpeechChars`, and on
  success copies the upstream stream to the response with its content type. No `File(byte[], …)`.
- `Program.cs` — an `AspNetCoreRateLimit` rule for `/api/v1/ai/speak`. A session calls it once per
  sentence, so the ceiling is higher than `/transcribe`'s.
- `Security/RbacControllerPolicyCoverageTests` — a row for the new action.

**`EvangelionERPV2.Shared`**
- `DTOs/AiChatDTOs.cs` — `AiSpeakRequestDTO { string Text }`. There is no response DTO: the
  response is audio.

**Compose** — a `piper` service on the existing `voice` profile:

```yaml
  piper:
    profiles: [voice]
    image: ${PIPER_IMAGE:-ghcr.io/matatonic/openedai-speech-min:latest}
    container_name: evangelionerpv2_piper
    # Not optional — see "A dead Piper is permanent silence" below.
    restart: unless-stopped
    environment:
      TTS_HOME: /app/voices
    ports:
      - "${PIPER_HOST_PORT:-9100}:8000"
    volumes:
      - piper_voices:/app/voices        # voices only; never audio
    networks:
      - custom_network
```

plus `EVA_AI_VOICE_ENABLED`, `EVA_AI_VOICE_ENDPOINT` and `EVA_AI_VOICE_NAME` in `.env.local`,
mirroring the speech ones, and `AiSettings__Voice*` on the web service.

**FrontV2** — built and working against this endpoint. See `.specs/features/ai-assistant/spec.md`:
`erpService.synthesizeSpeech()`, the server-first path in `utils/speech.ts`, the expiring demotion
to the device voice, and the "Test the voice" control that makes a broken deployment one press to
find instead of a conversation.

### A dead Piper is permanent silence

**Observed 2026-09-14, and the reason this section exists.** The container served four requests and
then exited with code 255 — not OOM-killed, nothing in its own logs, uvicorn's last line a normal
`200 OK`. The service is declared with the default `restart: no`, so nothing brought it back. From
that moment `POST /ai/speak` answered 503 "O serviço de voz está indisponível", and the assistant
stopped talking. It took a browser trace to find, because everything upstream looked healthy and the
client had already failed over.

Two things follow, one on each side:

- **`restart: unless-stopped` on the service.** A synthesis process that dies is a transient fault,
  not a reason to take voice out of the product until somebody runs `docker start` by hand. This is
  the whole fix on the backend side and it is one line.
- **The client retries rather than giving up.** The demotion described above was originally for the
  rest of the session, which turned a container crash into silence until the page was reloaded — on
  a machine whose device voice is nothing at all, that is silence full stop. It now expires after a
  minute, so a restarted Piper is picked up on the next sentence without the user doing anything.

Neither half is sufficient alone: without the restart policy the retry has nothing to find, and
without the retry a restarted container is ignored for as long as the page stays open.

**Worth knowing why it died, separately.** Exit 255 with an empty log is not diagnosed, only
survived. If it recurs, `docker events` and the container's stderr around the fourth request are
where to look; the suspicion is the image's own worker handling rather than resource pressure, since
the host had 12 GB free and `OOMKilled` was false.

## Tasks

Ordered so the tree stays green at every commit.

0. **`restart: unless-stopped` on the `piper` service.** Done. See "A dead Piper is permanent
   silence".
   *Verify:* **not** `docker kill` — Docker treats that as a manual stop and deliberately declines to
   restart, so it proves nothing (observed: `restarts=0`, container left exited). Kill the server
   *inside* the container instead, which is how it actually died: find the `python speech.py` pid
   under `/proc` and `docker exec evangelionerpv2_piper sh -lc 'kill -9 <pid>'`. Measured
   2026-09-14: `RestartCount` 0 → 1, status back to `running` unaided, and a sentence synthesised
   0.64 s later with no manual `docker start`.
1. **Settings and DI.** The `Voice*` fields, `AiIoC` behind `VoiceEnabled`, the Compose service.
   *Verify:* `dotnet build`; the stack starts with voice disabled and logs nothing new.
2. **`IAiVoiceService` + `AiVoiceService`.**
   *Verify:* `AiModule.Test` — a successful synthesis returns a stream and a content type; a 500
   from the server becomes a typed failure, not an exception; a timeout does the same.
3. **`POST /ai/speak`.** RBAC row, caps, 503 when disabled, streamed response.
   *Verify:* 401 unauthenticated; empty text 400; over `MaxSpeechChars` 400; disabled 503; a real
   sentence returns playable audio; **no file appears under `Path.GetTempPath()` during the call**;
   the response carries no cache headers.
4. **Rate limit for the audio endpoint.**
   *Verify:* the rule is in config; a burst gets 429.
5. **A pt-BR voice in the image.** Pick and pin one (`pt_BR-faber-medium` is the starting bet) and
   document how to add it to the volume.
   *Verify:* one sentence synthesised end to end, listened to by a human. This is the acceptance
   test that matters: a robotic voice that mangles "R$ 9.226,24" fails it.

## Risks

- **Latency in front of the first word.** Piper on CPU is fast, but the request adds a round trip
  per sentence. If the first word lands more than ~400 ms after the text, prefer synthesising
  sentence *n+1* while sentence *n* plays.
- **How numbers and currency are read.** "R$ 9.226,24" must come out as reais, not as digits and
  punctuation. This is a property of the voice and the text, and it is the most likely reason to
  change either. The spoken-answer prompt rule from the previous design already asks for numbers
  written for speech; this is where that pays off.
- **Container size and cold start.** 1.22 GB pulled, plus voices in a volume. A first request that
  waits on a model load is a bad first impression; warm it on container start.
- **Concurrency.** One Piper process serialises requests. Two tills speaking at once is the first
  scenario that shows it.
- **The fallback masking a broken deployment.** A demoted client sounds slightly different and
  nobody files a bug. The endpoint failing should be visible in logs, and the client says which
  voice it is using in the call screen. This one stopped being hypothetical on the day it was
  written: Piper died, the client failed over exactly as designed, and the only visible symptom was
  an assistant that had gone quiet. A restart policy plus an expiring demotion is the answer;
  see above.
- **Piper exiting on its own.** Once, at exit 255, with no log and no memory pressure. Undiagnosed,
  now survivable. If it becomes regular it is worth a proper look rather than a longer restart
  policy.

## Non-goals

- **No cache and no stored audio.** See above; this is the design.
- No audio download of an answer, no "export this reply as a file".
- No voice cloning, no per-user voices, no speaker identification.
- No change to the transcription path, which stays local and still destroys the clip in flight.
- **Still no confirming a write by voice.** Unchanged and non-negotiable: the card is tapped.

## Open questions

1. ~~**Which pt-BR Piper voice.**~~ Answered: `pt_BR-faber-medium`, after hearing the same sentence —
   a figure read as currency — through all four. `jeff` was the first pick and was changed on a
   second listen. All four pt-BR voices Piper publishes (`faber`, `jeff`, `cadu`, `edresson`) are
   mapped in `docker/piper/voice_to_speaker.yaml` and downloaded into the `piper_voices` volume, so
   revisiting this is `EVA_AI_VOICE_NAME` and a restart — nothing to edit, nothing to fetch.
2. **Warm-up.** Synthesise a throwaway word on container start, or accept a slow first sentence?
3. **Does the fallback deserve to be visible?** A one-line note in the call screen saying the
   device voice is in use is honest, and is also a thing nobody wants to see every day.
4. **Rate limit shape.** Per sentence is much chattier than per question; the ceiling should come
   from a real session rather than a guess.
