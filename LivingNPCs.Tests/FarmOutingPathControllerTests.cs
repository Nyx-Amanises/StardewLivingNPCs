using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using LivingNPCs.Behavior;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Netcode;
using StardewValley;
using StardewValley.Network;
using StardewValley.Pathfinding;
using xTile;
using xTile.Dimensions;
using xTile.Layers;
using xTile.Tiles;
using Rectangle = Microsoft.Xna.Framework.Rectangle;

namespace LivingNPCs.Tests;

[CollectionDefinition("Farm outing game collision", DisableParallelization = true)]
public sealed class FarmOutingGameCollisionCollection
{
}

[Collection("Farm outing game collision")]
public sealed class FarmOutingPathControllerTests : IDisposable
{
    private static readonly Point BarrierTile = new(6, 2);
    private static readonly FieldInfo PlayerField = AccessTools.Field(typeof(Game1), "_player");
    private readonly object? previousPlayer = PlayerField.GetValue(null);
    private readonly Game1 previousGame = Game1.game1;
    private readonly NetRoot<NetWorldState> previousWorldState = Game1.netWorldState;
    private readonly NetRootDictionary<long, Farmer> previousOtherFarmers = Game1.otherFarmers;
    private readonly Harmony harmony = new("LivingNPCs.Tests.FarmOutingCollision");

    public FarmOutingPathControllerTests()
    {
        Game1.game1 = (Game1)RuntimeHelpers.GetUninitializedObject(typeof(Game1));
        GC.SuppressFinalize(Game1.game1);
        Game1.netWorldState = new(new NetWorldState());
        Game1.otherFarmers = new NetRootDictionary<long, Farmer>();
        // NPC.CurrentDialogue only needs the player's spouse field for these map-only tests.
        var player = (Farmer)RuntimeHelpers.GetUninitializedObject(typeof(Farmer));
        AccessTools.Field(typeof(Farmer), "netSpouse").SetValue(player, new NetString());
        AccessTools.Field(typeof(Character), "currentLocationRef").SetValue(player, new NetLocationRef());
        PlayerField.SetValue(null, player);
        player.currentLocation = null!;
        FarmOutingCollisionPatch.Apply(this.harmony);
    }

    public void Dispose()
    {
        this.harmony.UnpatchAll(this.harmony.Id);
        PlayerField.SetValue(null, this.previousPlayer);
        Game1.game1 = this.previousGame;
        Game1.netWorldState = this.previousWorldState;
        Game1.otherFarmers = this.previousOtherFarmers;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvitedNpcWalksThroughFarmBarrierInBothDirections(bool returning)
    {
        GameLocation farm = CreateFarmCorridor();
        Point start = returning ? new Point(2, 2) : new Point(7, 2);
        Point target = returning ? new Point(7, 2) : new Point(2, 2);
        NPC npc = CreateNpc(farm, start);
        Assert.Null(new PathFindController(npc, farm, target, 2).pathToEndPoint);
        Vector2 originalPosition = npc.Position;

        Assert.True(NpcTravelRuntime.TryAssignVanillaScheduleRoute(npc, farm, target, 2, out var route));
        Assert.IsType<FarmOutingPathController>(route);
        Assert.Contains(BarrierTile, route!.pathToEndPoint);
        Assert.Equal(originalPosition, npc.Position);
        Assert.False(IsBlocked(farm, npc, BarrierTile, pathfinding: false));

        var time = new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(16));
        bool completed = false;
        for (int tick = 0; tick < 500 && !completed; tick++)
        {
            Vector2 before = npc.Position;
            completed = route.update(time);
            Assert.InRange(Vector2.Distance(before, npc.Position), 0f, 4f);
        }

        Assert.True(completed);
        Assert.Equal(target, npc.TilePoint);
        Assert.False(npc.EventActor);
        Assert.Equal("T", farm.Map.GetLayer("Back").Tiles[BarrierTile.X, BarrierTile.Y].Properties["NPCBarrier"].ToString());
    }

