// AutoZoom
// ---------------------------------------------------------------------------
// Zooms apps in or out on their own when you maximize or restore their window.
// For each app you choose two zoom levels: one for when its window is maximized
// and one for when it isn't. AutoZoom watches the window in front and, when it
// changes between maximized and not, presses the app's own zoom keys for you
// (Ctrl+= to zoom in, Ctrl+- to zoom out, Ctrl+0 to reset).
//
// Settings: %LOCALAPPDATA%\AutoZoom\settings.ini  (the toolkit tray app edits it; so can you)
//   App.<process name>=<steps when maximized>,<steps when not maximized>
//       e.g.  App.claude=4,1   One step is one press of Ctrl+= (positive) or Ctrl+- (negative)
//             counted from the app's normal 100% size.
//   PollMs=10     how often the window in front is checked, in ms
//   KeyGapMs=0    pause between key presses; raise to 10-20 if a switch lands on the wrong size
//   Log=true      write one line per zoom change to log.txt (process names only, never window titles)
// The file is re-read within a second of being saved, so changes apply right away.
//
// Every window is tracked on its own, so two windows of the same app (for example two
// accounts) never throw each other's zoom off. The first time AutoZoom sees a window it
// resets that window to 100% once (Ctrl+0), then sets the right level.
//
// Nothing here goes online. Built on your PC by Install.ps1 with the C# compiler built into Windows.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace AutoZoomApp
{
    static class Program
    {
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int idx);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint ms);

        const int GWL_STYLE = -16;
        const int WS_MAXIMIZEBOX = 0x10000;

        class Rule { public int Max, Normal; }

        class WinState
        {
            public int Last = -1;      // -1 unknown, 0 not maximized, 1 maximized
            public int Current;        // steps from 100% that this window is at now
            public bool Synced;        // has been reset to 100% once
        }

        static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoZoom");
        static readonly string SettingsPath = Path.Combine(Dir, "settings.ini");
        static readonly string LogPath = Path.Combine(Dir, "log.txt");

        static Dictionary<string, Rule> rules = new Dictionary<string, Rule>(StringComparer.OrdinalIgnoreCase);
        static int pollMs = 10, gapMs = 0;
        static bool logOn = true;
        static DateTime stamp;

        [STAThread]
        static int Main(string[] args)
        {
            Directory.CreateDirectory(Dir);
            bool created;
            var mutex = new Mutex(true, "Local\\AutoZoom", out created);
            if (!created) return 0; // already running

            try { Run(); }
            catch (Exception ex) { Log("stopped by an error: " + ex.Message); return 1; }
            finally { GC.KeepAlive(mutex); }
            return 0;
        }

        static void Run()
        {
            timeBeginPeriod(1); // lets Sleep(10) really be about 10 ms
            Load();
            Log("started, watching " + rules.Count + (rules.Count == 1 ? " app" : " apps"));

            var names = new Dictionary<uint, string>();
            var wins = new Dictionary<IntPtr, WinState>();
            DateTime nextCheck = DateTime.UtcNow, nextPrune = DateTime.UtcNow.AddMinutes(1);

            while (true)
            {
                Thread.Sleep(pollMs);

                if (DateTime.UtcNow >= nextCheck)
                {
                    nextCheck = DateTime.UtcNow.AddSeconds(1);
                    if (Stamp() != stamp)
                    {
                        Load();
                        foreach (var w in wins.Values) w.Last = -1; // apply the new levels the next time each window is in front
                        Log("settings changed, watching " + rules.Count + (rules.Count == 1 ? " app" : " apps"));
                    }
                }
                if (DateTime.UtcNow >= nextPrune)
                {
                    nextPrune = DateTime.UtcNow.AddMinutes(1);
                    names.Clear(); // process ids get reused
                    foreach (var k in new List<IntPtr>(wins.Keys)) if (!IsWindow(k)) wins.Remove(k);
                }

                IntPtr h = GetForegroundWindow();
                if (h == IntPtr.Zero || IsIconic(h)) continue;
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                string name;
                if (!names.TryGetValue(pid, out name))
                {
                    try { using (var p = Process.GetProcessById((int)pid)) name = p.ProcessName; }
                    catch { name = ""; }
                    names[pid] = name;
                }
                Rule rule;
                if (name.Length == 0 || !rules.TryGetValue(name, out rule)) continue;
                if ((GetWindowLong(h, GWL_STYLE) & WS_MAXIMIZEBOX) == 0) continue; // main windows only, not dialogs or menus

                WinState s;
                if (!wins.TryGetValue(h, out s)) { s = new WinState(); wins[h] = s; }
                int want = IsZoomed(h) ? 1 : 0;
                if (want == s.Last) continue;
                if (ModifierHeld()) continue; // e.g. Win still down after Win+Up; try again in a moment

                int target = want == 1 ? rule.Max : rule.Normal;
                string what = want == 1 ? "maximized" : "not maximized";
                if (!s.Synced)
                {
                    // The window may have only just come forward and not be taking keys yet, and a lost
                    // Ctrl+0 would leave the zoom wrong for good. Wait, check again, and press slowly.
                    Thread.Sleep(300);
                    if (GetForegroundWindow() != h) continue;
                    Zoom(true, target, Math.Max(gapMs, 25));
                    Log(name + " " + what + ": first time, reset to 100% then " + Signed(target));
                }
                else
                {
                    int delta = target - s.Current;
                    Zoom(false, delta, gapMs);
                    if (delta != 0) Log(name + " " + what + ": " + Signed(delta));
                }
                s.Current = target;
                s.Synced = true;
                s.Last = want;
            }
        }

        // ---- settings

        static DateTime Stamp()
        {
            try { return File.Exists(SettingsPath) ? File.GetLastWriteTimeUtc(SettingsPath) : DateTime.MinValue; }
            catch { return DateTime.MinValue; }
        }

        static void Load()
        {
            if (!File.Exists(SettingsPath))
            {
                try
                {
                    File.WriteAllText(SettingsPath,
                        "# AutoZoom settings. The toolkit tray app edits this file; you can too." + Environment.NewLine +
                        "# App.<process name>=<steps when maximized>,<steps when not maximized>" + Environment.NewLine +
                        "PollMs=10" + Environment.NewLine + "KeyGapMs=0" + Environment.NewLine + "Log=true" + Environment.NewLine);
                }
                catch { }
            }
            var next = new Dictionary<string, Rule>(StringComparer.OrdinalIgnoreCase);
            int poll = 10, gap = 0;
            bool log = true;
            string[] lines;
            for (int attempt = 0; ; attempt++)
            {
                try { lines = File.ReadAllLines(SettingsPath); break; }
                catch (IOException) { if (attempt >= 5) { stamp = Stamp(); return; } Thread.Sleep(80); }
                catch (Exception) { stamp = Stamp(); return; }
            }
            foreach (var raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';' || line[0] == '[') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim();
                if (key.StartsWith("App.", StringComparison.OrdinalIgnoreCase))
                {
                    string app = key.Substring(4).Trim();
                    if (app.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) app = app.Substring(0, app.Length - 4);
                    string[] parts = value.Split(',');
                    int max, normal;
                    if (app.Length > 0 && parts.Length >= 2 && TryInt(parts[0], out max) && TryInt(parts[1], out normal))
                    {
                        var r = new Rule();
                        r.Max = Clamp(max);
                        r.Normal = Clamp(normal);
                        next[app] = r;
                    }
                }
                else if (key.Equals("PollMs", StringComparison.OrdinalIgnoreCase)) { int v; if (TryInt(value, out v)) poll = Math.Max(5, Math.Min(1000, v)); }
                else if (key.Equals("KeyGapMs", StringComparison.OrdinalIgnoreCase)) { int v; if (TryInt(value, out v)) gap = Math.Max(0, Math.Min(200, v)); }
                else if (key.Equals("Log", StringComparison.OrdinalIgnoreCase)) { string v = value.ToLowerInvariant(); log = !(v == "0" || v == "false" || v == "off" || v == "no"); }
            }
            rules = next;
            pollMs = poll;
            gapMs = gap;
            logOn = log;
            stamp = Stamp();
        }

        static bool TryInt(string s, out int v) { return int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v); }
        static int Clamp(int steps) { return Math.Max(-15, Math.Min(15, steps)); }
        static string Signed(int steps) { return steps.ToString("+0;-0;0", CultureInfo.InvariantCulture) + (Math.Abs(steps) == 1 ? " step" : " steps"); }

        // ---- keys

        static bool ModifierHeld()
        {
            int[] keys = { 0x10, 0x11, 0x12, 0x5B, 0x5C }; // Shift, Ctrl, Alt, left and right Windows key
            foreach (int k in keys) if ((GetAsyncKeyState(k) & 0x8000) != 0) return true;
            return false;
        }

        static void Tap(byte vk, int gap)
        {
            keybd_event(vk, 0, 0, UIntPtr.Zero);
            keybd_event(vk, 0, 2, UIntPtr.Zero);
            if (gap > 0) Thread.Sleep(gap);
        }

        // Holds Ctrl once and sends every key press back to back.
        static void Zoom(bool reset, int steps, int gap)
        {
            if (!reset && steps == 0) return;
            keybd_event(0x11, 0, 0, UIntPtr.Zero);              // Ctrl down
            if (reset) Tap(0x30, gap);                          // Ctrl+0
            byte key = steps > 0 ? (byte)0xBB : (byte)0xBD;     // Ctrl+= or Ctrl+-
            for (int i = 0; i < Math.Abs(steps); i++) Tap(key, gap);
            keybd_event(0x11, 0, 2, UIntPtr.Zero);              // Ctrl up
        }

        // ---- log

        static void Log(string msg)
        {
            if (!logOn) return;
            try
            {
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 200000) File.Delete(LogPath);
                File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + "  " + msg + Environment.NewLine);
            }
            catch { }
        }
    }
}
