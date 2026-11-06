using HarmonyLib;
using Il2Cpp;
using UnityEngine;

namespace ModManager;

// Adds a "Mods" entry to the main menu, directly above "Quit", styled by the game's own BasicMenu.
// shortcut: the item has no tint of its own, it borrows the Quit item's; upgrade = read the tint the game's AddMenuItem uses.
static class MenuPatch
{
    internal const string Label = "ModManager_Mods";

    internal static int ConfigureCalls, AddCalls;

    /// <summary>One-off dump of what the main menu looks like, for diagnosing the button.</summary>
    internal static void Dump(Panel_MainMenu panel)
    {
        var log = Plugin.Log;
        log.Msg($"[menu] Harmony hits: ConfigureMenu={ConfigureCalls}, AddMenuItem={AddCalls}");
        var items = panel.m_MenuItems;
        log.Msg($"[menu] panel.m_MenuItems: {(items == null ? "null" : items.Count.ToString())}");
        for (int i = 0; items != null && i < items.Count; i++) log.Msg($"[menu]   item {i}: type={items[i].m_Type} label={items[i].m_LabelLocalizationId}");
        var models = panel.m_BasicMenu?.m_ItemModelList;
        log.Msg($"[menu] BasicMenu models: {(models == null ? "null" : models.Count.ToString())}");
        for (int i = 0; models != null && i < models.Count; i++) log.Msg($"[menu]   model {i}: id={models[i].m_Id} idx={models[i].m_ItemIndex} text={models[i].m_LabelText}");
    }

    static bool Quit(Panel_MainMenu.MainMenuItem i) => i.m_Type == Panel_MainMenu.MainMenuItem.MainMenuItemType.Quit;

    [HarmonyPatch(typeof(Panel_MainMenu), nameof(Panel_MainMenu.ConfigureMenu))]
    internal static class Configure
    {
        static void Prefix(Panel_MainMenu __instance)
        {
            ConfigureCalls++;
            var items = __instance.m_MenuItems;
            if (items == null) return;
            int quit = -1;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].m_LabelLocalizationId == Label) return; // already added
                if (quit < 0 && Quit(items[i])) quit = i;
            }
            var src = quit >= 0 ? items[quit] : null;
            var item = new Panel_MainMenu.MainMenuItem
            {
                m_Type = Panel_MainMenu.MainMenuItem.MainMenuItemType.Debug, // never acted on: AddMenuItem below handles our label
                m_LabelLocalizationId = Label,
                m_UseOverrideTint = src != null && src.m_UseOverrideTint,
            };
            if (src != null) { item.m_OverrideTintNormal = src.m_OverrideTintNormal; item.m_OverrideTintHighlight = src.m_OverrideTintHighlight; }
            items.Insert(quit >= 0 ? quit : items.Count, item);
        }
    }

    [HarmonyPatch(typeof(Panel_MainMenu), nameof(Panel_MainMenu.AddMenuItem))]
    internal static class Add
    {
        static bool Prefix(Panel_MainMenu __instance, Panel_MainMenu.MainMenuItem mainMenuItem, int itemIndex)
        {
            AddCalls++;
            if (mainMenuItem.m_LabelLocalizationId != Label) return true;
            var open = (Il2CppSystem.Action)new System.Action(ManagerUI.OpenFromMenu);
            __instance.m_BasicMenu.AddItem(Label, 100, itemIndex, L.T("Mods"), "", "", open,
                mainMenuItem.m_OverrideTintNormal, mainMenuItem.m_OverrideTintHighlight);
            return false;
        }
    }
}
