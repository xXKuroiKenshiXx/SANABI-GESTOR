using System;
using BepInEx;
using BepInEx.IL2CPP;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using Sannabi._scripts.Jaeyun.Core.SceneTransition;
using Sannabi._scripts.Jaeyun.Core.Game_State;

#if HERO_DLC
namespace SanabiNativeHero;
#else
namespace SanabiNativeMajor;
#endif

#if HERO_DLC
[BepInPlugin("kuro.sanabi.nativehero", "SANABI · Héroe real en DLC", "0.2.0")]
#else
[BepInPlugin("kuro.sanabi.nativemajor", "SANABI · Major Son real en campaña", "0.2.0")]
#endif
[BepInProcess("SNB.exe")]
public sealed class Plugin : BasePlugin
{
    internal static ManualLogSource Logger=null!;
    public override void Load()
    {
        Logger=Log;
#if HERO_DLC
        var harmony=new Harmony("kuro.sanabi.nativehero");
#else
        var harmony=new Harmony("kuro.sanabi.nativemajor");
#endif
        var getter=AccessTools.PropertyGetter(typeof(PlayerBase),"Instance");
        if(getter==null)throw new MissingMethodException("Controladores nativos de SANABI no compatibles.");
        // Patch void entry points rather than an IL2CPP async struct return.
        // All restorers run before either mod chooses a new actor scene, so
        // both directions can coexist without copying each other's override.
        try
        {
        PatchEntry(harmony,"TransportToNewScene",nameof(NativeSwap.BeforeLoad));
        PatchEntry(harmony,"ContinueScene",nameof(NativeSwap.BeforeContinue));
        PatchEntry(harmony,"OnChapterSelectedEvent",nameof(NativeSwap.BeforeChapter));
        PatchEntry(harmony,"RestartScene",nameof(NativeSwap.BeforeRestart));
        PatchEntry(harmony,"ResetScene",nameof(NativeSwap.BeforeRestart));
        harmony.Patch(getter,prefix:new HarmonyMethod(typeof(NativeSwap),nameof(NativeSwap.Instance)));
        AddComponent<NativeControllerSetup>();
        }
        catch { harmony.UnpatchSelf();NativeSwap.Restore();throw; }
        Log.LogInfo("Intercambio experimental de escena/controlador nativo. No reemplaza texturas ni partidas guardadas.");
    }
    static void PatchEntry(Harmony harmony,string name,string prefix)
    {
        var method=AccessTools.Method(typeof(SceneTransporter),name) ?? throw new MissingMethodException(name);
        harmony.Patch(method,prefix:new HarmonyMethod(typeof(NativeSwap),nameof(NativeSwap.RestoreBeforeEntry)){priority=Priority.First});
        harmony.Patch(method,prefix:new HarmonyMethod(typeof(NativeSwap),prefix){priority=Priority.Last});
    }
}

public static class NativeSwap
{
    internal static bool Requested, NeedsSetup;
    internal static SceneTransporter? Loading;
    internal static GameSceneSO? Original;
#if HERO_DLC
    const GameModeType TargetMode=GameModeType.Dlc_1;
#else
    const GameModeType TargetMode=GameModeType.Main;
#endif
    public static void BeforeLoad(SceneTransporter __instance,GameSceneSO targetSceneSo)
    {
        Configure(__instance,targetSceneSo!=null && !targetSceneSo.notUsePlayer && targetSceneSo.gameModeType==TargetMode);
    }
    public static void BeforeContinue(SceneTransporter __instance,GameModeType gameModeType)=>Configure(__instance,gameModeType==TargetMode);
    public static void BeforeChapter(SceneTransporter __instance,ChapterPointSO chapterPoint)
    { if(chapterPoint!=null)BeforeLoad(__instance,chapterPoint.sceneSo); }
    public static void BeforeRestart(SceneTransporter __instance)=>Configure(__instance,__instance.gameStateManager!=null && __instance.gameStateManager.GetGameMode==TargetMode);
    public static void RestoreBeforeEntry()=>Restore();
    static void Configure(SceneTransporter __instance,bool requested)
    {
        Requested=requested;
        NeedsSetup=Requested;
        if(!Requested)return;
        // Replace the actor scene, not the chapter scene or its game/save mode.
#if HERO_DLC
        if(__instance.playerScene==null) { Requested=false;return; }
        Original=__instance.dlcPlayerScene;__instance.dlcPlayerScene=__instance.playerScene;
#else
        if(__instance.dlcPlayerScene==null) { Requested=false;return; }
        Original=__instance.playerScene;__instance.playerScene=__instance.dlcPlayerScene;
#endif
        Loading=__instance;
        Plugin.Logger.LogInfo("Solicitada escena de jugador nativa alternativa.");
    }
    internal static PlayerBase? Actor()
    {
#if HERO_DLC
        var actor=PlayerBase.DefaultPlayer;
#else
        var actor=PlayerBase.DlcPlayer;
#endif
        return actor!=null && actor.gameObject.activeInHierarchy ? actor : null;
    }
    public static bool Instance(ref PlayerBase __result)
    {
        if(!Requested)return true;
        var actor=Actor();if(actor==null)return true;
        __result=actor;return false;
    }
    internal static void Restore()
    {
        if(Loading!=null && Original!=null)
        {
#if HERO_DLC
            Loading.dlcPlayerScene=Original;
#else
            Loading.playerScene=Original;
#endif
        }
        Loading=null;Original=null;
    }
}

