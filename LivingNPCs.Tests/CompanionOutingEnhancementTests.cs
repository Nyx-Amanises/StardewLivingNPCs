using System;
using System.Collections.Generic;
using LivingNPCs.Behavior;

namespace LivingNPCs.Tests;

/// <summary>
/// 出游增强的规则层钉子：载具目的地（沙漠/姜岛/电影院/温泉）的网关映射与解锁门、
/// 目的地目录与别名、tick 计划的新相位、并肩行走步进决策、闲聊气泡节拍与文案池选择。
/// 全部走纯逻辑（无游戏状态），与 CompanionOutingTickPlanTests 同风格。
/// </summary>
public sealed class CompanionOutingEnhancementTests
{
    private sealed class FakeOutingWorldView : IOutingWorldView
    {
        public int TimeOfDay { get; set; } = 1200;
        public int TotalDays { get; set; } = 100;
        public bool IsMasterGame { get; set; } = true;
        public bool IsFestivalToday { get; set; }
        public bool IsLightning { get; set; }
        public bool IsEventActive { get; set; }
        public bool IsMenuOpen { get; set; }
        public string CurrentLocationName { get; set; } = "Town";
    }

    // ---- 载具网关映射 ----

    [Theory]
    [InlineData("Desert", "BusStop", "bus")]
    [InlineData("IslandSouth", "Beach", "boat")]
    [InlineData("MovieTheater", "Town", "theater")]
    [InlineData("BathHouse_Entry", "Mountain", "spa")]
    public void VehicleTargetsMapToRoutableGateways(string target, string expectedGateway, string expectedKind)
    {
        Assert.True(OutingVehicleRules.IsVehicleTarget(target));
        Assert.True(OutingVehicleRules.TryGetVehicle(target, out OutingVehicle vehicle));
        Assert.Equal(expectedGateway, vehicle.GatewayLocation);
        Assert.Equal(expectedKind, vehicle.Kind);
    }

    [Theory]
    [InlineData("Beach")]
    [InlineData("Town")]
    [InlineData("Saloon")]
    [InlineData("")]
    public void WalkableTargetsAreNotVehicles(string target)
    {
        Assert.False(OutingVehicleRules.IsVehicleTarget(target));
        Assert.False(OutingVehicleRules.TryGetVehicle(target, out _));
    }

    // ---- 解锁门 ----

    [Fact]
    public void DesertRequiresRepairedBusFromEitherRoute()
    {
        var mail = new HashSet<string>();
        Assert.False(OutingVehicleRules.IsUnlocked("Desert", mail.Contains, 999, out string reason));
        Assert.Contains("bus", reason, StringComparison.OrdinalIgnoreCase);

        mail.Add("ccVault");
        Assert.True(OutingVehicleRules.IsUnlocked("Desert", mail.Contains, 999, out _));

        var jojaMail = new HashSet<string> { "jojaVault" };
        Assert.True(OutingVehicleRules.IsUnlocked("Desert", jojaMail.Contains, 999, out _));
    }

    [Fact]
    public void IslandRequiresWillysBoat()
    {
        var mail = new HashSet<string>();
        Assert.False(OutingVehicleRules.IsUnlocked("IslandSouth", mail.Contains, 999, out string reason));
        Assert.Contains("boat", reason, StringComparison.OrdinalIgnoreCase);

        mail.Add("willyBoatFixed");
        Assert.True(OutingVehicleRules.IsUnlocked("IslandSouth", mail.Contains, 999, out _));
    }

    [Fact]
    public void MovieTheaterRequiresBeingBuilt()
    {
        var mail = new HashSet<string>();
        Assert.False(OutingVehicleRules.IsUnlocked("MovieTheater", mail.Contains, 999, out string reason));
        Assert.Contains("theater", reason, StringComparison.OrdinalIgnoreCase);

        Assert.True(OutingVehicleRules.IsUnlocked("MovieTheater", new HashSet<string> { "ccMovieTheater" }.Contains, 999, out _));
        Assert.True(OutingVehicleRules.IsUnlocked("MovieTheater", new HashSet<string> { "ccMovieTheaterJoja" }.Contains, 999, out _));
    }

    [Fact]
    public void SpaOpensWithTheRailroadOnDay31()
    {
        static bool NoMail(string id) => false;
        Assert.False(OutingVehicleRules.IsUnlocked("BathHouse_Entry", NoMail, 30, out string reason));
        Assert.Contains("railroad", reason, StringComparison.OrdinalIgnoreCase);
        Assert.True(OutingVehicleRules.IsUnlocked("BathHouse_Entry", NoMail, 31, out _));
    }

    [Fact]
    public void WalkableTargetsAreAlwaysUnlocked()
    {
        static bool NoMail(string id) => false;
        Assert.True(OutingVehicleRules.IsUnlocked("Beach", NoMail, 1, out _));
        Assert.True(OutingVehicleRules.IsUnlocked("Saloon", NoMail, 1, out _));
    }

    // ---- 目的地目录 / 别名 / 标签 ----

