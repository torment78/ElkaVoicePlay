# Elka VoicePlay

**Hands-free chat for games**

Hold a button, speak, and let VoicePlay type into your game's chat. Each game can have its own talk button, chat keys and timing. VoicePlay also turns microphone and speaker audio into timestamped transcripts, with optional speaker labels and English translation.

Windows 11 · x64 · local processing · by ElkaSoft

## Download and start

1. Open the [releases page](https://github.com/torment78/ElkaVoicePlay/releases) and download **ElkaVoicePlay-0.6.0-win-x64-small.zip**.
2. Extract the entire ZIP into a folder and run **ElkaVoicePlay.exe**. Keep its Models and Workers folders alongside it.
3. Choose your microphone and language, select a game profile, then Apply settings.
4. Focus the game, hold the talk button, speak and release it. VoicePlay checks the destination when it is ready to send.

The small package includes **Whisper Base** and the **standard speaker model (Classic)**. It works without NVIDIA software, Python, a paid API or an account. This is an **unsigned preview**. Windows may show an unfamiliar-publisher warning.

## Optional downloads and the full setup

Open **Downloads** in the app. Choose an individual extra or click **Install all extras** for the full setup:

| Optional extra | Download | Purpose |
|---|---:|---|
| Whisper Medium | 1.53 GB | Middle-size alternative between Base and Large-v3 |
| Whisper Large-v3 | 3.10 GB | Higher-quality transcription and English translation |
| Nemotron-3 speakers | 106 MB | Alternative speaker identification, up to eight voices per input |
| NVIDIA GPU support | 647 MB | Faster Whisper and Nemotron processing on compatible GPUs |

All downloads come from this repository's versioned GitHub release. VoicePlay checks sizes and SHA-256 checksums, joins Large-v3's two parts, and installs everything in the correct place. Progress, cancellation and retry are built in. Completed parts are reused after an interrupted download. Allow additional temporary disk space during installation.

After downloading, select the model under **Transcribe audio → Models & speakers**. Downloading does not change your selections. **The game model stays separate.** Automatic processing uses CPU until NVIDIA support is installed and a compatible GPU/driver is available. The NVIDIA pack does not install or modify drivers; it requires a CUDA 13-capable NVIDIA driver. Large-v3 works on CPU too, but needs more memory and processing time.

The small ZIP plus **Install all extras** is the full version. The release also contains the individual component assets for verification and archival; use the app to install them rather than manually unpacking them.

## Game profiles

Choose **New** or **Edit / Learn**. Profile name and hold-to-talk stay at the top; **Cancel** and **Save profile** stay at the bottom. Click a card to open its settings:

- **Game & automatic selection:** optional EXE link and process selection.
- **Chat sequence:** open chat, type, send, then optionally close chat.
- **Chat timings:** opening, typing and sending delays, plus button press duration.
- **Chat-switch command:** double-tap the talk button, or use a separate learned button, to send up to three keys.
- **Input & keyboard protection:** text insertion and optional keyboard blocking while sending.

Learn supports physical keyboard keys and the five standard mouse buttons. Press and release the chosen button. Escape cancels learning; choose Escape directly from the list if needed. Device-specific buttons can be mapped to keyboard keys in the device's own software.

For War Dogs, Enter opens chat and Enter sends it and returns to the game. The default open delay is 250 ms, character delay 10 ms and before-send delay 150 ms. Change these per profile. Disable automatic sending to review the inserted text yourself.

The **Game speech model** selector is global, not part of a profile. Choose Base, Medium or Large-v3, then **Load**. If a model is missing, the button opens Downloads; install it, return to Game profiles and load it. Game models run on CPU, and larger ones take more memory and processing time. Browse still supports a custom local ggml model. Changing the game model does not change the Transcribe audio model.

The optional chat-switch example is **Enter → Tab → Enter**. It changes the chat destination only in games that use those keys. It does not change profiles. In double-tap mode, hold beyond the adjustable window before speaking; a separate command button keeps hold-to-talk immediate.

Automatic EXE links are optional. Target checks happen when text or a command is ready to send; there is no idle foreground polling. Background games do not receive input. No valid destination means a warning and no delivery. Focus changes stop remaining steps, with no automatic retry.

Keyboard protection filters physical keys only during chat delivery. The mouse remains available. Escape cancels; a 15-second watchdog restores input if sending stalls. Already-held key releases pass through. Keys blocked during sending are discarded. Keep the game focused until delivery finishes.

## Transcribe audio

Switch to **Transcribe audio** in the same window. Game talk buttons are paused on this tab.

1. Expand **Audio inputs**. Enable up to three inputs, choose devices and give them names. Microphone inputs include virtual recording endpoints. Speaker / loopback inputs capture what plays through the selected headphones or speakers. Disabled inputs are not captured.
2. Expand **Language & translation** to choose the input language and either the same language or English output.
3. Expand **Models & speakers** to choose transcription and speaker models independently. Speaker labels are optional and are estimates, not real names.
4. Expand **Transcript files** to choose combined or separate TXT files and their folder.
5. Click **Start transcription**. **Stop & save** finishes captured audio and saves the remaining lines. The buttons stay visible at the bottom.

Each session creates its own folder. Finished lines appear after speech pauses; continuous speech can take up to 30 seconds plus processing. Lines include elapsed timestamps, source names and optional speaker labels. Combined output is chronological. Timing is approximate, not frame-exact video timecode. Similar voices, overlaps and short phrases can still be mislabelled or unknown.

Switching tabs finishes buffered audio first. Models run in separate worker processes and unload when transcription stops. A model failure preserves completed text and lets you start a new session.

## Settings and privacy

Audio is processed locally in memory and is not saved or uploaded. Only text transcripts are saved. Downloads contact GitHub only when requested.

New settings are stored in `Documents/ElkaSoftware/ElkaVoicePlay`. Existing VoiceType settings are imported once without overwriting existing VoicePlay settings. Your previous files and explicit model/transcript paths are preserved. Optional components are stored in `%LOCALAPPDATA%/ElkaSoft/ElkaVoicePlay/Components` and shared by your VoicePlay copies on this PC.

Send testers the complete small ZIP, not your settings folder. Each person can download the extras they want.

## Development

Open **ElkaVoicePlay.sln** in Visual Studio with the .NET desktop development workload. Select x64 and start **ElkaVoicePlay.App**. See [BUILD.md](BUILD.md), [verification](docs/VERIFICATION.md) and [interactive acceptance](docs/ACCEPTANCE.md).

Copyright © 2026 ElkaSoft. Source is publicly viewable but is not released under an open-source license. See [LICENSE](LICENSE) and [third-party notices](THIRD-PARTY-NOTICES.md).
