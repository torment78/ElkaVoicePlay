# Third-party components

Elka VoicePlay uses these upstream projects; their licenses apply to their respective code and binaries, independently of the application's license.

| Component | Pinned version | License / source |
|---|---|---|
| Whisper.net and CPU runtime | 1.9.1 | [MIT](https://github.com/sandrohanea/whisper.net/blob/main/LICENSE), [source](https://github.com/sandrohanea/whisper.net) |
| whisper.cpp (via Whisper.net runtime) | Runtime package's compatible bundled build | [MIT](https://github.com/ggml-org/whisper.cpp/blob/master/LICENSE) |
| NAudio.Wasapi / NAudio.Core | 2.3.0 | [MIT](https://github.com/naudio/NAudio/blob/master/license.txt) |
| OpenAI Whisper weights converted to ggml | Base / Medium / Large-v3 | [Whisper license](https://github.com/openai/whisper/blob/main/LICENSE), [model host](https://huggingface.co/ggerganov/whisper.cpp) |
| xUnit (development tests) | 2.9.3 | [Apache 2.0](https://github.com/xunit/xunit/blob/main/LICENSE) |
| .NET runtime / Windows Desktop runtime | .NET 8 | [MIT and notices](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) |

The JFK speech sample used for local verification comes from [whisper.cpp samples](https://github.com/ggml-org/whisper.cpp/tree/master/samples). It is not part of the application distribution.

The game bundle includes multilingual `ggml-base.bin` from the model host listed above: 147,951,465 bytes, SHA-256 `60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe`. Its upstream Whisper MIT license is included at `licenses/WHISPER-MODEL-LICENSE.txt`.

The game package includes the upstream Whisper.net, whisper.cpp, NAudio, Sherpa managed-binding and .NET license texts under `licenses/`, including .NET third-party notices. The optional transcription package includes its own runtime and speaker-model notices. The application's separate license is in `LICENSE`.

## Speaker labels (0.4.0)

- Sherpa-ONNX managed binding and Windows x64 CPU runtime: 1.13.8, Apache 2.0, Xiaomi Corporation and contributors. License: licenses/SHERPA-ONNX-LICENSE.txt; https://github.com/k2-fsa/sherpa-onnx/tree/v1.13.8 .
- ONNX Runtime, supplied by the Sherpa runtime package, MIT: licenses/ONNX-RUNTIME-LICENSE.txt; https://github.com/microsoft/onnxruntime .
- Pyannote segmentation 3.0 ONNX, MIT, copyright 2022 CNRS: licenses/PYANNOTE-SEGMENTATION-LICENSE.txt. Public archive: https://github.com/k2-fsa/sherpa-onnx/releases/tag/speaker-segmentation-models . Model SHA-256: 220AD67CA923BEF2FA91F2390C786097BF305BCEB5E261D4AF67B38E938E1079.
- 3D-Speaker ERes2Net base 16 kHz embedding model, Apache 2.0: licenses/3D-SPEAKER-LICENSE.txt; https://github.com/modelscope/3D-Speaker . ONNX conversion: https://github.com/k2-fsa/sherpa-onnx/releases/tag/speaker-recongition-models . SHA-256: 1A331345F04805BADBB495C775A6DDFFCDD1A732567D5EC8B3D5749E3C7A5E4B.

The public four-speaker Chinese diagnostic WAV from the segmentation release is used only for development verification and is not shipped.

## GPU transcription and Nemotron (0.5.0)

- Whisper Large-v3: OpenAI Whisper model, MIT; `licenses/WHISPER-MODEL-LICENSE.txt`. Published GGML artifact: https://huggingface.co/ggerganov/whisper.cpp.
- NVIDIA Nemotron-3-Diarization Q8 GGUF: OpenMDW 1.1; `licenses/NEMOTRON-MODEL-LICENSE.txt` and the accompanying official model card. https://huggingface.co/nvidia/Nemotron-3-Diarization.
- NVIDIA NeMo-Speech.cpp 0.2.0: Apache-2.0 with third-party components under their own licenses. Full notices and licenses are retained under `nemotron-1/NemoCpu/share/licenses/nemo-speech` and `nvidia-1/NemoCuda/share/licenses/nemo-speech`.
- NVIDIA CUDA Runtime 13.0.96 and cuBLAS 13.1.0.3 redistributable DLLs: CUDA Toolkit license. License copies are included in the optional `nvidia-1/Cuda` directory.
- Whisper.net.Runtime.Cuda.Windows 1.9.1: MIT, same upstream as the existing Whisper.net binding.

These components run locally. No hosted NVIDIA or OpenAI service is used.

The game application includes Base. Classic speaker models and their processing runtime are in the optional Transcribe audio component. Nemotron and NVIDIA runtime licenses accompany their optional download packs. Large-v3 uses the included Whisper MIT model license.
