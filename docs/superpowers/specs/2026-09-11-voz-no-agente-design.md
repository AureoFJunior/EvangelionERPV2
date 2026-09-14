# Voz no agente do ERP — design

**Status:** design, not yet implemented. Written 2026-09-11.
**Work item:** AB#33
**Builds on:** `2026-09-09-agente-do-erp-design.md` (the agent itself, already shipped)

## Context

The agent answers typed questions today. The gap this closes is the counter and the warehouse:
someone with their hands on a product who wants to ask "quanto tenho a receber?" without typing,
and hear the answer without looking.

**The experience to match is the one everybody already has**, from ChatGPT and Claude: press once,
talk normally, stop talking, and it answers out loud — then it is listening again, without being
asked. No press-and-hold, no "now press send", no reading a transcript back before anything
happens. From the user's side it is a conversation. Everything in this document is about making
that work against a local model, on this data, without keeping a second of audio.

One constraint decides most of the engineering: **the audio is never kept.** What is spoken is
turned into text and gone. Only the transcript survives, in the same `AiMessage` row a typed
question already produces.

That is not politeness. A voice recording identifies the person who spoke it — under LGPD it is
personal data of a kind the rest of this system never handles, and keeping it would put a new
category of sensitive data into a database whose retention story was written for text. Not storing
it is cheaper than protecting it.

## Decisions

### Transcription runs locally, beside Ollama — not in the browser

The obvious shortcut is the browser's own `SpeechRecognition`. It is rejected on the same ground
the model itself was chosen: **Chrome's implementation streams the audio to Google's servers**, and
Safari's to Apple's. A feature whose premise is "no tenant data leaves the machine" cannot have its
microphone path quietly ship a customer's name to a third party. The browser API would also be
unavailable in the Expo native builds, so it solves the easy half of the problem only.

So speech-to-text is a local Whisper server (`whisper.cpp` or `faster-whisper`), reached over HTTP
exactly like Ollama, profile-gated in Compose, and configured by one endpoint string. Same shape,
same reversibility: the seam is an interface, and swapping in a hosted transcription service later
is one registration — with the LGPD conversation that choice reopens.

### The device speaks; the server never renders audio

Text-to-speech is the *client's* job: `expo-speech` on native, `window.speechSynthesis` on web.
The server sends the same text it already sends and never produces an audio artifact.

This is the cheapest way to honour the constraint, because there is nothing to delete. A
server-side TTS would generate a file per answer, and every one of them would be a thing to keep
out of logs, out of disk, out of backups. The device already has a voice. Use it.

### A hands-free session, and the write gate is what makes that safe

Press the mic once and a **session** starts. It listens, detects when the sentence ended, sends,
speaks the answer, and listens again — until the person presses stop. The transcript appears on
screen as it is understood, but nobody has to approve it.

An earlier draft of this design made the person review each transcript before it was sent, on the
grounds that the agent can propose writes and a misheard "cadastra" is dangerous. That is the wrong
place to put the guard, and it would have made voice mode feel like dictation software instead of a
conversation.

The guard already exists and is better: **a write never happens without a confirmation card being
tapped.** A misheard question produces, at worst, a card nobody taps — and cards expire on their
own. Reads are harmless by construction. So auto-send costs nothing that the confirm gate was not
already paying for, and the interaction gets to feel like the thing people expect.

What follows from that, and is non-negotiable: **voice never confirms a write.** The card is read
with the eyes and tapped with a finger. "Sim, pode confirmar" does nothing. The one irreversible
step in the whole feature stays deliberate, visual and manual.

### The end of a sentence is detected, not declared

A session needs to know when the person stopped talking. That is voice-activity detection: watch
the input level, and when it stays below a threshold for ~1 second after speech was heard, close
the utterance and transcribe it.

On web this is the Web Audio API (`AnalyserNode` RMS) over the `getUserMedia` stream; on native it
is `expo-av` recording status metering. A hosted VAD model (Silero) is better at noisy rooms and is
the upgrade if a shop floor defeats the simple version — but an energy threshold with a
silence timeout is what ships first, because it is fifty lines and no dependency.

Two parameters, both configurable and both worth tuning with real users: how long silence must last
to end a turn (start at 1000 ms), and how long a single utterance may run before it is cut off
(`MaxAudioSeconds`, 60).

### Barge-in: talking over it stops it

While the answer is being spoken the microphone stays open. Speech onset does three things at once:
stop the TTS, abort any in-flight turn, and start capturing the new utterance.

