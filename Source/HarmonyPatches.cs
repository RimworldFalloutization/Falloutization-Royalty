using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld.QuestGen;
using Verse;
using FCP_Shuttles;

namespace Falloutization.Royalty
{
    [StaticConstructorOnStartup]
    public static class HarmonyPatchesInit
    {
        static HarmonyPatchesInit()
        {
            Log.Message("[Falloutization: Royalty] Harmony patches initialized");
        }
    }

    /// <summary>
    /// When the owning faction has an FCP FactionModExtension with a custom transportShipDef,
    /// create that def's shipThing (e.g. vertibird) instead of the vanilla Royalty shuttle.
    /// </summary>
    [HarmonyPatch(typeof(QuestNode_GenerateShuttle), nameof(QuestNode.RunInt))]
    public static class QuestNode_GenerateShuttle_Patch
    {
        public static bool Prefix(QuestNode_GenerateShuttle __instance)
        {
            Slate slate = QuestGen.slate;
            Faction faction = __instance.owningFaction.GetValue(slate);
            if (faction == null) {
                Log.Warning("[Falloutization: Royalty] QuestNode_GenerateShuttle: Owning faction not found, falling back to original.");
                return true;
            }

            FactionModExtension extension = faction.def.GetModExtension<FactionModExtension>();
            if (extension?.transportShipDef?.shipThing == null) {
                Log.Warning("[Falloutization: Royalty] QuestNode_GenerateShuttle: Custom transport ship def not found, falling back to original.");
                return true;
            }

            ThingDef thingDef = extension.transportShipDef.shipThing;
            Thing thing = ThingMaker.MakeThing(thingDef);
            thing.SetFaction(faction);

            CompShuttle compShuttle = thing.TryGetComp<CompShuttle>();
            if (compShuttle == null)
            {
                Log.Warning("[Falloutization: Royalty] QuestNode_GenerateShuttle: custom ship thing has no CompShuttle, falling back to original.");
                return true;
            }

            if (__instance.requiredPawns.GetValue(slate) != null)
                compShuttle.requiredPawns.AddRange(__instance.requiredPawns.GetValue(slate));
            if (__instance.requiredItems.GetValue(slate) != null)
                compShuttle.requiredItems.AddRange(__instance.requiredItems.GetValue(slate));

            compShuttle.acceptColonists = __instance.acceptColonists.GetValue(slate);
            compShuttle.acceptChildren = __instance.acceptChildren.GetValue(slate) ?? true;
            compShuttle.onlyAcceptColonists = __instance.onlyAcceptColonists.GetValue(slate);
            compShuttle.onlyAcceptHealthy = __instance.onlyAcceptHealthy.GetValue(slate);
            compShuttle.requiredColonistCount = __instance.requireColonistCount.GetValue(slate);
            compShuttle.permitShuttle = __instance.permitShuttle.GetValue(slate);
            compShuttle.minAge = __instance.minAge.GetValue(slate).GetValueOrDefault();

            if (__instance.overrideMass.TryGetValue(slate, out float mass) && mass > 0f)
                compShuttle.Transporter.massCapacityOverride = mass;

            slate.Set(__instance.storeAs.GetValue(slate), thing);
            return false;
        }
    }

    [HarmonyPatch(typeof(QuestNode_GenerateTransportShip), nameof(QuestNode.RunInt))]
    public static class QuestNode_GenerateTransportShip_Patch
    {
        public static bool Prefix(QuestNode_GenerateTransportShip __instance)
        {
            Slate slate = QuestGen.slate;        
            Thing shipThing = __instance.shipThing.GetValue(slate);
            if (shipThing == null) {
                Log.Warning("[Falloutization: Royalty] QuestNode_GenerateTransportShip: Ship thing not found, bailing.");
                return true;
            }

            Faction faction = shipThing.Faction;
            if (faction == null) {
                Log.Warning("[Falloutization: Royalty] QuestNode_GenerateTransportShip: Ship thing has no faction, bailing.");
                return true;
            }

            FactionModExtension extension = faction.def.GetModExtension<FactionModExtension>();
            if (extension?.transportShipDef == null) {
                Log.Warning("[Falloutization: Royalty] QuestNode_GenerateShuttle: Custom transport ship def not found, bailing.");
                return true;
            }

            __instance.def = extension.transportShipDef;

            return true;
        }
    }

    /// <summary>
    /// Use FCP vertibird size (10,8) for landing spot search when FCP_Vertibird def exists,
    /// so vertibirds get a large enough landing area instead of vanilla shuttle size.
    /// </summary>
    [HarmonyPatch(typeof(DropCellFinder))]
    public static class DropCellFinder_GetBestShuttleLandingSpot_Patch
    {
        static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(DropCellFinder), nameof(DropCellFinder.GetBestShuttleLandingSpot),
                new Type[] { typeof(Map), typeof(Faction), typeof(Thing).MakeByRefType() });
        }

        public static bool Prefix(Map map, Faction factionForFindingSpot, ref Thing firstBlockingThing, ref IntVec3 __result)
        {
            ThingDef vertibirdDef = DefDatabase<ThingDef>.GetNamedSilentFail("FCP_Vertibird");
            if (vertibirdDef == null)
                return true;

            IntVec2 size = vertibirdDef.size;
            IntVec2 sizeWithBorder = size + new IntVec2(2, 2);

            if (!DropCellFinder.TryFindShipLandingArea(map, out IntVec3 result, out firstBlockingThing))
                result = DropCellFinder.TryFindSafeLandingSpotCloseToColony(map, size, factionForFindingSpot);

            if (!result.IsValid && !DropCellFinder.FindSafeLandingSpot(out result, factionForFindingSpot, map, 35, 15, 25, sizeWithBorder))
            {
                IntVec3 intVec = DropCellFinder.RandomDropSpot(map);
                if (!intVec.IsValid)
                    intVec = DropCellFinder.RandomDropSpot(map, standableOnly: false);
                if (!DropCellFinder.TryFindDropSpotNear(intVec, map, out result, allowFogged: false, canRoofPunch: false, allowIndoors: false, sizeWithBorder))
                    result = intVec;
            }

            __result = result;
            return false;
        }
    }
}   
