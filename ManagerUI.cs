using System.Diagnostics;
using Il2Cpp;
using UnityEngine;

namespace ModManager;

// IMGUI with a TLD-like palette: near-black panels, cream text, cream "selected" buttons with dark text.
// Kept out of Plugin so Unity types are only touched once the game is running.
// GUI.TextField is unusable here (the game strips UnityEngine.TextEditor), so the search box reads keys itself.
static class ManagerUI
{
    // a class, not a record: records emit NullableAttribute, which Il2Cppmscorlib hides
    sealed class Row { public string Key; public ModEntry E; public InstalledMod I; public Row(string k, ModEntry e, InstalledMod i) { Key = k; E = e; I = i; } }

    static bool _open, _restartNeeded, _errLogged, _searchFocus;
    static int _tab;
    static string _search = "", _sel = "";
    static volatile bool _busy;
    static volatile string _status = "";
    static Vector2 _listScroll, _descScroll;

    static List<Row> _rows = new();
    static object _rowsInstalled, _rowsCatalog;
    static int _rowsTab = -1, _updates;
    static string _rowsSearch = "";

    static GUIStyle _thumbText, _dim, _bg, _panel, _row, _rowSel, _btn, _btnOn, _field, _title, _h2, _text, _muted, _wrap, _ok, _bad, _warn, _none;

    // ---------------- input ----------------

    public static void Update()
    {
        if (Plugin.Core == null) return;
        if (Input.GetKeyDown(KeyCode.F8) && Plugin.Asked.Value) Toggle();
        if (_open || !Plugin.Asked.Value) { Cursor.visible = true; Cursor.lockState = CursorLockMode.None; }
        MenuDiagnostic();
        if (_open) Img.Pump();
    }

    // Twice a second in the main menu: make sure the Mod Manager item is there and in the current language
    // (the menu may have been built before our patch, rebuilt since, or the language may have changed).
    static float _nextDiag; static int _diagCount;
    static void MenuDiagnostic()
    {
        if (Time.realtimeSinceStartup < _nextDiag || !InMainMenu) return;
        _nextDiag = Time.realtimeSinceStartup + 0.5f;
        try
        {
            var panel = InterfaceManager.GetPanel<Panel_MainMenu>();
            if (panel == null || panel.m_BasicMenu == null || panel.m_BasicMenu.m_ItemModelList == null || panel.m_BasicMenu.m_ItemModelList.Count == 0) return;
            int before = MenuPatch.Added;
            MenuPatch.Ensure(panel);
            if (_diagCount < 3 && (_diagCount == 0 || MenuPatch.Added != before)) { _diagCount++; MenuPatch.Dump(panel); }
        }
        catch (Exception e) { _nextDiag += 30f; Plugin.Log.Warning("[menu] ensure failed: " + e.Message); }
    }

    /// <summary>Called by the main-menu "Mods" item.</summary>
    public static void OpenFromMenu()
    {
        if (!Plugin.Asked.Value) return;
        _open = true;
        SetNgui(false);
    }

    static void Toggle()
    {
        _open = !_open;
        SetNgui(!_open);
    }

    static bool _nguiOn = true;
    static void SetNgui(bool on)
    {
        // NGUI would otherwise also receive the clicks that land on our window
        _nguiOn = on;
        try { foreach (var c in UnityEngine.Object.FindObjectsByType<UICamera>(FindObjectsSortMode.None)) c.enabled = on; }
        catch (Exception e) { Plugin.Log.Warning("UICamera: " + e.Message); }
    }

    static bool Btn(Rect r, string text, GUIStyle s) => GUI.Button(r, L.T(text), s);
    static void Lbl(Rect r, string text, GUIStyle s) => GUI.Label(r, L.T(text), s);

    // the scene's name is not "MainMenu" in every edition, so ask the game
    static bool InMainMenu { get { try { return GameManager.IsMainMenuActive(); } catch { return false; } } }

    // ---------------- drawing ----------------

