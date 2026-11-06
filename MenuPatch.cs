using HarmonyLib;
using Il2Cpp;
using UnityEngine;

namespace ModManager;

// Adds a "Mods" entry to the main menu, directly above "Quit", styled by the game's own BasicMenu.
// shortcut: the item has no tint of its own, it borrows the Quit item's; upgrade = read the tint the game's AddMenuItem uses.
static class MenuPatch
{
    const string Label = "ModManager_Mods";

    static bool Quit(Panel_MainMenu.MainMenuItem i) => i.m_Type == Panel_MainMenu.MainMenuItem.MainMenuItemType.Quit;

    [HarmonyPatch(typeof(Panel_MainMenu), nameof(Panel_MainMenu.ConfigureMenu))]
    internal static class Configure
    {
        static void Prefix(Panel_MainMenu __instance)
        {
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
            if (mainMenuItem.m_LabelLocalizationId != Label) return true;
            var open = (Il2CppSystem.Action)new System.Action(ManagerUI.OpenFromMenu);
            __instance.m_BasicMenu.AddItem(Label, 100, itemIndex, "Mods", "", "", open,
                mainMenuItem.m_OverrideTintNormal, mainMenuItem.m_OverrideTintHighlight);
            return false;
        }
    }
}
