using BepInEx;
using BepInEx.Configuration;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using R2API;
using RoR2;
using System;
using System.Collections;
using System.Security;
using System.Security.Permissions;
using UnityEngine;

#pragma warning disable CS0618 // Type or member is obsolete
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618 // Type or member is obsolete
[module: UnverifiableCode]
#pragma warning disable 
namespace ProcSolver
{
    [BepInDependency(R2API.ProcTypeAPI.PluginGUID)]
    [BepInDependency(R2API.LanguageAPI.PluginGUID)]
    [BepInDependency("LordVGames.DamageSourceForEnemies")]
    [BepInDependency("LordVGames.AddRunicLensToProcChain")]

    /// <summary>
    /// Curtails autoplay and excessive proc chaining.
    /// - Adds a proc rate parameter to damage info that is determined by proc chain depth
    /// - Adds damage source requirements for certain procs (bands)
    /// </summary>
    [BepInPlugin(guid, modName, version)]
    public class ProcSolverPlugin : BaseUnityPlugin
    {
        #region plugin info
        public static PluginInfo PInfo { get; private set; }
        public const string guid = "com." + teamName + "." + modName;
        public const string teamName = "RiskOfBrainrot";
        public const string modName = "ProcSolver";
        public const string version = "1.0.1";
        #endregion

        public static ModdedProcType ProccedBySkill;
        #region config
        internal static ConfigFile CustomConfigFile { get; private set; }
        public static ConfigEntry<bool> BandsDamageSourceRequirement { get; set; }
        public static ConfigEntry<int> MaxChainLength { get; set; }
        public static ConfigEntry<int> AutoplayChainLength { get; set; }
        public static ConfigEntry<float> AutoplayProcRate { get; set; }
        public static ConfigEntry<float> ChainProcRate { get; set; }
        #endregion

        void Awake()
        {
            CustomConfigFile = new ConfigFile(Paths.ConfigPath + "\\ProcSolver.cfg", true);
            BandsDamageSourceRequirement = CustomConfigFile.Bind<bool>(
                "Proc Solver : Bands", "Bands Damage Source", true,
                "Should Proc Patcher disable band procs on non-skill or non-equipment sources?"
                );
            MaxChainLength = CustomConfigFile.Bind<int>(
                "Proc Solver : Proc Chains", "Max Proc Chain Length Initiated By Skill Or Equipment", 3,
                "When a proc chain is initiated from a skill or equipment, how many times can proc items be recursively triggered before the chain is cut off. " +
                "Set to -1 to uncap proc chains, or 0 to disallow entirely. " +
                "For example: If this number is 3, then a chain would be SKILL > PROC > PROC > PROC. "
                );
            AutoplayChainLength = CustomConfigFile.Bind<int>(
                "Proc Solver : Proc Chains", "Max Proc Chain Length Initiated By Items (Autoplay)", 1,
                "When a proc chain is initiated from an item, how many times can proc items be recursively triggered before the chain is cut off. ]" +
                "Set to -1 to uncap proc chains, or 0 to disallow entirely. " +
                "For example: If this number is 1, then a chain would be ITEM > PROC. "
                );
            ChainProcRate = CustomConfigFile.Bind<float>(
                "Proc Solver : Proc Rate", "Proc Rate Modifier On Proc Chains Initiated By Skill Or Equipment", 1,
                "Proc chain items will proc other items at a reduced rate based on this modifier. " +
                "For example: If this number is 0.5, then ATG Missile has a 12.5% chance to proc Ukulele instead of 25%."
                );
            AutoplayProcRate = CustomConfigFile.Bind<float>(
                "Proc Solver : Proc Rate", "Proc Rate Modifier On Proc Chains Initiated By Items (Autoplay)", 0.4f,
                "Proc chain items will proc other items at a reduced rate based on this modifier. " +
                "For example: If this number is 0.4, then Ceremonial Dagger has a 10% chance to proc Ukulele instead of 25%."
                );

            ProccedBySkill = ProcTypeAPI.ReserveProcType();

            IL.RoR2.GlobalEventManager.ProcessHitEnemy += AddProcRateMod;
            if (BandsDamageSourceRequirement.Value)
            {
                IL.RoR2.GlobalEventManager.ProcessHitEnemy += AddBandsSkillRequirement;

                LanguageAPI.Add("ITEM_ICERING_DESC",
                    "Hits from <style=cIsUtility>skills or equipment</style> that deal <style=cIsDamage>more than 400% damage</style> also blast enemies with a <style=cIsDamage>runic ice blast</style>, <style=cIsUtility>slowing</style> them by <style=cIsUtility>80%</style> for <style=cIsUtility>3s</style> <style=cStack>(+3s per stack)</style> and dealing <style=cIsDamage>250%</style> <style=cStack>(+250% per stack)</style> TOTAL damage. Recharges every <style=cIsUtility>10</style> seconds.");
                LanguageAPI.Add("ITEM_FIRERING_DESC",
                    "Hits from <style=cIsUtility>skills or equipment</style> that deal <style=cIsDamage>more than 400% damage</style> also blast enemies with a <style=cIsDamage>runic flame tornado</style>, dealing <style=cIsDamage>300%</style> <style=cStack>(+300% per stack)</style> TOTAL damage over time. Recharges every <style=cIsUtility>10</style> seconds.");
                LanguageAPI.Add("ITEM_ELEMENTALRINGVOID_DESC",
                    "Hits from <style=cIsUtility>skills or equipment</style> that deal <style=cIsDamage>more than 400% damage</style> also fire a black hole that <style=cIsUtility>draws enemies within 15m into its center</style>. Lasts <style=cIsUtility>5</style> seconds before collapsing, dealing <style=cIsDamage>100%</style> <style=cStack>(+100% per stack)</style> TOTAL damage. Recharges every <style=cIsUtility>20</style> seconds. <style=cIsVoid>Corrupts all Runald's and Kjaro's Bands</style>.");
            }
        }

