using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using StardewValley;
using StardewValley.Network;
using ValleyTalk;
using System;
using Newtonsoft.Json.Linq;
using StardewModdingAPI.Events;
using StardewModdingAPI;

namespace ValleyTalk;

internal class GameSummaryBuilder
{
    private readonly string _gameSummaryPath;
    private readonly string _baseContentPackFileName;
    private GameSummary _gameSummaryDict;

    private GameSummary GameSummaryDict
    {
        get
        {
            if (_gameSummaryDict == null)
            {
                _gameSummaryDict = string.IsNullOrWhiteSpace(_baseContentPackFileName)
                    ? Game1.content.LoadLocalized<GameSummary>(_gameSummaryPath)
                    : LoadBaseContentPackSummary(_baseContentPackFileName);
            }
            return _gameSummaryDict;
        }
    }

    private static bool _assetEventsRegistered;

    public GameSummaryBuilder(string gameSummaryPath = null, string baseContentPackFileName = null)
    {
        _gameSummaryPath = gameSummaryPath ?? VtConstants.GameSummaryPath;
        _baseContentPackFileName = baseContentPackFileName;
        EnsureAssetEventsRegistered();
    }

    /// <summary>
    /// Registers the asset hooks once for the whole class. Builders used to subscribe per
    /// instance with lambdas capturing <c>this</c>, which both leaked every builder into the
    /// event system and only invalidated that builder's private dictionary — the summary strings
    /// cached by <see cref="Prompts"/> kept the old language after a locale switch.
    /// </summary>
    private static void EnsureAssetEventsRegistered()
    {
        if (_assetEventsRegistered)
        {
            return;
        }

        _assetEventsRegistered = true;
        ModEntry.SHelper.Events.Content.AssetRequested += (sender, e) =>
        {
            if (e.Name.IsEquivalentTo(VtConstants.GameSummaryPath) || e.Name.IsEquivalentTo(VtConstants.OptimizedGameSummaryPath))
            {
                e.LoadFrom(() => new GameSummary(), AssetLoadPriority.High);
            }
        };
        ModEntry.SHelper.Events.Content.AssetsInvalidated += (object sender, AssetsInvalidatedEventArgs e) =>
        {
            if (e.NamesWithoutLocale.Any(an => an.IsEquivalentTo(VtConstants.GameSummaryPath) || an.IsEquivalentTo(VtConstants.OptimizedGameSummaryPath)))
            {
                Prompts.InvalidateWorldSummaries();
            }
        };
    }

    private static GameSummary LoadBaseContentPackSummary(string fileName)
    {
        string modsDirectory = Directory.GetParent(ModEntry.SHelper.DirectoryPath)?.FullName ?? ModEntry.SHelper.DirectoryPath;
        string contentPackPath = Path.Combine(modsDirectory, "[CP] ValleyTalk Base");
        string filePath = Path.Combine(contentPackPath, "assets", fileName);
        try
        {
            var root = JObject.Parse(File.ReadAllText(filePath));
            var entries = root["Changes"]?
                .Children<JObject>()
                .Select(change => change["Entries"])
                .FirstOrDefault(token => token != null);
            return entries?.ToObject<GameSummary>() ?? new GameSummary();
        }
        catch (Exception ex)
        {
            ModEntry.SMonitor.Log($"Unable to load base ValleyTalk GameSummary file '{filePath}': {ex}", LogLevel.Error);
            return new GameSummary();
        }
    }

    internal string Build()
    {
        var builder = new StringBuilder();
        Dictionary<string, bool> sections = GameSummaryDict.SectionOrder;
        if (sections == null)
        {
            ModEntry.SMonitor.Log("GameSummary is missing SectionOrder", StardewModdingAPI.LogLevel.Error);
            return string.Empty;
        }
        foreach (var section in sections)
        {
            var property = GameSummaryDict.GetType().GetProperty(section.Key);
            var sectionObject = property?.GetValue(GameSummaryDict) as IGameSummarySection;
            if (sectionObject == null)
            {
                ModEntry.SMonitor.Log($"GameSummary is missing section {section.Key}", StardewModdingAPI.LogLevel.Error);
                continue;
            }
            if (section.Value)
            {
                builder.AppendLine($"### {section.Key} :");
            }

            if (!string.IsNullOrWhiteSpace(sectionObject.Text))
            {
                builder.AppendLine(sectionObject.Text);
            }

            switch (section.Key)
            {
                case "Seasons":
                    var seasonsList = sectionObject as IGameSummarySection<SeasonObject>;
                    try
                    {
                        var seasons = seasonsList.Entries.Values.ToList(); // Convert values to list for GroupBy
                        foreach (var season in seasons)
                        {
                            builder.Append($"- **{season.Name}** - {season.Description} ");
                            if (season.Crops != null && season.Crops.Any())
                            {
                                builder.Append($"{Util.GetString("seasonCrops")} {Util.ConcatAnd(season.Crops)}. ");
                            }
                            if (season.Forage != null && season.Forage.Any())
                            {
                                builder.AppendLine($"{Util.GetString("seasonForage")} {Util.ConcatAnd(season.Forage)}.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ModEntry.SMonitor.Log($"Error parsing seasons: {ex}", StardewModdingAPI.LogLevel.Error);
                    }
                    break;
                case "Locations":
                    var locationsList = sectionObject as IGameSummarySection<LocationObject>;
                    var locations = locationsList.Entries.Values.ToList(); // Convert values to list for GroupBy
                    var regions = locations.GroupBy(x => x.Region);
                    foreach (var region in regions)
                    {
                        foreach (var location in region)
                        {
                            builder.AppendLine($"- **{location.Name}** - {location.Description}");
                        }
                    }
                    break;
                default:
                    var itemsList = sectionObject as IGameSummarySection<GeneralObject>;
                    var items = itemsList.Entries.Values.ToList(); // Convert values to list for GroupBy
                    foreach (var item in items)
                    {
                        builder.AppendLine($"- **{item.Name}** - {item.Description}");
                    }
                    break;


            }
        }

        var gameSummaryTranslations = Util.GetString("gameSummaryTranslations");
        if (!string.IsNullOrWhiteSpace(gameSummaryTranslations))
        {
            builder.AppendLine(gameSummaryTranslations);
        }
        return builder.ToString();
    }
}

public class GeneralObject
{
    public string id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
}

public interface IGameSummarySection
{
    public string Text { get; set; }
}

public interface IGameSummarySection<T> : IGameSummarySection
{
    public Dictionary<string, T> Entries { get; set; }
}

public class GeneralList : IGameSummarySection<GeneralObject>
{
    public string Text { get; set; }
    public Dictionary<string, GeneralObject> Entries { get; set; }
}

public class LocationObject : GeneralObject
{
    public string Region { get; set; }
}

public class LocationList : IGameSummarySection<LocationObject>
{
    public string Text { get; set; }
    public Dictionary<string, LocationObject> Entries { get; set; }
}

public class SeasonObject : GeneralObject
{
    public List<string> Crops { get; set; }
    public List<string> Forage { get; set; }
}

public class SeasonList : IGameSummarySection<SeasonObject>
{
    public string Text { get; set; }
    public Dictionary<string, SeasonObject> Entries { get; set; }
}
internal class GameSummary
{
    public Dictionary<string, bool> SectionOrder { get; set; }
    public GeneralList Intro { get; set; }
    public GeneralList FarmerBackground { get; set; }
    public GeneralList Villagers { get; set; }
    public SeasonList Seasons { get; set; }
    public LocationList Locations { get; set; }
    public GeneralList Festivals { get; set; }
    public GeneralList Outro { get; set; }
}
