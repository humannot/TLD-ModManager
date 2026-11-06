using MelonLoader;
using MelonLoader.Utils;

[assembly: MelonInfo(typeof(ModManager.Plugin), "ModManager", "1.0.0", "humannot", "https://github.com/humannot/TLD-ModManager")]
[assembly: MelonGame("Hinterland", "TheLongDark")]

namespace ModManager;

// A plugin, not a mod: it runs before Mods/ is loaded, so updates replace files that nothing has locked yet.
public sealed class Plugin : MelonPlugin
{
    internal static Core Core = null!;
    internal static MelonPreferences_Entry<bool> AutoUpdate = null!;
    internal static MelonLogger.Instance Log = null!;

    public override void OnPreModsLoaded()
    {
        Log = LoggerInstance;
        AutoUpdate = MelonPreferences.CreateCategory("ModManager").CreateEntry("AutoUpdate", true,
            description: "Update installed mods from tldmods.net at game start");
        Core = new Core(MelonEnvironment.GameRootDirectory, s => Log.Msg(s));
        try
        {
            if (Core.ApplyPending() is > 0 and var p) Log.Msg($"Применено отложенных файлов: {p}");
            Core.LoadCatalog();
            Core.Scan();
            Log.Msg($"Каталог: {Core.Catalog.Count} модов, установлено: {Core.Installed.Count}");
            if (AutoUpdate.Value && Core.UpdateAll(onlyAutoUpdate: true) is > 0 and var n) Log.Msg($"Обновлено модов: {n}");
        }
        catch (Exception e) { Log.Error("Автообновление не удалось: " + e); }
    }

    public override void OnUpdate() => ManagerUI.Update();
    public override void OnGUI() => ManagerUI.Draw();
}