public sealed class NativeControllerSetup : MonoBehaviour
{
    public NativeControllerSetup(IntPtr ptr):base(ptr) { }
    int lastNativeHp, lastWorldHp;
    public void Update()
    {
        if(!NativeSwap.Requested || !NativeSwap.NeedsSetup || SceneTransporter.IsSceneLoading || SceneTransporter.IsTransportToNewScene)return;
        var actor=NativeSwap.Actor();if(actor==null)return;
        try
        {
            InitializeHealth();
#if HERO_DLC
            var hero=PlayerBase.DefaultPlayer;
            hero.SetActiveJump(true);hero.SetActiveWallStick(true);hero.SetActiveHook(true);
            hero.SetActiveWinding(true);hero.SetActiveChargeDash(true);hero.SetActiveRapidChargeDash(true);
#else
            var major=PlayerBase.DlcPlayer;
            major.SetActiveRangeAttack(true);major.SetActiveGainComboGauge(true);
            major.SetActiveBurstAttack(true);major.SetActiveBurstBoost(true);
#endif
            NativeSwap.NeedsSetup=false;
            Plugin.Logger.LogInfo("Controlador real activo: "+actor.GetIl2CppType().FullName);
        }
        catch(Exception e)
        { NativeSwap.Restore();NativeSwap.NeedsSetup=false;Plugin.Logger.LogWarning("Intercambio nativo: "+e.Message); }
    }
    void InitializeHealth()
    {
        var transport=NativeSwap.Loading;
        if(transport==null || transport.playerHp==null || transport.DlcplayerHp==null)return;
#if HERO_DLC
        var max=transport.DlcplayerHp.MaxHealth;var hp=transport.DlcplayerHp.CurrentHealth;
        if(max>0)transport.playerHp.SetPlayerHpData(max,hp);
#else
        var max=transport.playerHp.MaxHealth;var hp=transport.playerHp.GetCurrentHp;
        if(max>0)transport.DlcplayerHp.SetPlayerHpData(max,hp);
#endif
        lastNativeHp=lastWorldHp=hp;
    }
    public void LateUpdate()
    {
        if(!NativeSwap.Requested || NativeSwap.NeedsSetup || SceneTransporter.IsSceneLoading || SceneTransporter.IsTransportToNewScene)return;
        var transport=NativeSwap.Loading;if(transport==null || transport.playerHp==null || transport.DlcplayerHp==null)return;
        try
        {
            // The actor and chapter use different HP services. Mirror changes
            // using setters, never apply damage a second time or change save mode.
#if HERO_DLC
            var native=transport.playerHp.GetCurrentHp;var world=transport.DlcplayerHp.CurrentHealth;
            var next=SanabiCharacterCore.HealthBridge.Resolve(native,world,lastNativeHp,lastWorldHp);
            if(native!=next)transport.playerHp.SetPlayerHpData(transport.DlcplayerHp.MaxHealth,next);
            if(world!=next)transport.DlcplayerHp.SetPlayerHpData(transport.playerHp.MaxHealth,next);
#else
            var native=transport.DlcplayerHp.CurrentHealth;var world=transport.playerHp.GetCurrentHp;
            var next=SanabiCharacterCore.HealthBridge.Resolve(native,world,lastNativeHp,lastWorldHp);
            if(native!=next)transport.DlcplayerHp.SetPlayerHpData(transport.playerHp.MaxHealth,next);
            if(world!=next)transport.playerHp.SetPlayerHpData(transport.DlcplayerHp.MaxHealth,next);
#endif
            lastNativeHp=lastWorldHp=next;
        }
        catch(Exception e) { Plugin.Logger.LogWarning("Sincronización de vida: "+e.Message); }
    }
    public void OnDestroy()=>NativeSwap.Restore();
}
