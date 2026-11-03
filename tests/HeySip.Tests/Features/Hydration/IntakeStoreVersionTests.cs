using HeySip.Core.Features.Hydration;
using HeySip.Tests.Support;
using Microsoft.Data.Sqlite;

namespace HeySip.Tests.Features.Hydration;

public sealed class IntakeStoreVersionTests : IDisposable
{
    private readonly TempData _data = new();

    public void Dispose() => _data.Dispose();

    private static void SetVersion(string path, int version)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA user_version = {version};";
        command.ExecuteNonQuery();
    }

    [Fact]
    public void A_new_file_records_the_layout_version_and_starting_again_changes_nothing()
    {
        var store = new IntakeStore(_data.Location.DatabasePath);

        store.Initialize();
        store.Add(DateTimeOffset.UtcNow, 250);
        store.Initialize();

        Assert.Equal(IntakeStore.CurrentVersion, store.SchemaVersion());
        Assert.Single(store.Between(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)));
    }

    [Fact]
    public void A_file_from_before_versions_were_recorded_keeps_its_drinks_and_gets_the_number()
    {
        var store = new IntakeStore(_data.Location.DatabasePath);
        store.Initialize();
        store.Add(DateTimeOffset.UtcNow, 300);
        SetVersion(_data.Location.DatabasePath, 0); // what every earlier copy of HeySip left behind

        store.Initialize();

        Assert.Equal(IntakeStore.CurrentVersion, store.SchemaVersion());
        Assert.Single(store.Between(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)));
    }

    [Fact]
    public void A_file_from_a_newer_version_is_left_alone_and_still_works()
    {
        var store = new IntakeStore(_data.Location.DatabasePath);
        store.Initialize();
        SetVersion(_data.Location.DatabasePath, 7);

        store.Initialize();
        store.Add(DateTimeOffset.UtcNow, 200);

        Assert.Equal(7, store.SchemaVersion());
        Assert.Single(store.Between(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)));
    }
}
