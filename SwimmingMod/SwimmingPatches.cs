using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using UnityEngine;
using HarmonyLib;
using System.Reflection;


namespace Swimming {
    [StaticConstructorOnStartup]
    public static class SwimmingLoader {
        public const String HarmonyId = "net.mseal.rimworld.mod.swimming";
        public const String DeepWaterTag = "DeepWater";
        public const float DefaultWaterSwimCost = 15;
        public static readonly IEnumerable<string> DeepWaterTiles = new HashSet<string> {
            "WaterDeep", "WaterOceanDeep"
        };
        public static readonly IEnumerable<string> WaterTiles = new HashSet<string> {
            "WaterDeep", "WaterOceanDeep", "WaterMovingChestDeep", "WaterShallow", "WaterOceanShallow", "WaterMovingShallow", "Marsh"
        };
        public static readonly Dictionary<string, float> WaterSwimCost = new Dictionary<string, float> {
            { "WaterDeep", 5 },
            { "WaterOceanDeep", 10 },
            { "WaterMovingChestDeep", 8 },
            { "WaterShallow", 15 },
            { "WaterOceanShallow", 15 },
            { "WaterMovingShallow", 15 },
            { "Marsh", 30 }
        };

        static SwimmingLoader()
        {
            if (!Harmony.HasAnyPatches(HarmonyId))
            {
                PatchWater();
                var harmony = new Harmony(HarmonyId);
                harmony.PatchAll(Assembly.GetExecutingAssembly());
            }
        }

        public static void PatchPathCostSwimming(String waterName)
        {
            TerrainDef water = TerrainDef.Named(waterName);
            StatModifier pathCost = new StatModifier
            {
                stat = StatDef.Named("pathCostSwimming"),
                value = WaterSwimCost.TryGetValue(waterName, DefaultWaterSwimCost)
            };
            if (water != null)
            {
                bool appliedChange = false;
                if (water.statBases == null)
                {
                    water.statBases = new List<StatModifier>();
                }
                foreach (StatModifier statBase in water.statBases)
                {
                    if (statBase.stat == pathCost.stat)
                    {
                        appliedChange = true;
                    }
                }
                if (!appliedChange)
                {
                    water.statBases.Add(pathCost);
                    Log.Message(String.Format("[SwimmingKit] Applied swimming cost to '{0}' of value {1}", waterName, pathCost.value));
                }
            } else
            {
                Log.Warning(String.Format("[SwimmingKit] Attempted to apply swimming speed to '{0}' tile type that was not found", waterName));
            }
        }
        public static void PatchWater()
        {
            foreach (String waterName in DeepWaterTiles)
            {
                var water = TerrainDef.Named(waterName);
                water.passability = Traversability.Standable;
                water.tags.Add(DeepWaterTag);
            }
            foreach (String waterName in WaterTiles)
            {
                PatchPathCostSwimming(waterName);
            }
        }
    }

    public class AquaticExtension : DefModExtension
    {
        public bool aquatic = false;
    }

    static class MapExtensions {
        public static Dictionary<int, SwimmerPathFinder> PatherLookup = new Dictionary<int, SwimmerPathFinder>();

        public static void ResetLookup()
        {
            PatherLookup = new Dictionary<int, SwimmerPathFinder>();
        }

        public static SwimmerPathFinder SwimPather(this Map map)
        {
            if (!PatherLookup.TryGetValue(map.uniqueID, out SwimmerPathFinder pather))
            {
                pather = new SwimmerPathFinder(map);
                PatherLookup.Add(map.uniqueID, pather);
            }
            return pather;
        }

        public static bool UnreachableAquaticCheck(this Map map, LocalTargetInfo target, Pawn pawn)
        {
            if (pawn != null)
            {
                bool aquatic = pawn.def.HasModExtension<AquaticExtension>() && pawn.def.GetModExtension<AquaticExtension>().aquatic;
                bool destWater = target.Cell.GetTerrain(map).HasTag("Water");
                if (!destWater && aquatic)
                {
                    return true;
                }
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(Map), "FinalizeInit", new Type[0])]
    internal static class Map_FinalizeInit_Patch {
        private static void Postfix()
        {
            MapExtensions.ResetLookup();
        }
    }

    [HarmonyPatch(typeof(PathFinder), "FindPath", new Type[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(TraverseParms), typeof(PathEndMode) })]
    class SwimmerPathPatch {
        static bool Prefix(ref PawnPath __result, Map ___map, IntVec3 start, LocalTargetInfo dest, TraverseParms traverseParms, PathEndMode peMode)
        {
            __result = ___map.SwimPather().FindPath(start, dest, traverseParms, peMode);
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn), "TicksPerMove", new Type[] { typeof(bool) })]
    class PawnTicksPerMoveNoMoveCheck
    {
        static bool Prefix(ref int __result, Pawn __instance, bool diagonal)
        {
            if (__instance.GetStatValue(StatDefOf.MoveSpeed) < 0.0000001)
            {
                __result = 100000000;
                return false;
            }
            return true;
        }
    }
    
    [HarmonyPatch(typeof(Reachability), "CanReach", new Type[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(PathEndMode), typeof(TraverseParms) })]
    class CanReachMoveCheck
    {

