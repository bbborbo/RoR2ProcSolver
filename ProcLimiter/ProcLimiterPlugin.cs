using BepInEx;
using BepInEx.Configuration;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using R2API;
using R2API.Utils;
using RoR2;
using System;
using System.Security;
using System.Security.Permissions;

#pragma warning disable CS0618 // Type or member is obsolete
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618 // Type or member is obsolete
[module: UnverifiableCode]
#pragma warning disable 
namespace ProcLimiter
{
    [BepInDependency(R2API.LanguageAPI.PluginGUID)]
    [BepInDependency("LordVGames.DamageSourceForEnemies", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("LordVGames.DamageSourceForEquipment", BepInDependency.DependencyFlags.SoftDependency)]
    /// <summary>
    /// Curtails autoplay and excessive proc chaining.
    /// - Adds a proc rate parameter to damage info that is determined by proc chain depth
    /// - Adds damage source requirements for certain procs (bands)
    /// </summary>
    [BepInPlugin(guid, modName, version)]
    [NetworkCompatibility(CompatibilityLevel.NoNeedForSync, VersionStrictness.DifferentModVersionsAreOk)]
    public class ProcLimiterPlugin : BaseUnityPlugin
    {
        #region plugin info
        public static PluginInfo PInfo { get; private set; }
        public const string guid = "com." + teamName + "." + modName;
        public const string teamName = "RiskOfBrainrot";
        public const string modName = "ProcLimiter";
        public const string version = "1.0.3";
        #endregion
        internal static ConfigFile CustomConfigFile { get; private set; }
        public static ConfigEntry<bool> DoBands { get; set; }
        public static ConfigEntry<bool> DoChronic { get; set; }
        void Awake()
        {
            CustomConfigFile = new ConfigFile(Paths.ConfigPath + "\\ProcSolver.cfg", true);
            DoBands = CustomConfigFile.Bind<bool>(
                "Proc Limiter", "Bands Damage Source", true,
                "Should Proc Limiter disable band procs on non-skill or non-equipment sources?"
                );
            DoChronic = CustomConfigFile.Bind<bool>(
                "Proc Limiter", "Chronix Expansion Damage Source", true,
                "Should Proc Limiter disable chronic expansion buff extension on non-skill or non-equipment sources?"
                );

            if (DoBands.Value)
            {
                IL.RoR2.GlobalEventManager.ProcessHitEnemy += AddBandsSkillRequirement;

                //LanguageAPI.Add("ITEM_ICERING_DESC",
                //    "Hits from <style=cIsUtility>skills or equipment</style> that deal <style=cIsDamage>more than 400% damage</style> also blast enemies with a <style=cIsDamage>runic ice blast</style>, <style=cIsUtility>slowing</style> them by <style=cIsUtility>80%</style> for <style=cIsUtility>3s</style> <style=cStack>(+3s per stack)</style> and dealing <style=cIsDamage>250%</style> <style=cStack>(+250% per stack)</style> TOTAL damage. Recharges every <style=cIsUtility>10</style> seconds.");
                //LanguageAPI.Add("ITEM_FIRERING_DESC",
                //    "Hits from <style=cIsUtility>skills or equipment</style> that deal <style=cIsDamage>more than 400% damage</style> also blast enemies with a <style=cIsDamage>runic flame tornado</style>, dealing <style=cIsDamage>300%</style> <style=cStack>(+300% per stack)</style> TOTAL damage over time. Recharges every <style=cIsUtility>10</style> seconds.");
                //LanguageAPI.Add("ITEM_ELEMENTALRINGVOID_DESC",
                //    "Hits from <style=cIsUtility>skills or equipment</style> that deal <style=cIsDamage>more than 400% damage</style> also fire a black hole that <style=cIsUtility>draws enemies within 15m into its center</style>. Lasts <style=cIsUtility>5</style> seconds before collapsing, dealing <style=cIsDamage>100%</style> <style=cStack>(+100% per stack)</style> TOTAL damage. Recharges every <style=cIsUtility>20</style> seconds. <style=cIsVoid>Corrupts all Runald's and Kjaro's Bands</style>.");
            }
            if (DoChronic.Value)
            {
                IL.RoR2.GlobalEventManager.ProcessHitEnemy += AddChronicSkillRequirement;

                //LanguageAPI.Add("ITEM_INCREASEDAMAGEONMULTIKILL_DESC",
                //    "Killing an enemy increases your damage by <style=cIsDamage>3.5%</style> <style=cStack>(+1% per stack)</style>, " +
                //    "up to <style=cIsUtility>10</style> <style=cStack>(+5 per stack)</style>, for <style=cIsUtility>7s</style>. " +
                //    "Dealing damage with <style=cIsUtility>skills or equipment</style> refreshes the timer.");
            }        
        }

        private void AddChronicSkillRequirement(ILContext il)
        {
            ILCursor c = new ILCursor(il);

            bool b = c.TryGotoNext(
                MoveType.After,
                x => x.MatchLdsfld("RoR2.DLC2Content/Items", "IncreaseDamageOnMultikill"),
                x => x.MatchCallOrCallvirt("RoR2.Inventory", nameof(RoR2.Inventory.GetItemCount))
                );
            if (!b)
                return;

            c.Emit(OpCodes.Ldarg_1);
            c.EmitDelegate<Func<int, DamageInfo, int>>((itemCount, damageInfo) =>
            {
                if (damageInfo.damageType.IsDamageSourceSkillBased || damageInfo.damageType.damageSource == DamageSource.Equipment)
                    return itemCount;
                return 0;
            });
        }

        private void AddBandsSkillRequirement(ILContext il)
        {
            ILCursor c = new ILCursor(il);

            bool b = c.TryGotoNext(MoveType.After,
                x => x.MatchLdcI4((int)ProcType.Rings),
                x => x.MatchCallOrCallvirt("RoR2.ProcChainMask", nameof(RoR2.ProcChainMask.HasProc))
                );
            if (!b)
                return;
            c.Emit(OpCodes.Ldarg_1);
            c.EmitDelegate<Func<bool, DamageInfo, bool>>((cantProc, damageInfo) =>
            {
                return cantProc || !(damageInfo.damageType.IsDamageSourceSkillBased || damageInfo.damageType.damageSource == DamageSource.Equipment);
            });
        }
    }
}
