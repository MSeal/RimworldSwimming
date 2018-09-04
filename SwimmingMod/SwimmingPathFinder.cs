using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Swimming {

    /*
     * It was easier to copy the ENTIRE class to be able to overwrite the giant FindPath method correctly
     */
    public class SwimmerPathFinder {
        protected struct CostNode {
            public int index;

            public int cost;

            public CostNode(int index, int cost)
            {
                this.index = index;
                this.cost = cost;
            }
        }

        protected struct PathFinderNodeFast {
            public int knownCost;

            public int heuristicCost;

            public int parentIndex;

            public int costNodeCost;

            public ushort status;
        }

        protected class CostNodeComparer : IComparer<SwimmerPathFinder.CostNode> {
            public int Compare(SwimmerPathFinder.CostNode a, SwimmerPathFinder.CostNode b)
            {
                return a.cost.CompareTo(b.cost);
            }
        }

        protected Map map;

        protected FastPriorityQueue<SwimmerPathFinder.CostNode> openList;

        protected SwimmerPathFinder.PathFinderNodeFast[] calcGrid;

        protected ushort statusOpenValue = 1;

        protected ushort statusClosedValue = 2;

        protected RegionCostCalculatorWrapper regionCostCalculator;

        protected int mapSizeX;

        protected int mapSizeZ;

        protected PathGrid pathGrid;

        protected Building[] edificeGrid;

        protected List<Blueprint>[] blueprintGrid;

        protected CellIndices cellIndices;

        protected List<int> disallowedCornerIndices = new List<int>(4);

        public const int DefaultMoveTicksCardinal = 13;

        protected const int DefaultMoveTicksDiagonal = 18;

        protected const int SearchLimit = 160000;

        protected static readonly int[] Directions = new int[]
        {
            0,
            1,
            0,
            -1,
            1,
            1,
            -1,
            -1,
            -1,
            0,
            1,
            0,
            -1,
            1,
            1,
            -1
        };

        protected const int Cost_DoorToBash = 300;

        protected const int Cost_BlockedWallBase = 70;

        protected const float Cost_BlockedWallExtraPerHitPoint = 0.2f;

        protected const int Cost_BlockedDoor = 50;

        protected const float Cost_BlockedDoorPerHitPoint = 0.2f;

        public const int Cost_OutsideAllowedArea = 600;

        protected const int Cost_PawnCollision = 175;

        protected const int NodesToOpenBeforeRegionBasedPathing_NonColonist = 2000;

        protected const int NodesToOpenBeforeRegionBasedPathing_Colonist = 100000;

        protected const float NonRegionBasedHeuristicStrengthAnimal = 1.75f;

        protected static readonly SimpleCurve NonRegionBasedHeuristicStrengthHuman_DistanceCurve = new SimpleCurve
        {
            {
                new CurvePoint(40f, 1f),
                true
            },
            {
                new CurvePoint(120f, 2.8f),
                true
            }
        };

        protected static readonly SimpleCurve RegionHeuristicWeightByNodesOpened = new SimpleCurve
        {
            {
                new CurvePoint(0f, 1f),
                true
            },
            {
                new CurvePoint(3500f, 1f),
                true
            },
            {
                new CurvePoint(4500f, 5f),
                true
            },
            {
                new CurvePoint(30000f, 50f),
                true
            },
            {
                new CurvePoint(100000f, 500f),
                true
            }
        };

        public SwimmerPathFinder(Map map)
        {
            this.map = map;
            this.mapSizeX = map.Size.x;
            this.mapSizeZ = map.Size.z;
            this.calcGrid = new SwimmerPathFinder.PathFinderNodeFast[this.mapSizeX * this.mapSizeZ];
            this.openList = new FastPriorityQueue<SwimmerPathFinder.CostNode>(new SwimmerPathFinder.CostNodeComparer());
            this.regionCostCalculator = new RegionCostCalculatorWrapper(map);
        }

        public PawnPath FindPath(IntVec3 start, LocalTargetInfo dest, Pawn pawn, PathEndMode peMode = PathEndMode.OnCell)
        {
            bool flag = false;
            if (pawn != null && pawn.CurJob != null && pawn.CurJob.canBash)
            {
                flag = true;
            }
            Danger maxDanger = Danger.Deadly;
            bool canBash = flag;
            return this.FindPath(start, dest, TraverseParms.For(pawn, maxDanger, TraverseMode.ByPawn, canBash), peMode);
        }

        public PawnPath FindPath(IntVec3 start, LocalTargetInfo dest, TraverseParms traverseParms, PathEndMode peMode = PathEndMode.OnCell)
        {
            if (DebugSettings.pathThroughWalls)
            {
                traverseParms.mode = TraverseMode.PassAllDestroyableThings;
            }
            Pawn pawn = traverseParms.pawn;
            if (pawn != null && pawn.Map != this.map)
            {
                Log.Error(string.Concat(new object[]
                {
                    "Tried to FindPath for pawn which is spawned in another map. His map SwimmerPathFinder should have been used, not this one. pawn=",
                    pawn,
                    " pawn.Map=",
                    pawn.Map,
                    " map=",
                    this.map
                }), false);
                return PawnPath.NotFound;
            }
            if (!start.IsValid)
            {
                Log.Error(string.Concat(new object[]
                {
                    "Tried to FindPath with invalid start ",
                    start,
                    ", pawn= ",
                    pawn
                }), false);
                return PawnPath.NotFound;
            }
            if (!dest.IsValid)
            {
                Log.Error(string.Concat(new object[]
                {
                    "Tried to FindPath with invalid dest ",
                    dest,
                    ", pawn= ",
                    pawn
                }), false);
                return PawnPath.NotFound;
            }
            if (traverseParms.mode == TraverseMode.ByPawn)
            {
                if (!pawn.CanReach(dest, peMode, Danger.Deadly, traverseParms.canBash, traverseParms.mode))
                {
                    return PawnPath.NotFound;
                }
            }
            else if (!this.map.reachability.CanReach(start, dest, peMode, traverseParms))
            {
                return PawnPath.NotFound;
            }
            this.PfProfilerBeginSample(string.Concat(new object[]
            {
                "FindPath for ",
                pawn,
                " from ",
                start,
                " to ",
                dest,
                (!dest.HasThing) ? string.Empty : (" at " + dest.Cell)
            }));
            this.cellIndices = this.map.cellIndices;
            this.pathGrid = this.map.pathGrid;
            this.edificeGrid = this.map.edificeGrid.InnerArray;
            this.blueprintGrid = this.map.blueprintGrid.InnerArray;
            int x = dest.Cell.x;
            int z = dest.Cell.z;
            int num = this.cellIndices.CellToIndex(start);
            int num2 = this.cellIndices.CellToIndex(dest.Cell);
            ByteGrid byteGrid = (pawn == null) ? null : pawn.GetAvoidGrid();
            bool flag = traverseParms.mode == TraverseMode.PassAllDestroyableThings || traverseParms.mode == TraverseMode.PassAllDestroyableThingsNotWater;
            bool flag2 = traverseParms.mode != TraverseMode.NoPassClosedDoorsOrWater && traverseParms.mode != TraverseMode.PassAllDestroyableThingsNotWater;
            bool flag3 = !flag;
            CellRect cellRect = this.CalculateDestinationRect(dest, peMode);
            bool flag4 = cellRect.Width == 1 && cellRect.Height == 1;
            int[] array = this.map.pathGrid.pathGrid;
            TerrainDef[] topGrid = this.map.terrainGrid.topGrid;
            EdificeGrid edificeGrid = this.map.edificeGrid;
            int num3 = 0;
            int num4 = 0;
            Area allowedArea = this.GetAllowedArea(pawn);
            bool flag5 = pawn != null && PawnUtility.ShouldCollideWithPawns(pawn);
            bool flag6 = true && DebugViewSettings.drawPaths;
            bool flag7 = !flag && start.GetRegion(this.map, RegionType.Set_Passable) != null && flag2;
            bool flag8 = !flag || !flag3;
            bool flag9 = false;
            bool flag10 = pawn != null && pawn.Drafted;
            bool flag11 = pawn != null && pawn.IsColonist;
            int num5 = (!flag11) ? 2000 : 100000;
            int num6 = 0;
            int num7 = 0;
            float num8 = this.DetermineHeuristicStrength(pawn, start, dest);
            // MOVED SECTION
            this.CalculateAndAddDisallowedCorners(traverseParms, peMode, cellRect);
            this.InitStatusesAndPushStartNode(ref num, start);
            while (true)
            {
                this.PfProfilerBeginSample("Open cell");
                if (this.openList.Count <= 0)
                {
                    break;
                }
                num6 += this.openList.Count;
                num7++;
                SwimmerPathFinder.CostNode costNode = this.openList.Pop();
                num = costNode.index;
                if (costNode.cost != this.calcGrid[num].costNodeCost)
                {
                    this.PfProfilerEndSample();
                }
                else if (this.calcGrid[num].status == this.statusClosedValue)
                {
                    this.PfProfilerEndSample();
                }
                else
                {
                    IntVec3 c = this.cellIndices.IndexToCell(num);
                    // BEGIN CHANGED SECTION
                    int num9;
                    int num10;
                    float swimSpeed = 0;
                    if (pawn != null)
                    {
                        num9 = pawn.TerrainAwareTicksPerMoveCardinal(c);
                        num10 = pawn.TerrainAwareTicksPerMoveDiagonal(c);
                        StatDef swimDef = DefDatabase<StatDef>.GetNamed("SwimSpeed", false);
                        swimSpeed = (swimDef != null) ? pawn.GetStatValue(swimDef, true) : 0;
                    }
                    else
                    {
                        num9 = 13;
                        num10 = 18;
                    }
                    // END CHANGE SECTION
                    int x2 = c.x;
                    int z2 = c.z;
                    if (flag6)
                    {
                        this.DebugFlash(c, (float)this.calcGrid[num].knownCost / 1500f, this.calcGrid[num].knownCost.ToString());
                    }
                    if (flag4)
                    {
                        if (num == num2)
                        {
                            goto Block_32;
                        }
                    }
                    else if (cellRect.Contains(c) && !this.disallowedCornerIndices.Contains(num))
                    {
                        goto Block_34;
                    }
                    if (num3 > 160000)
                    {
                        goto Block_35;
                    }
                    this.PfProfilerEndSample();
                    this.PfProfilerBeginSample("Neighbor consideration");
                    for (int i = 0; i < 8; i++)
                    {
                        uint num11 = (uint)(x2 + SwimmerPathFinder.Directions[i]);
                        uint num12 = (uint)(z2 + SwimmerPathFinder.Directions[i + 8]);
                        if ((ulong)num11 < (ulong)((long)this.mapSizeX) && (ulong)num12 < (ulong)((long)this.mapSizeZ))
                        {
                            int num13 = (int)num11;
                            int num14 = (int)num12;
                            int num15 = this.cellIndices.CellToIndex(num13, num14);
                            bool water = new IntVec3(num13, 0, num14).GetTerrain(this.map).HasTag("Water");
                            bool swimming = water && swimSpeed > 0;
                            if (this.calcGrid[num15].status != this.statusClosedValue || flag9)
                            {
                                int num16 = 0;
                                bool flag12 = false;
                                if (flag2 || !water)
                                {
                                    if (!this.pathGrid.WalkableFast(num15))
                                    {
                                        if (!flag)
                                        {
                                            if (flag6)
                                            {
                                                this.DebugFlash(new IntVec3(num13, 0, num14), 0.22f, "walk");
                                            }
                                            goto IL_D53;
                                        }
                                        flag12 = true;
                                        num16 += 70;
                                        Building building = edificeGrid[num15];
                                        if (building == null)
                                        {
                                            goto IL_D53;
                                        }
                                        if (!SwimmerPathFinder.IsDestroyable(building))
                                        {
                                            goto IL_D53;
                                        }
                                        num16 += (int)((float)building.HitPoints * 0.2f);
                                    }
                                    if (i > 3)
                                    {
                                        switch (i)
                                        {
                                            case 4:
                                                if (this.BlocksDiagonalMovement(num - this.mapSizeX))
                                                {
                                                    if (flag8)
                                                    {
                                                        if (flag6)
                                                        {
                                                            this.DebugFlash(new IntVec3(x2, 0, z2 - 1), 0.9f, "corn");
                                                        }
                                                        goto IL_D53;
                                                    }
                                                    num16 += 70;
                                                }
                                                if (this.BlocksDiagonalMovement(num + 1))
                                                {
                                                    if (flag8)
                                                    {
                                                        if (flag6)
                                                        {
                                                            this.DebugFlash(new IntVec3(x2 + 1, 0, z2), 0.9f, "corn");
                                                        }
                                                        goto IL_D53;
                                                    }
                                                    num16 += 70;
                                                }
                                                break;
                                            case 5:
                                                if (this.BlocksDiagonalMovement(num + this.mapSizeX))
                                                {
                                                    if (flag8)
                                                    {
                                                        if (flag6)
                                                        {
                                                            this.DebugFlash(new IntVec3(x2, 0, z2 + 1), 0.9f, "corn");
                                                        }
                                                        goto IL_D53;
                                                    }
                                                    num16 += 70;
                                                }
                                                if (this.BlocksDiagonalMovement(num + 1))
                                                {
                                                    if (flag8)
                                                    {
                                                        if (flag6)
                                                        {
                                                            this.DebugFlash(new IntVec3(x2 + 1, 0, z2), 0.9f, "corn");
                                                        }
                                                        goto IL_D53;
                                                    }
                                                    num16 += 70;
                                                }
                                                break;
                                            case 6:
                                                if (this.BlocksDiagonalMovement(num + this.mapSizeX))
                                                {
                                                    if (flag8)
                                                    {
                                                        if (flag6)
                                                        {
                                                            this.DebugFlash(new IntVec3(x2, 0, z2 + 1), 0.9f, "corn");
                                                        }
                                                        goto IL_D53;
                                                    }
                                                    num16 += 70;
                                                }
                                                if (this.BlocksDiagonalMovement(num - 1))
                                                {
                                                    if (flag8)
                                                    {
                                                        if (flag6)
                                                        {
                                                            this.DebugFlash(new IntVec3(x2 - 1, 0, z2), 0.9f, "corn");
                                                        }
                                                        goto IL_D53;
                                                    }
                                                    num16 += 70;
                                                }
                                                break;
                                            case 7:
                                                if (this.BlocksDiagonalMovement(num - this.mapSizeX))
                                                {
                                                    if (flag8)
                                                    {
                                                        if (flag6)
                                                        {
                                                            this.DebugFlash(new IntVec3(x2, 0, z2 - 1), 0.9f, "corn");
                                                        }
                                                        goto IL_D53;
                                                    }
                                                    num16 += 70;
                                                }
                                                if (this.BlocksDiagonalMovement(num - 1))
                                                {
                                                    if (flag8)
                                                    {
                                                        if (flag6)
                                                        {
                                                            this.DebugFlash(new IntVec3(x2 - 1, 0, z2), 0.9f, "corn");
                                                        }
                                                        goto IL_D53;
                                                    }
                                                    num16 += 70;
                                                }
                                                break;
                                        }
                                    }
                                    int num17 = (i <= 3) ? num9 : num10;
                                    num17 += num16;
                                    if (!flag12)
                                    {
                                        num17 += array[num15];
                                        // CHANGED TO SKIP PENALTY IF SWIMMING IN WATER
                                        if (!swimming)
                                        {
                                            if (flag10)
                                            {
                                                num17 += topGrid[num15].extraDraftedPerceivedPathCost;
                                            }
                                            else
                                            {
                                                num17 += topGrid[num15].extraNonDraftedPerceivedPathCost;
                                            }
                                        }
                                    }
                                    if (byteGrid != null)
                                    {
                                        num17 += (int)(byteGrid[num15] * 8);
                                    }
                                    if (allowedArea != null && !allowedArea[num15])
                                    {
                                        num17 += 600;
                                    }
                                    if (flag5 && PawnUtility.AnyPawnBlockingPathAt(new IntVec3(num13, 0, num14), pawn, false, false, true))
                                    {
                                        num17 += 175;
                                    }
                                    Building building2 = this.edificeGrid[num15];
                                    if (building2 != null)
                                    {
                                        this.PfProfilerBeginSample("Edifices");
                                        int buildingCost = SwimmerPathFinder.GetBuildingCost(building2, traverseParms, pawn);
                                        if (buildingCost == 2147483647)
                                        {
                                            this.PfProfilerEndSample();
                                            goto IL_D53;
                                        }
                                        num17 += buildingCost;
                                        this.PfProfilerEndSample();
                                    }
                                    List<Blueprint> list = this.blueprintGrid[num15];
                                    if (list != null)
                                    {
                                        this.PfProfilerBeginSample("Blueprints");
                                        int num18 = 0;
                                        for (int j = 0; j < list.Count; j++)
                                        {
                                            num18 = Mathf.Max(num18, SwimmerPathFinder.GetBlueprintCost(list[j], pawn));
                                        }
                                        if (num18 == 2147483647)
                                        {
                                            this.PfProfilerEndSample();
                                            goto IL_D53;
                                        }
                                        num17 += num18;
                                        this.PfProfilerEndSample();
                                    }
                                    int num19 = num17 + this.calcGrid[num].knownCost;
                                    ushort status = this.calcGrid[num15].status;
                                    if (status == this.statusClosedValue || status == this.statusOpenValue)
                                    {
                                        int num20 = 0;
                                        if (status == this.statusClosedValue)
                                        {
                                            num20 = num9;
                                        }
                                        if (this.calcGrid[num15].knownCost <= num19 + num20)
                                        {
                                            goto IL_D53;
                                        }
                                    }
                                    if (status != this.statusClosedValue && status != this.statusOpenValue)
                                    {
                                        if (flag9)
                                        {
                                            this.calcGrid[num15].heuristicCost = Mathf.RoundToInt((float)this.regionCostCalculator.GetPathCostFromDestToRegion(num15) * SwimmerPathFinder.RegionHeuristicWeightByNodesOpened.Evaluate((float)num4));
                                        }
                                        else
                                        {
                                            int dx = Math.Abs(num13 - x);
                                            int dz = Math.Abs(num14 - z);
                                            int num21 = GenMath.OctileDistance(dx, dz, num9, num10);
                                            this.calcGrid[num15].heuristicCost = Mathf.RoundToInt((float)num21 * num8);
                                        }
                                    }
                                    int num22 = num19 + this.calcGrid[num15].heuristicCost;
                                    this.calcGrid[num15].parentIndex = num;
                                    this.calcGrid[num15].knownCost = num19;
                                    this.calcGrid[num15].status = this.statusOpenValue;
                                    this.calcGrid[num15].costNodeCost = num22;
                                    num4++;
                                    this.openList.Push(new SwimmerPathFinder.CostNode(num15, num22));
                                }
                            }
                        }
                        IL_D53:;
                    }
                    this.PfProfilerEndSample();
                    num3++;
                    this.calcGrid[num].status = this.statusClosedValue;
                    if (num4 >= num5 && flag7 && !flag9)
                    {
                        flag9 = true;
                        this.regionCostCalculator.Init(cellRect, traverseParms, num9, num10, byteGrid, allowedArea, flag10, this.disallowedCornerIndices);
                        this.InitStatusesAndPushStartNode(ref num, start);
                        num4 = 0;
                        num3 = 0;
                    }
                }
            }
            string text = (pawn == null || pawn.CurJob == null) ? "null" : pawn.CurJob.ToString();
            string text2 = (pawn == null || pawn.Faction == null) ? "null" : pawn.Faction.ToString();
            Log.Warning(string.Concat(new object[]
            {
                pawn,
                " pathing from ",
                start,
                " to ",
                dest,
                " ran out of cells to process.\nJob:",
                text,
                "\nFaction: ",
                text2
            }), false);
            this.DebugDrawRichData();
            this.PfProfilerEndSample();
            return PawnPath.NotFound;
            Block_32:
            this.PfProfilerEndSample();
            PawnPath result = this.FinalizedPath(num, flag9);
            this.PfProfilerEndSample();
            return result;
            Block_34:
            this.PfProfilerEndSample();
            PawnPath result2 = this.FinalizedPath(num, flag9);
            this.PfProfilerEndSample();
            return result2;
            Block_35:
            Log.Warning(string.Concat(new object[]
            {
                pawn,
                " pathing from ",
                start,
                " to ",
                dest,
                " hit search limit of ",
                160000,
                " cells."
            }), false);
            this.DebugDrawRichData();
            this.PfProfilerEndSample();
            return PawnPath.NotFound;
        }

        public static int GetBuildingCost(Building b, TraverseParms traverseParms, Pawn pawn)
        {
            Building_Door building_Door = b as Building_Door;
            if (building_Door != null)
            {
                switch (traverseParms.mode)
                {
                    case TraverseMode.ByPawn:
                        if (!traverseParms.canBash && building_Door.IsForbiddenToPass(pawn))
                        {
                            if (DebugViewSettings.drawPaths)
                            {
                                SwimmerPathFinder.DebugFlash(b.Position, b.Map, 0.77f, "forbid");
                            }
                            return 2147483647;
                        }
                        if (building_Door.PawnCanOpen(pawn) && !building_Door.FreePassage)
                        {
                            return building_Door.TicksToOpenNow;
                        }
                        if (building_Door.CanPhysicallyPass(pawn))
                        {
                            return 0;
                        }
                        if (traverseParms.canBash)
                        {
                            return 300;
                        }
                        if (DebugViewSettings.drawPaths)
                        {
                            SwimmerPathFinder.DebugFlash(b.Position, b.Map, 0.34f, "cant pass");
                        }
                        return 2147483647;
                    case TraverseMode.PassDoors:
                        if (pawn != null && building_Door.PawnCanOpen(pawn) && !building_Door.IsForbiddenToPass(pawn) && !building_Door.FreePassage)
                        {
                            return building_Door.TicksToOpenNow;
                        }
                        if (building_Door.CanPhysicallyPass(pawn))
                        {
                            return 0;
                        }
                        return 150;
                    case TraverseMode.NoPassClosedDoors:
                    case TraverseMode.NoPassClosedDoorsOrWater:
                        if (building_Door.FreePassage)
                        {
                            return 0;
                        }
                        return 2147483647;
                    case TraverseMode.PassAllDestroyableThings:
                    case TraverseMode.PassAllDestroyableThingsNotWater:
                        if (pawn != null && building_Door.PawnCanOpen(pawn) && !building_Door.IsForbiddenToPass(pawn) && !building_Door.FreePassage)
                        {
                            return building_Door.TicksToOpenNow;
                        }
                        if (building_Door.CanPhysicallyPass(pawn))
                        {
                            return 0;
                        }
                        return 50 + (int)((float)building_Door.HitPoints * 0.2f);
                }
            }
            else if (pawn != null)
            {
                return (int)b.PathFindCostFor(pawn);
            }
            return 0;
        }

        public static int GetBlueprintCost(Blueprint b, Pawn pawn)
        {
            if (pawn != null)
            {
                return (int)b.PathFindCostFor(pawn);
            }
            return 0;
        }

        public static bool IsDestroyable(Thing th)
        {
            return th.def.useHitPoints && th.def.destroyable;
        }

        protected bool BlocksDiagonalMovement(int x, int z)
        {
            return SwimmerPathFinder.BlocksDiagonalMovement(x, z, this.map);
        }

        protected bool BlocksDiagonalMovement(int index)
        {
            return SwimmerPathFinder.BlocksDiagonalMovement(index, this.map);
        }

        public static bool BlocksDiagonalMovement(int x, int z, Map map)
        {
            return SwimmerPathFinder.BlocksDiagonalMovement(map.cellIndices.CellToIndex(x, z), map);
        }

        public static bool BlocksDiagonalMovement(int index, Map map)
        {
            return !map.pathGrid.WalkableFast(index) || map.edificeGrid[index] is Building_Door;
        }

        protected void DebugFlash(IntVec3 c, float colorPct, string str)
        {
            SwimmerPathFinder.DebugFlash(c, this.map, colorPct, str);
        }

        protected static void DebugFlash(IntVec3 c, Map map, float colorPct, string str)
        {
            map.debugDrawer.FlashCell(c, colorPct, str, 50);
        }

        protected PawnPath FinalizedPath(int finalIndex, bool usedRegionHeuristics)
        {
            PawnPath emptyPawnPath = this.map.pawnPathPool.GetEmptyPawnPath();
            int num = finalIndex;
            while (true)
            {
                SwimmerPathFinder.PathFinderNodeFast pathFinderNodeFast = this.calcGrid[num];
                int parentIndex = pathFinderNodeFast.parentIndex;
                emptyPawnPath.AddNode(this.map.cellIndices.IndexToCell(num));
                if (num == parentIndex)
                {
                    break;
                }
                num = parentIndex;
            }
            emptyPawnPath.SetupFound((float)this.calcGrid[finalIndex].knownCost, usedRegionHeuristics);
            return emptyPawnPath;
        }

        protected void InitStatusesAndPushStartNode(ref int curIndex, IntVec3 start)
        {
            this.statusOpenValue += 2;
            this.statusClosedValue += 2;
            if (this.statusClosedValue >= 65435)
            {
                this.ResetStatuses();
            }
            curIndex = this.cellIndices.CellToIndex(start);
            this.calcGrid[curIndex].knownCost = 0;
            this.calcGrid[curIndex].heuristicCost = 0;
            this.calcGrid[curIndex].costNodeCost = 0;
            this.calcGrid[curIndex].parentIndex = curIndex;
            this.calcGrid[curIndex].status = this.statusOpenValue;
            this.openList.Clear();
            this.openList.Push(new SwimmerPathFinder.CostNode(curIndex, 0));
        }

        protected void ResetStatuses()
        {
            int num = this.calcGrid.Length;
            for (int i = 0; i < num; i++)
            {
                this.calcGrid[i].status = 0;
            }
            this.statusOpenValue = 1;
            this.statusClosedValue = 2;
        }

        [Conditional("PFPROFILE")]
        protected void PfProfilerBeginSample(string s)
        {
        }

        [Conditional("PFPROFILE")]
        protected void PfProfilerEndSample()
        {
        }

        protected void DebugDrawRichData()
        {
            if (DebugViewSettings.drawPaths)
            {
                while (this.openList.Count > 0)
                {
                    int index = this.openList.Pop().index;
                    IntVec3 c = new IntVec3(index % this.mapSizeX, 0, index / this.mapSizeX);
                    this.map.debugDrawer.FlashCell(c, 0f, "open", 50);
                }
            }
        }

        protected float DetermineHeuristicStrength(Pawn pawn, IntVec3 start, LocalTargetInfo dest)
        {
            if (pawn != null && pawn.RaceProps.Animal)
            {
                return 1.75f;
            }
            float lengthHorizontal = (start - dest.Cell).LengthHorizontal;
            return (float)Mathf.RoundToInt(SwimmerPathFinder.NonRegionBasedHeuristicStrengthHuman_DistanceCurve.Evaluate(lengthHorizontal));
        }

        protected CellRect CalculateDestinationRect(LocalTargetInfo dest, PathEndMode peMode)
        {
            CellRect result;
            if (!dest.HasThing || peMode == PathEndMode.OnCell)
            {
                result = CellRect.SingleCell(dest.Cell);
            }
            else
            {
                result = dest.Thing.OccupiedRect();
            }
            if (peMode == PathEndMode.Touch)
            {
                result = result.ExpandedBy(1);
            }
            return result;
        }

        protected Area GetAllowedArea(Pawn pawn)
        {
            if (pawn != null && pawn.playerSettings != null && !pawn.Drafted && ForbidUtility.CaresAboutForbidden(pawn, true))
            {
                Area area = pawn.playerSettings.EffectiveAreaRestrictionInPawnCurrentMap;
                if (area != null && area.TrueCount <= 0)
                {
                    area = null;
                }
                return area;
            }
            return null;
        }

        protected void CalculateAndAddDisallowedCorners(TraverseParms traverseParms, PathEndMode peMode, CellRect destinationRect)
        {
            this.disallowedCornerIndices.Clear();
            if (peMode == PathEndMode.Touch)
            {
                int minX = destinationRect.minX;
                int minZ = destinationRect.minZ;
                int maxX = destinationRect.maxX;
                int maxZ = destinationRect.maxZ;
                if (!this.IsCornerTouchAllowed(minX + 1, minZ + 1, minX + 1, minZ, minX, minZ + 1))
                {
                    this.disallowedCornerIndices.Add(this.map.cellIndices.CellToIndex(minX, minZ));
                }
                if (!this.IsCornerTouchAllowed(minX + 1, maxZ - 1, minX + 1, maxZ, minX, maxZ - 1))
                {
                    this.disallowedCornerIndices.Add(this.map.cellIndices.CellToIndex(minX, maxZ));
                }
                if (!this.IsCornerTouchAllowed(maxX - 1, maxZ - 1, maxX - 1, maxZ, maxX, maxZ - 1))
                {
                    this.disallowedCornerIndices.Add(this.map.cellIndices.CellToIndex(maxX, maxZ));
                }
                if (!this.IsCornerTouchAllowed(maxX - 1, minZ + 1, maxX - 1, minZ, maxX, minZ + 1))
                {
                    this.disallowedCornerIndices.Add(this.map.cellIndices.CellToIndex(maxX, minZ));
                }
            }
        }

        protected bool IsCornerTouchAllowed(int cornerX, int cornerZ, int adjCardinal1X, int adjCardinal1Z, int adjCardinal2X, int adjCardinal2Z)
        {
            return TouchPathEndModeUtility.IsCornerTouchAllowed(cornerX, cornerZ, adjCardinal1X, adjCardinal1Z, adjCardinal2X, adjCardinal2Z, this.map);
        }
    }
}
