# Build Elka VoicePlay

Open `ElkaVoicePlay.sln` with Visual Studio's .NET desktop development workload. Use **x64** and **ElkaVoicePlay.App** as the startup project. Projects, assemblies and namespaces all use ElkaVoicePlay. The solution uses .NET 8 and pinned NuGet lock files.

```powershell
dotnet restore ElkaVoicePlay.sln --locked-mode -p:Platform=x64
dotnet build ElkaVoicePlay.sln -c Release -p:Platform=x64 --no-restore
dotnet test ElkaVoicePlay.sln -c Release -p:Platform=x64 --no-build
```

## Small portable bundle

Obtain Base and Classic models first (downloads are verified):

```powershell
./scripts/Get-Model.ps1 -Model base -Destination artifacts/gpu-downloads
./scripts/Get-SpeakerModels.ps1
./scripts/Publish.ps1
```

Publish requires an empty output directory, defaults to `artifacts/ElkaVoicePlay-win-x64-0.6.0`, and includes the .NET runtime. It includes Base and Classic models, the app and an isolated CPU worker. It excludes Medium, Large-v3, Nemotron and CUDA. Preserve the complete output directory when zipping or copying it. The script rejects accidental optional binaries in the small output. ARM/x86 native runtimes are omitted from this x64 bundle.

## Optional component release assets

The application catalog is embedded from `src/ElkaVoicePlay.Shared/optional-components.json`. It pins release URLs, sizes and SHA-256 values for all archives, split pieces and installed files. Native runtime upgrades require a new package version and app catalog; a running release never loads a remote mutable manifest.

To regenerate optional assets (Python 3.11+ is a packaging tool only):

```powershell
./scripts/Get-TranscriptionAssets.ps1
python scripts/Package-Components.py
```

Restore the solution first to obtain the pinned Whisper CUDA NuGet package. The script writes assets under `artifacts/release-0.6.0` and regenerates the catalog. Large-v3 is split into two pieces below GitHub's 2 GiB per-asset limit. Native NVIDIA common DLLs are stored once and shared by the isolated Whisper/Nemotron processes through their process-local search paths.

Upload the exact catalog assets to `torment78/ElkaVoicePlay`, release `v0.6.0-preview.1`, together with the small application ZIP and SHA256SUMS. Build the application after generating its catalog. The small ZIP plus the in-app **Install all extras** action is the full setup.

## Downloader and worker checks

`ComponentInstaller` checks transport length and SHA-256, extracts only catalogued paths, verifies installed files and atomically promotes a complete directory. Interrupted downloads keep verified pieces; incomplete pieces and staging directories are removed. It does not need administrator access. The installation lock prevents concurrent writers to the same data folder. The UI stops transcription before entering Downloads and prevents model use while installation runs.

`ElkaVoicePlay.Probe --components-fixture <assets-directory-or-github> <empty-test-component-directory> <published-worker-exe> <JFK-16k-mono.wav> <four-speakers-zh.wav>` verifies the small CPU fallback, installs all actual assets and runs Large-v3 GPU plus Nemotron CPU/GPU. Use `github` for anonymous release-download verification after publication. Test assets and generated transcripts belong under ignored artifacts; never use private recordings for public release evidence.

The UiCheck tool renders profile, transcription and download pages without opening audio or keyboard hooks. Flags: `--profile`, `--transcript`, `--downloads`, and `--expand`. It accepts an output PNG path. Live UI and gameplay acceptance are described in `docs/ACCEPTANCE.md`.

New user settings use `Documents/ElkaSoftware/ElkaVoicePlay`. The one compatibility reference to the old product name imports legacy settings without overwriting existing new settings or moving user recordings. Explicit paths in imported settings remain unchanged.
