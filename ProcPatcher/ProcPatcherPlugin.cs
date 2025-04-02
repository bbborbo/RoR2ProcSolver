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
        public const string version = "1.0.0";
        #endregion

        #region config
        internal static ConfigFile CustomConfigFile { get; private set; }
        public static ConfigEntry<bool> ShurikenDamageSource { get; set; }
        #endregion

        void Awake()
        {
            CustomConfigFile = new ConfigFile(Paths.ConfigPath + "\\ProcPatcher.cfg", true);
            ShurikenDamageSource = CustomConfigFile.Bind<bool>("Proc Patcher", "Shuriken Damage Source", true, "Should Proc Patcher set Shuriken's Damage Source to Primary?");

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

            FixChanceForProcItem(c, "RoR2.RoR2Content/Items", "BleedOnHitAndExplode", false); //this is bleed chance
            FixChanceForProcItem(c, "RoR2.RoR2Content/Items", "Missile");
            FixChanceForProcItem(c, "RoR2.RoR2Content/Items", "ChainLightning");
            FixChanceForProcItem(c, "RoR2.DLC1Content/Items", "ChainLightningVoid");
            FixChanceForProcItem(c, "RoR2.RoR2Content/Items", "BounceNearby");
            FixChanceForProcItem(c, "RoR2.RoR2Content/Items", "StickyBomb", false);
            FixChanceForProcItem(c, "RoR2.RoR2Content/Items", "FireballsOnHit");
            FixChanceForProcItem(c, "RoR2.RoR2Content/Items", "LightningStrikeOnHit");
            //FixChanceForProcItem(c, "RoR2.DLC2Content/Items", "MeteorAttackOnHighDamage");
            //FixChanceForProcItem(c, "RoR2.DLC2Content/Items", "StunAndPierceDamage");
        }

        private void FixChanceForProcItem(ILCursor c, string a, string b, bool isChainProc = true)
        {
            c.Index = 0;

            c.GotoNext(
                MoveType.After,
                x => x.MatchLdsfld(a, b),
                x => x.MatchCallOrCallvirt("RoR2.Inventory", nameof(RoR2.Inventory.GetItemCount))
                );
            c.GotoNext(
                MoveType.Before,
                x => x.MatchLdfld<DamageInfo>(nameof(DamageInfo.procCoefficient))
                );
            c.Remove();
            c.EmitDelegate<Func<DamageInfo, float>>((damageInfo) =>
            {
                return GetProcRate(damageInfo);
            });
        }

        private float GetProcRate(DamageInfo damageInfo)
        {
            if(BepInEx.Bootstrap.Chainloader.PluginInfos[ProcSolverPlugin.guid] == null)
            {
                return 1;
            }
            return _GetProcRate(damageInfo);
        }
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        private float _GetProcRate(DamageInfo damageInfo)
        {
            float mod = ProcSolverPlugin.GetProcRateMod();
            return mod;
        }
    }
}
