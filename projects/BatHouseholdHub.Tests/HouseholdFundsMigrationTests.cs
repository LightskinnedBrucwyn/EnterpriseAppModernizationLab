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

        AssertLegacyKeysRemoved(saved);
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

    public static TheoryData<string, decimal, decimal> LegacyKeyRemovalCases => new()
    {
        { LegacyFundsJson(""" "Trey": 101.25, "Jess": 202.50, """), 101.25m, 202.50m },
        { LegacyFundsJson(""" "Trey": 101.25, """), 101.25m, 0m },
        { LegacyFundsJson(""" "Jess": 202.50, """), 0m, 202.50m },
        { LegacyFundsJson(""" "PersonA": 11.11, "Trey": 101.25, """), 11.11m, 0m },
        { LegacyFundsJson(""" "PersonB": 22.22, "Jess": 202.50, """), 0m, 22.22m }
    };

    [Theory]
    [MemberData(nameof(LegacyKeyRemovalCases))]
    public void LegacyKeysAreRemovedWithoutOverwritingPopulatedGenericValues(string json, decimal expectedPersonA, decimal expectedPersonB)
    {
        var data = JsonSerializer.Deserialize<HouseholdData>(json)!;
        var saved = JsonSerializer.Serialize(data, JsonOptions);

        Assert.Equal(expectedPersonA, data.Funds.PersonA);
        Assert.Equal(expectedPersonB, data.Funds.PersonB);
        Assert.Equal(303.75m, data.Funds.Shared);
        Assert.Equal(40.00m, data.Funds.Buffer);
        Assert.Equal(new DateTime(2026, 1, 15), data.Funds.LastUpdated);
        AssertLegacyKeysRemoved(saved);
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

    private static string LegacyFundsJson(string memberFields) =>
        $$"""
        {
          "Funds": {
            {{memberFields}}
            "Shared": 303.75,
            "Buffer": 40.00,
            "LastUpdated": "2026-01-15T00:00:00"
          }
        }
        """;

    private static void AssertLegacyKeysRemoved(string saved)
    {
        Assert.Contains("\"PersonA\"", saved);
        Assert.Contains("\"PersonB\"", saved);
        Assert.DoesNotContain("\"Trey\"", saved);
        Assert.DoesNotContain("\"Jess\"", saved);
    }
}
