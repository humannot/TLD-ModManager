using System.Diagnostics;
using Il2Cpp;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ModManager;

// IMGUI with a TLD-like palette: near-black panels, cream text, cream "selected" buttons with dark text.
// Kept out of Plugin so Unity types are only touched once the game is running.
static class ManagerUI
{
    // a class, not a record: records emit NullableAttribute, which Il2Cppmscorlib hides
    sealed class Row { public string Key; public ModEntry E; public InstalledMod I; public Row(string k, ModEntry e, InstalledMod i) { Key = k; E = e; I = i; } }

    static bool _open, _restartNeeded;
    static int _tab;
    static string _search = "", _sel = "";
    static volatile bool _busy;
    static volatile string _status = "";
    static Vector2 _listScroll, _descScroll;

    static List<Row> _rows = new();
    static object _rowsInstalled, _rowsCatalog;
    static int _rowsTab = -1, _updates;
    static string _rowsSearch = "";

    static GUIStyle _dim, _bg, _panel, _row, _rowSel, _btn, _btnOn, _field, _title, _h2, _text, _muted, _wrap, _ok, _bad, _warn, _none;

    // ---------------- input ----------------

    public static void Update()
    {
        if (Plugin.Core == null) return;
        if (Input.GetKeyDown(KeyCode.F8)) Toggle();
        if (_open) { Cursor.visible = true; Cursor.lockState = CursorLockMode.None; }
    }

    static void Toggle()
    {
        _open = !_open;
        // NGUI would otherwise also receive the clicks that land on our window
        try { foreach (var c in UnityEngine.Object.FindObjectsByType<UICamera>(FindObjectsSortMode.None)) c.enabled = !_open; }
        catch (Exception e) { Plugin.Log.Warning("UICamera: " + e.Message); }
    }

    // ---------------- drawing ----------------

    public static void Draw()
    {
        if (Plugin.Core == null) return;
        EnsureStyles();
        RefreshRows();

        if (!_open)
        {
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenu" &&
                GUI.Button(new Rect(24, Screen.height - 70, 230, 46), _updates > 0 ? $"МОДЫ  ·  {_updates} ОБН." : "МОДЫ  [F8]", _updates > 0 ? _btnOn : _btn))
                Toggle();
            return;
        }

        GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "", _dim);
        float w = Mathf.Min(1180, Screen.width - 40), h = Mathf.Min(740, Screen.height - 40);
        var r = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
        GUI.Box(r, "", _bg);

        float x = r.x + 22, y = r.y + 18;
        GUI.Label(new Rect(x, y, w - 200, 36), "МЕНЕДЖЕР МОДОВ", _title);
        GUI.Label(new Rect(x + 300, y + 8, 300, 24), "tldmods.net", _muted);
        if (GUI.Button(new Rect(r.xMax - 64, y, 42, 36), "✕", _btn)) Toggle();
        y += 52;

        var core = Plugin.Core;
        string[] tabs = { $"УСТАНОВЛЕННЫЕ ({core.Installed.Count})", $"ДОСТУПНЫЕ ({core.Catalog.Count(e => !core.Installed.ContainsKey(e.Name))})", $"ОБНОВЛЕНИЯ ({_updates})" };
        for (int i = 0; i < tabs.Length; i++)
            if (GUI.Button(new Rect(x + i * 232, y, 224, 34), tabs[i], _tab == i ? _btnOn : _btn)) { _tab = i; _listScroll = Vector2.zero; }
        GUI.Label(new Rect(r.xMax - 392, y + 6, 70, 24), "ПОИСК", _muted);
        _search = GUI.TextField(new Rect(r.xMax - 322, y, 300, 34), _search, _field);
        y += 48;

        const float bottomH = 58;
        float listW = Mathf.Floor(w * 0.56f), bodyH = r.yMax - y - bottomH - 14;
        DrawList(new Rect(x, y, listW, bodyH));
        DrawDetails(new Rect(x + listW + 14, y, r.xMax - 22 - (x + listW + 14), bodyH));
        DrawBottom(new Rect(x, r.yMax - bottomH - 4, w - 44, 44));
    }

    static void DrawList(Rect area)
    {
        const float rh = 50;
        GUI.Box(area, "", _panel);
        if (_rows.Count == 0)
        {
            GUI.Label(new Rect(area.x + 16, area.y + 16, area.width - 32, 30), Plugin.Core.Catalog.Count == 0 ? "Каталог не загружен — нет сети?" : "Ничего не найдено", _muted);
            return;
        }
        var view = new Rect(0, 0, area.width - 22, _rows.Count * rh);
        _listScroll = GUI.BeginScrollView(area, _listScroll, view);
        int first = Mathf.Max(0, (int)(_listScroll.y / rh)), last = Mathf.Min(_rows.Count, first + (int)(area.height / rh) + 2);
        for (int i = first; i < last; i++) DrawRow(_rows[i], new Rect(0, i * rh, view.width, rh - 3));
        GUI.EndScrollView();
    }

    static void DrawRow(Row row, Rect r)
    {
        var core = Plugin.Core;
        GUI.Box(r, "", row.Key == _sel ? _rowSel : _row);
        var e = row.E;
        GUI.Label(new Rect(r.x + 12, r.y + 4, r.width - 280, 24), e?.Title ?? row.Key, _h2);
        GUI.Label(new Rect(r.x + 12, r.y + 26, r.width - 280, 20), e == null ? "нет в каталоге tldmods.net" : e.Author, _muted);

        string ver = row.I != null && e != null && core.HasUpdate(row.I) ? $"{row.I.Version} → {e.Version}" : row.I?.Version is { Length: > 0 } v ? v : e?.Version ?? "";
        GUI.Label(new Rect(r.xMax - 268, r.y + 14, 120, 24), ver, e?.Error == true ? _bad : _muted);

        // the action button is drawn before the row's click area so it gets the click first
        var btn = new Rect(r.xMax - 140, r.y + 8, 130, r.height - 16);
        if (e != null && row.I == null && e.CanInstall) { if (GUI.Button(btn, "УСТАНОВИТЬ", _btn)) InstallWithDeps(e); }
        else if (row.I != null && core.HasUpdate(row.I)) { if (GUI.Button(btn, "ОБНОВИТЬ", _btnOn)) Run("Обновление " + e!.Title, () => core.Install(e)); }
        else if (row.I != null) GUI.Label(btn, "✓ УСТАНОВЛЕН", _ok);
        else if (e != null) GUI.Label(btn, "ВРУЧНУЮ", _warn);

        if (GUI.Button(new Rect(r.x, r.y, r.width - 145, r.height), "", _none)) { _sel = row.Key; _descScroll = Vector2.zero; }
    }

    static void DrawDetails(Rect area)
    {
        GUI.Box(area, "", _panel);
        var core = Plugin.Core;
        var e = core.Find(_sel);
        core.Installed.TryGetValue(e?.Name ?? _sel, out var inst);
        if (e == null && inst == null)
        {
            GUI.Label(new Rect(area.x + 16, area.y + 16, area.width - 32, 60), "Выберите мод в списке", _muted);
            return;
        }

        float x = area.x + 16, y = area.y + 14, w = area.width - 32;
        GUI.Label(new Rect(x, y, w, 32), e?.Title ?? _sel, _title); y += 38;
        if (e != null)
        {
            GUI.Label(new Rect(x, y, w, 22), $"Автор: {e.Author}", _text); y += 24;
            if (e.Categories.Length > 0) { GUI.Label(new Rect(x, y, w, 22), string.Join(" · ", e.Categories), _muted); y += 24; }
        }
        GUI.Label(new Rect(x, y, w, 22), $"Версия: {(e?.Version is { Length: > 0 } cv ? cv : "—")}   ·   установлена: {(inst == null ? "нет" : inst.Version is { Length: > 0 } iv ? iv : "?")}", _text); y += 26;
        if (e?.Error == true) { GUI.Label(new Rect(x, y, w, 22), "⚠ Помечен на tldmods.net как неработающий", _bad); y += 24; }

        // dependencies
        if (e != null && e.Dependencies.Length > 0)
        {
            GUI.Label(new Rect(x, y, w, 22), "ЗАВИСИМОСТИ", _h2); y += 26;
            foreach (var d in e.Dependencies)
            {
                bool have = core.IsInstalled(d), known = core.Find(d) != null;
                GUI.Label(new Rect(x + 8, y, w, 22), (have ? "✓  " : "✗  ") + d + (known || have ? "" : "  (нет в каталоге)"), have ? _ok : _bad); y += 22;
            }
            var missing = core.MissingDependencies(e).Count();
            if (missing > 0 && GUI.Button(new Rect(x, y + 4, w, 34), $"ДОКАЧАТЬ НЕДОСТАЮЩИЕ ({missing})", _btnOn))
                Run("Установка зависимостей " + e.Title, () => core.InstallMissingDependencies(e.Name));
            y += missing > 0 ? 44 : 6;
        }

        // actions
        float bw = (w - 10) / 2;
        if (e != null && e.CanInstall)
        {
            var label = inst == null ? "УСТАНОВИТЬ" : core.HasUpdate(inst) ? "ОБНОВИТЬ" : "ПЕРЕУСТАНОВИТЬ";
            if (GUI.Button(new Rect(x, y, bw, 36), label, inst == null || core.HasUpdate(inst) ? _btnOn : _btn)) InstallWithDeps(e);
        }
        else if (e != null && GUI.Button(new Rect(x, y, bw, 36), "СТРАНИЦА МОДА", _btn)) Open(e.ModUrl);
        if (inst != null && GUI.Button(new Rect(x + bw + 10, y, bw, 36), "УДАЛИТЬ", _btn))
            Run("Удаление " + inst.Key, () => core.Uninstall(inst));
        else if (inst == null && e != null && e.CanInstall && e.ModUrl.Length > 0 && GUI.Button(new Rect(x + bw + 10, y, bw, 36), "СТРАНИЦА МОДА", _btn)) Open(e.ModUrl);
        y += 46;

        // description
        var desc = e?.Description ?? "Файлы: " + string.Join(", ", inst!.Files);
        var descArea = new Rect(x, y, w, area.yMax - y - 12);
        float th = _wrap!.CalcHeight(new GUIContent(desc), w - 22);
        _descScroll = GUI.BeginScrollView(descArea, _descScroll, new Rect(0, 0, w - 22, th));
        GUI.Label(new Rect(0, 0, w - 22, th), desc, _wrap);
        GUI.EndScrollView();
    }

    static void DrawBottom(Rect r)
    {
        var core = Plugin.Core;
        float x = r.x;
        if (GUI.Button(new Rect(x, r.y, 210, r.height), $"ОБНОВИТЬ ВСЁ ({_updates})", _updates > 0 ? _btnOn : _btn) && _updates > 0)
            Run("Обновление всех модов", () => core.UpdateAll(onlyAutoUpdate: false));
        x += 218;
        if (GUI.Button(new Rect(x, r.y, 210, r.height), "ОТКРЫТЬ ПАПКУ МОДОВ", _btn)) Open(core.ModsDir);
        x += 218;
        if (GUI.Button(new Rect(x, r.y, 160, r.height), "ОБНОВИТЬ СПИСОК", _btn)) Run("Загрузка каталога", () => core.LoadCatalog(), changesFiles: false);
        x += 168;
        if (GUI.Button(new Rect(r.xMax - 240, r.y, 240, r.height), "ПЕРЕЗАПУСТИТЬ ИГРУ", _restartNeeded ? _btnOn : _btn)) Restart();
        var status = _busy ? _status + "  подождите…" : _restartNeeded && _status.Length == 0 ? "Изменения применятся после перезапуска" : _status;
        GUI.Label(new Rect(x + 6, r.y, r.xMax - 250 - x, r.height), status, _status.StartsWith("Ошибка") ? _bad : _muted);
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
            0 => inst.Values.Select(i => new Row(i.Entry?.Name ?? i.Key, i.Entry, i)).OrderBy(r => r.E?.Title ?? r.Key, StringComparer.OrdinalIgnoreCase),
            1 => core.Catalog.Where(e => !inst.ContainsKey(e.Name)).Select(e => new Row(e.Name, e, null)),
            _ => inst.Values.Where(core.HasUpdate).Select(i => new Row(i.Entry!.Name, i.Entry, i)),
        };
        var q = _search.Trim();
        if (q.Length > 0)
            rows = rows.Where(r => (r.E?.Title ?? r.Key).Contains(q, StringComparison.OrdinalIgnoreCase) || r.Key.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                   (r.E?.Author.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                                   (r.E?.Categories.Any(c => c.Contains(q, StringComparison.OrdinalIgnoreCase)) ?? false));
        _rows = rows.ToList();
    }

    // ---------------- actions ----------------

    static void InstallWithDeps(ModEntry e) => Run("Установка " + e.Title, () => Plugin.Core.InstallWithDependencies(e.Name));

    static void Run(string what, Action act, bool changesFiles = true)
    {
        if (_busy) return;
        _busy = true; _status = what;
        Task.Run(() =>
        {
            try
            {
                act();
                _status = changesFiles ? what + ": готово, перезапустите игру" : what + ": готово";
                if (changesFiles) _restartNeeded = true;
            }
            catch (Exception ex) { _status = "Ошибка: " + ex.Message; Plugin.Log.Error(ex.ToString()); }
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
        catch (Exception ex) { _status = "Ошибка: " + ex.Message; }
    }

    static void Restart()
    {
        // wait for this process to exit, then start the exe again (Steam's DRM re-routes it through Steam if needed)
        var exe = Process.GetCurrentProcess().MainModule!.FileName!.Replace("'", "''");
        var dir = Path.GetDirectoryName(exe)!;
        Process.Start(new ProcessStartInfo("powershell.exe",
            $"-NoProfile -WindowStyle Hidden -Command \"Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue; Start-Process -FilePath '{exe}' -WorkingDirectory '{dir}'\"")
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
        _field = new GUIStyle(GUI.skin.textField) { fontSize = 16, alignment = TextAnchor.MiddleLeft };
        Set(_field.normal, Tex(new Color(0.13f, 0.14f, 0.15f)), Cream);
        Set(_field.focused, Tex(new Color(0.18f, 0.2f, 0.22f)), Color.white);
        Set(_field.hover, Tex(new Color(0.16f, 0.17f, 0.18f)), Cream);
        _title = Label(Cream, 24, FontStyle.Bold);
        _h2 = Label(Cream, 17, FontStyle.Bold);
        _text = Label(Cream, 15);
        _muted = Label(Muted, 14);
        _ok = Label(new Color(0.58f, 0.78f, 0.52f), 15, FontStyle.Bold);
        _bad = Label(new Color(0.88f, 0.42f, 0.36f), 15, FontStyle.Bold);
        _warn = Label(new Color(0.93f, 0.72f, 0.38f), 14, FontStyle.Bold);
        _wrap = Label(Cream, 15); _wrap.wordWrap = true; _wrap.alignment = TextAnchor.UpperLeft;
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