    [Theory]
    [InlineData(71, false)]
    [InlineData(72, false)]
    [InlineData(71, true)]
    [InlineData(72, true)]
    public void StandardFarmLogCoordinatesAreReachableDuringOuting(int anchorX, bool returning)
    {
        using var content = new ContentManager(new EmptyServices(), Path.Combine(AppContext.BaseDirectory, "GameContent"));
        GameLocation farm = CreateFarmCorridor();
        farm.map = content.Load<Map>("Maps/Farm");
        Point entrance = new(79, 17);
        Point anchor = new(anchorX, 23);
        Point start = returning ? anchor : entrance;
        Point target = returning ? entrance : anchor;
        NPC npc = CreateNpc(farm, start);
        Assert.Null(new PathFindController(npc, farm, target, 2).pathToEndPoint);

        Assert.True(NpcTravelRuntime.TryAssignVanillaScheduleRoute(npc, farm, target, 2, out var route));
        Assert.Contains(route!.pathToEndPoint, tile =>
            farm.Map.GetLayer("Back").Tiles[tile.X, tile.Y]?.Properties.ContainsKey("NPCBarrier") == true);
        var time = new GameTime(TimeSpan.Zero, TimeSpan.FromMilliseconds(16));
        bool completed = false;
        for (int tick = 0; tick < 1500 && !completed; tick++)
        {
            Vector2 before = npc.Position;
            completed = route.update(time);
            Assert.InRange(Vector2.Distance(before, npc.Position), 0f, 4f);
        }

        Assert.True(completed);
        Assert.Equal(target, npc.TilePoint);
        Assert.False(npc.EventActor);
        npc.controller = null;
        Assert.False(FarmOutingPathController.CanCrossNpcBarrier(npc, farm));
    }

    [Fact]
    public void BarrierPermissionBelongsToOneNpcAndOneFarmController()
    {
        GameLocation farm = CreateFarmCorridor();
        NPC penny = CreateNpc(farm, new Point(7, 2));
        NPC other = CreateNpc(farm, new Point(7, 2), "Haley");
        Assert.True(NpcTravelRuntime.TryAssignVanillaScheduleRoute(penny, farm, new Point(2, 2), 2, out var route));

        Assert.False(IsBlocked(farm, penny, BarrierTile));
        Assert.True(IsBlocked(farm, other, BarrierTile));
        other.controller = route;
        Assert.True(IsBlocked(farm, other, BarrierTile));
        Assert.False(FarmOutingPathController.CanCrossNpcBarrier(penny, CreateFarmCorridor()));

        penny.controller = new PathFindController(new Stack<Point>(), farm, penny, Point.Zero);
        Assert.True(IsBlocked(farm, penny, BarrierTile));
        penny.controller = null;
        Assert.True(IsBlocked(farm, penny, BarrierTile));
    }

    [Theory]
    [InlineData("building")]
    [InlineData("temporaryBarrier")]
    [InlineData("water")]
    [InlineData("object")]
    public void InvitedNpcStillRespectsRealObstacles(string obstacleKind)
    {
        GameLocation farm = CreateFarmCorridor();
        NPC npc = CreateNpc(farm, new Point(7, 2));
        Assert.True(NpcTravelRuntime.TryAssignVanillaScheduleRoute(npc, farm, new Point(2, 2), 2, out _));
        Tile back = farm.Map.GetLayer("Back").Tiles[BarrierTile.X, BarrierTile.Y];
        switch (obstacleKind)
        {
            case "building":
                Layer buildings = farm.Map.GetLayer("Buildings");
                buildings.Tiles[BarrierTile.X, BarrierTile.Y] = new StaticTile(buildings, back.TileSheet, BlendMode.Alpha, 0);
                break;
            case "temporaryBarrier":
                back.Properties["TemporaryBarrier"] = "T";
                break;
            case "water":
                back.Properties["Water"] = "T";
                back.Properties["Passable"] = "F";
                break;
            case "object":
                farm.objects[new Vector2(BarrierTile.X, BarrierTile.Y)] = new BlockingObject
                {
                    TileLocation = new Vector2(BarrierTile.X, BarrierTile.Y)
                };
                break;
        }

        Assert.False(NpcTravelRuntime.TryAssignVanillaScheduleRoute(npc, farm, new Point(2, 2), 2, out var failed));
        Assert.Null(failed);
        Assert.Null(npc.controller);
        Assert.True(IsBlocked(farm, npc, BarrierTile, pathfinding: true));
        Assert.True(IsBlocked(farm, npc, BarrierTile, pathfinding: false));
        Assert.False(FarmOutingPathController.CanCrossNpcBarrier(npc, farm));
        if (obstacleKind == "object")
        {
            Assert.IsType<BlockingObject>(farm.objects[new Vector2(BarrierTile.X, BarrierTile.Y)]);
        }
    }

