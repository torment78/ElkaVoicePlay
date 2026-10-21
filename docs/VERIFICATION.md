# Release verification

## 0.7.0 preview

- Game packaging omits the transcription worker, standard speaker models and ONNX native binaries; Base and game chat remain included.
- The Transcribe audio tab shows a download screen when its versioned component is absent or incomplete, then activates after verified installation. The component reuses the main application's Base model.
- Existing optional models retain their original versioned release URLs. Main and worker catalogs are built in sequence to avoid a self-referential component hash.
- Storage matches the ElkaSoft family: Program Files for the installer, roaming Saves, local Models/Components and Documents transcript output. Migration preserves originals, custom paths and existing destination settings.
- 178 tests pass. The real component installer, installed worker lookup, shared Base CPU transcription and standard speakers pass public-fixture integration checks.
- The dark Inno installer creates desktop/Start-menu shortcuts and uninstall registration. Isolated install/reinstall/uninstall checks verify payload hashes and retention of user files.
- Horizontal/vertical original artwork, both sharing copies under 1 MB, and a multi-size Windows ICO are committed. The app, taskbar/tray and installer use the new identity.
- Public asset verification and exact package sizes are recorded in the release notes. Detailed logs remain under ignored artifacts/verification.

## 0.6.0 preview

- All source projects, assemblies, namespaces, the solution and the public repository use ElkaVoicePlay.
- Release x64 builds have zero warnings/errors. Regression tests cover previous chat/input/transcription behavior, optional-download integrity, split model assembly, cancellation, retry, unsafe archive paths and settings migration.
- Small-bundle packaging excludes Medium, Large-v3, Nemotron and NVIDIA binaries. Base and standard speakers are included. Optional assets have pinned SHA-256 values and are installed atomically.
- Profile and transcription layouts use collapsible cards. Profile name/talk button and Save/Cancel are outside the scrolling area. Transcription Start/Stop remain visible.
- Models run in isolated processes. Game recognition stays independent. Classic short-phrase/overlap fixes from 0.5.0 are retained.

Per-release hashes and public download verification are recorded in the GitHub release. Detailed machine-local logs and public-fixture outputs are kept under ignored `artifacts/verification`.

Speaker labels remain experimental; fixture results do not establish accuracy on arbitrary videos. Real microphone, button-learning and game acceptance still require hands-on testing. This is an unsigned preview.
