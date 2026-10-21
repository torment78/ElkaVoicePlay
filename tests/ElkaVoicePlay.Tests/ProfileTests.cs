using ElkaVoicePlay.Core;
using ElkaVoicePlay.Shared;
using Xunit;

namespace ElkaVoicePlay.Tests;

public sealed class ProfileTests
{
    [Fact] public void OldSettingsMigrateWithoutLosingAudioOrHotkey()
    {
        var settings = new AppSettings { SchemaVersion = 1, HotkeyVirtualKey = 0x7B, SuppressHotkey = false,
            MicrophoneId = "mic", Language = "is", ModelPath = "model.bin" }.Validate();
        Assert.Equal(2, settings.SchemaVersion); Assert.Equal("mic", settings.MicrophoneId); Assert.Equal("is", settings.Language);
        Assert.Equal("model.bin", settings.ModelPath);
        var profile = settings.Profiles!.Single(p => p.Id == "notepad");
        Assert.Equal(new InputBinding(InputKind.Keyboard, 0x7B), profile.Trigger); Assert.False(profile.SuppressTrigger);
        Assert.False(settings.Profiles!.Single(p => p.Id == "war-dogs").AutoActivate);
    }
    [Fact] public void OptionalAutomaticActivationUsesForegroundGameAndManualFallback()
    {
        var manual = new GameProfile { Id = "manual" };
        var game = new GameProfile { Id = "game", Executable = "game.exe", AutoActivate = true };
        var settings = new AppSettings { Profiles = [manual, game], SelectedProfileId = manual.Id }.Validate();
        Assert.Equal(game, ProfileSelector.Select(settings, "GAME", null));
        Assert.Equal(manual, ProfileSelector.Select(settings, "other", null));
        Assert.Equal(manual, ProfileSelector.Select(settings with { AutomaticProfiles = false }, "game", null));
        Assert.Equal(manual, ProfileSelector.Select(settings with { Profiles = [manual, game with { AutoActivate = false }] }, "game", null));
    }
    [Fact] public void ExactExePathWinsAndDoesNotMatchSameNameElsewhere()
    {
        var name = new GameProfile { Executable = "game.exe", AutoActivate = true };
        var path = new GameProfile { Executable = @"C:\Games\game.exe", AutoActivate = true };
        var settings = new AppSettings { Profiles = [name, path], SelectedProfileId = name.Id }.Validate();
        Assert.Equal(path, ProfileSelector.Select(settings, "game", @"c:\games\GAME.EXE"));
        Assert.False(ProfileSelector.Matches(path, "game", @"D:\Other\game.exe"));
        Assert.False(ProfileSelector.Matches(path, "game", null));
    }
    [Fact] public void AutoActivationRequiresExeButManualDoesNot()
    {
        new GameProfile().Validate();
        Assert.Throws<InvalidDataException>(() => new GameProfile { AutoActivate = true }.Validate());
        var duplicate = new GameProfile();
        Assert.Throws<InvalidDataException>(() => new AppSettings { Profiles = [duplicate, duplicate] }.Validate());
    }
    [Fact] public async Task ProfilesRoundTripAndPreservePreMigrationFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), "VoicePlay-profile-" + Guid.NewGuid());
        string path = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(directory);
        try
        {
            const string old = "{\"SchemaVersion\":1,\"Language\":\"is\",\"HotkeyVirtualKey\":123}";
            await File.WriteAllTextAsync(path, old);
            var store = new SettingsStore(path);
            var settings = await store.LoadAsync();
            var game = new GameProfile { Trigger = new(InputKind.Mouse, 5), CloseChat = new(InputKind.Keyboard, 0x1B), OpenDelayMs = 1234 };
            await store.SaveAsync(settings with { Profiles = [game], SelectedProfileId = game.Id });
            var loaded = await store.LoadAsync();
            Assert.Equal(game, Assert.Single(loaded.Profiles!)); Assert.Equal(game.Id, loaded.SelectedProfileId);
            Assert.Equal(old, await File.ReadAllTextAsync(path + ".before-profiles.bak"));
            await store.SaveAsync(loaded);
            Assert.Equal(old, await File.ReadAllTextAsync(path + ".before-profiles.bak"));
        }
        finally { Directory.Delete(directory, true); }
    }
}