    public static void Draw()
    {
        try { DrawAll(); }
        catch (Exception e)
        {
            if (_errLogged) return; // OnGUI runs several times a frame: log once, not thousands of times
            _errLogged = true;
            Plugin.Log.Error("UI error: " + e);
        }
    }

    static void DrawAll()
    {
        if (Plugin.Core == null) return;
        EnsureStyles();
        RefreshRows();

        if (!Plugin.Asked.Value) { DrawFirstRun(); return; }
        if (!_open) return;

        GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "", _dim);
        float w = Mathf.Min(1180, Screen.width - 40), h = Mathf.Min(740, Screen.height - 40);
        var r = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
        GUI.Box(r, "", _bg);

        float x = r.x + 22, y = r.y + 18;
        Lbl(new Rect(x, y, 300, 36), "MOD MANAGER", _title);
        bool auto = Plugin.AutoUpdate.Value;
        if (Btn(new Rect(r.xMax - 64 - 350, y, 340, 36), L.T("UPDATE ON LAUNCH:  ") + L.T(auto ? "YES" : "NO"), auto ? _btnOn : _btn))
            Plugin.SetAutoUpdate(!auto);
        if (Btn(new Rect(r.xMax - 64 - 350 - 290, y, 280, 36), "CHECK MANAGER UPDATE", _btn)) CheckSelfUpdate();
        if (Btn(new Rect(r.xMax - 64, y, 42, 36), "X", _btn)) Toggle();
        y += 52;

        var core = Plugin.Core;
        string[] tabs = { L.T("INSTALLED") + $" ({core.Installed.Count})", L.T("AVAILABLE") + $" ({core.Catalog.Count(e => !core.Installed.ContainsKey(e.Name))})", L.T("UPDATES") + $" ({_updates})" };
        for (int i = 0; i < tabs.Length; i++)
            if (Btn(new Rect(x + i * 202, y, 196, 34), tabs[i], _tab == i ? _btnOn : _btn)) { _tab = i; _listScroll = Vector2.zero; }
        if (_tab == 1 && Btn(new Rect(x + 3 * 202, y, 150, 34), Plugin.TileView.Value ? "VIEW: TILES" : "VIEW: LIST", _btn))
        {
            Plugin.TileView.Value = !Plugin.TileView.Value;
            MelonLoader.MelonPreferences.Save();
            _listScroll = Vector2.zero;
        }
        Lbl(new Rect(r.xMax - 392, y + 6, 70, 24), "SEARCH", _muted);
        SearchField(new Rect(r.xMax - 322, y, 300, 34));
        y += 48;

