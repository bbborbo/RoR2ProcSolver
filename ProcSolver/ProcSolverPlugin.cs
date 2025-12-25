using BepInEx;
using BepInEx.Configuration;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using R2API;
using RoR2;
using RoR2BepInExPack.Utilities;
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
    [BepInDependency("LordVGames.AddRunicLensToProcChain", BepInDependency.DependencyFlags.SoftDependency)]

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
        public const string version = "1.1.5";
        #endregion

        public static FixedConditionalWeakTable<DamageInfo, MoreDamageInfoStats> moreDamageInfoStats = new FixedConditionalWeakTable<DamageInfo, MoreDamageInfoStats>();
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
                "When a proc chain is initiated from an item, how many times can proc items be recursively triggered before the chain is cut off. " +
                "Set to -1 to uncap proc chains, or 0 to disallow entirely. " +
                "For example: If this number is 1, then a chain would be ITEM > PROC. "
                );
            ChainProcRate = CustomConfigFile.Bind<float>(
                "Proc Solver : Proc Rate", "Proc Rate Modifier On Proc Chains Initiated By Skill Or Equipment", 1.0f,
                "Proc chain items will proc other items at a reduced rate based on this modifier. " +
                "For example: If this number is 0.5, then ATG Missile has a 12.5% chance to proc Ukulele instead of 25%."
                );
            AutoplayProcRate = CustomConfigFile.Bind<float>(
                "Proc Solver : Proc Rate", "Proc Rate Modifier On Proc Chains Initiated By Items (Autoplay)", 0.5f,
                "Proc chain items will proc other items at a reduced rate based on this modifier. " +
                "For example: If this number is 0.2, then Ceremonial Dagger has a 5% chance to proc Ukulele instead of 25%."
                );

            ProccedBySkill = ProcTypeAPI.ReserveProcType(); 

            IL.RoR2.HealthComponent.TakeDamage += AddProcRateMod;
            IL.RoR2.GlobalEventManager.OnHitEnemy += AddProcRateMod;
        }

        public static float GetProcRateMod(DamageInfo damageInfo)
        {
            if (damageInfo == null)
                return 1;
            return moreDamageInfoStats.GetOrCreateValue(damageInfo).procRate;
        }
        static int maxChainLength => MaxChainLength.Value;
        static int allowedChainDepthOnAutoplay => AutoplayChainLength.Value;
        private void AddProcRateMod(ILContext il)
        {
            ILCursor c = new ILCursor(il);

            c.Emit(OpCodes.Ldarg_1);
            c.EmitDelegate<Action<DamageInfo>>((damageInfo) =>
            {
                MoreDamageInfoStats mdis = moreDamageInfoStats.GetOrCreateValue(damageInfo);
                mdis.procRate = GetProcRate(damageInfo);
                if (mdis.procRate == 0 && damageInfo.procChainMask.HasProc(ProcType.SureProc))
                    damageInfo.procChainMask.RemoveProc(ProcType.SureProc);
            });
        }
        static float GetProcRate(DamageInfo damageInfo)
        {
            if (maxChainLength < 0)
                return 1;
            if (maxChainLength == 0)
                return 0;

            float procRate = damageInfo.procCoefficient;
            bool skillBased = damageInfo.damageType.IsDamageSourceSkillBased || damageInfo.damageType.damageSource == DamageSource.Equipment;
            // ignore depth checks if damage came from a skill or equipment
            if (skillBased)
            {
                damageInfo.procChainMask.AddModdedProc(ProccedBySkill);
                return Mathf.Max(procRate, 1);
            }

            int chainDepth = GetProcChainDepth(damageInfo.procChainMask);
            bool initiatedBySkill = ProcTypeAPI.HasModdedProc(damageInfo.procChainMask, ProccedBySkill);
            //procced by skill increases the chain depth by 1 which should be accounted for
            if (initiatedBySkill)
                chainDepth -= 1;
            else if(allowedChainDepthOnAutoplay >= 0)//if procced by item, add to chain depth
                chainDepth += Mathf.Max(0, maxChainLength - allowedChainDepthOnAutoplay);


            if (chainDepth >= maxChainLength)
                return 0;

            return procRate * (initiatedBySkill ? ChainProcRate.Value : AutoplayProcRate.Value);
        }

        public static readonly uint exemptProcTypes =
            (1 << (int)ProcType.AACannon) |
            (1 << (int)ProcType.Backstab) |
            (1 << (int)ProcType.Behemoth) |
            (1 << (int)ProcType.BleedOnHit) |
            (1 << (int)ProcType.Count) |
            (1 << (int)ProcType.CritAtLowerElevation) |
            (1 << (int)ProcType.CritHeal) |
            (1 << (int)ProcType.ExplodeOnDeathVoid) |
            (1 << (int)ProcType.FractureOnHit) |
            (1 << (int)ProcType.HealNova) |
            (1 << (int)ProcType.HealOnHit) |
            (1 << (int)ProcType.LoaderLightning) |
            (1 << (int)ProcType.LunarPotionActivation) |
            (1 << (int)ProcType.MicroMissile) |
            (1 << (int)ProcType.PlasmaCore) |
            (1 << (int)ProcType.RepeatHeal) |
            (1 << (int)ProcType.Rings) |
            (1 << (int)ProcType.SharedSuffering) |
            (1 << (int)ProcType.SureProc) |
            (1 << (int)ProcType.Thorns) |
            (1 << (int)ProcType.VoidSurvivorCrush);
        public static readonly uint ChainProcTypes =
            //(1 << (int)ProcType.Behemoth) | //behemoth
            (1 << (int)ProcType.BounceNearby) | //meathook
            (1 << (int)ProcType.ChainLightning) | //ukulele
            (1 << (int)ProcType.LightningStrikeOnHit) | //cherf
            (1 << (int)ProcType.Meatball) | //merf
            (1 << (int)ProcType.MeteorAttackOnHighDamage) | //runic
            (1 << (int)ProcType.Missile) | //atg
            (1 << (int)ProcType.Rings) | //bands
            (1 << (int)ProcType.SharedSuffering) |
            (1 << (int)ProcType.StunAndPierceDamage) | //eboomerang
            (1 << (int)ProcType.WyrmOnHit); //sauteed worms
        public static int GetProcChainDepth(ProcChainMask procChainMask)
        {
            int chainDepth = 1;
            for (ProcType procType = 0; procType < ProcType.Count; procType++)
            {
                if((procChainMask.mask & ChainProcTypes) != 0L)
                {
                    chainDepth++;
                }
            }

            BitArray moddedMask = ProcTypeAPI.GetModdedMask(procChainMask);
            for (int i = 1; i < moddedMask.Count; i++)
            {
                //get value for this bit, i
                if (moddedMask.Get(i))
                {
                    chainDepth++;
                }
            }
            return chainDepth;
        }
    }
    public class MoreDamageInfoStats
    {
        public float procRate = 1;
        public int procChainDepth = 0;
        public bool isAutoplay = false;
    }
}