    [Theory]
    [InlineData("Desert")]
    [InlineData("IslandSouth")]
    [InlineData("MovieTheater")]
    [InlineData("BathHouse_Entry")]
    public void NewDestinationsAreKnownPublicOutingTargets(string target)
    {
        Assert.True(TravelLocationRules.IsKnownPublicOutingTarget(target));
        Assert.Contains(target, TravelLocationRules.KnownPublicOutingTargets);
    }

    [Theory]
    [InlineData("沙漠", "Desert")]
    [InlineData("Calico Desert", "Desert")]
    [InlineData("姜岛", "IslandSouth")]
    [InlineData("Ginger Island", "IslandSouth")]
    [InlineData("电影院", "MovieTheater")]
    [InlineData("Movie Theater", "MovieTheater")]
    [InlineData("温泉", "BathHouse_Entry")]
    [InlineData("Spa", "BathHouse_Entry")]
    [InlineData("BathHouse", "BathHouse_Entry")]
    public void NewDestinationAliasesNormalize(string alias, string expected)
    {
        Assert.Equal(expected, TravelLocationRules.Normalize(alias, string.Empty));
    }

    [Theory]
    [InlineData("Desert")]
    [InlineData("IslandSouth")]
    [InlineData("MovieTheater")]
    [InlineData("BathHouse_Entry")]
    public void NewDestinationsHaveHumanLabels(string target)
    {
        Assert.NotEqual(target, TravelLocationRules.GetLabel(target));
        Assert.NotEqual(target, TravelLocationRules.GetChineseLabel(target));
    }

    // ---- 活动风格 ----

    [Theory]
    [InlineData("Desert", "", "scenic")]
    [InlineData("IslandSouth", "", "scenic")]
    [InlineData("MovieTheater", "", "browse")]
    [InlineData("BathHouse_Entry", "", "quiet")]
    [InlineData("Beach", "一起去看场电影吧", "browse")]
    [InlineData("Beach", "let's soak at the hot spring", "quiet")]
    public void ActivityStyleCoversNewDestinationsAndKeywords(string target, string reason, string expected)
    {
        Assert.Equal(expected, CompanionOutingRules.DetermineActivityStyle(target, reason));
    }

    // ---- tick 计划新相位 ----

    [Fact]
    public void VehicleGatewayPhaseIsATravelingPhase()
    {
        var world = new FakeOutingWorldView();
        Assert.True(CompanionOutingRules.IsTravelingPhase(CompanionOutingPhase.TravelingToVehicleGateway));
        Assert.Equal(
            CompanionOutingTickPlan.AdvanceTravel,
            CompanionOutingRules.PlanTick(world, outingTotalDays: 100, npcExists: true, CompanionOutingPhase.TravelingToVehicleGateway));

        world.TimeOfDay = CompanionOutingRules.LatestPlannedStayEndTime;
        Assert.Equal(
            CompanionOutingTickPlan.StopForLateTravel,
            CompanionOutingRules.PlanTick(world, outingTotalDays: 100, npcExists: true, CompanionOutingPhase.TravelingToVehicleGateway));
    }

    // ---- 并肩行走步进 ----

    [Fact]
    public void StrollOwnsNothingWhenDisabledOrApart()
    {
        Assert.Equal(
            OutingStrollStep.None,
            OutingCompanionshipRules.PlanStrollStep(strollEnabled: false, farmerAtDestination: true, npcAtDestination: true, 5f, 0));
        Assert.Equal(
            OutingStrollStep.None,
            OutingCompanionshipRules.PlanStrollStep(strollEnabled: true, farmerAtDestination: false, npcAtDestination: true, 5f, 0));
        Assert.Equal(
            OutingStrollStep.None,
            OutingCompanionshipRules.PlanStrollStep(strollEnabled: true, farmerAtDestination: true, npcAtDestination: false, 5f, 0));
    }

    [Fact]
    public void StrollFollowsWhenFarAndSettlesWhenClose()
    {
        Assert.Equal(
            OutingStrollStep.FollowFarmer,
            OutingCompanionshipRules.PlanStrollStep(true, true, true, OutingCompanionshipRules.FollowTriggerDistanceTiles + 1f, ticksSinceFarmerMoved: 10_000));
        Assert.Equal(
            OutingStrollStep.SettleBesideFarmer,
            OutingCompanionshipRules.PlanStrollStep(true, true, true, OutingCompanionshipRules.SettleDistanceTiles, ticksSinceFarmerMoved: 0));
    }

    [Fact]
    public void StrollMidBandUsesFarmerMovementHysteresis()
    {
        float midBand = (OutingCompanionshipRules.SettleDistanceTiles + OutingCompanionshipRules.FollowTriggerDistanceTiles) / 2f;
        Assert.Equal(
            OutingStrollStep.FollowFarmer,
            OutingCompanionshipRules.PlanStrollStep(true, true, true, midBand, ticksSinceFarmerMoved: 0));
        Assert.Equal(
            OutingStrollStep.SettleBesideFarmer,
            OutingCompanionshipRules.PlanStrollStep(true, true, true, midBand, ticksSinceFarmerMoved: OutingCompanionshipRules.FarmerRecentMoveTicks + 1));
    }

