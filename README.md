# RimworldSwimming

![Version](https://img.shields.io/badge/Rimworld-1.2-brightgreen.svg) on [Steam](https://steamcommunity.com/sharedfiles/filedetails/?id=1542399915)

![Alt text](About/Preview.png?raw=true "Swimming")

A RimWorld mod which adds swimming attribute for pawns to change speeds while in water tiles.

## Description:

The mod acts as a toolkit for adding swimming statistics to the game. It makes pawns respect a new SwimSpeed baseStat for both moving through water and for planning pathing through water.

In working on another mod it took the better part of a long weekend to get all the various places that needed changing correct, so to save other modders time and to enable adding actual aquatic gear / pawns I put the changes together in this mod.

### How to Use

Adding the `SwimSpeed` to any `ThingDef`'s `statBases` will apply swim speed when in water tiles.

```xml
<ThingDef ParentName="AnimalThingBase"">
    <defName>Turtle</defName>
    <label>turtle</label>
    <description>A tortoise that likes water</description>
    <statBases>
        <MoveSpeed>1</MoveSpeed>
        <!-- This makes turtles have a base speed of 4 in water -->
        <SwimSpeed>4</SwimSpeed>
    </statBases>
</ThingDef>
```

If you want to conditionally set a SwimSpeed only if this mod is present you can do the following:

```objc
// Dynamically set the SwimSpeed to avoid requiring SwimmingKit
StatDef swimDef = DefDatabase<StatDef>.GetNamed("SwimSpeed", false);
if (swimDef != null)
{
    ThingDef turtle = ThingDef.Named("Turtle");
    turtle.SetStatBaseValue(swimDef, 4.0f);
}
```

You can also effect SwimSpeed by modifying equipment.

```xml
<ThingDef ParentName="ArmorMachineableBase">
    <defName>Apparel_HydroPowerArmor</defName>
    <label>hydro plate armor</label>
    <description>Augmented power armor with built-in water-jets for fast traversal underwater.</description>
    </statBases>
    <equippedStatOffsets>
        <MoveSpeed>-1.0</MoveSpeed>
        <SwimSpeed>3.5</SwimSpeed>
    </equippedStatOffsets>
</ThingDef>
```

#### Aquatic Only

Setting aquatic in the Swimming.AquaticExtension will make something aquatic only, meaning it won't go on land.

```xml
<ThingDef ParentName="AnimalThingBase">
    <defName>Shark</defName>
    <label>shark</label>
    <description>A shark that can only go in water</description>
    <statBases>
        <MoveSpeed>0.15</MoveSpeed>
        <SwimSpeed>4</SwimSpeed>
    </statBases>
    <modExtensions>
        <!-- This makes sharks stay in the water -->
        <li Class="Swimming.AquaticExtension">
            <aquatic>true</aquatic>
        </li>
    </modExtensions>
</ThingDef>
```

## Mod Compatibility:

In order to add swimming a bunch of core functions had to be rewritten. This means that anything which modifies pathing search and terrain cost functions might conflict, but I'm not aware of any mods which do conflict.

## Change in Terrain:

This mod makes Ocean and DeepWater tiles accessible. Normal pawns will move very very slow through ocean, but it is possible for pawns and items to now be in Ocean. If a pawn has SwimSpeed it will travel much quicker through ocean tiles.

## World Map:

Swimming does not affect the World Map in anyway, so even if the caravan has pawns which can swim it will not change travel times or the ability to travel to / through Ocean tiles.

## Water Only Pawns:

This isn't implemented yet (let me know if there's interest).

## Other Developer Notes:

### TerrainAwareTicksPerMove

`TerrainAwareTicksPerMoveCardinal` and `TerrainAwareTicksPerMoveDiagonal` are now available on pawns. These functions take a location and determine the cost for traveling in said direction given terrain speed. In this case it just changes the value to respect SwimSpeed instead of MoveSpeed when in water.

### PerceivedPathCost

`extraDraftedPerceivedPathCost` and `extraNonDraftedPerceivedPathCost` are not respected when swimming. Water tiles were the only tiles which used this attribute, but this could affect other mods when attempting to be compatible. 

### Water Tile Swim Costs

The `Patches/PatchWater.xml` file holds the injected water tile swim costs. Generally they're higher than most basic terrain, with more penalty for how hard it would likely be in general to swim in that setting.

Adding your own patch can override these values via:

```xml
<Operation Class="PatchOperationAdd">
<xpath>*/TerrainDef[defName = "WaterDeep"]/statBases</xpath>
<value>
  <pathCostSwimming>1</pathCostSwimming>
</value>
</Operation>
```

Note that you shouldn't set this to 0 or it will fallback on compatability numbers based on normal `pathCost` settings.

## Credits

[Icon Graphic Base](https://www.freeiconspng.com/img/3775)
[Powered by Harmony Patch Library](https://github.com/pardeike/Harmony)