This is the difference between a voice mode people keep using and one they abandon — being unable
to interrupt a wrong forty-second answer is intolerable. It also brings the one genuinely hard
problem in this document: the device hears its own voice. Browser `getUserMedia` with
`echoCancellation: true` handles most of it; on native the recorder must be configured for
voice-chat mode. Where echo cancellation is unavailable or weak, the fallback is to gate the mic
while speaking and accept that barge-in needs a tap. This is the piece most likely to need a second
pass after real use.

### The answer streams, so it can be spoken as it arrives

Today the final answer is produced whole and then chunked for display — `AiTextChunker` carries a
note saying exactly that, and names the upgrade: re-issue the final turn through
`GetStreamingResponseAsync` once no tool calls remain.

Voice is the reason to do it. A spoken answer that begins seven seconds after the question feels
broken; one that begins as soon as the first sentence exists feels like a conversation. The client
speaks sentence by sentence as `AiChunk` events arrive on the hub that already exists.

Tool turns stay non-streaming and keep announcing themselves through `AiStatus` ("consultando
pedidos…"), which in a voice session becomes a short earcon or a spoken filler rather than silence.

### A spoken answer is a different answer

A markdown table read aloud is unusable. A voice turn sets `spoken: true`, which appends one rule to
the system prompt: answer in one or two sentences, no tables, no lists, numbers rounded for speech
("nove mil duzentos e vinte e seis reais"). The screen still receives the same text, so nothing is
hidden from someone who wants to read it — it is simply written to be heard.

Without this, voice mode reads punctuation for fifteen seconds and everybody turns it off.

### Audio is never written down — and that needs more than good intentions

Three places would store it by accident:

1. **ASP.NET Core spools multipart uploads to disk.** The default form binding buffers a file over
   `MemoryBufferThreshold` (64 KB) into a temp file. Binding the upload as `IFormFile` therefore
   writes the audio to disk, which is exactly what this feature promises not to do. The endpoint
   reads the body with `MultipartReader` and streams it to the transcription client, or raises the
   memory threshold above the hard cap so the spool never happens. This is the single most
   important implementation note in this document.
2. **The client's recorder writes a file.** `expo-av` records to a temp path on native and produces
   a `Blob` on web. Both are deleted the moment the upload returns — `FileSystem.deleteAsync` and
   `URL.revokeObjectURL` — in a `finally`, so a failed upload deletes it too. In a session this
   happens once per utterance, so the delete cannot be left to teardown.
3. **The transcription container.** No volume mount for audio, no request-body logging.

And two places that must stay clean: Serilog never sees the bytes (this repo logs `ex.GetType().Name`
and never message bodies), and no entity anywhere gains an audio column.

The transcript is stored like any other message and inherits `AiSettings.MessageRetentionDays`.

## Architecture

```
FrontV2 (Expo: web + native)          session state machine
  ┌───────────────────────────────────────────────────────────────┐
  │  idle ──press──► listening ──silence──► transcribing          │
  │                     ▲                        │                │
  │                     │                        ▼                │
  │              speaking ◄──chunks──── thinking (agent turn)     │
  │                     │                                          │
  │                     └── speech onset = barge-in: stop TTS,    │
  │                         abort turn, back to listening          │
  └───────────────────────────────────────────────────────────────┘
        │ utterance (transient audio, never persisted)
        ▼
  POST /api/v1/ai/transcribe   (multipart, MultipartReader, never IFormFile)
        │
        ├──► IAiSpeechService ──► Whisper server (local, :9000)
        │◄── { text }
        ▼
  POST /api/v1/ai/chat { message, spoken: true }      ← unchanged path
        │
  AiAgentService ──► tools ──► final turn streamed
        │
        └──► AiHub: AiStatus / AiChunk / AiDone ──► client speaks each sentence
```

Nothing in that diagram stores audio at any hop.

## Layer breakdown

Everything lands in the existing `Ai` module. No new bounded context, no new module, **no new
entity and no migration** — a transcript is an `AiMessage.Content` like any other.

**`EvangelionERPV2.Ai.Application`**
- `Interface/IAiSpeechService.cs` — `Task<AiTranscript> TranscribeAsync(Stream audio, string contentType, CancellationToken)`; record `AiTranscript(string Text, string Model, long LatencyMs)`.
- `Services/AiSpeechService.cs` — posts the stream to Whisper, maps the response and failures to a typed result. No buffering to disk.
- `Configs/AiSettings.cs` — `SpeechEnabled`, `SpeechEndpoint`, `SpeechModel`, `SpeechLanguage` (`pt`), `SpeechTimeoutSeconds`, `MaxAudioSeconds`, `MaxAudioBytes`, `SilenceMsToEndTurn`.
- `Services/AiSystemPrompt.cs` — the spoken-answer rule, applied only when the turn is spoken.
- `Interface/IAiAgentService.cs` — `AiTurnRequest` gains `bool Spoken`.
- `Services/AiAgentService.cs` — final turn re-issued via `GetStreamingResponseAsync` when no tool calls remain; `AiTextChunker` and its ponytail note retire.
- `DI/AiIoC.cs` — `IAiSpeechService` on a named `HttpClient`, registered only when `SpeechEnabled`.

**`EvangelionERPV2.Web`**
- `Controllers/AiController.cs` — `POST ai/transcribe`, `[Authorize(Policy = "rbac:" + RbacPermissions.Ai.Chat)]`, `[RequestSizeLimit]`, `MultipartReader`, 503 when disabled, 400 on empty or over-long audio.
- `Program.cs` — a tighter `AspNetCoreRateLimit` rule for `/api/v1/ai/transcribe`; a session calls it once per sentence.
- `Security/` — **no new permission.** Voice reaches `ai.chat`; it is not a new capability.

**`EvangelionERPV2.Shared`**
- `DTOs/AiChatDTOs.cs` — `AiTranscriptDTO { Text, LatencyMs }`; `AiChatRequestDTO` gains `Spoken`.

**Compose** — `whisper` service on a `voice` profile, no volumes for audio.

**FrontV2**
- `hooks/useVoiceSession.ts` — the state machine above: permission, VAD, per-utterance upload and delete, auto-send, barge-in, abort, stop.
- `utils/speech.ts` — `speak(text)` / `stop()` over `expo-speech` and `speechSynthesis`, sentence queue, markdown stripped.
- `components/AiAssistant.tsx` — a voice affordance with an unambiguous live state, the running transcript, and a stop control.
- `services/erpService.ts` — `transcribeAudio(blob, signal)`; `askAi` gains `spoken` and an `AbortSignal`.

## Tasks

Ordered so the tree stays green at every commit.

1. **Settings and DI for a speech backend.** `AiSettings` speech fields, `AiIoC` behind `SpeechEnabled`, Compose `voice` profile.
   *Verify:* `dotnet build`; container starts with speech disabled and logs nothing new.
2. **`IAiSpeechService` + `AiSpeechService`.**
   *Verify:* `AiModule.Test` — maps a transcript; empty clip refused; a server error becomes a typed failure, not an exception.
3. **`POST /ai/transcribe`.** `MultipartReader`, caps, 503 when disabled, RBAC row in `RbacControllerPolicyCoverageTests`.
   *Verify:* 401 unauthenticated; oversize refused; disabled 503; a real clip returns text; **no file appears under `Path.GetTempPath()` during the call**.
4. **Spoken answers.** `Spoken` on the request and the turn; one extra system-prompt rule.
   *Verify:* the rule is present only when spoken; `AiAgentServiceTests` — the flag reaches the prompt.
5. **Stream the final turn.** `GetStreamingResponseAsync` when no tool calls remain; retire `AiTextChunker`.
   *Verify:* `AiChunk` events arrive before the turn completes; a tool-using turn still emits `AiStatus` first; existing agent tests stay green.
6. **Rate limit for the audio endpoint.**
   *Verify:* the rule is in config; a burst gets 429.
7. **Client capture with VAD.** `useVoiceSession` up to transcription: permission, silence detection, per-utterance delete in `finally`.
   *Verify:* jest — silence ends the utterance; upload success and failure both delete; denial surfaces a message.
8. **Client speech.** `speak`/`stop`, sentence queue fed by `AiChunk`, markdown stripped.
   *Verify:* jest — a table answer is spoken without pipes; `stop()` cancels mid-queue.
9. **Close the loop.** Auto-send, speak, reopen the mic, stop control.
   *Verify:* jest — a completed utterance sends without a tap and the session returns to listening.
10. **Barge-in.** Speech onset stops TTS, aborts the in-flight turn, starts a new utterance.
    *Verify:* jest — onset during `speaking` cancels the request and the TTS.

Tasks 1–6 are the backend and are useful on their own; 7–10 are the experience.

## Security & LGPD

- **Audio at rest: none.** Enforced at the three points above, and worth a line in the PR
  description so a reviewer checks it rather than assumes it.
- **Audio in logs: none.** The endpoint logs duration and byte count, never content.
- **Transcript** is personal data of the same class as a typed message: `AiMessage`, behind
  `ai.chat`, expiring with `MessageRetentionDays`.
- **Tenant** still comes from the claim. Voice touches nothing in that path.
- **Writes** still require a tapped card. Auto-send is safe *because* of that gate, and the gate is
  the reason this design can be hands-free at all.
- **Consent.** The mic opens on an explicit press and closes on an explicit stop, with a live state
  that cannot be mistaken for idle. No wake word, no background listening, no session that survives
  leaving the screen.

## Configuration

```json
"AiSettings": {
  "SpeechEnabled": false,
  "SpeechEndpoint": "http://host.docker.internal:9000",
  "SpeechModel": "small",
  "SpeechLanguage": "pt",
  "SpeechTimeoutSeconds": 30,
  "MaxAudioSeconds": 60,
  "MaxAudioBytes": 5242880,
  "SilenceMsToEndTurn": 1000
}
```

`SpeechEnabled = false` by default, the same posture as `Enabled`. `EVA_AI_SPEECH_ENABLED` in
`.env.local` mirrors `EVA_AI_ENABLED`.

## Risks

- **Echo.** The hardest problem here. Without working echo cancellation the assistant transcribes
  itself and the session talks to itself. Mitigation is layered: `echoCancellation: true`, native
  voice-chat audio mode, and a fallback that gates the mic while speaking. Budget a second pass.
- **Latency is the whole product.** Record → transcribe (1–3 s) → agent (7–15 s with tools) → first
  spoken word. Streaming the final turn (task 5) is what makes this tolerable; without it the
  feature is technically complete and unpleasant.
- **GPU contention.** `qwen3:14b` already holds ~9 GB of 16 GB. Whisper `small` is ~1 GB and can run
  on CPU; start there. Two models fighting for VRAM turns a 10 s answer into a 40 s one.
- **Container audio formats.** Chrome records `webm/opus`, Safari `mp4`, native `m4a`. The Whisper
  image needs ffmpeg or the feature works in exactly one browser.
- **Noise and false endpointing.** A shop floor will cut sentences short. That is the trigger for
  swapping the energy threshold for Silero VAD, not a reason to delay the simple version.
- **Empty transcripts.** Silence must never become a chat turn.

## Verification

- `dotnet build`, `dotnet test`, `npx tsc --noEmit`, `npx jest` all green.
- Manual, and this is the real test: start a session, ask "quanto eu tenho a receber", hear a
  one-sentence answer, then ask "e a pagar?" without touching anything, and interrupt it halfway.
- **Privacy check, done by hand once:** run a session, then look — `Path.GetTempPath()` on the API
  host, the client's document directory, the Whisper container's filesystem, and the Serilog output.
  Nothing anywhere. This is the acceptance test that matters most.

## Non-goals

- No wake word, no always-on listening.
- No speaker identification, no voice biometrics, no per-voice personalisation.
- No server-side TTS, no audio download of an answer.
- **No confirming a write by voice.** The card is tapped. Deliberately visual, deliberately manual.
- No offline queueing of recordings — if the network is down, so is the agent.

## Open questions

1. **Whisper image.** `faster-whisper` (CTranslate2, OpenAI-compatible HTTP) or `whisper.cpp --server`?
   Both fit the interface; decide by running one pt-BR clip through each and comparing latency and
   ffmpeg support.
2. **Filler while thinking.** Seven silent seconds during tool calls feels broken. A short earcon, a
   spoken "só um instante", or nothing? Cheap to try all three once the loop runs.
3. **Does the spoken answer need its own model call?** Asking for a short spoken form as a second
   call doubles latency. The prompt rule is the cheap bet; measure before spending more.
4. **Retention of the transcript.** 90 days is inherited from text. A spoken question is more likely
   to be careless — worth asking whether voice turns deserve a shorter window.
5. ~~**Where the session lives.**~~ **Answered: inside the existing assistant sheet.** Voice mode
   replaces the log and composer with an orb, a caption and a stop control, and leaving it costs
   nothing because both modes write to the same thread. A dedicated full-screen view buys little
   once the sheet is already the full height of the window on a phone. Built in
   `components/ai/VoiceOrb.tsx`; see the FrontV2 spec at
   `.specs/features/ai-assistant/spec.md` for the state-to-motion mapping and the consent rule
   (the microphone does not open when voice mode does — the orb is the switch).

   Two client facts found while building it, both worth carrying back here: the TTS decision above
   means a browser with no voices installed (Firefox on Linux without speech-dispatcher) accepts an
   utterance, says nothing, and never calls `onDone` — `speak()` needs a watchdog or the session
   parks in `speaking` and stops listening. And voice mode must be gated on microphone
   availability, not speech availability, or browsers that can hear perfectly well show no voice
   control at all.