    [Fact]
    public void NonFarmRouteKeepsNormalNpcBarrierRules()
    {
        GameLocation otherFarm = CreateFarmCorridor();
        SetLocationName(otherFarm, "OtherFarm");
        NPC npc = CreateNpc(otherFarm, new Point(7, 2));

        Assert.False(NpcTravelRuntime.TryAssignVanillaScheduleRoute(npc, otherFarm, new Point(2, 2), 2, out _));
        Assert.True(IsBlocked(otherFarm, npc, BarrierTile));
        Assert.False(FarmOutingPathController.CanCrossNpcBarrier(npc, otherFarm));
    }

    [Fact]
    public void FailedConstructorRestoresBarrierRules()
    {
        GameLocation farm = CreateFarmCorridor();
        NPC npc = CreateNpc(farm, new Point(7, 2));
        Map map = farm.map;
        farm.map = null!;

        Assert.ThrowsAny<Exception>(() => FarmOutingPathController.Create(npc, farm, new Point(2, 2), 2));

        farm.map = map;
        Assert.False(FarmOutingPathController.CanCrossNpcBarrier(npc, farm));
        Assert.True(IsBlocked(farm, npc, BarrierTile));
    }

    [Fact]
    public void TranspilerLeavesAmbiguousGameMethodUnchanged()
    {
        var getter = AccessTools.PropertyGetter(typeof(Character), nameof(Character.EventActor));
        var instructions = new[] { new CodeInstruction(OpCodes.Callvirt, getter), new CodeInstruction(OpCodes.Callvirt, getter) };

        var patched = FarmOutingCollisionPatch.Transpiler(instructions).ToArray();

        Assert.Equal(2, patched.Length);
        Assert.All(patched, instruction => Assert.True(instruction.Calls(getter)));
    }

    private static NPC CreateNpc(GameLocation location, Point tile, string name = "Penny")
    {
        return new NPC
        {
            Name = name,
            Sprite = new AnimatedSprite(),
            Position = new Vector2(tile.X * 64, tile.Y * 64),
            currentLocation = location,
            TemporaryDialogue = new Stack<StardewValley.Dialogue>(),
            ignoreMovementAnimation = true,
            Speed = 2
        };
    }

    private static bool IsBlocked(GameLocation location, NPC npc, Point tile, bool pathfinding = true)
    {
        return location.isCollidingPosition(
            new Rectangle(tile.X * 64 + 1, tile.Y * 64 + 1, 62, 62),
            Game1.viewport, isFarmer: false, 0, glider: false, npc, pathfinding);
    }

    private static GameLocation CreateFarmCorridor()
    {
        var map = new Map();
        var tileSheet = new TileSheet("test", map, "unused.png", new Size(1, 1), new Size(16, 16));
        map.AddTileSheet(tileSheet);
        var back = new Layer("Back", map, new Size(9, 5), new Size(64, 64));
        var buildings = new Layer("Buildings", map, new Size(9, 5), new Size(64, 64));
        map.AddLayer(back);
        map.AddLayer(buildings);
        for (int x = 0; x < 9; x++)
        {
            for (int y = 0; y < 5; y++)
            {
                back.Tiles[x, y] = new StaticTile(back, tileSheet, BlendMode.Alpha, 0);
                if (x is 0 or 8 || y != 2)
                {
                    buildings.Tiles[x, y] = new StaticTile(buildings, tileSheet, BlendMode.Alpha, 0);
                }
            }
        }

        back.Tiles[BarrierTile.X, BarrierTile.Y].Properties["NPCBarrier"] = "T";
        var location = new GameLocation { map = map, IsFarm = true };
        SetLocationName(location, "Farm");
        return location;
    }

    private static void SetLocationName(GameLocation location, string name)
    {
        // GameLocation.Name has no setter; map-only fixtures set its backing net field.
#pragma warning disable AvoidNetField
        location.name.Value = name;
#pragma warning restore AvoidNetField
    }

    private sealed class EmptyServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class BlockingObject : StardewValley.Object
    {
        public override bool isPassable() => false;
    }
}