    // ---- 闲聊节拍与文案池 ----

    [Fact]
    public void ChatFiresOnlyWhenAllGatesOpen()
    {
        Assert.True(OutingCompanionshipRules.ShouldShowChat(
            chatEnabled: true, farmerPresent: true, arrivalRemarkShown: true,
            chatRemarksShown: 0, timeOfDay: 1300, nextChatTimeOfDay: 1250,
            distanceToFarmerTiles: 4f, maxDistanceTiles: 8f));

        // 每个门各自关一次。
        Assert.False(OutingCompanionshipRules.ShouldShowChat(false, true, true, 0, 1300, 1250, 4f, 8f));
        Assert.False(OutingCompanionshipRules.ShouldShowChat(true, false, true, 0, 1300, 1250, 4f, 8f));
        Assert.False(OutingCompanionshipRules.ShouldShowChat(true, true, false, 0, 1300, 1250, 4f, 8f));
        Assert.False(OutingCompanionshipRules.ShouldShowChat(true, true, true, OutingCompanionshipRules.MaxChatRemarksPerOuting, 1300, 1250, 4f, 8f));
        Assert.False(OutingCompanionshipRules.ShouldShowChat(true, true, true, 0, 1240, 1250, 4f, 8f));
        Assert.False(OutingCompanionshipRules.ShouldShowChat(true, true, true, 0, 1300, 0, 4f, 8f));
        Assert.False(OutingCompanionshipRules.ShouldShowChat(true, true, true, 0, 1300, 1250, 9f, 8f));
    }

    [Fact]
    public void ChatCadenceIsTwentyToFortyMinutesAndDeterministic()
    {
        int first = OutingCompanionshipRules.NextChatTimeOfDay("Abigail", "IslandSouth", 100, 0, 1200);
        int again = OutingCompanionshipRules.NextChatTimeOfDay("Abigail", "IslandSouth", 100, 0, 1200);
        Assert.Equal(first, again);

        int minutesAhead = BehaviorTimeMath.GetElapsedMinutes(1200, first);
        Assert.InRange(
            minutesAhead,
            OutingCompanionshipRules.MinChatIntervalMinutes,
            OutingCompanionshipRules.MinChatIntervalMinutes + OutingCompanionshipRules.ChatIntervalJitterMinutes - 1);
    }

    [Theory]
    [InlineData("Desert", "scenic", "outing.chat.desert.")]
    [InlineData("IslandSouth", "scenic", "outing.chat.island.")]
    [InlineData("MovieTheater", "browse", "outing.chat.theater.")]
    [InlineData("BathHouse_Entry", "quiet", "outing.chat.spa.")]
    [InlineData("Beach", "scenic", "outing.chat.scenic.")]
    [InlineData("Saloon", "social", "outing.chat.social.")]
    [InlineData("Town", "festival", "outing.chat.quiet.")]
    [InlineData("Hospital", "visit", "outing.chat.visit.")]
    public void ChatKeyUsesDestinationPoolThenStylePool(string target, string style, string expectedPrefix)
    {
        string key = OutingCompanionshipRules.SelectChatKey(
            target, style, "Abigail", 100, chatRemarksShown: 0, previousVariant: 0, strollingNow: false, out int variant);
        Assert.StartsWith(expectedPrefix, key, StringComparison.Ordinal);
        Assert.InRange(variant, 1, OutingCompanionshipRules.ChatPoolVariants);
        Assert.EndsWith(variant.ToString(), key, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatVariantNeverRepeatsBackToBack()
    {
        int previous = 0;
        for (int count = 0; count < 12; count++)
        {
            OutingCompanionshipRules.SelectChatKey(
                "IslandSouth", "scenic", "Abigail", 100, count, previous, strollingNow: false, out int variant);
            Assert.InRange(variant, 1, OutingCompanionshipRules.ChatPoolVariants);
            Assert.NotEqual(previous, variant);
            previous = variant;
        }
    }

    [Fact]
    public void StrollSlotsAlternateIntoWalkingTogetherPool()
    {
        // 并肩行走进行中：奇数槽位换用同行文案池，偶数槽位保持目的地池。
        string oddKey = OutingCompanionshipRules.SelectChatKey(
            "Desert", "scenic", "Abigail", 100, chatRemarksShown: 1, previousVariant: 0, strollingNow: true, out _);
        Assert.StartsWith("outing.chat.stroll.", oddKey, StringComparison.Ordinal);

        string evenKey = OutingCompanionshipRules.SelectChatKey(
            "Desert", "scenic", "Abigail", 100, chatRemarksShown: 2, previousVariant: 0, strollingNow: true, out _);
        Assert.StartsWith("outing.chat.desert.", evenKey, StringComparison.Ordinal);
    }
}
