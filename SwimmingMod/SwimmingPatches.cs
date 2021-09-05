using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using HarmonyLib;
using System.Reflection;
using TerrainMovement;
using System.IO;
using System.Linq;

namespace Swimming {
    public sealed class SwimmingMod : Mod
    {
        public const String HarmonyId = "net.mseal.rimworld.mod.swimming";

        public SwimmingMod(ModContentPack content) : base(content)
        {
            Assembly localAssembly = Assembly.GetExecutingAssembly();
            string DLLName = localAssembly.GetName().Name;
            Version loadedVersion = localAssembly.GetName().Version;
            Version laterVersion = loadedVersion;

            List<ModContentPack> runningModsListForReading = LoadedModManager.RunningModsListForReading;
            foreach (ModContentPack mod in runningModsListForReading)
            {
                foreach (FileInfo item in from f in ModContentPack.GetAllFilesForMod(mod, "Assemblies/", (string e) => e.ToLower() == ".dll") select f.Value)
                {
                    var newAssemblyName = AssemblyName.GetAssemblyName(item.FullName);
                    if (newAssemblyName.Name == DLLName && newAssemblyName.Version > laterVersion)
                    {
                        laterVersion = newAssemblyName.Version;
                        Log.Error(String.Format("{0} load order error detected. {1} is loading an older version {2} before {3} loads version {4}. Please put the {5}, or BiomesCore mods above this one if they are active.",
                            DLLName, content.Name, loadedVersion, mod.Name, laterVersion, DLLName));
                    }
                }
            }

            var harmony = new Harmony(HarmonyId);
            harmony.PatchAll(localAssembly);
        }
    }

    [StaticConstructorOnStartup]
    public static class SwimmingLoader
    {
        public const float DefaultWaterSwimCost = 15;
        public const string SwimStat = "SwimSpeed";
        public const string WaterTag = "Water";
        public const string DeepWaterTag = "DeepWater";
        public const string SaltWaterTag = "SaltWater";
        public const string FreshWaterTag = "FreshWater";
        public static readonly IEnumerable<string> WaterTiles = new HashSet<string> {
            "WaterDeep", "WaterOceanDeep", "WaterMovingChestDeep", "WaterShallow", "WaterOceanShallow", "WaterMovingShallow", "Marsh"
        };
        public static readonly Dictionary<string, float> WaterSwimCost = new Dictionary<string, float> {
            { "WaterDeep", 10 },
            { "WaterOceanDeep", 10 },
            { "WaterMovingChestDeep", 10 },
            { "WaterShallow", 15 },
            { "WaterOceanShallow", 15 },
            { "WaterMovingShallow", 20 },
            { "Marsh", 30 }
        };
        public static readonly HashSet<string> SaltWaterTerrain = new HashSet<string>() {
            "WaterOceanShallow", "WaterOceanDeep"
        };
        public static TerrainMovementStatDef WaterTerrainModExt = new TerrainMovementStatDef();
        public static TerrainMovementTerrainRestrictions DeepWaterTerrainRestrictionModeExt = new TerrainMovementTerrainRestrictions();

        static SwimmingLoader()
        {
            // Set our extension defaults (they can't have constructors)
            WaterTerrainModExt.terrainPathCostStat = "pathCostSwimming";
            WaterTerrainModExt.pawnSpeedStat = SwimStat;
            DeepWaterTerrainRestrictionModeExt.disallowedPathCostStat = "pathCost";
            PatchWater();
        }

        public static void PatchDeepWaterRestrictions(TerrainDef water)
        {
            if (water.passability == Traversability.Impassable)
            {
                water.passability = Traversability.Standable;
                if (water.tags == null)
                {
                    water.tags = new List<string>();
                }
                if (!water.tags.Contains(DeepWaterTag))
                {
                    // Helpful for assigning rules to deep water in extensions of the kit
                    water.tags.Add(DeepWaterTag);
                }

                bool foundExtension = false;
                if (water.modExtensions == null)
                {
                    water.modExtensions = new List<DefModExtension>();
                }
                foreach (DefModExtension ext in water.modExtensions)
                {
                    if (ext is TerrainMovementTerrainRestrictions)
                    {
                        TerrainMovementTerrainRestrictions restriction = ext as TerrainMovementTerrainRestrictions;
                        if (restriction.disallowedPathCostStat == null || restriction.disallowedPathCostStat == "pathCost")
                        {
                            foundExtension = true;
                        }
                    }
                }
                if (!foundExtension)
                {
                    water.modExtensions.Add(DeepWaterTerrainRestrictionModeExt);
                }
            }
        }

