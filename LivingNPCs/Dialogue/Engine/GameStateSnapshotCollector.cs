using System;
using System.Collections.Generic;
using System.Linq;
using StardewValley;
using StardewValley.Characters;
using StardewValley.TerrainFeatures;

using LivingNPCs.Dialogue.Diagnostics;
using LivingNPCs.Dialogue.Llm;
using LivingNPCs.Dialogue.Persistence;
namespace LivingNPCs.Dialogue.Engine;

/// <summary>
/// 在主线程对游戏状态做一次快照（WP10 §4.3）。所有游戏读取集中在此，逐组容错：
/// 任何一组读取失败只损失该组上下文，绝不让生成请求崩溃。
/// </summary>
internal static class GameStateSnapshotCollector
{
    private const int NearbyTileRadius = 20;

    private static readonly string[] ShopLocations =
    {
        "SeedShop", "Saloon", "JojaMart", "ScienceHouse", "AnimalShop", "FishShop", "Blacksmith", "Hospital", "Sewer"
    };

    private sealed record ScheduleSnapshot(
        ScheduleAvailability Availability,
        string CurrentActivity,
        string CurrentStop,
        int? CurrentStopStart,
        string NextStop,
        int? MinutesUntilNext,
        SchedulePurposeResult NextPurpose,
        IReadOnlyList<string> RemainingStops,
        bool IsTravelling,
        string TravelDestination,
        SchedulePurposeResult TravelPurpose);
    public static GameStateSnapshot Collect(NPC? npc)
    {
        try
        {
            return CollectCore(npc);
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.stepFailed",
                    new { step = "snapshot game state", error = ex },
                    $"Failed to snapshot game state: {ex}"),
                StardewModdingAPI.LogLevel.Warn);
            return new GameStateSnapshot();
        }
    }

    private static GameStateSnapshot CollectCore(NPC? npc)
    {
        Farmer player = Game1.player;
        RegisterRuntimePromptAliases();
        string rawSpouseName = player.spouse ?? string.Empty;
        string promptSpouseName = RsvAiPolicy.IsBlockedNpcName(rawSpouseName)
            ? string.Empty
            : rawSpouseName;
        string currentLocationName = PromptLocationName(npc?.currentLocation?.Name);
        var friendship = TryGet(() =>
            npc != null && player.friendshipData.TryGetValue(npc.Name, out Friendship data) ? data : null);

        var schedule = CollectSchedule(npc);
        var (buildings, animals, crops, pets) = CollectFarm();

        return new GameStateSnapshot
        {
            Year = Game1.year,
            SeasonIndex = Game1.seasonIndex,
            SeasonName = Game1.currentSeason ?? "spring",
            DayOfMonth = Game1.dayOfMonth,
            TimeOfDay = Game1.timeOfDay,
            WeatherFlags = CollectWeather(npc?.currentLocation ?? player.currentLocation),

            FriendshipPoints = friendship?.Points ?? -1,
            IsMarriedToFarmer = TryGet(() => friendship?.IsMarried() == true && !friendship.IsRoommate()),
            IsRoommate = TryGet(() => friendship?.IsRoommate() == true),
            DaysMarried = TryGet(() => friendship != null && npc != null && string.Equals(player.spouse, npc.Name, StringComparison.Ordinal)
                ? player.GetDaysMarried()
                : 0),
            IsDating = TryGet(() => friendship?.IsDating() == true),
            IsEngaged = TryGet(() => friendship?.IsEngaged() == true),
            DaysUntilWedding = TryGet(() => friendship?.CountdownToWedding ?? 0),
            IsDivorced = TryGet(() => friendship?.IsDivorced() == true),
            ProposalRejected = TryGet(() => friendship?.ProposalRejected == true),

            NpcIsChild = TryGet(() => npc?.Age == NPC.child),
            NpcIsAdult = TryGet(() => npc == null || npc.Age == NPC.adult),
            NpcIsDatable = TryGet(() => npc?.datable.Value == true),
            NpcIsMarriedToOther = TryGet(() => npc?.isMarried() == true && !string.Equals(player.spouse, npc.Name, StringComparison.Ordinal)),
            InlawOfSpouse = TryGet(() =>
            {
                string spouse = promptSpouseName;
                if (npc == null || spouse.Length == 0 || string.Equals(npc.Name, spouse, StringComparison.Ordinal))
                {
                    return string.Empty;
                }

                var family = npc.GetData()?.FriendsAndFamily;
                return family != null && family.ContainsKey(spouse) ? spouse : string.Empty;
            }) ?? string.Empty,

            LocationName = currentLocationName,
            LocationDisplayName = currentLocationName.Length == 0
                ? string.Empty
                : TryGet(() => npc?.currentLocation?.DisplayName) ?? string.Empty,
            TileX = TryGet(() => npc?.TilePoint.X ?? 0),
            TileY = TryGet(() => npc?.TilePoint.Y ?? 0),
            IsTravelling = schedule.IsTravelling,
            ScheduleAvailability = schedule.Availability,
            CurrentTravelDestination = schedule.TravelDestination,
            CurrentTravelPurpose = schedule.TravelPurpose.Kind,
            CurrentTravelPurposeConfirmed = schedule.TravelPurpose.Confirmed,
            HomeLocationName = ResolveHomeLocation(npc),
            MightBeInShop = ShopLocations.Contains(currentLocationName, StringComparer.Ordinal),
            CurrentActivity = schedule.CurrentActivity,
            CurrentSchedulePoint = schedule.CurrentStop,
            CurrentScheduleStartTime = schedule.CurrentStopStart,
            NextScheduleLocation = schedule.NextStop,
            MinutesUntilNextSchedule = schedule.MinutesUntilNext,
            NextSchedulePurpose = schedule.NextPurpose.Kind,
            NextSchedulePurposeConfirmed = schedule.NextPurpose.Confirmed,
            RemainingScheduleStops = schedule.RemainingStops,

            FarmerIsMale = player.IsMale,
            FarmerName = player.Name ?? string.Empty,
            FarmerMoney = player.Money,
            FarmerSpouseName = promptSpouseName,
            FarmerIsMarried = TryGet(() => player.isMarriedOrRoommates()),
            Children = TryGet(() => (IReadOnlyList<Content.ChildDescription>)player.getChildren()
                .Select(child => new Content.ChildDescription(child.Name, child.Gender == Gender.Male, child.Age))
                .ToList()) ?? Array.Empty<Content.ChildDescription>(),
            DaysUntilBirth = TryGet<int?>(() =>
            {
                var spouseFriendship = player.GetSpouseFriendship();
                return spouseFriendship != null && spouseFriendship.DaysUntilBirthing > 0
                    ? spouseFriendship.DaysUntilBirthing
                    : null;
            }),

            CommunityCenterDone = HasMail("ccIsComplete"),
            BusFixed = HasMail("ccVault"),
            QuarryBridgeFixed = HasMail("ccCraftsRoom"),
            MinecartFixed = HasMail("ccBoilerRoom"),
            BoulderRemoved = HasMail("ccFishTank"),
            KentAbsent = Game1.year <= 1,

            NearbyNpcNames = CollectNearby(npc),

            FarmBuildings = buildings,
            FarmAnimals = animals,
            FarmCrops = crops,
            FarmPets = pets,

            HasFairyBox = HasTrinket("FairyBox"),
            HasFrogCompanion = HasTrinket("FrogEgg"),
            HasFlyCompanion = HasTrinket("MagicQuiver") || HasTrinket("ParrotEgg"),

            ActiveDialogueEvents = TryGet(() => (IReadOnlyList<KeyValuePair<string, int>>)player.previousActiveDialogueEvents.Pairs
                .Where(pair => !RsvAiPolicy.IsBlockedDialogueKey(pair.Key))
                .Select(pair => new KeyValuePair<string, int>(pair.Key, pair.Value))
                .ToList()) ?? Array.Empty<KeyValuePair<string, int>>(),
            IsBirthdayToday = TryGet(() => npc?.isBirthday() == true),
            TodaysBirthdayOthers = TryGet(() => GetTodaysBirthdayOthers(npc)) ?? string.Empty,
            IsSleepingNow = TryGet(() => npc?.isSleeping.Value == true)
        };
    }

    private static void RegisterRuntimePromptAliases()
    {
        RsvAiPolicy.RegisterGameThreadAliases();
    }

    private static int birthdayCacheDay = -1;
    private static ulong birthdayCacheSaveId = ulong.MaxValue;
    private static List<(string Name, string DisplayName)> birthdayCacheNpcs = new();

    /// <summary>今天过生日的其他村民（全村扫描按游戏日缓存一次）。</summary>
    private static string GetTodaysBirthdayOthers(NPC? self)
    {
        int today = Game1.Date.TotalDays;
        ulong saveId = Game1.uniqueIDForThisGame;
        if (birthdayCacheDay != today || birthdayCacheSaveId != saveId)
        {
            birthdayCacheDay = today;
            birthdayCacheSaveId = saveId;
            birthdayCacheNpcs = new List<(string, string)>();
            foreach (NPC villager in Utility.getAllVillagers())
            {
                if (villager?.isBirthday() == true && !RsvAiPolicy.IsBlockedNpc(villager))
                {
                    birthdayCacheNpcs.Add((villager.Name, villager.displayName ?? villager.Name));
                }
            }
        }

        return string.Join(", ", FilterPromptVisibleNpcNames(birthdayCacheNpcs, self?.Name));
    }

    /// <summary>Filters prompt-facing NPC identities before localized display names are emitted.</summary>
    internal static IReadOnlyList<string> FilterPromptVisibleNpcNames(
        IEnumerable<(string InternalName, string DisplayName)> candidates,
        string? excludedInternalName = null)
    {
        return (candidates ?? Enumerable.Empty<(string, string)>())
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.InternalName))
            .Where(candidate => !string.Equals(candidate.InternalName, excludedInternalName, StringComparison.OrdinalIgnoreCase))
            .Where(candidate => !RsvAiPolicy.IsBlockedNpcName(candidate.InternalName))
            .Select(candidate => string.IsNullOrWhiteSpace(candidate.DisplayName)
                ? candidate.InternalName
                : candidate.DisplayName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<string> CollectWeather(GameLocation? location)
    {
        var flags = new List<string>();
        try
        {
            if (location == null)
            {
                return flags;
            }

            if (location.IsGreenRainingHere())
            {
                flags.Add("green rain");
            }

            if (location.IsLightningHere())
            {
                flags.Add("lightning");
            }

            if (location.IsSnowingHere())
            {
                flags.Add("snow");
            }

            if (location.IsRainingHere())
            {
                flags.Add("rain");
            }
        }
        catch
        {
            // 天气读取失败只损失天气上下文。
        }

        return flags;
    }

    private static string ResolveHomeLocation(NPC? npc)
    {
        try
        {
            if (npc == null)
            {
                return string.Empty;
            }

            var data = npc.GetData();
            if (data != null
                && NPC.ReadNpcHomeData(data, npc.currentLocation, out string locationName, out _, out _)
                && !string.IsNullOrWhiteSpace(locationName))
            {
                return PromptLocationName(locationName);
            }

            return string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static ScheduleSnapshot CollectSchedule(NPC? npc)
    {
        if (npc == null)
        {
            return EmptySchedule(ScheduleAvailability.Missing);
        }

        try
        {
            var travel = npc.DirectionsToNewLocation;
            string rawTravelLocationName = travel?.targetLocationName ?? string.Empty;
            string travelLocationName = PromptLocationName(rawTravelLocationName);
            bool isTravelling = npc.controller != null || npc.isMovingOnPathFindPath.Value;
            SchedulePurposeResult travelPurpose = !isTravelling || travel == null || travelLocationName.Length == 0
                ? default
                : SchedulePurposeResolver.Resolve(
                    travelLocationName,
                    travel.endOfRouteBehavior ?? string.Empty,
                    travel.endOfRouteMessage ?? string.Empty,
                    Game1.seasonIndex,
                    Game1.dayOfMonth);

            var schedule = npc.Schedule;
            if (schedule == null || schedule.Count == 0)
            {
                return EmptySchedule(
                    ScheduleAvailability.Missing,
                    isTravelling,
                    DisplayNameFor(travelLocationName),
                    travelPurpose);
            }

            int now = Game1.timeOfDay;
            var ordered = schedule.OrderBy(pair => pair.Key).ToList();
            var current = ordered.LastOrDefault(pair => pair.Key <= now);
            if (current.Value != null && RsvAiPolicy.IsBlockedLocationName(current.Value.targetLocationName))
            {
                current = default;
            }

            var upcoming = ordered
                .Where(pair => pair.Key > now)
                .Where(pair => pair.Value != null && !RsvAiPolicy.IsBlockedLocationName(pair.Value.targetLocationName))
                .ToList();
            var next = upcoming.FirstOrDefault();

            string currentStop = current.Value != null
                ? DisplayNameFor(current.Value.targetLocationName)
                : string.Empty;
            int? currentStopStart = current.Value != null ? current.Key : null;
            string nextStop = next.Value != null ? DisplayNameFor(next.Value.targetLocationName) : string.Empty;
            int? minutes = next.Value != null ? Math.Max(0, Utility.CalculateMinutesBetweenTimes(now, next.Key)) : null;
            SchedulePurposeResult nextPurpose = next.Value == null
                ? default
                : SchedulePurposeResolver.Resolve(
                    next.Value.targetLocationName ?? string.Empty,
                    next.Value.endOfRouteBehavior ?? string.Empty,
                    next.Value.endOfRouteMessage ?? string.Empty,
                    Game1.seasonIndex,
                    Game1.dayOfMonth);

            var remaining = upcoming
                .Select(pair => DisplayNameFor(pair.Value.targetLocationName))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            string activity = isTravelling
                ? string.Empty
                : ScheduleActivityNormalizer.Describe(
                    current.Value?.endOfRouteBehavior ?? string.Empty,
                    current.Value?.endOfRouteMessage ?? string.Empty);

            return new(
                ScheduleAvailability.Available,
                activity,
                currentStop,
                currentStopStart,
                nextStop,
                minutes,
                nextPurpose,
                remaining,
                isTravelling,
                DisplayNameFor(travelLocationName),
                travelPurpose);
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log(
                $"Failed to read runtime schedule for {npc.Name}: {ex.Message}",
                StardewModdingAPI.LogLevel.Debug);
            return EmptySchedule(ScheduleAvailability.ReadFailed);
        }
    }

    private static ScheduleSnapshot EmptySchedule(
        ScheduleAvailability availability,
        bool isTravelling = false,
        string travelDestination = "",
        SchedulePurposeResult travelPurpose = default)
        => new(
            availability,
            string.Empty,
            string.Empty,
            null,
            string.Empty,
            null,
            default,
            Array.Empty<string>(),
            isTravelling,
            travelDestination,
            travelPurpose);
    private static string DisplayNameFor(string? locationName)
    {
        string promptLocationName = PromptLocationName(locationName);
        if (promptLocationName.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            return Game1.getLocationFromName(promptLocationName)?.DisplayName ?? promptLocationName;
        }
        catch
        {
            return promptLocationName;
        }
    }

    internal static string PromptLocationName(string? locationName)
    {
        return string.IsNullOrWhiteSpace(locationName) || RsvAiPolicy.IsBlockedLocationName(locationName)
            ? string.Empty
            : locationName;
    }

    private static IReadOnlyList<string> CollectNearby(NPC? npc)
    {
        try
        {
            var location = npc?.currentLocation;
            if (npc == null || location == null)
            {
                return Array.Empty<string>();
            }

            var origin = npc.TilePoint;
            var candidates = location.characters
                .Where(other => other != null
                    && !ReferenceEquals(other, npc)
                    && other.IsVillager
                    && Math.Abs(other.TilePoint.X - origin.X) + Math.Abs(other.TilePoint.Y - origin.Y) <= NearbyTileRadius)
                .Select(other => (InternalName: other.Name, DisplayName: other.displayName ?? other.Name));
            return FilterPromptVisibleNpcNames(candidates, npc.Name);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static (IReadOnlyList<string> Buildings, IReadOnlyList<string> Animals, IReadOnlyList<string> Crops, IReadOnlyList<string> Pets)
        CollectFarm()
    {
        try
        {
            var farm = Game1.getFarm();
            var buildings = farm.buildings
                .Select(building => building.buildingType.Value)
                .Where(type => !string.IsNullOrWhiteSpace(type))
                .Where(type => !RsvAiPolicy.IsBlockedContentId(type))
                .GroupBy(type => type, StringComparer.Ordinal)
                .Select(group => group.Count() > 1 ? $"{group.Key} x{group.Count()}" : group.Key)
                .ToList();

            var animals = farm.getAllFarmAnimals()
                .Where(animal => !RsvAiPolicy.IsBlockedContentId(animal.type.Value))
                .Select(animal => animal.displayType ?? animal.type.Value)
                .Where(type => !string.IsNullOrWhiteSpace(type)
                    && !RsvAiPolicy.ContainsBlockedReference(type))
                .GroupBy(type => type, StringComparer.Ordinal)
                .Select(group => group.Count() > 1 ? $"{group.Key} x{group.Count()}" : group.Key)
                .ToList();

            var crops = farm.terrainFeatures.Values
                .OfType<HoeDirt>()
                .Select(dirt => dirt.crop)
                .Where(crop => crop != null && !crop.dead.Value)
                .Select(crop => TryGet(() => crop!.GetData()?.HarvestItemId) ?? string.Empty)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Where(id => !RsvAiPolicy.IsBlockedContentId(id))
                .Select(id => TryGet(() => ItemRegistry.GetDataOrErrorItem(id).DisplayName) ?? id)
                .GroupBy(name => name, StringComparer.Ordinal)
                .Select(group => group.Count() > 1 ? $"{group.Key} x{group.Count()}" : group.Key)
                .ToList();

            var pets = farm.characters
                .OfType<Pet>()
                .Select(pet => pet.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();

            return (buildings, animals, crops, pets);
        }
        catch
        {
            return (Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
        }
    }

    private static bool HasMail(string flag)
    {
        return TryGet(() => Game1.MasterPlayer?.mailReceived.Contains(flag) == true || Game1.player.mailReceived.Contains(flag));
    }

    private static bool HasTrinket(string trinketId)
    {
        return TryGet(() => Game1.player.trinketItems.Any(trinket =>
            trinket != null && trinket.ItemId.Contains(trinketId, StringComparison.OrdinalIgnoreCase)));
    }

    private static T? TryGet<T>(Func<T?> reader)
    {
        try
        {
            return reader();
        }
        catch
        {
            return default;
        }
    }
}

/// <summary>
/// 日程 endOfRouteBehavior/endOfRouteMessage → 活动短语（WP10 §4.3 关键词映射）。
/// 纯字符串逻辑，可单测。
/// </summary>
internal static class ScheduleActivityNormalizer
{
    private static readonly (string Keyword, string Key)[] Map =
    {
        ("book", "activityReading"),
        ("read", "activityReading"),
        ("fish", "activityFishing"),
        ("drink", "activityDrinking"),
        ("beer", "activityDrinking"),
        ("work", "activityWorking"),
        ("shop", "activityWorking"),
        ("sit", "activitySitting"),
        ("bench", "activitySitting"),
        ("exercise", "activityExercising"),
        ("lift", "activityExercising"),
        ("yoga", "activityExercising"),
        ("music", "activityMusic"),
        ("flute", "activityMusic"),
        ("guitar", "activityMusic"),
        ("dance", "activityDancing"),
        ("sleep", "activityResting"),
        ("rest", "activityResting"),
        ("nap", "activityResting")
    };

    /// <summary>命中关键词 → WP20 活动键；不命中 → 带原始提示的通用描述键。</summary>
    public static string Describe(string endOfRouteBehavior, string endOfRouteMessage)
    {
        string hint = $"{endOfRouteBehavior} {endOfRouteMessage}".Trim();
        if (hint.Length == 0)
        {
            return string.Empty;
        }

        foreach (var (keyword, key) in Map)
        {
            if (hint.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return Util.GetString(key, new { hint });
            }
        }

        return Util.GetString("activityGeneric", new { hint });
    }
}
