using System.Security.Cryptography;

namespace ElkaVoicePlay.Core;

public static class BundledModelSetup
{
    public const string FileName = "ggml-base.bin";
    public const long ExpectedBytes = 147951465;
    public const string ExpectedSha256 = "60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe";

    public static async Task<string> ResolveAsync(string configuredPath, string userModelsDirectory,
        string applicationDirectory, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (File.Exists(configuredPath)) return Path.GetFullPath(configuredPath);
        // Repair Base paths copied from another Windows account, without replacing a chosen Small/Medium/etc. model.
        if (!string.Equals(Path.GetFileName(configuredPath), FileName, StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("The selected speech model was not found. Use Browse to choose its .bin file, then Load.", configuredPath);
        string destination = Path.Combine(userModelsDirectory, FileName);
        if (File.Exists(destination)) return Path.GetFullPath(destination);
        string source = Path.Combine(applicationDirectory, "Models", FileName);
        if (!File.Exists(source))
            throw new FileNotFoundException("The Base speech model is missing. Extract the complete VoicePlay package, including Models\\ggml-base.bin, then reopen VoicePlay. Or use Browse to choose an installed .bin model.", source);
        await CopyVerifiedAsync(source, destination, ExpectedBytes, ExpectedSha256, token).ConfigureAwait(false);
        return Path.GetFullPath(destination);
    }

    internal static async Task CopyVerifiedAsync(string source, string destination, long expectedBytes,
        string expectedSha256, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (File.Exists(destination)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, useAsync: true))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 131072, useAsync: true))
            {
                if (input.Length != expectedBytes) throw new InvalidDataException("The bundled Base model is incomplete. Extract a fresh copy of the complete VoicePlay package.");
                await input.CopyToAsync(output, token).ConfigureAwait(false);
                await output.FlushAsync(token).ConfigureAwait(false);
                output.Position = 0;
                string hash = Convert.ToHexString(await SHA256.HashDataAsync(output, token).ConfigureAwait(false));
                if (output.Length != expectedBytes || !string.Equals(hash, expectedSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The bundled Base model failed its integrity check. Extract a fresh copy of the complete VoicePlay package.");
            }
            token.ThrowIfCancellationRequested();
            try { File.Move(temporary, destination, overwrite: false); }
            catch (IOException) when (File.Exists(destination)) { /* Another setup completed first; preserve its installed model. */ }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
