using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace PgTranslateLive;

/// <summary>
/// Boot: ver fluxo completo em FLOW.md (Steam → BepInEx → Load → Unity → Translation/ → login).
/// </summary>
[BepInPlugin("com.pg.translatelive", "Pg Translate Live", "0.10.0")]
public class Plugin : BasePlugin
{
    internal static new ManualLogSource Log = null!;

    /// <summary>Momento 1 — síncrono: Harmony Talk + (futuro) agendar PackSync.</summary>
    public override void Load()
    {
        Log = base.Log;

        TranslateClient.Init(Config);
        NpcYamlStore.Init(Config);

        var harmony = new Harmony("com.pg.translatelive");
        var talkPatches = TalkScreenPatch.PatchAll(harmony);

        LogAscii.LogInfo($"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss} Pg Translate Live v0.10.0 - Falar + NpcYaml + Google");
        LogAscii.LogInfo($"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss} Patches: talk:{talkPatches}");
        LogAscii.LogInfo($"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss} UseGoogle: {TranslateClient.UseGoogle} | NpcYaml: {NpcYamlStore.Count} | TalkVerbose: {TranslateClient.LogVerbose}");
    }
}