        private void AddBandsSkillRequirement(ILContext il)
        {
            ILCursor c = new ILCursor(il);

                c.GotoNext(MoveType.After,
                    x => x.MatchLdcI4((int)ProcType.Rings),
                    x => x.MatchCallOrCallvirt("RoR2.ProcChainMask", nameof(RoR2.ProcChainMask.HasProc))
                    );
                c.Emit(OpCodes.Ldarg_1);
                c.EmitDelegate<Func<bool, DamageInfo, bool>>((cantProc, damageInfo) =>
                {
                    return cantProc || !(damageInfo.damageType.IsDamageSourceSkillBased || damageInfo.damageType.damageSource == DamageSource.Equipment);
                });
        }

        public static float GetProcRateMod(DamageInfo damageInfo) 
        {
            return procRateMod;
        }


        static float procRateMod = 1;
        static int maxChainLength => MaxChainLength.Value;
        static int allowedChainDepthOnAutoplay => AutoplayChainLength.Value;
        private void AddProcRateMod(ILContext il)
        {
            ILCursor c = new ILCursor(il);

            c.Emit(OpCodes.Ldarg_1);
            c.EmitDelegate<Action<DamageInfo>>((damageInfo) =>
            {
                procRateMod = GetProcRate(damageInfo);
            });
        }
        static float GetProcRate(DamageInfo damageInfo)
        {
            if (maxChainLength < 0)
                return 1;
            if (maxChainLength == 0)
                return 0;

            bool skillBased = damageInfo.damageType.IsDamageSourceSkillBased || damageInfo.damageType.damageSource == DamageSource.Equipment;
            //if damage came from a skill or equipment th
            if (skillBased)
            {
                damageInfo.procChainMask.AddModdedProc(ProccedBySkill);
                return 1;
            }

            int chainDepth = GetProcChainDepth(damageInfo.procChainMask);
            bool initiatedBySkill = ProcTypeAPI.HasModdedProc(damageInfo.procChainMask, ProccedBySkill);
            //procced by skill increases the chain depth by 1 which should be accounted for
            if (initiatedBySkill)
                chainDepth -= 1;
            else if(allowedChainDepthOnAutoplay >= 0)//procced by item
                chainDepth += Mathf.Max(0, maxChainLength - allowedChainDepthOnAutoplay);

            if (chainDepth < maxChainLength)
                return initiatedBySkill ? ChainProcRate.Value : AutoplayProcRate.Value;

            return 0;
        }

        public static int GetProcChainDepth(ProcChainMask procChainMask)
        {
            int chainDepth = 1;
            for (ProcType procType = 0; procType < ProcType.Count; procType++)
            {
                if (procType == ProcType.LoaderLightning ||
                    procType == ProcType.Thorns ||
                    procType == ProcType.Backstab ||
                    procType == ProcType.LoaderLightning ||
                    procType == ProcType.FractureOnHit ||
                    procType == ProcType.MicroMissile
                )
                    continue;
                
                if (procChainMask.HasProc(procType))
                {
                    chainDepth++;
                }
            }

            BitArray moddedMask = ProcTypeAPI.GetModdedMask(procChainMask);
            for (int i = 1; i < moddedMask.Count; i++)
            {
                if (moddedMask.Get(i))
                {
                    chainDepth++;
                }
            }
            return chainDepth;
        }
    }
}
