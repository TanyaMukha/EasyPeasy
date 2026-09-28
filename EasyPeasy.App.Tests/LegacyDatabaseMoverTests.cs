using EasyPeasy.App.Services;

namespace EasyPeasy.App.Tests;

/// <summary>
/// The move runs once, on a real learner's real database, and a mistake here is silent data loss —
/// so these tests use actual files in a temp folder rather than a mocked file system.
/// </summary>
public class LegacyDatabaseMoverTests : IDisposable
{
    private const string Legacy = "EasyEnglish.db";
    private const string Current = "mukhalab.easypeasy.db";

    private readonly string _dir;

    public LegacyDatabaseMoverTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "easypeasy-move-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);

        GC.SuppressFinalize(this);
    }

    private string Path_(string name) => Path.Combine(_dir, name);

    private string Connection => $"Data Source={Path_(Current)};Cache=Shared";

    private void Write(string name, string content) => File.WriteAllText(Path_(name), content);

    private bool Exists(string name) => File.Exists(Path_(name));

    // ── Extracting the path ───────────────────────────────────────────────────

    [Theory]
    [InlineData("Data Source=C:/x/db.sqlite", "C:/x/db.sqlite")]
    [InlineData("Data Source=C:/x/db.sqlite;Cache=Shared", "C:/x/db.sqlite")]
    [InlineData("Cache=Shared;Data Source=C:/x/db.sqlite", "C:/x/db.sqlite")]
    [InlineData("data source=C:/x/db.sqlite", "C:/x/db.sqlite")]
    [InlineData(" Data Source = C:/x/db.sqlite ;Cache=Shared", "C:/x/db.sqlite")]
    [InlineData("DataSource=C:/x/db.sqlite", "C:/x/db.sqlite")]
    [InlineData("Filename=C:/x/db.sqlite", "C:/x/db.sqlite")]
    public void Data_source_is_read_out_of_the_connection_string(string connection, string expected)
    {
        Assert.Equal(expected, LegacyDatabaseMover.ExtractDataSource(connection));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Cache=Shared")]
    [InlineData("Data Source=")]
    public void A_connection_string_without_a_file_yields_nothing(string? connection)
    {
        Assert.Null(LegacyDatabaseMover.ExtractDataSource(connection));
    }

    // ── Moving ────────────────────────────────────────────────────────────────

    [Fact]
    public void The_old_database_is_renamed_with_its_log_and_shared_memory()
    {
        Write(Legacy, "courses");
        Write(Legacy + "-wal", "pending");
        Write(Legacy + "-shm", "index");

        Assert.True(LegacyDatabaseMover.Move(Connection, Legacy));

        Assert.Equal("courses", File.ReadAllText(Path_(Current)));
        Assert.Equal("pending", File.ReadAllText(Path_(Current + "-wal")));
        Assert.Equal("index", File.ReadAllText(Path_(Current + "-shm")));

        Assert.False(Exists(Legacy));
        Assert.False(Exists(Legacy + "-wal"));
        Assert.False(Exists(Legacy + "-shm"));
    }

    [Fact]
    public void A_database_without_a_write_ahead_log_moves_just_as_well()
    {
        Write(Legacy, "courses");

        Assert.True(LegacyDatabaseMover.Move(Connection, Legacy));

        Assert.Equal("courses", File.ReadAllText(Path_(Current)));
        Assert.False(Exists(Current + "-wal"));
    }

    [Fact]
    public void An_existing_database_is_never_overwritten()
    {
        Write(Current, "current");
        Write(Legacy, "old");

        Assert.False(LegacyDatabaseMover.Move(Connection, Legacy));

        Assert.Equal("current", File.ReadAllText(Path_(Current)));
        Assert.Equal("old", File.ReadAllText(Path_(Legacy)));
    }

    [Fact]
    public void Nothing_happens_on_a_device_that_never_had_the_old_file()
    {
        Assert.False(LegacyDatabaseMover.Move(Connection, Legacy));
        Assert.False(Exists(Current));
    }

    [Fact]
    public void A_second_start_leaves_the_moved_database_alone()
    {
        Write(Legacy, "courses");

        Assert.True(LegacyDatabaseMover.Move(Connection, Legacy));
        Assert.False(LegacyDatabaseMover.Move(Connection, Legacy));

        Assert.Equal("courses", File.ReadAllText(Path_(Current)));
    }

    [Fact]
    public void A_start_interrupted_after_the_log_moved_finishes_the_job()
    {
        // The companions move first, so this is what a half-done previous start looks like.
        Write(Legacy, "courses");
        Write(Current + "-wal", "pending");

        Assert.True(LegacyDatabaseMover.Move(Connection, Legacy));

        Assert.Equal("courses", File.ReadAllText(Path_(Current)));
        Assert.Equal("pending", File.ReadAllText(Path_(Current + "-wal")));
    }

    [Fact]
    public void Without_a_usable_connection_string_nothing_is_touched()
    {
        Write(Legacy, "courses");

        Assert.False(LegacyDatabaseMover.Move("Cache=Shared", Legacy));
        Assert.False(LegacyDatabaseMover.Move(null, Legacy));

        Assert.Equal("courses", File.ReadAllText(Path_(Legacy)));
    }
}
