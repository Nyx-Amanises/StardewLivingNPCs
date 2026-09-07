using LivingNPCs.Behavior.Ui;
using StardewValley.ItemTypeDefinitions;
using Xunit;

namespace LivingNPCs.Tests;

public sealed class MemoryBookMomentItemNamesTests
{
    [Fact]
    public void StableIdUsesCurrentDisplayNameEvenWhenSavedLabelDiffers()
    {
        string actual = MemoryBookMomentItemNames.Resolve(
            " (O)169 ",
            "Driftwood",
            id =>
            {
                Assert.Equal("(O)169", id);
                return Item("169", "Driftwood", "浮木");
            },
            () => throw new InvalidOperationException("No name lookup is needed."));

        Assert.Equal("浮木", actual);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrErrorIdKeepsSavedLabelWithoutGuessingByName(bool errorItem)
    {
        string actual = MemoryBookMomentItemNames.Resolve(
            "(O)removed",
            "Driftwood",
            _ => errorItem ? Item("removed", "Driftwood", "Error Item", isErrorItem: true) : null,
            () => [Item("169", "Driftwood", "浮木")]);

        Assert.Equal("Driftwood", actual);
    }

    [Fact]
    public void LegacyEnglishNameUsesUniqueCompleteInternalNameMatch()
    {
        string actual = MemoryBookMomentItemNames.Resolve(
            string.Empty,
            "driftwood",
            _ => throw new InvalidOperationException("No ID is stored."),
            () => [Item("169", "Driftwood", "浮木"), Item("388", "Wood", "木材")]);

        Assert.Equal("浮木", actual);
    }

    [Fact]
    public void LegacyLocalizedNameCanMatchCurrentDisplayNameExactly()
    {
        string actual = MemoryBookMomentItemNames.Resolve(
            string.Empty,
            "浮木",
            _ => null,
            () => [Item("169", "Driftwood", "浮木")]);

        Assert.Equal("浮木", actual);
    }

    [Theory]
    [InlineData("Wooden chair")]
    [InlineData("Spiced Driftwood")]
    [InlineData("Drift")]
    public void PartialOrDecoratedLegacyNamesAreNotChanged(string storedLabel)
    {
        string actual = MemoryBookMomentItemNames.Resolve(
            string.Empty,
            storedLabel,
            _ => null,
            () => [Item("169", "Driftwood", "浮木"), Item("388", "Wood", "木材")]);

        Assert.Equal(storedLabel, actual);
    }

    [Fact]
    public void AmbiguousLegacyNameKeepsSavedLabel()
    {
        string actual = MemoryBookMomentItemNames.Resolve(
            string.Empty,
            "Driftwood",
            _ => null,
            () => [Item("169", "Driftwood", "浮木"), Item("mod.wood", "Driftwood", "异国浮木")]);

        Assert.Equal("Driftwood", actual);
    }

    [Fact]
    public void ErrorItemCannotProvideLegacyNameMatch()
    {
        string actual = MemoryBookMomentItemNames.Resolve(
            string.Empty,
            "Driftwood",
            _ => null,
            () => [Item("169", "Driftwood", "Error Item", isErrorItem: true)]);

        Assert.Equal("Driftwood", actual);
    }

    [Fact]
    public void UnavailableGameOrContentKeepsSavedLabel()
    {
        Assert.Equal("Driftwood", MemoryBookMomentItemNames.Resolve(
            "(O)169",
            "Driftwood",
            _ => throw new InvalidOperationException("Game content isn't loaded."),
            () => []));
        Assert.Equal("Driftwood", MemoryBookMomentItemNames.Resolve(
            string.Empty,
            "Driftwood",
            _ => null,
            () => throw new InvalidOperationException("Game content isn't loaded.")));
    }

    [Fact]
    public void ResolvingAgainUsesFreshLocalizedData()
    {
        string currentLabel = "浮木";
        ParsedItemData ReadItem(string _) => Item("169", "Driftwood", currentLabel);

        Assert.Equal("浮木", MemoryBookMomentItemNames.Resolve("(O)169", "Driftwood", ReadItem, () => []));
        currentLabel = "Driftwood";
        Assert.Equal("Driftwood", MemoryBookMomentItemNames.Resolve("(O)169", "浮木", ReadItem, () => []));
    }

    private static ParsedItemData Item(string id, string internalName, string displayName, bool isErrorItem = false)
    {
        return new ParsedItemData(
            new ObjectDataDefinition(),
            id,
            0,
            "",
            internalName,
            displayName,
            "",
            0,
            "Basic",
            null,
            isErrorItem,
            false);
    }
}
