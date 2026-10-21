# Release verification

## 0.6.0 preview

- All source projects, assemblies, namespaces, the solution and the public repository use ElkaVoicePlay.
- Release x64 builds have zero warnings/errors. Regression tests cover previous chat/input/transcription behavior, optional-download integrity, split model assembly, cancellation, retry, unsafe archive paths and settings migration.
- Small-bundle packaging excludes Medium, Large-v3, Nemotron and NVIDIA binaries. Base and standard speakers are included. Optional assets have pinned SHA-256 values and are installed atomically.
- Profile and transcription layouts use collapsible cards. Profile name/talk button and Save/Cancel are outside the scrolling area. Transcription Start/Stop remain visible.
- Models run in isolated processes. Game recognition stays independent. Classic short-phrase/overlap fixes from 0.5.0 are retained.

Per-release hashes and public download verification are recorded in the GitHub release. Detailed machine-local logs and public-fixture outputs are kept under ignored `artifacts/verification`.

Speaker labels remain experimental; fixture results do not establish accuracy on arbitrary videos. Real microphone, button-learning and game acceptance still require hands-on testing. This is an unsigned preview.
