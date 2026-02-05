using System.Reflection;
using HarmonyLib;
using RimWorld.QuestGen;
using Verse;

namespace Falloutization.Royalty
{
    [StaticConstructorOnStartup]
    public static class Mod
    {
        private static readonly Harmony harmony = new Harmony("Falloutization.Royalty");
        
        static Mod()
        {
            Log.Message("[Falloutization: Royalty] Initializing Harmony patches...");
            harmony.PatchAll();
                       
            Log.Message("[Falloutization: Royalty] Mod loaded successfully.");
        }
    }
}
