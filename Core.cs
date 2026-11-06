using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mono.Cecil;

namespace ModManager;

public sealed class ModEntry
{
    public string Name = "", Title = "", Type = "mod", Author = "", Description = "", Version = "", ModUrl = "";
    public string[] Aliases = Array.Empty<string>(), Dependencies = Array.Empty<string>(),
        Downloads = Array.Empty<string>(), Categories = Array.Empty<string>();
    public bool Error, AutoUpdate;

    /// <summary>Downloads we can place ourselves (.dll / .modcomponent / .zip), one per file name.</summary>
    public IEnumerable<string> Files => Downloads.Where(u => Installable(FileName(u))).DistinctBy(FileName, StringComparer.OrdinalIgnoreCase);
    public bool CanInstall => Files.Any();

    internal static string FileName(string url) => Path.GetFileName(new Uri(url).AbsolutePath);
    static bool Installable(string f) => f.EndsWith(".dll", true, null) || f.EndsWith(".modcomponent", true, null) || f.EndsWith(".zip", true, null);
}

public sealed class InstalledMod
{
    public string Key = "", Version = "";
    public ModEntry Entry;
    public readonly List<string> Files = new(); // paths relative to game root
}

/// <summary>Everything that touches the network or disk. No Unity / MelonLoader types, so it runs before mods load and in tests.</summary>
public sealed class Core
{
    public const string ApiUrl = "https://tldmods.net/api.json?details";
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60), DefaultRequestHeaders = { { "User-Agent", "TLD-ModManager" } } };

    public readonly string Root, ModsDir, PluginsDir, DataDir, PendingDir;
    readonly string _manifestPath, _cachePath;
    readonly Action<string> _log;

    public List<ModEntry> Catalog { get; private set; } = new();
    Dictionary<string, ModEntry> _index = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, InstalledMod> Installed { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> _manifest; // catalog name -> version we installed
    readonly object _lock = new();

    public Core(string root, Action<string> log)
    {
        Root = root; _log = log;
        ModsDir = Path.Combine(root, "Mods");
        PluginsDir = Path.Combine(root, "Plugins");
        DataDir = Path.Combine(root, "UserData", "ModManager");
        PendingDir = Path.Combine(DataDir, "pending");
        _manifestPath = Path.Combine(DataDir, "installed.json");
        _cachePath = Path.Combine(DataDir, "api.json");
        Directory.CreateDirectory(DataDir);
        try { _manifest = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_manifestPath)) ?? new(); }
        catch { _manifest = new(); }
        _manifest = new(_manifest, StringComparer.OrdinalIgnoreCase);
    }

    public ModEntry Find(string name) => _index.GetValueOrDefault(name);

    // ---------------- catalog ----------------

    public bool LoadCatalog()
    {
        string json; bool online = true;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            json = Http.GetStringAsync(ApiUrl, cts.Token).GetAwaiter().GetResult();
            File.WriteAllText(_cachePath, json);
        }
        catch (Exception e)
        {
            _log("tldmods.net недоступен (" + e.Message + "), использую кэш");
            if (!File.Exists(_cachePath)) return false;
            json = File.ReadAllText(_cachePath); online = false;
        }
        var list = ParseCatalog(json);
        var index = new Dictionary<string, ModEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in list)
            foreach (var key in new[] { m.Name, m.Title }.Concat(m.Aliases).Concat(m.Files.Select(f => Path.GetFileNameWithoutExtension(ModEntry.FileName(f)))))
                index.TryAdd(key, m);
        foreach (var m in list) index[m.Name] = m; // exact names win over aliases / file stems
        Catalog = list; _index = index;
        return online;
    }

    public static List<ModEntry> ParseCatalog(string json)
    {
        var list = new List<ModEntry>();
        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList()
            : doc.RootElement.EnumerateObject().Select(p => p.Value).ToList();
        foreach (var e in items)
        {
            try
            {
                var m = new ModEntry
                {
                    Name = Str(e, "Name"), Type = Str(e, "Type"), Author = Str(e, "Author"), Version = Str(e, "Version"),
                    ModUrl = Str(e, "ModUrl"), Aliases = Arr(e, "Aliases"), Dependencies = Arr(e, "Dependencies"),
                    Categories = Arr(e, "Categories"), Downloads = Arr(e, "Downloads"),
                    Error = Bool(e, "Error"), AutoUpdate = Bool(e, "AutoUpdate"),
                    Description = Regex.Replace(Regex.Replace(Str(e, "Description"), "<br\\s*/?>", "\n", RegexOptions.IgnoreCase), "<[^>]+>", ""),
                };
                m.Title = Str(e, "DisplayName") is { Length: > 0 } t ? t : m.Name;
                if (m.Downloads.Length == 0 && Str(e, "Download") is { Length: > 0 } d) m.Downloads = new[] { d };
                m.Downloads = m.Downloads.Where(u => Uri.TryCreate(u, UriKind.Absolute, out _)).ToArray();
                if (m.Name.Length > 0) list.Add(m);
            }
            catch { /* one broken record must not hide the other 300 */ }
        }
        return list.OrderBy(m => m.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    static string Str(JsonElement e, string p) => e.TryGetProperty(p, out var v) ? v.ValueKind switch
    {
        JsonValueKind.String => v.GetString() ?? "",
        JsonValueKind.Array => string.Join(", ", v.EnumerateArray().Select(x => x.ToString())),
        JsonValueKind.Null => "",
        _ => v.ToString(),
    } : "";
    static string[] Arr(JsonElement e, string p) => e.TryGetProperty(p, out var v) ? v.ValueKind switch
    {
        JsonValueKind.Array => v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray(),
        JsonValueKind.String => new[] { v.GetString()! },
        _ => Array.Empty<string>(),
    } : Array.Empty<string>();
    static bool Bool(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.True;

    // ---------------- installed mods ----------------

    public void Scan()
    {
        var found = new Dictionary<string, InstalledMod>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in new[] { ModsDir, PluginsDir })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var path in Directory.EnumerateFiles(dir))
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext != ".dll" && ext != ".modcomponent") continue;
                string stem = Path.GetFileNameWithoutExtension(path), version = "";
                ModEntry entry = null;
                if (ext == ".dll")
                {
                    var (melonName, melonVersion) = ReadMelonInfo(path);
                    if (melonName == "ModManager") continue; // ourselves
                    version = melonVersion;
                    if (melonName.Length > 0) entry = Find(melonName);
                }
                entry ??= Find(stem);
                var key = entry?.Name ?? stem;
                if (!found.TryGetValue(key, out var mod)) found[key] = mod = new InstalledMod { Key = key, Entry = entry };
                mod.Files.Add(Path.GetRelativePath(Root, path));
                if (mod.Version.Length == 0) mod.Version = version;
            }
        }
        lock (_lock)
            foreach (var mod in found.Values)
                if (_manifest.TryGetValue(mod.Key, out var v)) mod.Version = v; // MelonInfo versions often lag the catalog
        Installed = found;
    }

    static (string name, string version) ReadMelonInfo(string path)
    {
        try
        {
            using var asm = AssemblyDefinition.ReadAssembly(path);
            var a = asm.CustomAttributes.FirstOrDefault(c => c.AttributeType.Name == "MelonInfoAttribute");
            if (a == null || a.ConstructorArguments.Count < 3) return ("", "");
            return (a.ConstructorArguments[1].Value as string ?? "", a.ConstructorArguments[2].Value as string ?? "");
        }
        catch { return ("", ""); }
    }

    public bool IsInstalled(string name) => Find(name) is { } e ? Installed.ContainsKey(e.Name) : Installed.ContainsKey(name);

    public bool HasUpdate(InstalledMod m) => m.Entry is { } e && e.CanInstall && !e.Error && CompareVersions(e.Version, m.Version) > 0;

    /// <summary>Compares the numeric parts: "v1.6.1" &gt; "1.6", "2.0" == "2.0.0". Unknown local version counts as older.</summary>
    public static int CompareVersions(string a, string b)
    {
        var x = Regex.Matches(a, "\\d+").Select(m => long.Parse(m.Value)).ToList();
        var y = Regex.Matches(b, "\\d+").Select(m => long.Parse(m.Value)).ToList();
        if (x.Count == 0) return y.Count == 0 ? 0 : -1;
        if (y.Count == 0) return 1;
        for (int i = 0; i < Math.Max(x.Count, y.Count); i++)
        {
            int c = (i < x.Count ? x[i] : 0).CompareTo(i < y.Count ? y[i] : 0);
            if (c != 0) return c;
        }
        return 0;
    }

    // ---------------- dependencies ----------------

    /// <summary>Mods to install for <paramref name="name"/>, dependencies first. Already-installed ones are skipped; unknown names go to <paramref name="unknown"/>.</summary>
    public List<ModEntry> Resolve(string name, List<string> unknown, bool includeSelf = true)
    {
        var order = new List<ModEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(string n, bool self)
        {
            var e = Find(n);
            if (e == null) { if (!unknown.Contains(n)) unknown.Add(n); return; }
            if (!seen.Add(e.Name)) return; // also breaks cycles
            foreach (var d in e.Dependencies) Visit(d, false);
            if ((self ? includeSelf : !IsInstalled(e.Name))) order.Add(e);
        }
        Visit(name, true);
        return order;
    }

    public IEnumerable<string> MissingDependencies(ModEntry e) => e.Dependencies.Where(d => !IsInstalled(d));

    // ---------------- install / remove ----------------

    public void InstallWithDependencies(string name)
    {
        var unknown = new List<string>();
        foreach (var e in Resolve(name, unknown)) Install(e);
        if (unknown.Count > 0) _log("Нет в каталоге tldmods.net: " + string.Join(", ", unknown));
    }

    public void InstallMissingDependencies(string name)
    {
        var unknown = new List<string>();
        foreach (var e in Resolve(name, unknown, includeSelf: false)) Install(e);
        if (unknown.Count > 0) _log("Нет в каталоге tldmods.net: " + string.Join(", ", unknown));
    }

    public void Install(ModEntry e)
    {
        if (!e.CanInstall) throw new InvalidOperationException(e.Title + ": нет файла для автоустановки, откройте страницу мода");
        _log($"Установка {e.Title} {e.Version}");
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targetDir = e.Type.Equals("plugin", StringComparison.OrdinalIgnoreCase) ? "Plugins" : "Mods";
        foreach (var url in e.Files)
        {
            var file = ModEntry.FileName(url);
            var bytes = Http.GetByteArrayAsync(url).GetAwaiter().GetResult();
            if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var zip = new ZipArchive(new MemoryStream(bytes));
                foreach (var entry in zip.Entries.Where(z => z.Name.Length > 0))
                {
                    using var ms = new MemoryStream();
                    entry.Open().CopyTo(ms);
                    var rel = ZipTarget(entry.FullName, targetDir);
                    Place(rel, ms.ToArray());
                    written.Add(rel);
                }
            }
            else
            {
                var rel = Path.Combine(targetDir, file);
                Place(rel, bytes);
                written.Add(rel);
            }
        }
        // a renamed release file must not leave the old copy loading next to the new one
        if (Installed.TryGetValue(e.Name, out var old))
            foreach (var f in old.Files.Where(f => !written.Contains(f))) Delete(f);
        lock (_lock) { _manifest[e.Name] = e.Version; SaveManifest(); }
        Scan(); // so the next Resolve sees this mod as installed
    }

    public void Uninstall(InstalledMod m)
    {
        _log("Удаление " + m.Key);
        foreach (var f in m.Files) Delete(f);
        lock (_lock) { _manifest.Remove(m.Key); SaveManifest(); }
    }

    /// <summary>Zip entries already rooted at Mods/Plugins/UserLibs/UserData keep their path; anything else goes into the mod folder.</summary>
    public static string ZipTarget(string entryPath, string defaultDir)
    {
        var p = entryPath.Replace('\\', '/').TrimStart('/');
        var first = p.Split('/')[0];
        var rooted = new[] { "Mods", "Plugins", "UserLibs", "UserData" }.Contains(first, StringComparer.OrdinalIgnoreCase);
        var rel = Path.Combine((rooted ? p : defaultDir + "/" + p).Split('/'));
        if (rel.Split(Path.DirectorySeparatorChar).Contains("..")) throw new InvalidDataException("Недопустимый путь в архиве: " + entryPath);
        return rel;
    }

    public int UpdateAll(bool onlyAutoUpdate)
    {
        int n = 0;
        foreach (var m in Installed.Values.Where(HasUpdate).ToList())
        {
            // unknown local version (e.g. a hand-copied .modcomponent): offer it in the UI, never replace silently
            if (onlyAutoUpdate && (!m.Entry!.AutoUpdate || m.Version.Length == 0)) continue;
            try { _log($"Обновление {m.Entry!.Title}: {m.Version} -> {m.Entry.Version}"); Install(m.Entry); n++; }
            catch (Exception ex) { _log($"Не удалось обновить {m.Key}: {ex.Message}"); }
        }
        if (n > 0) Scan();
        return n;
    }

    // ---------------- file ops (locked files are deferred to the next start) ----------------

    public bool HasPending => Directory.Exists(PendingDir) && Directory.EnumerateFiles(PendingDir, "*", SearchOption.AllDirectories).Any();

    void Place(string rel, byte[] bytes)
    {
        var target = Path.Combine(Root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        try { File.WriteAllBytes(target, bytes); }
        catch (IOException)
        {
            var p = Path.Combine(PendingDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllBytes(p, bytes);
            File.Delete(p + ".delete");
        }
    }

    void Delete(string rel)
    {
        var target = Path.Combine(Root, rel);
        try { File.Delete(target); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var p = Path.Combine(PendingDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllBytes(p + ".delete", Array.Empty<byte>());
            File.Delete(p);
        }
    }

    /// <summary>Runs before any mod is loaded, when no mod file is locked.</summary>
    public int ApplyPending()
    {
        if (!Directory.Exists(PendingDir)) return 0;
        int n = 0;
        foreach (var p in Directory.EnumerateFiles(PendingDir, "*", SearchOption.AllDirectories).ToList())
        {
            try
            {
                var rel = Path.GetRelativePath(PendingDir, p);
                if (rel.EndsWith(".delete")) File.Delete(Path.Combine(Root, rel[..^".delete".Length]));
                else
                {
                    var target = Path.Combine(Root, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(p, target, true);
                }
                File.Delete(p); n++;
            }
            catch (Exception ex) { _log("Не удалось применить " + p + ": " + ex.Message); }
        }
        return n;
    }

    void SaveManifest() => File.WriteAllText(_manifestPath, JsonSerializer.Serialize(_manifest, new JsonSerializerOptions { WriteIndented = true }));
}