        public static void PatchSaltWaterTag(TerrainDef water)
        {
            if (SaltWaterTerrain.Contains(water.defName))
            {
                if (water.tags == null)
                {
                    water.tags = new List<string>();
                }
                if (!water.tags.Contains(FreshWaterTag) && !water.tags.Contains(SaltWaterTag))
                {
                    // Allows for constraining aquatic animals to fresh/salt water
                    water.tags.Add(SaltWaterTag);
                }
            }
        }
        public static void PatchFreshWaterTag(TerrainDef water)
        {
            if (!SaltWaterTerrain.Contains(water.defName))
            {
                if (water.tags == null)
                {
                    water.tags = new List<string>();
                }
                if (!water.tags.Contains(FreshWaterTag) && !water.tags.Contains(SaltWaterTag))
                {
                    // Allows for constraining aquatic animals to fresh/salt water
                    water.tags.Add(FreshWaterTag);
                }
            }
        }

        public static void PatchPathCostSwimming(TerrainDef water)
        {
            bool foundStat = false;
            StatModifier pathCost = new StatModifier
            {
                stat = StatDef.Named(WaterTerrainModExt.terrainPathCostStat),
                value = WaterSwimCost.TryGetValue(water.defName, DefaultWaterSwimCost)
            };
            if (water.statBases == null)
            {
                water.statBases = new List<StatModifier>();
            }
            foreach (StatModifier statBase in water.statBases)
            {
                if (statBase.stat == pathCost.stat)
                {
                    foundStat = true;
                }
            }
            if (!foundStat)
            {
                water.statBases.Add(pathCost);
            }
        }

        public static void PatchTerrainMovementStatDefs(TerrainDef water)
        {
            bool foundExtension = false;
            if (water.modExtensions == null)
            {
                water.modExtensions = new List<DefModExtension>();
            }
            foreach (DefModExtension ext in water.modExtensions)
            {
                if (ext is TerrainMovementStatDef)
                {
                    TerrainMovementStatDef moveStatDef = ext as TerrainMovementStatDef;
                    if (moveStatDef.pawnSpeedStat == "pathCostSwimming")
                    {
                        foundExtension = true;
                    }
                }
            }
            if (!foundExtension)
            {
                water.modExtensions.Add(WaterTerrainModExt);
            }
        }
        public static void PatchWater()
        {
            foreach (TerrainDef terrain in DefDatabase<TerrainDef>.AllDefs)
            {
                if (terrain.HasTag("Water"))
                {
                    PatchDeepWaterRestrictions(terrain);
                    PatchPathCostSwimming(terrain);
                    PatchTerrainMovementStatDefs(terrain);
                    PatchFreshWaterTag(terrain);
                    PatchSaltWaterTag(terrain);
                }
            }
        }
    }

    public class AquaticExtension : DefModExtension
    {
        public bool aquatic = true;
        public bool saltWaterOnly = false;
        public bool freshWaterOnly = false;
    }


    [HarmonyPatch(typeof(PawnKindDefExtensions), "LoadTerrainMovementPawnRestrictionsExtension")]
    class AquaticExtensionTranslator
    {
        static bool Prefix(ref TerrainMovementPawnRestrictions __result, DefModExtension ext)
        {
            if (ext is AquaticExtension)
            {
                AquaticExtension aqext = ext as AquaticExtension;
                TerrainMovementPawnRestrictions tmext = new TerrainMovementPawnRestrictions();
                tmext.defaultMovementAllowed = false;
                tmext.stayOnTerrainTag = SwimmingLoader.WaterTag;
                if (aqext.saltWaterOnly)
                {
                    tmext.stayOffTerrainTag = SwimmingLoader.FreshWaterTag;
                }
                else if (aqext.freshWaterOnly)
                {
                    tmext.stayOffTerrainTag = SwimmingLoader.SaltWaterTag;
                }
                __result = tmext;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(GenGrid), "Walkable")]
    class ToggledDeepWaterWalkable
    {
        // A hack to temporarily treat deep water edge tiles as walkable or not
        public static bool DeepWaterValid = true;

        static void Postfix(ref bool __result, IntVec3 c, Map map)
        {
            if (__result && !DeepWaterValid)
            {
                TerrainDef terrain = map.terrainGrid.TerrainAt(c);
                if (terrain != null)
                {
                    __result = !terrain.HasTag(SwimmingLoader.DeepWaterTag);
                }
            }
        }
    }

    /* For Raid WalkIn */
    [HarmonyPatch(typeof(PawnsArrivalModeWorker_EdgeWalkIn), "TryResolveRaidSpawnCenter")]
    class DeepWaterNotPreferredForWalkIn
    {
        // A hack to allow second entry to a prefixed method to call the original
        static bool Entered = false;

