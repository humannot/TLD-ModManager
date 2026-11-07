using System.Collections.Concurrent;
using UnityEngine;

namespace ModManager;

/// <summary>Downloads preview images off the main thread and turns them into textures on it.</summary>
// shortcut: textures are never evicted (about 150 images at most); upgrade = LRU if memory becomes a problem.
static class Img
{
    static readonly Dictionary<string, Texture2D> Textures = new();   // null value = failed, do not retry
    static readonly HashSet<string> Requested = new();
    static readonly ConcurrentQueue<(string url, byte[] bytes)> Ready = new();
    static int _inflight;

    /// <summary>The texture if loaded; otherwise starts loading (at most 4 at a time) and returns null.</summary>
    public static Texture2D Get(string url)
    {
        if (url == null) return null;
        if (Textures.TryGetValue(url, out var t)) return t;
        if (Requested.Contains(url) || _inflight >= 4) return null;
        Requested.Add(url);
        Interlocked.Increment(ref _inflight);
        Task.Run(() =>
        {
            byte[] bytes = null;
            try { bytes = Plugin.Core.FetchImage(url); } catch { /* offline or 404: show the placeholder */ }
            Ready.Enqueue((url, bytes));
            Interlocked.Decrement(ref _inflight);
        });
        return null;
    }

    /// <summary>True when loading this image has failed (offline, not a PNG/JPEG, too big): nothing to wait for.</summary>
    public static bool Failed(string url) => url == null || Textures.TryGetValue(url, out var t) && t == null;

    /// <summary>Main thread, once per frame: decode at most two finished downloads.</summary>
    public static void Pump()
    {
        for (int n = 0; n < 2 && Ready.TryDequeue(out var r); n++)
        {
            Texture2D tex = null;
            if (r.bytes != null)
            {
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                if (!ImageConversion.LoadImage(tex, r.bytes)) { UnityEngine.Object.Destroy(tex); tex = null; }
            }
            Textures[r.url] = tex;
        }
    }
}
