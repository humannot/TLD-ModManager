using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mono.Cecil;

namespace ModManager;

public sealed class ModEntry
{
    public string Name = "", Title = "", Type = "mod", Author = "", Description = "", Version = "", ModUrl = "";
    /// <summary>Date of the last update on tldmods.net (ISO yyyy-MM-dd, so it sorts as text) and the game version the mod was tested on.</summary>
    public string Updated = "", TestedTld = "";
    public string[] Aliases = Array.Empty<string>(), Dependencies = Array.Empty<string>(),
        Downloads = Array.Empty<string>(), Categories = Array.Empty<string>(), Images = Array.Empty<string>();

    /// <summary>First preview image Unity can decode (PNG/JPEG; the site also lists WebP/GIF, which we skip), or null.</summary>
    public string Banner => Images.FirstOrDefault(u => Core.Decodable(u));
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
    public bool IsSelf; // this plugin: always listed as installed, never removed or replaced from the list
    public ModEntry Entry;
    public readonly List<string> Files = new(); // paths relative to game root
    public int EnabledFiles, DisabledFiles;
    /// <summary>A disabled mod keeps its files under a .disabled name, which MelonLoader does not load.</summary>
    public bool Disabled => DisabledFiles > 0 && EnabledFiles == 0;
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
            _log("tldmods.net is unreachable (" + e.Message + "), using the cached list");
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
                    ModUrl = Str(e, "ModUrl"), Updated = Str(e, "Updated"),
                    TestedTld = e.TryGetProperty("TestedOn", out var tested) && tested.ValueKind == JsonValueKind.Object ? Str(tested, "tld") : "",
                    Aliases = Arr(e, "Aliases"), Dependencies = Arr(e, "Dependencies"),
                    Categories = Arr(e, "Categories"), Downloads = Arr(e, "Downloads"), Images = Arr(e, "Images"),
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
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                bool off = file.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase);
                var path = off ? file[..^DisabledSuffix.Length] : file; // the name the file has while enabled
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext != ".dll" && ext != ".modcomponent") continue;
                string stem = Path.GetFileNameWithoutExtension(path), version = "";
                ModEntry entry = null;
                if (ext == ".dll")
                {
                    var (melonName, melonVersion) = ReadMelonInfo(file);
                    version = melonVersion;
                    if (melonName == "ModManager")
                    {
                        found["ModManager"] = new InstalledMod { Key = "ModManager", Version = version, IsSelf = true, Files = { Path.GetRelativePath(Root, file) } };
                        continue;
                    }
                    if (melonName.Length > 0) entry = Find(melonName);
                }
                entry ??= Find(stem);
                var key = entry?.Name ?? stem;
                if (!found.TryGetValue(key, out var mod)) found[key] = mod = new InstalledMod { Key = key, Entry = entry };
                mod.Files.Add(Path.GetRelativePath(Root, file));
                if (off) mod.DisabledFiles++; else mod.EnabledFiles++;
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
        if (unknown.Count > 0) _log("Not in the tldmods.net catalog: " + string.Join(", ", unknown));
    }

    public void InstallMissingDependencies(string name)
    {
        var unknown = new List<string>();
        foreach (var e in Resolve(name, unknown, includeSelf: false)) Install(e);
        if (unknown.Count > 0) _log("Not in the tldmods.net catalog: " + string.Join(", ", unknown));
    }

    public void Install(ModEntry e)
    {
        if (!e.CanInstall) throw new InvalidOperationException(e.Title + ": no file we can install automatically, open the mod page instead");
        _log($"Installing {e.Title} {e.Version}");
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targetDir = e.Type.Equals("plugin", StringComparison.OrdinalIgnoreCase) ? "Plugins" : "Mods";
        Installed.TryGetValue(e.Name, out var old);
        // updating a disabled mod must not switch it back on
        string Name(string rel) => old is { Disabled: true } && (rel.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || rel.EndsWith(".modcomponent", StringComparison.OrdinalIgnoreCase)) ? rel + DisabledSuffix : rel;
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
                    var rel = Name(ZipTarget(entry.FullName, targetDir));
                    Place(rel, ms.ToArray());
                    written.Add(rel);
                }
            }
            else
            {
                var rel = Name(Path.Combine(targetDir, file));
                Place(rel, bytes);
                written.Add(rel);
            }
        }
        // a renamed release file must not leave the old copy loading next to the new one
        if (old != null)
            foreach (var f in old.Files.Where(f => !written.Contains(f))) Delete(f);
        lock (_lock) { _manifest[e.Name] = e.Version; SaveManifest(); }
        Scan(); // so the next Resolve sees this mod as installed
    }

    // ---------------- preview images ----------------

    public static bool Decodable(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp) return false;
        var ext = Path.GetExtension(u.AbsolutePath).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".php" or ""; // .php / extensionless are checked by magic bytes after download
    }

    /// <summary>Image bytes from the disk cache or the network; null when it is not a PNG/JPEG or is too big. Called off the main thread.</summary>
    public byte[] FetchImage(string url)
    {
        var dir = Path.Combine(DataDir, "img");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(url))) + ".img");
        byte[] bytes;
        if (File.Exists(file)) bytes = File.ReadAllBytes(file);
        else
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            bytes = Http.GetByteArrayAsync(url, cts.Token).GetAwaiter().GetResult();
            if (bytes.Length > 6_000_000) bytes = Array.Empty<byte>(); // huge screenshots would cost more memory than they are worth
            File.WriteAllBytes(file, bytes);
        }
        bool png = bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50, jpg = bytes.Length > 8 && bytes[0] == 0xFF && bytes[1] == 0xD8;
        return png || jpg ? bytes : null;
    }

    // ---------------- description translation (MyMemory: free, no key) ----------------

    const string TranslateUrl = "https://api.mymemory.translated.net/get";
    Dictionary<string, string> _translations;

    /// <summary>
    /// Translates English text to <paramref name="toLang"/> (ISO code) line by line, in pieces of at most 450 bytes (the API's limit is 500).
    /// Results are cached in UserData/ModManager/translations.json because the anonymous quota is 5,000 characters a day. Blocking; call off the main thread.
    /// </summary>
    public string Translate(string text, string toLang)
    {
        lock (_lock)
        {
            if (_translations == null)
            {
                try { _translations = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(DataDir, "translations.json"))) ?? new(); }
                catch { _translations = new(); }
            }
            if (_translations.TryGetValue(toLang + "|" + text, out var cached)) return cached;
        }
        var lines = text.Split('\n').Select(line => line.Trim().Length == 0 ? "" : string.Join(" ", Pieces(line.Trim(), 450).Select(p => TranslatePiece(p, toLang))));
        var result = string.Join("\n", lines);
        lock (_lock)
        {
            _translations[toLang + "|" + text] = result;
            File.WriteAllText(Path.Combine(DataDir, "translations.json"), JsonSerializer.Serialize(_translations));
        }
        return result;
    }

    /// <summary>Splits one line at sentence ends into pieces of at most <paramref name="maxBytes"/> UTF-8 bytes (a longer sentence is cut mid-way).</summary>
    public static IEnumerable<string> Pieces(string line, int maxBytes)
    {
        var enc = System.Text.Encoding.UTF8;
        var cur = new System.Text.StringBuilder();
        foreach (var sentence in Regex.Split(line, @"(?<=[.!?])\s+"))
        {
            var s = sentence;
            while (enc.GetByteCount(s) > maxBytes) // at most 3 bytes per UTF-16 unit outside surrogate pairs; 4 bytes per pair
            {
                if (cur.Length > 0) { yield return cur.ToString(); cur.Clear(); }
                int n = Math.Min(s.Length, maxBytes / 3);
                if (n < s.Length && char.IsHighSurrogate(s[n - 1])) n--;
                yield return s.Substring(0, n);
                s = s.Substring(n);
            }
            if (s.Length == 0) continue;
            if (cur.Length > 0 && enc.GetByteCount(cur.ToString()) + 1 + enc.GetByteCount(s) > maxBytes) { yield return cur.ToString(); cur.Clear(); }
            if (cur.Length > 0) cur.Append(' ');
            cur.Append(s);
        }
        if (cur.Length > 0) yield return cur.ToString();
    }

    string TranslatePiece(string piece, string toLang)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var json = Http.GetStringAsync($"{TranslateUrl}?q={Uri.EscapeDataString(piece)}&langpair=en|{toLang}", cts.Token).GetAwaiter().GetResult();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("quotaFinished", out var q) && q.ValueKind == JsonValueKind.True)
            throw new InvalidOperationException("Free translation limit reached for today, try again tomorrow");
        var status = root.TryGetProperty("responseStatus", out var s) ? s.ToString() : "200";
        if (status != "200") throw new InvalidOperationException("Translation service answered " + status);
        return System.Net.WebUtility.HtmlDecode(Str(root.GetProperty("responseData"), "translatedText"));
    }

    // ---------------- self update (GitHub releases) ----------------

    public const string SelfRepo = "humannot/TLD-ModManager";

    /// <summary>Newest release tag and the download URL of its ModManager.dll.</summary>
    public (string tag, string url) LatestSelf()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var json = Http.GetStringAsync($"https://api.github.com/repos/{SelfRepo}/releases/latest", cts.Token).GetAwaiter().GetResult();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var tag = Str(root, "tag_name");
        foreach (var a in root.GetProperty("assets").EnumerateArray())
            if (Str(a, "name").Equals("ModManager.dll", StringComparison.OrdinalIgnoreCase))
                return (tag, Str(a, "browser_download_url"));
        throw new InvalidOperationException("Release " + tag + " has no ModManager.dll");
    }

    /// <summary>Takes effect on the next start (a locked file is deferred to the start after that).</summary>
    public void InstallSelf(string url)
    {
        _log("Updating Mod Manager from " + url);
        if (!url.StartsWith("https://github.com/" + SelfRepo + "/", StringComparison.Ordinal)) throw new InvalidDataException("Unexpected download URL: " + url);
        var bytes = Http.GetByteArrayAsync(url).GetAwaiter().GetResult();
        if (bytes.Length < 1024 || bytes[0] != 'M' || bytes[1] != 'Z') throw new InvalidDataException("Downloaded file is not a DLL");
        Place(Path.Combine("Plugins", "ModManager.dll"), bytes);
    }

    public void Uninstall(InstalledMod m)
    {
        if (m.IsSelf) throw new InvalidOperationException("Mod Manager cannot uninstall itself");
        _log("Removing " + m.Key);
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
        if (rel.Split(Path.DirectorySeparatorChar).Contains("..")) throw new InvalidDataException("Illegal path in archive: " + entryPath);
        return rel;
    }

    public int UpdateAll(bool onlyAutoUpdate)
    {
        int n = 0;
        foreach (var m in Installed.Values.Where(HasUpdate).ToList())
        {
            // unknown local version (e.g. a hand-copied .modcomponent): offer it in the UI, never replace silently
            if (onlyAutoUpdate && (!m.Entry!.AutoUpdate || m.Version.Length == 0)) continue;
            try { _log($"Updating {m.Entry!.Title}: {m.Version} -> {m.Entry.Version}"); Install(m.Entry); n++; }
            catch (Exception ex) { _log($"Could not update {m.Key}: {ex.Message}"); }
        }
        if (n > 0) Scan();
        return n;
    }

    // ---------------- disable / enable ----------------

    const string DisabledSuffix = ".disabled";

    /// <summary>
    /// Disables or enables a mod by renaming its files to or from <c>*.disabled</c>. Takes effect at the next start;
    /// a file the running game holds is renamed then (see <see cref="ApplyPending"/>).
    /// </summary>
    public void SetEnabled(InstalledMod m, bool enable)
    {
        if (m.IsSelf) throw new InvalidOperationException("Mod Manager cannot disable itself");
        foreach (var f in m.Files.ToList())
        {
            bool off = f.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase);
            if (off == !enable) continue; // already in the wanted state
            var to = enable ? f[..^DisabledSuffix.Length] : f + DisabledSuffix;
            try { File.Move(Path.Combine(Root, f), Path.Combine(Root, to), true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                var p = Path.Combine(PendingDir, f + ".rename");
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                File.WriteAllText(p, to);
            }
        }
        _log((enable ? "Enabled " : "Disabled ") + m.Key);
        Scan();
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
                else if (rel.EndsWith(".rename"))
                {
                    var to = Path.GetFullPath(Path.Combine(Root, File.ReadAllText(p).Trim()));
                    if (!to.StartsWith(Path.GetFullPath(Root), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Rename target outside the game folder");
                    File.Move(Path.Combine(Root, rel[..^".rename".Length]), to, true);
                }
                else
                {
                    var target = Path.Combine(Root, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(p, target, true);
                }
                File.Delete(p); n++;
            }
            catch (Exception ex) { _log("Could not apply " + p + ": " + ex.Message); }
        }
        return n;
    }

    void SaveManifest() => File.WriteAllText(_manifestPath, JsonSerializer.Serialize(_manifest, new JsonSerializerOptions { WriteIndented = true }));
}