        const float bottomH = 58;
        float listW = Mathf.Floor(w * 0.56f), bodyH = r.yMax - y - bottomH - 14;
        DrawList(new Rect(x, y, listW, bodyH));
        DrawDetails(new Rect(x + listW + 14, y, r.xMax - 22 - (x + listW + 14), bodyH));
        DrawBottom(new Rect(x, r.yMax - bottomH - 4, w - 44, 44));
    }

    static void SearchField(Rect r)
    {
        var e = Event.current;
        if (e.type == EventType.MouseDown) _searchFocus = r.Contains(e.mousePosition);
        GUI.Box(r, "", _field);
        bool caret = _searchFocus && (int)(Time.realtimeSinceStartup * 2) % 2 == 0;
        Lbl(new Rect(r.x + 8, r.y, r.width - 16, r.height), _search + (caret ? "|" : ""), _text);
        if (!_searchFocus || e.type != EventType.KeyDown) return;
        if (e.keyCode == KeyCode.Backspace) { if (_search.Length > 0) _search = _search.Substring(0, _search.Length - 1); e.Use(); }
        else if (e.keyCode == KeyCode.Escape || e.keyCode == KeyCode.Return) { _searchFocus = false; e.Use(); }
        else if (e.character >= ' ' && e.character != (char)127) { _search += e.character; e.Use(); }
    }

    static void DrawFirstRun()
    {
        if (_nguiOn) SetNgui(false); // UICameras can appear after the first frame, so keep them off until answered
        GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "", _dim);
        float w = Mathf.Min(620, Screen.width - 40), h = 270;
        var r = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
        GUI.Box(r, "", _bg);
        Lbl(new Rect(r.x + 24, r.y + 20, w - 48, 34), "AUTO-UPDATE MODS", _title);
        Lbl(new Rect(r.x + 24, r.y + 66, w - 48, 120),
            "Check tldmods.net and update your installed mods every time the game starts?\n\n" +
            "Updating starts from the next launch. You can change this any time in the mod manager (Mod Manager in the main menu, or F8).", _wrap);
        float bw = (w - 48 - 16) / 2, by = r.yMax - 64;
        bool? answer = null;
        if (Btn(new Rect(r.x + 24, by, bw, 42), "YES, UPDATE", _btnOn)) answer = true;
        if (Btn(new Rect(r.x + 24 + bw + 16, by, bw, 42), "NO", _btn)) answer = false;
        if (answer is bool on)
        {
            Plugin.SetAutoUpdate(on);
            SetNgui(!_open);
        }
    }

    static void DrawList(Rect area)
    {
        const float rh = 50;
        GUI.Box(area, "", _panel);
        if (_rows.Count == 0)
        {
            Lbl(new Rect(area.x + 16, area.y + 16, area.width - 32, 30), Plugin.Core.Catalog.Count == 0 ? "Catalog not loaded - no network?" : "Nothing found", _muted);
            return;
        }
        // _rowsTab, not _tab: a tab button clicked earlier this frame changes _tab before the rows are rebuilt
        if (_rowsTab == 1 && Plugin.TileView.Value) { DrawTiles(area); return; }
        var view = new Rect(0, 0, area.width - 22, _rows.Count * rh);
        _listScroll = GUI.BeginScrollView(area, _listScroll, view);
        int first = Mathf.Max(0, (int)(_listScroll.y / rh)), last = Mathf.Min(_rows.Count, first + (int)(area.height / rh) + 2);
        for (int i = first; i < last; i++) DrawRow(_rows[i], new Rect(0, i * rh, view.width, rh - 3));
        GUI.EndScrollView();
    }

    // Tiles like tldmods.net: preview image on top, name, author and the install button below. Only the "Available" tab uses them.
    static void DrawTiles(Rect area)
    {
        const int cols = 3; const float gap = 8, footer = 74;
        float tw = (area.width - 22 - gap * (cols - 1) - 8) / cols, ih = Mathf.Floor(tw * 9f / 16f), th = ih + footer;
        int rowsN = (_rows.Count + cols - 1) / cols;
        var view = new Rect(0, 0, area.width - 22, 4 + rowsN * (th + gap));
        _listScroll = GUI.BeginScrollView(area, _listScroll, view);
        int first = Mathf.Max(0, (int)(_listScroll.y / (th + gap))), last = Mathf.Min(rowsN, first + (int)(area.height / (th + gap)) + 2);
        for (int ri = first; ri < last; ri++)
            for (int c = 0; c < cols && ri * cols + c < _rows.Count; c++)
                DrawTile(_rows[ri * cols + c], new Rect(4 + c * (tw + gap), 4 + ri * (th + gap), tw, th), ih);
        GUI.EndScrollView();
    }

    static void DrawTile(Row row, Rect r, float imgH)
    {
        var e = row.E;
        GUI.Box(r, "", row.Key == _sel ? _rowSel : _row);
        DrawBanner(e, new Rect(r.x, r.y, r.width, imgH));
        Lbl(new Rect(r.x + 8, r.y + imgH + 4, r.width - 16, 22), e.Title, _h2);
        Lbl(new Rect(r.x + 8, r.y + imgH + 24, r.width - 16, 18), e.Author, _muted);
        var btn = new Rect(r.x + 8, r.yMax - 32, r.width - 16, 26);
        if (e.CanInstall) { if (Btn(btn, "INSTALL", _btn)) InstallWithDeps(e); }
        else Lbl(btn, "MANUAL", _warn);
        if (GUI.Button(new Rect(r.x, r.y, r.width, r.height - 36), "", _none)) { _sel = row.Key; _descScroll = Vector2.zero; }
    }

    static bool _noDrawTexture;

    /// <summary>Preview image cropped to the rect, or the mod's name on a dark panel while loading / when it has none.</summary>
    static void DrawBanner(ModEntry e, Rect r)
    {
        var tex = Img.Get(e.Banner);
        if (tex != null && !_noDrawTexture)
        {
            try { GUI.DrawTexture(r, tex, ScaleMode.ScaleAndCrop); return; }
            catch (Exception ex) { _noDrawTexture = true; Plugin.Log.Warning("GUI.DrawTexture unavailable, using placeholders: " + ex.Message); }
        }
        GUI.Box(r, "", _panel);
        GUI.Label(new Rect(r.x + 8, r.y + 4, r.width - 16, r.height - 8), e.Title, _thumbText);
    }

    static void DrawRow(Row row, Rect r)
    {
        var core = Plugin.Core;
        GUI.Box(r, "", row.Key == _sel ? _rowSel : _row);
        var e = row.E;
        Lbl(new Rect(r.x + 12, r.y + 4, r.width - 280, 24), e?.Title ?? row.Key, _h2);
        Lbl(new Rect(r.x + 12, r.y + 26, r.width - 280, 20), e != null ? e.Author : row.I != null && row.I.IsSelf ? "this plugin" : "not in the tldmods.net catalog", _muted);

        string ver = row.I != null && e != null && core.HasUpdate(row.I) ? $"{row.I.Version} -> {e.Version}" : row.I != null && row.I.Version.Length > 0 ? row.I.Version : e != null ? e.Version : "";
        Lbl(new Rect(r.xMax - 268, r.y + 14, 120, 24), ver, e != null && e.Error ? _bad : _muted);

        // the action button is drawn before the row's click area so it gets the click first
        var btn = new Rect(r.xMax - 140, r.y + 8, 130, r.height - 16);
        if (e != null && row.I == null && e.CanInstall) { if (Btn(btn, "INSTALL", _btn)) InstallWithDeps(e); }
        else if (row.I != null && core.HasUpdate(row.I)) { if (Btn(btn, "UPDATE", _btnOn)) Run(L.T("Updating ") + e.Title, () => core.Install(e)); }
        else if (row.I != null) Lbl(btn, "INSTALLED", _ok);
        else if (e != null) Lbl(btn, "MANUAL", _warn);

        if (Btn(new Rect(r.x, r.y, r.width - 145, r.height), "", _none)) { _sel = row.Key; _descScroll = Vector2.zero; }
    }

    static void DrawDetails(Rect area)
    {
        GUI.Box(area, "", _panel);
        var core = Plugin.Core;
        var e = core.Find(_sel);
        InstalledMod inst;
        core.Installed.TryGetValue(e != null ? e.Name : _sel, out inst);
        if (e == null && inst == null)
        {
            Lbl(new Rect(area.x + 16, area.y + 16, area.width - 32, 60), "Select a mod from the list", _muted);
            return;
        }

        float x = area.x + 16, y = area.y + 14, w = area.width - 32;
        Lbl(new Rect(x, y, w, 32), e != null ? e.Title : _sel, _title); y += 38;
        if (e != null && e.Banner != null) { DrawBanner(e, new Rect(x, y, w, 130)); y += 138; }
        if (e != null)
        {
            Lbl(new Rect(x, y, w, 22), L.T("Author: ") + e.Author, _text); y += 24;
            if (e.Categories.Length > 0) { Lbl(new Rect(x, y, w, 22), string.Join(" / ", e.Categories), _muted); y += 24; }
        }
        string catalogVer = e != null && e.Version.Length > 0 ? e.Version : "-";
        string localVer = inst == null ? L.T("no") : inst.Version.Length > 0 ? inst.Version : "?";
        Lbl(new Rect(x, y, w, 22), L.T("Version: ") + catalogVer + "   |   " + L.T("installed: ") + localVer, _text); y += 26;
        if (e != null && e.Error) { Lbl(new Rect(x, y, w, 22), "! Marked as broken on tldmods.net", _bad); y += 24; }

        // dependencies
        if (e != null && e.Dependencies.Length > 0)
        {
            Lbl(new Rect(x, y, w, 22), "DEPENDENCIES", _h2); y += 26;
            foreach (var d in e.Dependencies)
            {
                bool have = core.IsInstalled(d), known = core.Find(d) != null;
                Lbl(new Rect(x + 8, y, w, 22), (have ? "[x]  " : "[ ]  ") + d + (known || have ? "" : "  " + L.T("(not in catalog)")), have ? _ok : _bad); y += 22;
            }
            var missing = core.MissingDependencies(e).Count();
            if (missing > 0 && Btn(new Rect(x, y + 4, w, 34), L.T("DOWNLOAD MISSING") + $" ({missing})", _btnOn))
                Run(L.T("Installing dependencies of ") + e.Title, () => core.InstallMissingDependencies(e.Name));
            y += missing > 0 ? 44 : 6;
        }

        // actions
        float bw = (w - 10) / 2;
        if (e != null && e.CanInstall)
        {
            var label = inst == null ? "INSTALL" : core.HasUpdate(inst) ? "UPDATE" : "REINSTALL";
            if (Btn(new Rect(x, y, bw, 36), label, inst == null || core.HasUpdate(inst) ? _btnOn : _btn)) InstallWithDeps(e);
        }
        else if (e != null && Btn(new Rect(x, y, bw, 36), "MOD PAGE", _btn)) Open(e.ModUrl);
        if (inst != null && !inst.IsSelf && Btn(new Rect(x + bw + 10, y, bw, 36), "REMOVE", _btn))
            Run(L.T("Removing ") + inst.Key, () => core.Uninstall(inst));
        else if (inst == null && e != null && e.CanInstall && e.ModUrl.Length > 0 && Btn(new Rect(x + bw + 10, y, bw, 36), "MOD PAGE", _btn)) Open(e.ModUrl);
        y += 46;

        // description
        var desc = e != null ? e.Description : inst.IsSelf ? "This is Mod Manager itself: it updates your other mods and installs new ones from tldmods.net." : "Files: " + string.Join(", ", inst.Files);
        var descArea = new Rect(x, y, w, area.yMax - y - 12);
        if (descArea.height < 24) return; // the banner and a long dependency list used the space
        float th = _wrap.CalcHeight(new GUIContent(desc), w - 22);
        _descScroll = GUI.BeginScrollView(descArea, _descScroll, new Rect(0, 0, w - 22, th));
        Lbl(new Rect(0, 0, w - 22, th), desc, _wrap);
        GUI.EndScrollView();
    }

    static void DrawBottom(Rect r)
    {
        var core = Plugin.Core;
        float x = r.x;
        if (Btn(new Rect(x, r.y, 190, r.height), L.T("UPDATE ALL") + $" ({_updates})", _updates > 0 ? _btnOn : _btn) && _updates > 0)
            Run(L.T("Updating all mods"), () => core.UpdateAll(onlyAutoUpdate: false));
        x += 198;
        if (Btn(new Rect(x, r.y, 210, r.height), "OPEN MODS FOLDER", _btn)) Open(core.ModsDir);
        x += 218;
        if (Btn(new Rect(x, r.y, 170, r.height), "REFRESH LIST", _btn)) Run(L.T("Loading catalog"), () => core.LoadCatalog(), changesFiles: false);
        x += 178;
        if (Btn(new Rect(r.xMax - 240, r.y, 240, r.height), "RESTART GAME", _restartNeeded ? _btnOn : _btn)) Restart();
        var status = _busy ? _status + "  " + L.T("please wait...") : _restartNeeded && _status.Length == 0 ? L.T("Changes apply after a restart") : _status;
        Lbl(new Rect(x + 6, r.y, r.xMax - 250 - x, r.height), status, _statusErr ? _bad : _muted);
    }

    // ---------------- data ----------------

    static void RefreshRows()
    {
        var core = Plugin.Core;
        if (_rowsTab == _tab && _rowsSearch == _search && ReferenceEquals(_rowsInstalled, core.Installed) && ReferenceEquals(_rowsCatalog, core.Catalog)) return;
        _rowsTab = _tab; _rowsSearch = _search; _rowsInstalled = core.Installed; _rowsCatalog = core.Catalog;

        var inst = core.Installed;
        _updates = inst.Values.Count(core.HasUpdate);
        IEnumerable<Row> rows = _tab switch
        {
            0 => inst.Values.Select(i => new Row(i.Entry != null ? i.Entry.Name : i.Key, i.Entry, i)).OrderBy(r => r.E != null ? r.E.Title : r.Key, StringComparer.OrdinalIgnoreCase),
            1 => core.Catalog.Where(e => !inst.ContainsKey(e.Name)).Select(e => new Row(e.Name, e, null)),
            _ => inst.Values.Where(core.HasUpdate).Select(i => new Row(i.Entry.Name, i.Entry, i)),
        };
        var q = _search.Trim();
        if (q.Length > 0)
            rows = rows.Where(r => (r.E != null ? r.E.Title : r.Key).Contains(q, StringComparison.OrdinalIgnoreCase) || r.Key.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                   (r.E != null && r.E.Author.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                                   (r.E != null && r.E.Categories.Any(c => c.Contains(q, StringComparison.OrdinalIgnoreCase))));
        _rows = rows.ToList();
    }

    // ---------------- actions ----------------

    static void InstallWithDeps(ModEntry e) => Run(L.T("Installing ") + e.Title, () => Plugin.Core.InstallWithDependencies(e.Name));

    static void CheckSelfUpdate() => Run(L.T("Checking for a Mod Manager update"), () => { }, changesFiles: false, custom: () =>
    {
        var core = Plugin.Core;
        var (tag, url) = core.LatestSelf();
        var cur = core.Installed.TryGetValue("ModManager", out var me) ? me.Version : "";
        if (Core.CompareVersions(tag, cur) <= 0) return L.T("Mod Manager is up to date: ") + cur;
        core.InstallSelf(url);
        _restartNeeded = true;
        return L.T("Mod Manager updated to ") + tag.TrimStart('v') + L.T(", restart the game to use it");
    });

    static bool _statusErr;

    /// <param name="custom">Runs instead of <paramref name="act"/> and returns the final status text.</param>
    static void Run(string what, Action act, bool changesFiles = true, Func<string> custom = null)
    {
        if (_busy) return;
        _busy = true; _status = what; _statusErr = false;
        Task.Run(() =>
        {
            try
            {
                if (custom != null) _status = custom();
                else
                {
                    act();
                    _status = what + ": " + L.T(changesFiles ? "done, restart the game" : "done");
                    if (changesFiles) _restartNeeded = true;
                }
            }
            catch (Exception ex) { _status = L.T("Error: ") + ex.Message; _statusErr = true; Plugin.Log.Error(ex.ToString()); }
            finally
            {
                try { Plugin.Core.Scan(); } catch (Exception ex) { Plugin.Log.Error(ex.ToString()); }
                _busy = false;
            }
        });
    }

    static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { _status = L.T("Error: ") + ex.Message; _statusErr = true; }
    }

    static void Restart()
    {
        // wait for this process to exit, then start the exe again (Steam's DRM re-routes it through Steam if needed)
        var exe = Process.GetCurrentProcess().MainModule.FileName.Replace("'", "''");
        var dir = Path.GetDirectoryName(exe);
        // a downloaded Mod Manager update waits in pending/ because the running game locks the plugin: swap it in once the game has exited
        var pend = Path.Combine(Plugin.Core.PendingDir, "Plugins", "ModManager.dll").Replace("'", "''");
        var self = Path.Combine(Plugin.Core.PluginsDir, "ModManager.dll").Replace("'", "''");
        Process.Start(new ProcessStartInfo("powershell.exe",
            $"-NoProfile -WindowStyle Hidden -Command \"Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue; if (Test-Path -LiteralPath '{pend}') {{ Copy-Item -LiteralPath '{pend}' -Destination '{self}' -Force; Remove-Item -LiteralPath '{pend}' -Force }}; Start-Process -FilePath '{exe}' -WorkingDirectory '{dir}'\"")
        { UseShellExecute = false, CreateNoWindow = true });
        Application.Quit();
    }

    // ---------------- styles ----------------

    static readonly Color Cream = new(0.87f, 0.85f, 0.78f), Muted = new(0.56f, 0.57f, 0.56f), Dark = new(0.07f, 0.08f, 0.09f);

    static void EnsureStyles()
    {
        if (_bg != null && _bg.normal.background != null) return;
        _dim = Box(new Color(0, 0, 0, 0.55f));
        _bg = Box(new Color(0.06f, 0.07f, 0.08f, 0.97f));
        _panel = Box(new Color(0.10f, 0.11f, 0.12f, 0.95f));
        _row = Box(new Color(0.14f, 0.15f, 0.16f, 0.9f));
        _rowSel = Box(new Color(0.24f, 0.28f, 0.31f, 0.95f));
        _btn = Button(new Color(0.17f, 0.19f, 0.21f), Cream, new Color(0.29f, 0.34f, 0.38f));
        _btnOn = Button(new Color(0.80f, 0.77f, 0.68f), Dark, new Color(0.93f, 0.91f, 0.84f));
        _field = Box(new Color(0.13f, 0.14f, 0.15f));
        _title = Label(Cream, 24, FontStyle.Bold);
        _h2 = Label(Cream, 17, FontStyle.Bold);
        _text = Label(Cream, 15);
        _muted = Label(Muted, 14);
        _ok = Label(new Color(0.58f, 0.78f, 0.52f), 15, FontStyle.Bold);
        _bad = Label(new Color(0.88f, 0.42f, 0.36f), 15, FontStyle.Bold);
        _warn = Label(new Color(0.93f, 0.72f, 0.38f), 14, FontStyle.Bold);
        _wrap = Label(Cream, 15); _wrap.wordWrap = true; _wrap.alignment = TextAnchor.UpperLeft;
        _thumbText = Label(Muted, 16, FontStyle.Bold); _thumbText.wordWrap = true; _thumbText.alignment = TextAnchor.MiddleCenter;
        _none = new GUIStyle();
        _ok.alignment = _warn.alignment = TextAnchor.MiddleCenter;
    }

    static Texture2D Tex(Color c)
    {
        var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
        t.SetPixel(0, 0, c); t.Apply();
        return t;
    }

    static void Set(GUIStyleState s, Texture2D bg, Color fg) { s.background = bg; s.textColor = fg; }

    static GUIStyle Box(Color c)
    {
        var s = new GUIStyle(GUI.skin.box);
        Set(s.normal, Tex(c), Cream);
        return s;
    }

    static GUIStyle Button(Color bg, Color fg, Color hover)
    {
        var s = new GUIStyle(GUI.skin.button) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        Set(s.normal, Tex(bg), fg);
        var h = Tex(hover);
        Set(s.hover, h, fg == Cream ? Color.white : Dark);
        Set(s.active, h, fg == Cream ? Color.white : Dark);
        Set(s.focused, Tex(bg), fg);
        return s;
    }

    static GUIStyle Label(Color c, int size, FontStyle st = FontStyle.Normal)
    {
        var s = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = st, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Clip };
        s.normal.textColor = c;
        return s;
    }
}
