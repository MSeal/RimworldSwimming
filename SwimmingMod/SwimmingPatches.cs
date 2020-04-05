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
        public static TerrainMovementStatDef WaterTerrainModExt = new TerrainMovementStatDef();
        public static TerrainMovementTerrainRestrictions DeepWaterTerrainRestrictionModeExt = new TerrainMovementTerrainRestrictions();

        static SwimmingLoader()
        {
            // Set our extension defaults (they can't have constructors)
            WaterTerrainModExt.terrainPathCostStat = "pathCostSwimming";
            WaterTerrainModExt.pawnSpeedStat = "SwimSpeed";
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
                if (!water.tags.Contains("DeepWater"))
                {
                    // Helpful for assigning rules to deep water in extensions of the kit
                    water.tags.Add("DeepWater");
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
                    Log.Message(String.Format("[SwimmingKit] Added deep water terrain restriction mod ext to '{0}'", water.defName));
                }
                else
                {
                    Log.Message(String.Format("[SwimmingKit] Found existing deep water terrain restriction mod ext for '{0}'", water.defName));
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
                Log.Message(String.Format("[SwimmingKit] Applied swimming cost to '{0}' of value {1}", water.defName, pathCost.value));
            }
            else
            {
                Log.Message(String.Format("[SwimmingKit] Found swimming cost for '{0}' with a value of {1}", water.defName, pathCost.value));
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
                Log.Message(String.Format("[SwimmingKit] Added default terrain mod ext to '{0}'", water.defName));
            }
            else
            {
                Log.Message(String.Format("[SwimmingKit] Found existing terrain mod ext for '{0}'", water.defName));
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
                }
            }
        }
    }

    public class AquaticExtension : DefModExtension
    {
        public bool aquatic = false;
    }


    [HarmonyPatch(typeof(PawnExtensions), "LoadTerrainMovementPawnRestrictionsExtension")]
    class AquaticExtensionTranslator
    {
        static bool Prefix(ref TerrainMovementPawnRestrictions __result, DefModExtension ext)
        {
            if (ext is AquaticExtension)
            {
                AquaticExtension aqext = ext as AquaticExtension;
                TerrainMovementPawnRestrictions tmext = new TerrainMovementPawnRestrictions();
                tmext.stayOnTerrainTag = "Water";
                __result = tmext;
                return false;
            }
            return true;
        }
    }
}
