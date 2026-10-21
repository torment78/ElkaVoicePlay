# Build Elka VoicePlay

Open ElkaVoicePlay.sln in Visual Studio with the .NET desktop development workload. Select x64 and start ElkaVoicePlay.App. The pinned .NET 8 SDK is listed in global.json.

```powershell
dotnet restore ElkaVoicePlay.sln --locked-mode -p:Platform=x64
dotnet build ElkaVoicePlay.sln -c Release -p:Platform=x64 --no-restore
dotnet test ElkaVoicePlay.sln -c Release -p:Platform=x64 --no-build
```

Game chat works without the optional transcription worker. Development builds use the same verified per-user component download as releases. To change the worker, create a fresh component using the release workflow and regenerate the pinned catalog before testing.

## Package the game application and optional transcription

Obtain the verified Base and standard speaker models using scripts/Get-Model.ps1 and scripts/Get-SpeakerModels.ps1. Publish to fresh directories:

```powershell
./scripts/Publish.ps1
./scripts/Build-Installer.ps1 -SourceDir artifacts/ElkaVoicePlay-win-x64-0.7.0 -OutputDir artifacts/release-0.7.0
./scripts/Test-Installer.ps1 -SourceDir artifacts/ElkaVoicePlay-win-x64-0.7.0
```

The publisher first builds ModelWorker against worker-components.json, a stable model catalog that deliberately excludes the worker package's own hash. It packages that worker, speaker models and licenses as VoicePlay-transcription-0.7.0.zip, then embeds the resulting SHA-256 catalog in the main app. These stages are sequential: do not run another build concurrently with packaging. The worker and main app share the same protocol but do not need identical resource catalogs. Informational versions omit the Git SHA to avoid self-referential hashes; record the release source commit in release metadata.

The main bundle excludes Workers, Models/Speakers, ONNX native libraries, larger speech models and GPU runtimes. It retains game recognition and Base. The model packs already published in v0.6.0-preview.1 keep their existing URLs and checksums; do not repackage or overwrite them. Package-Components.py is the legacy model-pack builder; it must not overwrite the current application catalog.

The installer requires Inno Setup 6.6 or newer and uses the ElkaSoft dark modern style. Its default destination is Program Files/ElkaSoft/Elka VoicePlay. The isolated installer test uses a separate application ID, temporary destination and unique shortcuts, and checks install/reinstall/uninstall plus user-file retention without launching the app.

Run `python scripts/Package-Release.py --app <published-game-folder> --output artifacts/release-0.7.0` after compiling the installer to create the portable/artwork ZIPs, payload inventory and SHA256SUMS.

## Artwork

Originals, sharing exports and ICO are committed under graphics. To regenerate format-only exports, run scripts/Export-Artwork.py with Python and Pillow. Exact image generation prompts are in graphics/artwork-prompts.json.

## Release validation

Build/test, inspect the module gate and icon in the app, validate installer layout/lifecycle, then publish installer, game ZIP, transcription ZIP, artwork ZIP and hashes as an unsigned prerelease. Download all assets anonymously and compare checksums. Use ElkaVoicePlay.Probe --transcription-component-fixture github <empty-component-root> <game-folder> <16kHz-mono-JFK.wav> <speaker-fixture.wav> to exercise the real downloader, optional worker lookup, shared Base and standard speakers. Fixture testing does not replace live game/microphone acceptance.