        static bool Prefix(ref bool __result, ref PawnsArrivalModeWorker_EdgeWalkIn __instance, IncidentParms parms)
        {
            if (Entered)
            {
                return true;
            }
            try
            {
                Entered = true;
                // We need to make DeepWater look impassible for a moment
                ToggledDeepWaterWalkable.DeepWaterValid = false;
                __result = __instance.TryResolveRaidSpawnCenter(parms);
            }
            finally
            {
                ToggledDeepWaterWalkable.DeepWaterValid = true;
                Entered = false;
            }
            return !__result;
        }
    }

    /* For Raid GroupWalkIn & CaravanArrival */
    [HarmonyPatch(typeof(RCellFinder), "TryFindRandomPawnEntryCell")]
    class DeepWaterNotPreferredForTryFindRandomPawnEntryCell
    {
        // A hack to allow second entry to a prefixed method to call the original
        static bool Entered = false;
        public static bool PreferNonDeepWater = false;

        static bool Prefix(ref bool __result, ref IntVec3 result, Map map, float roadChance, bool allowFogged, Predicate<IntVec3> extraValidator)
        {
            if (Entered || !PreferNonDeepWater)
            {
                return true;
            }
            try
            {
                Entered = true;
                // We need to make DeepWater look impassible for a moment
                ToggledDeepWaterWalkable.DeepWaterValid = false;
                __result = RCellFinder.TryFindRandomPawnEntryCell(out result, map, roadChance, allowFogged, extraValidator);
            }
            finally
            {
                ToggledDeepWaterWalkable.DeepWaterValid = true;
                Entered = false;
            }
            return !__result;
        }
    }

    [HarmonyPatch(typeof(PawnsArrivalModeWorker_EdgeWalkInGroups), "Arrive")]
    class DeepWaterNotPreferredForGroupWalkIn
    {
        static bool Prefix()
        {
            // Set downstream methods to toggle on preferences for avoiding deep water
            DeepWaterNotPreferredForTryFindRandomPawnEntryCell.PreferNonDeepWater = true;
            return true;
        }

        static void Postfix()
        {
            // Make sure we undo the setting
            DeepWaterNotPreferredForTryFindRandomPawnEntryCell.PreferNonDeepWater = false;
        }
    }

    [HarmonyPatch(typeof(IncidentWorker_TraderCaravanArrival), "TryExecuteWorker")]
    class DeepWaterNotPreferredForTraderCaravanArrival
    {
        static bool Prefix()
        {
            // Set downstream methods to toggle on preferences for avoiding deep water
            DeepWaterNotPreferredForTryFindRandomPawnEntryCell.PreferNonDeepWater = true;
            return true;
        }

        static void Postfix()
        {
            // Make sure we undo the setting
            DeepWaterNotPreferredForTryFindRandomPawnEntryCell.PreferNonDeepWater = false;
        }
    }

    [HarmonyPatch(typeof(IncidentWorker_ManhunterPack), "TryExecuteWorker")]
    class DeepWaterNotPreferredForManhunterPack
    {
        static bool Prefix(ref IncidentParms parms)
        {
            Map map = (Map)parms.target;
            // Set downstream methods to toggle on preferences for avoiding deep water
            PawnKindDef animalKind = parms.pawnKind;
            if ((animalKind == null && !ManhunterPackIncidentUtility.TryFindManhunterAnimalKind(parms.points, map.Tile, out animalKind)) || ManhunterPackIncidentUtility.GetAnimalsCount(animalKind, parms.points) == 0)
            {
                return true;
            }
            // Set the animal manually since we need to know ahead of time to determine if it swims
            parms.pawnKind = animalKind;
            // Check if the animal involved prefers to swim over walk
            if (animalKind.race.GetStatValueAbstract(StatDefOf.MoveSpeed) >= animalKind.race.GetStatValueAbstract(StatDef.Named(SwimmingLoader.SwimStat)) + 0.001)
            {
                DeepWaterNotPreferredForTryFindRandomPawnEntryCell.PreferNonDeepWater = true;
            }
            return true;
        }

        static void Postfix()
        {
            // Make sure we undo the setting
            DeepWaterNotPreferredForTryFindRandomPawnEntryCell.PreferNonDeepWater = false;
        }
    }

    [HarmonyPatch(typeof(IncidentWorker_HerdMigration), "TryFindAnimalKind")]
    class HerdTrackTryFindAnimalKind
    {
        public static PawnKindDef LastGenAnimal;

        static void Postfix(int tile, ref bool __result, ref PawnKindDef animalKind)
        {
            // Make sure we undo the setting
            LastGenAnimal = animalKind;
        }
    }

