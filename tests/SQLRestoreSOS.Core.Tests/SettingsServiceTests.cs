using SQLRestoreSOS.Core.Connections;
using SQLRestoreSOS.Core.Settings;

namespace SQLRestoreSOS.Core.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _tempFilePath = Path.Combine(Path.GetTempPath(), $"sqlrestoresos-tests-{Guid.NewGuid():N}.json");

    [Fact]
    public void Load_NoFileYet_ReturnsDefaults()
    {
        var service = new SettingsService(_tempFilePath);
        var settings = service.Load();

        Assert.Equal(RestoreEngineKind.DatabaseRestore, settings.RestoreEngine);
        Assert.Empty(settings.RecentServers);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        var service = new SettingsService(_tempFilePath);
        var settings = service.Load();
        settings.Theme = "Dark";
        settings.RestoreEngine = RestoreEngineKind.RestoreGene;
        settings.DefaultBackupPaths["WIN10"] = new BackupPathSet { Full = @"D:\Full", Diff = @"D:\Diff", Log = @"D:\Log" };

        service.Save(settings);
        var reloaded = service.Load();

        Assert.Equal("Dark", reloaded.Theme);
        Assert.Equal(RestoreEngineKind.RestoreGene, reloaded.RestoreEngine);
        Assert.Equal(@"D:\Full", reloaded.DefaultBackupPaths["WIN10"].Full);
    }

    [Fact]
    public void AddRecentServer_DedupesAndMovesMostRecentFirst()
    {
        var service = new SettingsService(_tempFilePath);
        var settings = service.Load();

        service.AddRecentServer(settings, "ServerA", SqlAuthMode.Windows);
        service.AddRecentServer(settings, "ServerB", SqlAuthMode.SqlLogin);
        service.AddRecentServer(settings, "ServerA", SqlAuthMode.Windows); // re-add -> should move to front, not duplicate

        Assert.Equal(2, settings.RecentServers.Count);
        Assert.Equal("ServerA", settings.RecentServers[0].ServerName);
        Assert.Equal("ServerB", settings.RecentServers[1].ServerName);
    }

    [Fact]
    public void AddRecentServer_CapsAtTenEntries()
    {
        var service = new SettingsService(_tempFilePath);
        var settings = service.Load();

        for (var i = 0; i < 15; i++)
        {
            service.AddRecentServer(settings, $"Server{i}", SqlAuthMode.Windows);
        }

        Assert.Equal(10, settings.RecentServers.Count);
        Assert.Equal("Server14", settings.RecentServers[0].ServerName);
    }

    [Fact]
    public void AddRecentServer_NeverPersistsAPassword()
    {
        var service = new SettingsService(_tempFilePath);
        var settings = service.Load();
        service.AddRecentServer(settings, "ServerA", SqlAuthMode.SqlLogin);
        service.Save(settings);

        var json = File.ReadAllText(_tempFilePath);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (File.Exists(_tempFilePath))
        {
            File.Delete(_tempFilePath);
        }
    }
}
