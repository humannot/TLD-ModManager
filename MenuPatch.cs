using HarmonyLib;
using Il2Cpp;
using UnityEngine;

namespace ModManager;

// Adds a "Mod Manager" entry to the main menu, directly above "Quit". Same approach as SkyCoop's MULTIPLAYER button:
//  - a MainMenuItem with a type the game does not know goes into Panel_MainMenu.m_MenuItems (ConfigureMenu skips Debug and
//    unowned upsell items, so it is the unknown type that gets it through to AddMenuItem), so every rebuild of the menu keeps it;
//  - a model is also added to the menu's BasicMenu right away, because the menu is already built when we get here;
//  - clicks are recognised in BasicMenu.InternalClickAction by the item's id (the type's number);
//  - the game looks the label and description up as Localization.Get(key); we answer those two keys in the game's language.
static class MenuPatch
{
    const int ItemType = 77; // SkyCoop uses 31
    const string LabelKey = "ModManager_Mods";
    const string DescKey = "GAMEPLAY_Description77"; // "GAMEPLAY_Description" + the item type, which the menu asks for on hover
    internal static readonly string ItemId = ItemType.ToString();
    static string _applied = ""; // the label text currently shown, so a language change can find and replace it

    static Panel_MainMenu.MainMenuItem.MainMenuItemType Type => (Panel_MainMenu.MainMenuItem.MainMenuItemType)ItemType;
    static string Label => L.T("Mod Manager");
    static string Desc => L.T("In-game mod manager: download and update mods from https://tldmods.net/");

    static BasicMenu.BasicMenuItemModel Model(BasicMenu bm)
    {
        var models = bm.m_ItemModelList;
        for (int i = 0; i < models.Count; i++) if (models[i].m_Id == ItemId) return models[i];
        return null;
    }

    /// <summary>Idempotent: makes sure both the panel's item list and the live menu contain the item, in the current language.</summary>
    internal static void Ensure(Panel_MainMenu panel)
    {
        var items = panel.m_MenuItems;
        if (items != null)
        {
            bool have = false; int quit = items.Count;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].m_Type == Type) have = true;
                else if (items[i].m_Type == Panel_MainMenu.MainMenuItem.MainMenuItemType.Quit && quit == items.Count) quit = i;
            }
            if (!have) items.Insert(quit, new Panel_MainMenu.MainMenuItem { m_Type = Type, m_LabelLocalizationId = LabelKey });
        }

        var bm = panel.m_BasicMenu;
        if (bm == null || bm.m_ItemModelList == null || bm.m_ItemModelList.Count == 0) return;
        string text = Label, desc = Desc;
        var model = Model(bm);
        if (model != null) { Retranslate(bm, model, text, desc); return; }

        var models = bm.m_ItemModelList;
        int q = models.Count - 1;
        for (int i = 0; i < models.Count; i++) if (models[i].m_Id == "Quit") { q = i; break; }
        var like = models[0]; // borrow the Survival Mode item's colours
        var open = (Il2CppSystem.Action)new System.Action(ManagerUI.OpenFromMenu);
        bm.AddItem(ItemId, ItemType, q, text, desc, "", open, like.m_NormalTint, like.m_HighlightTint);
        _applied = text;
    }

    /// <summary>The language changed while the menu was open: update the model and the label the player already sees.</summary>
    static void Retranslate(BasicMenu bm, BasicMenu.BasicMenuItemModel model, string text, string desc)
    {
        model.m_DescriptionText = desc;
        if (model.m_LabelText == text && _applied == text) return;
        var old = model.m_LabelText;
        model.m_LabelText = text;
        var views = bm.m_MenuItems;
        for (int i = 0; views != null && i < views.Count; i++)
        {
            var label = views[i].m_Label;
            if (label != null && (label.text == old || label.text == _applied)) label.text = text;
        }
        _applied = text;
    }

    [HarmonyPatch(typeof(Panel_MainMenu), nameof(Panel_MainMenu.Initialize))]
    internal static class Init
    {
        static void Postfix(Panel_MainMenu __instance)
        {
            try { Ensure(__instance); } catch (Exception e) { Plugin.Log.Error("Could not add the Mod Manager menu item: " + e); }
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

    // when the game rebuilds the menu (e.g. after a language change) it asks for our texts by key
    [HarmonyPatch(typeof(Localization), nameof(Localization.Get), new[] { typeof(string) })]
    internal static class Text
    {
        static void Postfix(string key, ref string __result)
        {
            if (key == LabelKey) __result = Label;
            else if (key == DescKey) __result = Desc;
        }
    }
}
