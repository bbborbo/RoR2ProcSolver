using BepInEx;
using BepInEx.Configuration;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using ProcSolver;
using RoR2;
using RoR2.Projectile;
using System;
using System.Runtime.CompilerServices;
using System.Security;
using System.Security.Permissions;
using UnityEngine;
using UnityEngine.AddressableAssets;

#pragma warning disable CS0618 // Type or member is obsolete
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618 // Type or member is obsolete
[module: UnverifiableCode]
#pragma warning disable 
namespace ProcPatcher
{
    //[BepInDependency(ProcSolverPlugin.guid, BepInDependency.DependencyFlags.SoftDependency)]
    /// <summary>
    /// Fixes certain negative proc interactions
    /// - Proc coefficient only affects damage-agnostic procs, once
    /// - Adds damage sourcing to Shuriken
    /// </summary>
    [BepInPlugin(guid, modName, version)]
    public class ProcPatcherPlugin : BaseUnityPlugin
    {
        #region plugin info
        public static PluginInfo PInfo { get; private set; }
        public const string guid = "com." + teamName + "." + modName;
        public const string teamName = "RiskOfBrainrot";
        public const string modName = "ProcPatcher";
        public const string version = "1.0.3";
        #endregion

        #region config
        internal static ConfigFile CustomConfigFile { get; private set; }
        public static ConfigEntry<bool> ShurikenDamageSource { get; set; }
        public static ConfigEntry<bool> BleedChanceProcCoeff { get; set; }
        public static ConfigEntry<bool> RunicLensProcCoeff { get; set; }
        public static ConfigEntry<bool> ElectricBoomerangProcCoeff { get; set; }
        #endregion

        void Awake()
        {
            CustomConfigFile = new ConfigFile(Paths.ConfigPath + "\\ProcPatcher.cfg", true);
            ShurikenDamageSource = CustomConfigFile.Bind<bool>("Proc Patcher : Damage Source", "Shuriken Damage Source", true, "Should Proc Patcher set Shuriken's Damage Source to Primary?");
            BleedChanceProcCoeff = CustomConfigFile.Bind<bool>("Proc Patcher : Proc Coeff Interactions", "Should Bleed Proc Chance Be Affected By Proc Coefficient", true, "Should Bleed Proc Chance Be Affected By Proc Coefficient");
            RunicLensProcCoeff = CustomConfigFile.Bind<bool>("Proc Patcher : Proc Coeff Interactions", "Should Runic Lens Proc Chance Be Affected By Proc Coefficient", true, "Should Runic Lens Proc Chance Be Affected By Proc Coefficient");
            ElectricBoomerangProcCoeff = CustomConfigFile.Bind<bool>("Proc Patcher : Proc Coeff Interactions", "Should Electric Boomerang Proc Chance Be Affected By Proc Coefficient", true, "Should Electric Boomerang Proc Chance Be Affected By Proc Coefficient");

            IL.RoR2.GlobalEventManager.ProcessHitEnemy += ProcCoeffFix_OnHitEnemy;

            if (ShurikenDamageSource.Value)
            {
                GameObject shurikenProjectile = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC1/PrimarySkillShuriken/ShurikenProjectile.prefab").WaitForCompletion();
                if (shurikenProjectile != null)
                {
                    ProjectileDamage pd = shurikenProjectile.GetComponent<ProjectileDamage>();
                    if (pd)
                    {
                        pd.damageType.damageSource = DamageSource.Primary;
                    }
                }
            }
        }
        private void ProcCoeffFix_OnHitEnemy(ILContext il)
        {
            ILCursor c = new ILCursor(il);

            FixChanceForProcItem(c, "RoR2.RoR2Content/Items", "BleedOnHitAndExplode", isChainProc: false, fixProcCoeff: BleedChanceProcCoeff.Value); //this is bleed chance
            FixChanceForProcItem(c, "RoR2.RoR2Content/Items", "Missile");
            FixChanceForProcItem(c, "RoR2.RoR2Content/Items", "ChainLightning");
            FixChanceForProcItem(c, "RoR2.DLC1Content/Items", "ChainLightningVoid");
            FixChanceForProcItem(c, "RoR2.RoR2Content/Items", "BounceNearby");
            FixChanceForProcItem(c, "RoR2.RoR2Content/Items", "StickyBomb", isChainProc: false);
            FixChanceForProcItem(c, "RoR2.RoR2Content/Items", "FireballsOnHit");
            FixChanceForProcItem(c, "RoR2.RoR2Content/Items", "LightningStrikeOnHit");
            FixChanceForProcItem(c, "RoR2.DLC2Content/Items", "MeteorAttackOnHighDamage", fixProcCoeff: RunicLensProcCoeff.Value);
            FixChanceForProcItem(c, "RoR2.DLC2Content/Items", "StunAndPierce", fixProcCoeff: ElectricBoomerangProcCoeff.Value);
        }

        private void FixChanceForProcItem(ILCursor c, string a, string b, bool isChainProc = true, bool fixProcCoeff = true)
        {
            c.Index = 0;

            if(
                c.TryGotoNext(
                    MoveType.After,
                    x => x.MatchLdsfld(a, b),
                    x => x.MatchCallOrCallvirt("RoR2.Inventory", nameof(RoR2.Inventory.GetItemCount))
                    )
                && c.TryGotoNext(
                    MoveType.Before,
                    x => x.MatchLdfld<DamageInfo>(nameof(DamageInfo.procCoefficient))
                    )
                )
            {
                c.Remove();
                c.EmitDelegate<Func<DamageInfo, float>>((damageInfo) =>
                {
                    float procRate = 1;

                    if (isChainProc)
                        procRate *= GetProcRate(damageInfo);
                    if (!fixProcCoeff)
                        procRate *= damageInfo.procCoefficient;

                    return procRate;
                });
                Debug.Log(b + " Proc Hook Success");
            }
            else
            {
                Debug.LogError(b + " Proc Hook Failed");
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        private float GetProcRate(DamageInfo damageInfo)
        {
            if(BepInEx.Bootstrap.Chainloader.PluginInfos[ProcSolverPlugin.guid] == null)
            {
                return 1;
            }
            return ProcSolverPlugin.GetProcRateMod(damageInfo);
        }
    }
}
