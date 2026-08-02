using System.Text.Json;
using BatHouseholdHub.Models;
using Xunit;

namespace BatHouseholdHub.Tests;

public class HouseholdFundsMigrationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [Fact]
    public void OldJsonContainingLegacyFundNamesLoadsIntoGenericProperties()
    {
        var data = JsonSerializer.Deserialize<HouseholdData>(LegacyJson())!;

        Assert.Equal(101.25m, data.Funds.PersonA);
        Assert.Equal(202.50m, data.Funds.PersonB);
        Assert.Equal(303.75m, data.Funds.Shared);
        Assert.Equal(40.00m, data.Funds.Buffer);
        Assert.Equal(new DateTime(2026, 1, 15), data.Funds.LastUpdated);
    }

    [Fact]
    public void MigratedValuesSurviveSaveAndReload()
    {
        var data = JsonSerializer.Deserialize<HouseholdData>(LegacyJson())!;
        var saved = JsonSerializer.Serialize(data, JsonOptions);
        var reloaded = JsonSerializer.Deserialize<HouseholdData>(saved)!;

        Assert.Equal(101.25m, reloaded.Funds.PersonA);
        Assert.Equal(202.50m, reloaded.Funds.PersonB);
        Assert.Equal(303.75m, reloaded.Funds.Shared);
        Assert.Equal(40.00m, reloaded.Funds.Buffer);
        Assert.Equal(new DateTime(2026, 1, 15), reloaded.Funds.LastUpdated);
    }

    [Fact]
    public void NewlySavedJsonContainsOnlyGenericFundNames()
    {
        var data = JsonSerializer.Deserialize<HouseholdData>(LegacyJson())!;
        var saved = JsonSerializer.Serialize(data, JsonOptions);

        Assert.Contains("\"PersonA\"", saved);
        Assert.Contains("\"PersonB\"", saved);
        Assert.DoesNotContain("\"Trey\"", saved);
        Assert.DoesNotContain("\"Jess\"", saved);
    }

    [Fact]
    public void MigrationIsIdempotentAfterSaveAndReload()
    {
        var firstLoad = JsonSerializer.Deserialize<HouseholdData>(LegacyJson())!;
        var saved = JsonSerializer.Serialize(firstLoad, JsonOptions);
        var secondLoad = JsonSerializer.Deserialize<HouseholdData>(saved)!;
        var secondSave = JsonSerializer.Serialize(secondLoad, JsonOptions);

        Assert.Equal(saved, secondSave);
        Assert.False(secondLoad.Funds.MigratedLegacyMemberFunds);
    }

    private static string LegacyJson() =>
        """
        {
          "Funds": {
            "Trey": 101.25,
            "Jess": 202.50,
            "Shared": 303.75,
            "Buffer": 40.00,
            "LastUpdated": "2026-01-15T00:00:00"
          }
        }
        """;
}
