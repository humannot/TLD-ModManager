using HarmonyLib;
using Il2Cpp;
using UnityEngine;

namespace ModManager;

// Adds a "Mods" entry to the main menu, directly above "Quit". Same approach as SkyCoop's MULTIPLAYER button:
//  - a MainMenuItem with a type the game does not know goes into Panel_MainMenu.m_MenuItems (ConfigureMenu skips Debug and
//    unowned upsell items, so it is the unknown type that gets it through to AddMenuItem), so every rebuild of the menu keeps it;
//  - a model is also added to the menu's BasicMenu right away, because the menu is already built when we get here;
//  - clicks are recognised in BasicMenu.InternalClickAction by the item's id (the type's number).
static class MenuPatch
{
    const int ItemType = 77; // SkyCoop uses 31
    internal static readonly string ItemId = ItemType.ToString();
    internal static int InitCalls, Added;

    static Panel_MainMenu.MainMenuItem.MainMenuItemType Type => (Panel_MainMenu.MainMenuItem.MainMenuItemType)ItemType;

    static bool HasModel(BasicMenu bm)
    {
        var models = bm.m_ItemModelList;
        for (int i = 0; i < models.Count; i++) if (models[i].m_Id == ItemId) return true;
        return false;
    }

    /// <summary>Idempotent: makes sure both the panel's item list and the live menu contain the Mods item.</summary>
    internal static void Ensure(Panel_MainMenu panel)
    {
        var text = L.T("Mods");
        var items = panel.m_MenuItems;
        if (items != null)
        {
            bool have = false; int quit = items.Count;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].m_Type == Type) have = true;
                else if (items[i].m_Type == Panel_MainMenu.MainMenuItem.MainMenuItemType.Quit && quit == items.Count) quit = i;
            }
            if (!have) items.Insert(quit, new Panel_MainMenu.MainMenuItem { m_Type = Type, m_LabelLocalizationId = text });
        }

        var bm = panel.m_BasicMenu;
        if (bm == null || bm.m_ItemModelList == null || bm.m_ItemModelList.Count == 0 || HasModel(bm)) return;
        var models = bm.m_ItemModelList;
        int q = models.Count - 1;
        for (int i = 0; i < models.Count; i++) if (models[i].m_Id == "Quit") { q = i; break; }
        var like = models[0]; // borrow the Survival Mode item's colours
        var open = (Il2CppSystem.Action)new System.Action(ManagerUI.OpenFromMenu);
        bm.AddItem(ItemId, ItemType, q, text, "", "", open, like.m_NormalTint, like.m_HighlightTint);
        Added++;
    }

    /// <summary>One-off dump of what the main menu looks like, for diagnosing the button.</summary>
    internal static void Dump(Panel_MainMenu panel)
    {
        var log = Plugin.Log;
        log.Msg($"[menu] Initialize hits: {InitCalls}, items added: {Added}");
        var items = panel.m_MenuItems;
        for (int i = 0; items != null && i < items.Count; i++) log.Msg($"[menu]   item {i}: type={items[i].m_Type} label={items[i].m_LabelLocalizationId}");
        var models = panel.m_BasicMenu?.m_ItemModelList;
        log.Msg($"[menu] BasicMenu models: {(models == null ? "null" : models.Count.ToString())}, views: {panel.m_BasicMenu?.m_MenuItems?.Count}");
        for (int i = 0; models != null && i < models.Count; i++) log.Msg($"[menu]   model {i}: id={models[i].m_Id} idx={models[i].m_ItemIndex} text={models[i].m_LabelText}");
    }

    [HarmonyPatch(typeof(Panel_MainMenu), nameof(Panel_MainMenu.Initialize))]
    internal static class Init
    {
        static void Postfix(Panel_MainMenu __instance)
        {
            InitCalls++;
            try { Ensure(__instance); } catch (Exception e) { Plugin.Log.Error("Could not add the Mods menu item: " + e); }
        }
    }

    [HarmonyPatch(typeof(BasicMenu), nameof(BasicMenu.InternalClickAction))]
    internal static class Click
    {
        static void Prefix(int index, BasicMenu __instance)
        {
            var models = __instance.m_ItemModelList;
            if (models != null && index >= 0 && index < models.Count && models[index].m_Id == ItemId) ManagerUI.OpenFromMenu();
        }
    }
}
