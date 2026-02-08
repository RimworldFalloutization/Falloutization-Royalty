using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.QuestGen;
using Verse;
using FCP.Core.Shuttles;

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
            TransportShipDef transportShipDef = extension?.transportShipDef;
            if (transportShipDef?.shipThing == null) {
                transportShipDef = DefDatabase<TransportShipDef>.GetNamedSilentFail("FCP_Vertibird");
                if (transportShipDef?.shipThing == null) {
                    Log.Warning("[Falloutization: Royalty] QuestNode_GenerateShuttle: No custom transport ship and FCP_Vertibird not found, falling back to original.");
                    return true;
                }
            }

            ThingDef thingDef = transportShipDef.shipThing;
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
            TransportShipDef transportShipDef = extension?.transportShipDef;
            if (transportShipDef == null) {
                transportShipDef = DefDatabase<TransportShipDef>.GetNamedSilentFail("FCP_Vertibird");
                if (transportShipDef == null) {
                    Log.Warning("[Falloutization: Royalty] QuestNode_GenerateTransportShip: No custom transport ship and FCP_Vertibird not found, bailing.");
                    return true;
                }
            }

            __instance.def = transportShipDef;

            return true;
        }
    }

    /// <summary>
    /// Patch the Hospitality quests pickup if the mod is enabled with the quest already in progress
    /// or gets broken by breaking changes in FCP mods
    /// </summary>
    [HarmonyPatch(typeof(QuestPart_AddShipJob), nameof(QuestPart_AddShipJob.Notify_QuestSignalReceived))]
    public static class QuestPart_AddShipJob_Notify_QuestSignalReceived_Patch
    {
        public static void Prefix(QuestPart_AddShipJob __instance, Signal signal)
        {
            if (__instance == null) {
                return;
            }

            if (signal.tag != __instance.inSignal) {
                return;
            }

            if (__instance.transportShip != null)  {
                return;
            }

            if (__instance.shipJob.def.defName != "Arrive") {
                return;
            }

            var involvedFactions = __instance.quest.InvolvedFactions?.ToList();
            Faction askerFaction = involvedFactions != null && involvedFactions.Count > 0 ? involvedFactions[0] : null;
            if (askerFaction?.def == null)
            {
                Log.Warning("[Falloutization: Royalty] QuestPart_AddShipJob: asker faction is null, bailing.");
                return;
            }

            FactionModExtension extension = askerFaction.def.GetModExtension<FactionModExtension>();
            TransportShipDef transportShipDef = extension?.transportShipDef;
            if (transportShipDef == null) {
                transportShipDef = DefDatabase<TransportShipDef>.GetNamedSilentFail("FCP_Vertibird");
                if (transportShipDef == null) {
                    Log.Warning("[Falloutization: Royalty] QuestPart_AddShipJob: No custom transport ship and FCP_Vertibird not found, bailing.");
                    return;
                }
            }

            var parts = __instance.quest?.PartsListForReading;
            if (parts == null) {
                Log.Warning("[Falloutization: Royalty] QuestPart_AddShipJob: PartsListForReading is null, bailing");
                return;
            }

            List<Pawn> lodgers = null;
            foreach (var part in parts) {
                if (part == null) {
                    continue;
                }

                if (part is QuestPart_ShuttleDelay shuttleDelayPart) {
                    lodgers = shuttleDelayPart.lodgers;
                    break;
                }
            }
            if (lodgers == null)
            {
                Log.Warning("[Falloutization: Royalty] QuestPart_AddShipJob: could not find QuestPart_ShuttleDelay lodgers, bailing.");
                return;
            }
            
            Thing shipThing = ThingMaker.MakeThing(transportShipDef.shipThing);
            // link the ship to the quest so it can detect when the quest is completed
            QuestUtility.AddQuestTag(shipThing, $"Quest{__instance.quest.id}.pickupShipThing");

            var comp = shipThing.TryGetComp<CompShuttle>();
            if (comp == null)
            {
                Log.Warning("[Falloutization: Royalty] QuestPart_AddShipJob: shipThing has no CompShuttle, bailing.");
                return;
            }
            comp.requiredPawns.AddRange(lodgers);
            comp.onlyAcceptColonists = false;

            var transportShip = TransportShipMaker.MakeTransportShip(transportShipDef, new List<Thing>(), shipThing);
            
            __instance.transportShip = transportShip;

            // Propagate the reconstructed transportShip to the subsequent pickup ship-job parts
            int idx = parts.IndexOf(__instance);
            if (idx >= 0)
            {
                void TryAssignNext(int offset)
                {
                    int i = idx + offset;
                    if (i < 0 || i >= parts.Count) return;
                    if (parts[i] is not QuestPart_AddShipJob addJobPart) return;

                    string defName = addJobPart.shipJob?.def?.defName ?? addJobPart.shipJobDef?.defName;
                    if (defName == "WaitTime" || defName == "FlyAway")
                    {
                        addJobPart.transportShip = transportShip;
                    }
                }

                TryAssignNext(1);
                TryAssignNext(2);
            }
            else
            {
                Log.Warning("[Falloutization: Royalty] QuestPart_AddShipJob: could not find current part index to propagate ship, bailing.");
                return;
            }

            transportShip.started = true;
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
