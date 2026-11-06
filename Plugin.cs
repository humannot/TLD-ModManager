using MelonLoader;
using MelonLoader.Utils;

[assembly: MelonInfo(typeof(ModManager.Plugin), "ModManager", "1.1.0", "humannot", "https://github.com/humannot/TLD-ModManager")]
[assembly: MelonGame("Hinterland", "TheLongDark")]
[assembly: HarmonyDontPatchAll] // patched by hand in OnApplicationStarted, once the Il2Cpp runtime is ready

namespace ModManager;

// A plugin, not a mod: it runs before Mods/ is loaded, so updates replace files that nothing has locked yet.
public sealed class Plugin : MelonPlugin
{
    internal static Core Core = null!;
    internal static MelonPreferences_Entry<bool> AutoUpdate = null!, Asked = null!;
    internal static MelonLogger.Instance Log = null!;

    internal static void SetAutoUpdate(bool on)
    {
        AutoUpdate.Value = on;
        Asked.Value = true;
        MelonPreferences.Save();
    }

    public override void OnPreModsLoaded()
    {
        Log = LoggerInstance;
        var cat = MelonPreferences.CreateCategory("ModManager");
        AutoUpdate = cat.CreateEntry("AutoUpdate", false, description: "Update installed mods from tldmods.net at game start");
        Asked = cat.CreateEntry("AskedAutoUpdate", false, description: "The first-run question about auto-update was answered");
        Core = new Core(MelonEnvironment.GameRootDirectory, s => Log.Msg(s));
        try
        {
            if (Core.ApplyPending() is > 0 and var p) Log.Msg($"Applied deferred files: {p}");
            Core.LoadCatalog();
            Core.Scan();
            Log.Msg($"Catalog: {Core.Catalog.Count} mods, installed: {Core.Installed.Count}");
            if (Asked.Value && AutoUpdate.Value && Core.UpdateAll(onlyAutoUpdate: true) is > 0 and var n) Log.Msg($"Updated mods: {n}");
        }
        catch (Exception e) { Log.Error("Auto-update failed: " + e); }
    }

    // Il2Cpp types are only patchable once the game runtime is up; same moment mods use for OnInitializeMelon.
    public override void OnApplicationStarted()
    {
        try
        {
            HarmonyInstance.CreateClassProcessor(typeof(MenuPatch.Configure)).Patch();
            HarmonyInstance.CreateClassProcessor(typeof(MenuPatch.Add)).Patch();
        }
        catch (Exception e) { Log.Error("Could not add the main-menu button (F8 still works): " + e); }
    }

    public override void OnUpdate() => ManagerUI.Update();
    public override void OnGUI() => ManagerUI.Draw();
}