        static bool Prefix(ref bool __result, Map ___map, IntVec3 start, LocalTargetInfo dest, PathEndMode peMode, TraverseParms traverseParams)
        {
            if (___map.UnreachableAquaticCheck(dest, traverseParams.pawn))
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(ReachabilityImmediate), "CanReachImmediate", new Type[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(Map), typeof(PathEndMode), typeof(Pawn) })]
    class CanReachImmediateMoveCheck
    {
        static bool Prefix(ref bool __result, IntVec3 start, LocalTargetInfo target, Map map, PathEndMode peMode, Pawn pawn)
        {
            if (map.UnreachableAquaticCheck(target, pawn))
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), "CostToMoveIntoCell", new Type[] { typeof(IntVec3) })]
    class SwimmerFollowerPatch {
        public static int CostToMoveIntoCell(Pawn pawn, IntVec3 c)
        {
            int num;
            if (c.x == pawn.Position.x || c.z == pawn.Position.z)
            {
                num = pawn.TerrainAwareTicksPerMoveCardinal(c);
            }
            else
            {
                num = pawn.TerrainAwareTicksPerMoveDiagonal(c);
            }
            int gridCost = pawn.Map.pathGrid.CalculatedCostAt(c, false, pawn.Position);
            TerrainDef terrain = c.GetTerrain(pawn.Map);
            StatDef swimDef = DefDatabase<StatDef>.GetNamed("SwimSpeed", true);
            StatDef swimPathCostDef = DefDatabase<StatDef>.GetNamed("pathCostSwimming", false);
            int swimPathCost = (swimPathCostDef != null) ? (int)terrain.GetStatValueAbstract(swimPathCostDef) : 0;
            float swimSpeed = pawn.GetStatValue(swimDef, true);
            bool water = terrain.HasTag("Water");
            bool swimming = water && swimSpeed > 0;
            if (swimming)
            {
                if (swimPathCost > 0)
                {
                    // Replace grid cost with swimming cost
                    gridCost += swimPathCost - terrain.pathCost;
                }
                else
                {
                    // Reduce the path penalty for swimming by 10x
                    gridCost -= (terrain.pathCost * 9) / 10;
                }
            }
            num += gridCost;
            Building edifice = c.GetEdifice(pawn.Map);
            if (edifice != null)
            {
                num += (int)edifice.PathWalkCostFor(pawn);
            }
            if (num > 450)
            {
                num = 450;
            }
            if (pawn.CurJob != null)
            {
                Pawn locomotionUrgencySameAs = pawn.jobs.curDriver.locomotionUrgencySameAs;
                if (locomotionUrgencySameAs != null && locomotionUrgencySameAs != pawn && locomotionUrgencySameAs.Spawned)
                {
                    int num2 = SwimmerFollowerPatch.CostToMoveIntoCell(locomotionUrgencySameAs, c);
                    if (num < num2)
                    {
                        num = num2;
                    }
                }
                else
                {
                    switch (pawn.jobs.curJob.locomotionUrgency)
                    {
                        case LocomotionUrgency.Amble:
                            num *= 3;
                            if (num < 60)
                            {
                                num = 60;
                            }
                            break;
                        case LocomotionUrgency.Walk:
                            num *= 2;
                            if (num < 50)
                            {
                                num = 50;
                            }
                            break;
                        case LocomotionUrgency.Jog:
                            break;
                        case LocomotionUrgency.Sprint:
                            num = Mathf.RoundToInt((float)num * 0.75f);
                            break;
                    }
                }
            }
            return Mathf.Max(num, 1);
        }

        static bool Prefix(ref int __result, Pawn ___pawn, IntVec3 c)
        {
            __result = CostToMoveIntoCell(___pawn, c);
            return false;
        }
    }

    static class PawnExtensions {
        public static int TerrainAwareTicksPerMoveCardinal(this Pawn pawn, IntVec3 loc)
        {
            return pawn.TerrainAwareTicksPerMove(loc, false);
        }

        public static int TerrainAwareTicksPerMoveDiagonal(this Pawn pawn, IntVec3 loc)
        {
            return pawn.TerrainAwareTicksPerMove(loc, true);
        }

        public static int TerrainAwareTicksPerMove(this Pawn pawn, IntVec3 loc, bool diagonal)
        {
            float num;
            TerrainDef terrain = pawn.Map.terrainGrid.TerrainAt(loc);
            StatDef swimDef = DefDatabase<StatDef>.GetNamed("SwimSpeed", false);
            float swimSpeed = (swimDef != null) ? pawn.GetStatValue(swimDef, true) : 0;
            if (terrain.HasTag("Water") && swimSpeed > 0.000001)
            {
                num = swimSpeed;
            }
            else
            {
                num = pawn.GetStatValue(StatDefOf.MoveSpeed, true);
            }
            if (RestraintsUtility.InRestraints(pawn))
            {
                num *= 0.35f;
            }
            if (pawn.carryTracker != null && pawn.carryTracker.CarriedThing != null && pawn.carryTracker.CarriedThing.def.category == ThingCategory.Pawn)
            {
                num *= 0.6f;
            }
            float num2 = num / 60f;
            float num3;
            if (num2 == 0f)
            {
                num3 = 450f;
            }
            else
            {
                num3 = 1f / num2;
                if (pawn.Spawned && !pawn.Map.roofGrid.Roofed(pawn.Position))
                {
                    num3 /= pawn.Map.weatherManager.CurMoveSpeedMultiplier;
                }
                if (diagonal)
                {
                    num3 *= 1.41421f;
                }
            }
            int value = Mathf.RoundToInt(num3);
            return Mathf.Clamp(value, 1, 450);
        }
    }
}