    // We must patch the wrapper in TMK, not the original method
    [HarmonyPatch(typeof(IncidentWorker_HerdMigration_Extensions), "TryFindStartAndEndCells")]
    class DeepWaterNotPreferredForHeadMigrationStartAndEnd
    {
        static bool Prefix()
        {
            // Set downstream methods to toggle on preferences for avoiding deep water
            PawnKindDef animalKind = HerdTrackTryFindAnimalKind.LastGenAnimal;
            // Check if the animal involved prefers to swim over walk
            if (animalKind != null && animalKind.race.GetStatValueAbstract(StatDefOf.MoveSpeed) >= animalKind.race.GetStatValueAbstract(StatDef.Named(SwimmingLoader.SwimStat)) + 0.001)
            {
                DeepWaterNotPreferredForTryFindRandomPawnEntryCell.PreferNonDeepWater = true;
            }
            return true;
        }

        static void Postfix()
        {
            // Make sure we undo the setting
            DeepWaterNotPreferredForTryFindRandomPawnEntryCell.PreferNonDeepWater = false;
        }
    }

    [HarmonyPatch()]
    class DeepWaterNotPreferredForAlphabeavers
    {
        static MethodBase TargetMethod()
        {
            // This is required because the class is internal ...
            var q = from t in Assembly.GetAssembly(typeof(Pawn)).GetTypes()
                    where t.IsClass && t.Name == "IncidentWorker_Alphabeavers"
                    select t;
            foreach (var t in q)
            {
                return t.GetMethod("TryExecuteWorker", AccessTools.all);
            }
            return null;
        }

        static bool Prefix(ref IncidentParms parms)
        {
            DeepWaterNotPreferredForTryFindRandomPawnEntryCell.PreferNonDeepWater = true;
            return true;
        }

        static void Postfix()
        {
            // Make sure we undo the setting
            DeepWaterNotPreferredForTryFindRandomPawnEntryCell.PreferNonDeepWater = false;
        }
    }

    [HarmonyPatch(typeof(IncidentWorker_ThrumboPasses), "TryExecuteWorker")]
    class DeepWaterNotPreferredForThrumbos
    {
        static bool Prefix(ref IncidentParms parms)
        {
            DeepWaterNotPreferredForTryFindRandomPawnEntryCell.PreferNonDeepWater = true;
            return true;
        }

        static void Postfix()
        {
            // Make sure we undo the setting
            DeepWaterNotPreferredForTryFindRandomPawnEntryCell.PreferNonDeepWater = false;
        }
    }

    [HarmonyPatch(typeof(IncidentWorker_TravelerGroup), "TryExecuteWorker")]
    class DeepWaterNotPreferredForTravelerGroup
    {
        static bool Prefix(ref IncidentParms parms)
        {
            DeepWaterNotPreferredForTryFindRandomPawnEntryCell.PreferNonDeepWater = true;
            return true;
        }

        static void Postfix()
        {
            // Make sure we undo the setting
            DeepWaterNotPreferredForTryFindRandomPawnEntryCell.PreferNonDeepWater = false;
        }
    }

    [HarmonyPatch(typeof(IncidentWorker_VisitorGroup), "TryExecuteWorker")]
    class DeepWaterNotPreferredForVisitorGroup
    {
        static bool Prefix(ref IncidentParms parms)
        {
            DeepWaterNotPreferredForTryFindRandomPawnEntryCell.PreferNonDeepWater = true;
            return true;
        }

        static void Postfix()
        {
            // Make sure we undo the setting
            DeepWaterNotPreferredForTryFindRandomPawnEntryCell.PreferNonDeepWater = false;
        }
    }

    [HarmonyPatch(typeof(IncidentWorker_WandererJoin), "TryExecuteWorker")]
    class DeepWaterNotPreferredForWandererJoin
    {
        static bool Prefix(ref IncidentParms parms)
        {
            DeepWaterNotPreferredForTryFindRandomPawnEntryCell.PreferNonDeepWater = true;
            return true;
        }

        static void Postfix()
        {
            // Make sure we undo the setting
            DeepWaterNotPreferredForTryFindRandomPawnEntryCell.PreferNonDeepWater = false;
        }
    }

    [HarmonyPatch(typeof(IncidentWorker_WildManWandersIn), "TryExecuteWorker")]
    class DeepWaterNotPreferredForWildMan
    {
        static bool Prefix(ref IncidentParms parms)
        {
            DeepWaterNotPreferredForTryFindRandomPawnEntryCell.PreferNonDeepWater = true;
            return true;
        }

        static void Postfix()
        {
            // Make sure we undo the setting
            DeepWaterNotPreferredForTryFindRandomPawnEntryCell.PreferNonDeepWater = false;
        }
    }
}
