using LivingNPCs.Behavior;

namespace LivingNPCs.Tests;

/// <summary>
/// Pins the gift-mail i18n fallback chain against SMAPI's real missing-key behavior: a missing
/// key returns the placeholder "(no translation:&lt;key&gt;)" (not the key itself), which must be
/// treated as missing so the chain per-NPC variants → tone variants → generic variants → base key
/// stays reachable. Regression guard for the bug where players received the literal placeholder
/// as the mail body.
/// </summary>
public sealed class GiftMailTranslationFallbackTests
{
    private static string Placeholder(string key) => $"(no translation:{key})";

    private static NpcGiftMailFact Mail(string motive) => new()
    {
        MailKey = "LivingNPCs.GiftMail.Penny.42.0600.00001",
        NpcName = "Penny",
        NpcDisplayName = "Penny",
        Motive = motive,
        ItemId = "(O)221",
        SourceGiftName = "Emerald",
        CreatedTotalDays = 42
    };

    /// <summary>A translate delegate that "has" exactly the given keys; every other key returns
    /// the SMAPI placeholder, mirroring an initialized ITranslationHelper in-game.</summary>
    private static Func<string, object, string> TranslatorWith(params string[] existingKeys)
    {
        var existing = new HashSet<string>(existingKeys, StringComparer.Ordinal);
        return (key, _) => existing.Contains(key) ? "T:" + key : Placeholder(key);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("gift.mail.body.birthday.penny.0", true)] // key echo: uninitialized I18n (tests)
    [InlineData("(no translation:gift.mail.body.birthday.penny.0)", true)] // SMAPI placeholder
    [InlineData("(no translation:some.other.key)", true)] // any placeholder counts as missing
    [InlineData("亲爱的农夫，谢谢你送的礼物。", false)]
    [InlineData("Dear @, thanks for the gift!", false)]
    [InlineData("Dear farmer (no translation:x)", false)] // only a leading placeholder counts
    public void LooksMissingTranslation_RecognizesSmapiPlaceholder(string text, bool expected)
    {
        Assert.Equal(
            expected,
            BehaviorMailService.LooksMissingTranslation("gift.mail.body.birthday.penny.0", text));
    }

    [Fact]
    public void Birthday_MappedProfile_FallsBackToToneVariant()
    {
        // Per-NPC birthday keys do not exist in i18n; only the tone tier does. Before the fix the
        // first per-NPC lookup "succeeded" with the placeholder and this fell over.
        var translate = TranslatorWith(
            "gift.mail.body.birthday.gentle.0",
            "gift.mail.body.birthday.gentle.1",
            "gift.mail.body.birthday.gentle.2");

        string body = BehaviorMailService.GetGiftMailBody(
            "birthday", "penny", Mail("birthday"), new { npc = "Penny" }, translate);

        Assert.StartsWith("T:gift.mail.body.birthday.gentle.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("(no translation:", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Birthday_UnmappedProfile_UsesGenericVariant()
    {
        var translate = TranslatorWith(
            "gift.mail.body.birthday.0",
            "gift.mail.body.birthday.1",
            "gift.mail.body.birthday.2");

        string body = BehaviorMailService.GetGiftMailBody(
            "birthday", string.Empty, Mail("birthday"), new { npc = "Custom" }, translate);

        Assert.StartsWith("T:gift.mail.body.birthday.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("(no translation:", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Reciprocal_MappedProfile_UsesProfiledVariant()
    {
        var translate = TranslatorWith(
            "gift.mail.body.reciprocal.penny.0",
            "gift.mail.body.reciprocal.penny.1",
            "gift.mail.body.reciprocal.penny.2",
            "gift.mail.body.reciprocal");

        string body = BehaviorMailService.GetGiftMailBody(
            "reciprocal", "penny", Mail("reciprocal"), new { npc = "Penny" }, translate);

        Assert.StartsWith("T:gift.mail.body.reciprocal.penny.", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Reciprocal_UnmappedProfile_FallsBackToBaseKey()
    {
        // No profile and no unprofiled variant keys exist in i18n: must land on the base key
        // instead of returning the placeholder of gift.mail.body.reciprocal.<n>.
        var translate = TranslatorWith("gift.mail.body.reciprocal");

        string body = BehaviorMailService.GetGiftMailBody(
            "reciprocal", string.Empty, Mail("reciprocal"), new { npc = "Custom" }, translate);

        Assert.Equal("T:gift.mail.body.reciprocal", body);
    }

    [Fact]
    public void HelpRequestReward_UnmappedProfile_FallsBackToBaseKey()
    {
        var translate = TranslatorWith("gift.mail.body.help_request_reward");

        string body = BehaviorMailService.GetGiftMailBody(
            "help_request_reward", string.Empty, Mail("help_request_reward"), new { npc = "Custom" }, translate);

        Assert.Equal("T:gift.mail.body.help_request_reward", body);
    }

    [Fact]
    public void VariantLookup_SkipsPlaceholderVariantsAndFindsTheOnlyRealOne()
    {
        var translate = TranslatorWith("gift.mail.body.birthday.gentle.2");

        bool found = BehaviorMailService.TryGetGiftMailVariantBody(
            "birthday", "gentle", Mail("birthday"), new { }, translate, out string body);

        Assert.True(found);
        Assert.Equal("T:gift.mail.body.birthday.gentle.2", body);
    }

    [Fact]
    public void VariantLookup_AllVariantsMissing_ReportsNotFound()
    {
        var translate = TranslatorWith(); // every key resolves to a placeholder

        bool found = BehaviorMailService.TryGetGiftMailVariantBody(
            "birthday", "penny", Mail("birthday"), new { }, translate, out string body);

        Assert.False(found);
        Assert.Equal(string.Empty, body);
    }
}
