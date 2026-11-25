// Windows Unlocked Toolkit Setup
// ---------------------------------------------------------------------------
// A small window for installing, updating and removing the toolkit's tools, so nobody has to answer
// prompts in a command window. It holds the same all-in-one installer as the website (install.ps1,
// with every tool's source inside) and runs it hidden, without its menu:
//   WUT_MODE=install|uninstall  WUT_TOOLS=leanback,toolkit,...
// It reads the "##WUT begin/end <tool>" lines the installer prints to show progress.
//
// After an install it copies itself to %LOCALAPPDATA%\WindowsUnlockedToolkit\Setup and adds
// "Windows Unlocked Toolkit" to Settings > Apps (for this user only, no admin rights), so the toolkit
// can be changed or removed from there.
//
// Built by scripts/build-setup.ps1 (in GitHub Actions) with the C# compiler that ships with Windows.
// Embedded resources: Setup.xaml, Theme.xaml (the tray app's styles), install.ps1, manifest.ini.
//
// Options:
//   --uninstall                 open on "Remove tools"
//   --silent [ids|all]          install without a window (default: all), exit code 0 when everything worked
//   --make-icon <file.ico>      write the app icon (used by the build)
//   --selftest <folder>         render every screen to PNG with sample data and check bindings, then exit
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using SD = System.Drawing;

namespace UnlockedToolkitSetup
{
    static class Program
    {
        public const string Site = "https://2wanderingcrows.github.io/windows-unlocked-toolkit/";
        public const string Repo = "https://github.com/2wanderingcrows/windows-unlocked-toolkit";
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\WindowsUnlockedToolkit";

        public static string Local { get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } }
        public static string Root { get { return Path.Combine(Local, "WindowsUnlockedToolkit"); } }
        public static string SetupDir { get { return Path.Combine(Root, "Setup"); } }
        public static string SetupCopy { get { return Path.Combine(SetupDir, "WindowsUnlockedToolkitSetup.exe"); } }
        public static string TrayExe { get { return Path.Combine(Root, @"App\WindowsUnlockedToolkit.exe"); } }

        [STAThread]
        static int Main(string[] args)
        {
            string first = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            try
            {
                if (first == "--make-icon") return MakeIcon(args.Length > 1 ? args[1] : "setup.ico");
                if (first == "--selftest") return SelfTest.Run(args.Length > 1 ? args[1] : ".");
                if (first == "--silent") return Silent(args.Length > 1 ? args[1] : "all");

                var app = new Application();
                app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                app.DispatcherUnhandledException += delegate(object s, DispatcherUnhandledExceptionEventArgs e)
                {
                    MessageBox.Show("Something went wrong:\n\n" + e.Exception.Message, "Windows Unlocked Toolkit Setup");
                    e.Handled = true;
                };
                Ui.LoadTheme(app);
                var vm = new SetupVm(Manifest.Load(), first == "--uninstall");
                var w = Ui.BuildWindow(vm);
                app.Run(w);
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Windows Unlocked Toolkit Setup couldn't start:\n\n" + ex.Message, "Windows Unlocked Toolkit Setup");
                return 1;
            }
        }

        static int Silent(string ids)
        {
            var m = Manifest.Load();
            if (ids == "all") ids = string.Join(",", m.Ids.ToArray());
            var log = new StringBuilder();
            int rc = Runner.RunBlocking("install", ids, log);
            if (rc == 0) Registration.Register(m);
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "WindowsUnlockedToolkitSetup.log"), log.ToString()); } catch { }
            return rc;
        }

        static int MakeIcon(string path)
        {
            var sizes = new[] { 16, 20, 24, 32, 40, 48, 64, 256 };
            var images = new List<byte[]>();
            foreach (int size in sizes)
                using (var bmp = Ui.DrawLogo(size))
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, SD.Imaging.ImageFormat.Png);
                    images.Add(ms.ToArray());
                }
            using (var f = File.Create(path))
            using (var w = new BinaryWriter(f))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32);
                    w.Write(images[i].Length); w.Write(offset);
                    offset += images[i].Length;
                }
                foreach (var img in images) w.Write(img);
            }
            return 0;
        }
    }

    // ------------------------------------------------------------------ what's in this setup, and what's on the PC

    class ToolInfo
    {
        public string Id, Name, Version, Summary, Detect;
        public bool Admin;
    }

    class Manifest
    {
        public string Build = "";
        public List<string> Ids = new List<string>();
        public List<ToolInfo> Tools = new List<ToolInfo>();

        public static string Resource(string name)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (s == null) throw new FileNotFoundException("Setup was built without " + name + ".");
                using (var r = new StreamReader(s)) return r.ReadToEnd();
            }
        }

        public static Dictionary<string, string> Parse(string text)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
                int eq = line.IndexOf('=');
                if (eq > 0) d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            return d;
        }

        public static string Get(Dictionary<string, string> d, string key)
        {
            string v;
            return d.TryGetValue(key, out v) ? v : "";
        }

        public static Manifest Load() { return FromText(Resource("manifest.ini")); }

        public static Manifest FromText(string text)
        {
            var d = Parse(text);
            var m = new Manifest();
            m.Build = Get(d, "build");
            foreach (var raw in Get(d, "tools").Split(','))
            {
                string id = raw.Trim();
                if (id.Length == 0) continue;
                m.Ids.Add(id);
                var t = new ToolInfo();
                t.Id = id;
                t.Name = Get(d, id + ".name");
                t.Version = Get(d, id + ".version");
                t.Summary = Get(d, id + ".summary");
                t.Detect = Get(d, id + ".detect");
                t.Admin = Get(d, id + ".admin") == "true";
                m.Tools.Add(t);
            }
            return m;
        }

        // The version on this PC, "?" if it's there from before versions were recorded, or null.
        public static string InstalledVersion(ToolInfo t)
        {
            var have = Parse(File.Exists(VersionFile) ? File.ReadAllText(VersionFile) : "");
            string v = Get(have, t.Id);
            if (v.Length > 0) return v;
            if (t.Detect.Length > 0 && File.Exists(Environment.ExpandEnvironmentVariables(t.Detect))) return "?";
            return null;
        }

        public static string VersionFile { get { return Path.Combine(Program.Root, "installed.ini"); } }
    }

    // Settings > Apps entry, for this user.
    static class Registration
    {
        public static void Register(Manifest m)
        {
            try
            {
                string self = Process.GetCurrentProcess().MainModule.FileName;
                Directory.CreateDirectory(Program.SetupDir);
                if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(Program.SetupCopy), StringComparison.OrdinalIgnoreCase))
                    File.Copy(self, Program.SetupCopy, true);
                using (var k = Registry.CurrentUser.CreateSubKey(Program.UninstallKey))
                {
                    string exe = "\"" + Program.SetupCopy + "\"";
                    k.SetValue("DisplayName", "Windows Unlocked Toolkit");
                    k.SetValue("DisplayVersion", DisplayVersion(m.Build));
                    k.SetValue("Publisher", "Windows Unlocked Toolkit (open source)");
                    k.SetValue("DisplayIcon", Program.SetupCopy + ",0");
                    k.SetValue("InstallLocation", Program.Root);
                    k.SetValue("UninstallString", exe + " --uninstall");
                    k.SetValue("ModifyPath", exe);
                    k.SetValue("URLInfoAbout", Program.Site);
                    k.SetValue("HelpLink", Program.Repo + "/issues");
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    k.SetValue("EstimatedSize", 2048, RegistryValueKind.DWord);
                    k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
                }
            }
            catch { }
        }

        // When nothing is left: take the entry away and delete the setup copy once this window closes.
        public static void Unregister()
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(Program.UninstallKey, false); } catch { }
            try
            {
                if (!Directory.Exists(Program.SetupDir)) return;
                var psi = new ProcessStartInfo("cmd.exe", "/c ping -n 4 127.0.0.1 >nul & rmdir /s /q \"" + Program.SetupDir + "\"");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                Process.Start(psi).Dispose();
            }
            catch { }
        }

        static string DisplayVersion(string build)
        {
            // "2026-11-25 (abc1234)" -> "2026.11.25"
            string date = build.Split(' ')[0];
            return date.Replace('-', '.');
        }
    }

    // ------------------------------------------------------------------ running the installer

    static class Runner
    {
        static string ScriptPath()
        {
            string dir = Path.Combine(Path.GetTempPath(), "WindowsUnlockedToolkitSetup");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "install.ps1");
            File.WriteAllText(path, Manifest.Resource("install.ps1"), Encoding.ASCII);
            return path;
        }

        public static Process Start(string mode, string ids, Action<string> line, Action<int> exited)
        {
            string ps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
            var psi = new ProcessStartInfo(ps, "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + ScriptPath() + "\"");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.WorkingDirectory = Path.GetTempPath();
            psi.EnvironmentVariables["WUT_MODE"] = mode;
            psi.EnvironmentVariables["WUT_TOOLS"] = ids;
            var p = new Process();
            p.StartInfo = psi;
            p.EnableRaisingEvents = true;
            p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) line(e.Data); };
            p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) line(e.Data); };
            p.Exited += delegate { p.WaitForExit(); exited(p.ExitCode); };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            return p;
        }

        public static int RunBlocking(string mode, string ids, StringBuilder log)
        {
            bool ok = false;
            var done = new ManualResetEvent(false);
            using (var p = Start(mode, ids, delegate(string l) { lock (log) { log.AppendLine(l); if (l.Trim() == "##WUT done ok") ok = true; } }, delegate { done.Set(); }))
                done.WaitOne();
            return ok ? 0 : 1;
        }
    }

    // ------------------------------------------------------------------ view models

    abstract class Obs : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void Raise([CallerMemberName] string name = null)
        {
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(name));
        }
        public void RaiseAll()
        {
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(string.Empty));
        }
    }

    class Cmd : ICommand
    {
        readonly Action run;
        public Cmd(Action run) { this.run = run; }
        public event EventHandler CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object p) { return true; }
        public void Execute(object p) { run(); }
    }

    class ToolItem : Obs
    {
        public ToolInfo Info;
        public SetupVm Owner;
        public string Installed;  // version on the PC, "?" or null
        bool selected;
        public bool InstallPick;  // remembered while the remove page is open
        public string State = "";  // "" | pending | running | ok | fail

        public string Name { get { return Info.Name; } }
        public string Summary { get { return Info.Summary; } }
        public string Mono { get { return Info.Name.Length > 0 ? Info.Name.Substring(0, 1).ToUpperInvariant() : "?"; } }
        public Brush TileBrush { get { return Ui.ToolBrush(Info.Id); } }
        public string VersionText { get { return "v" + Info.Version; } }
        public bool IsInstalled { get { return Installed != null; } }
        public bool HasUpdate { get { return Installed != null && Installed != Info.Version; } }

        public string Badge
        {
            get
            {
                if (Owner.Mode == "remove") return Installed == "?" ? "Installed" : "v" + Installed + " installed";
                if (Installed == null) return Info.Admin ? "Needs admin" : "";
                if (Installed == Info.Version) return "Installed";
                return Installed == "?" ? "Update" : "Update from v" + Installed;
            }
        }
        public bool HasBadge { get { return Badge.Length > 0; } }
        public bool Listed { get { return Owner.Mode != "remove" || IsInstalled; } }

        public bool Selected
        {
            get { return selected; }
            set { selected = value; Raise(); if (Owner != null) Owner.SelectionChanged(); }
        }
        public void SetQuiet(bool value) { selected = value; RaiseAll(); }

        public bool InRun { get { return State.Length > 0; } }
        public bool IsPending { get { return State == "pending"; } }
        public bool IsRunning { get { return State == "running"; } }
        public bool IsOk { get { return State == "ok"; } }
        public bool IsFail { get { return State == "fail"; } }
        public string StepText
        {
            get
            {
                bool remove = Owner.RunMode == "uninstall";
                switch (State)
                {
                    case "pending": return "Waiting";
                    case "running": return remove ? "Removing\u2026" : HasUpdate ? "Updating\u2026" : "Installing\u2026";
                    case "ok": return remove ? "Removed" : "Ready";
                    case "fail": return "Didn\u2019t finish";
                }
                return "";
            }
        }
    }

    class SetupVm : Obs
    {
        readonly Manifest manifest;
        public ObservableCollection<ToolItem> Tools { get; private set; }
        public string Mode = "choose";     // choose | remove | work | done
        public string RunMode = "install"; // install | uninstall
        bool autoUpdate, showLog, doneOk;
        readonly StringBuilder log = new StringBuilder();
        Process proc;
        public Window Window;

        public SetupVm(Manifest m, bool startOnRemove)
        {
            manifest = m;
            Tools = new ObservableCollection<ToolItem>();
            foreach (var info in m.Tools)
            {
                var t = new ToolItem();
                t.Info = info; t.Owner = this;
                t.Installed = Manifest.InstalledVersion(info);
                Tools.Add(t);
            }
            bool any = AnyInstalled;
            foreach (var t in Tools) t.SetQuiet(any ? t.IsInstalled : true);
            var settings = Manifest.Parse(File.Exists(SettingsFile) ? File.ReadAllText(SettingsFile) : "");
            autoUpdate = Manifest.Get(settings, "AutoUpdate") != "false";
            if (startOnRemove && any) ShowRemove();
        }

        static string SettingsFile { get { return Path.Combine(Program.Root, "settings.ini"); } }
        bool AnyInstalled { get { foreach (var t in Tools) if (t.IsInstalled) return true; return false; } }
        ToolItem Tray { get { foreach (var t in Tools) if (t.Info.Id == "toolkit") return t; return null; } }

        public void SelectionChanged() { RaiseAll(); }

        int Count { get { int n = 0; foreach (var t in Tools) if (t.Selected && t.Listed) n++; return n; } }

        // ---------------- header
        public Brush LogoBrush { get { return Ui.Gradient("#3B6CF6", "#7C5CFF"); } }
        public string Tagline { get { return "Free, open-source fixes for the small annoyances in Windows."; } }

        // ---------------- pick pages
        public bool IsChoose { get { return Mode == "choose"; } }
        public bool IsPicking { get { return Mode == "choose" || Mode == "remove"; } }
        public string PickTitle { get { return Mode == "remove" ? "Remove tools" : AnyInstalled ? "Your tools" : "Pick your tools"; } }
        public string PickHint
        {
            get
            {
                if (Mode == "remove") return "Each tool puts things back the way they were when it\u2019s removed.";
                return AnyInstalled ? "Switch on anything you\u2019d like to add. Installed tools update in place and keep your settings."
                                    : "Everything is switched on. Turn off anything you don\u2019t want.";
            }
        }
        public bool AutoUpdate { get { return autoUpdate && AutoUpdatePossible; } set { autoUpdate = value; RaiseAll(); } }
        public bool AutoUpdatePossible { get { var t = Tray; return t != null && (t.Selected || t.IsInstalled); } }
        public string AutoUpdateHint
        {
            get
            {
                if (!AutoUpdatePossible) return "Needs the Toolkit tray app, which checks for new versions.";
                return "New versions install quietly in the background while you\u2019re away from your PC. You can turn this off in the tray app\u2019s settings.";
            }
        }
        public string Fineprint
        {
            get
            {
                return "Each tool is built on your PC from readable source code, with no admin rights needed. Leanback Mode also installs AntiMicroX (free and open source) with winget if you don\u2019t have it. Build " + manifest.Build + ".";
            }
        }

        // ---------------- run and done pages
        public bool IsWork { get { return Mode == "work"; } }
        public bool IsDone { get { return Mode == "done"; } }
        public bool IsRunOrDone { get { return IsWork || IsDone; } }
        public bool DoneOk { get { return IsDone && doneOk; } }
        public bool DoneFailed { get { return IsDone && !doneOk; } }
        public Brush DoneBrush { get { return doneOk ? (Brush)Application.Current.Resources["Accent"] : Ui.Solid("#E5484D"); } }
        public string RunTitle
        {
            get
            {
                bool remove = RunMode == "uninstall";
                if (IsWork) return remove ? "Removing\u2026" : "Installing\u2026";
                if (!doneOk) return "Some things didn\u2019t finish";
                return remove ? "Removed" : "You\u2019re all set";
            }
        }
        public string RunHint
        {
            get
            {
                bool remove = RunMode == "uninstall";
                if (IsWork) return remove ? "This takes a few seconds." : "Each tool is being built on your PC. This takes a minute, and Windows may ask once to let AntiMicroX install.";
                if (!doneOk) return "Open the details below to see what happened. Running setup again usually fixes it; if not, report a problem and paste the details.";
                if (remove) return "Those tools are gone, and the settings they changed are back to how they were.";
                var tray = Tray;
                if (tray != null && tray.IsOk) return "Look for the toolkit\u2019s icon in the tray, next to the clock (it may be under the \u02C4 arrow). Click it for quick switches, or open its settings.";
                return "Everything is running in the background.";
            }
        }
        public double Progress
        {
            get
            {
                int total = 0; double done = 0;
                foreach (var t in Tools)
                {
                    if (!t.InRun) continue;
                    total++;
                    if (t.IsOk || t.IsFail) done += 1; else if (t.IsRunning) done += 0.4;
                }
                return total == 0 ? 0 : done / total;
            }
        }
        public string LogText { get { return log.ToString(); } }
        public bool ShowLog { get { return showLog; } }
        public string LogButtonText { get { return showLog ? "Hide details" : "Show details"; } }
        public ICommand ToggleLog { get { return new Cmd(delegate { showLog = !showLog; RaiseAll(); }); } }

        // ---------------- bottom bar
        public string PrimaryText
        {
            get
            {
                if (Mode == "remove") return Count == 0 ? "Remove" : "Remove " + Plural(Count);
                if (Mode == "choose")
                {
                    int n = Count;
                    if (n == 0) return "Install";
                    bool allThere = true, anyNew = false;
                    foreach (var t in Tools) if (t.Selected) { if (!t.IsInstalled) allThere = false; if (t.HasUpdate || !t.IsInstalled) anyNew = true; }
                    if (allThere) return anyNew ? "Update" : "Reinstall";
                    return "Install " + Plural(n);
                }
                if (Mode == "done") { var tray = Tray; return tray != null && tray.IsOk && RunMode == "install" ? "Open the toolkit" : "Done"; }
                return "";
            }
        }
        public bool PrimaryEnabled { get { return !IsPicking || Count > 0; } }
        public bool HasPrimary { get { return Mode != "work"; } }
        public ICommand PrimaryCommand
        {
            get
            {
                return new Cmd(delegate
                {
                    if (Mode == "choose") Run("install");
                    else if (Mode == "remove") Run("uninstall");
                    else if (Mode == "done")
                    {
                        var tray = Tray;
                        if (tray != null && tray.IsOk && RunMode == "install" && File.Exists(Program.TrayExe))
                            try { Process.Start(Program.TrayExe, "--settings").Dispose(); } catch { }
                        if (Window != null) Window.Close();
                    }
                });
            }
        }

        public string SecondaryText { get { return Mode == "remove" ? "Back" : Mode == "done" ? "Close" : "Cancel"; } }
        public bool HasSecondary { get { return Mode == "remove" || Mode == "choose" || (Mode == "done" && PrimaryText != "Done"); } }
        public ICommand SecondaryCommand
        {
            get
            {
                return new Cmd(delegate
                {
                    if (Mode == "remove") ShowChoose();
                    else if (Window != null) Window.Close();
                });
            }
        }

        public string LinkText { get { return Mode == "choose" ? "Remove tools\u2026" : Mode == "done" && !doneOk ? "Report a problem" : ""; } }
        public bool HasLink { get { return (Mode == "choose" && AnyInstalled) || (Mode == "done" && !doneOk); } }
        public ICommand LinkCommand
        {
            get
            {
                return new Cmd(delegate
                {
                    if (Mode == "choose") ShowRemove();
                    else Open(Program.Repo + "/issues");
                });
            }
        }

        static string Plural(int n) { return n == 1 ? "1 tool" : n + " tools"; }

        static void Open(string url)
        {
            try { var psi = new ProcessStartInfo(url); psi.UseShellExecute = true; Process.Start(psi).Dispose(); } catch { }
        }

        void ShowRemove()
        {
            Mode = "remove";
            foreach (var t in Tools) { t.InstallPick = t.Selected; t.SetQuiet(false); }
            RaiseAll();
        }

        void ShowChoose()
        {
            Mode = "choose";
            foreach (var t in Tools) t.SetQuiet(t.InstallPick);
            RaiseAll();
        }

        // ---------------- running
        public void Run(string runMode)
        {
            RunMode = runMode;
            var ids = new List<string>();
            foreach (var t in Tools)
            {
                bool pick = t.Selected && t.Listed;
                t.State = pick ? "pending" : "";
                if (pick) ids.Add(t.Info.Id);
            }
            if (ids.Count == 0) return;
            if (runMode == "install" && AutoUpdatePossible) SaveAutoUpdate(autoUpdate);
            Mode = "work";
            log.Length = 0;
            Refresh();
            var d = Dispatcher.CurrentDispatcher;
            try
            {
                proc = Runner.Start(runMode, string.Join(",", ids.ToArray()),
                    delegate(string line) { d.BeginInvoke(new Action(delegate { OnLine(line); })); },
                    delegate(int code) { d.BeginInvoke(new Action(delegate { Finish(); })); });
            }
            catch (Exception ex)
            {
                log.AppendLine("Couldn't start the installer: " + ex.Message);
                foreach (var t in Tools) if (t.IsPending) t.State = "fail";
                Finish();
            }
        }

        void OnLine(string line)
        {
            string l = line.Trim();
            if (l.StartsWith("##WUT ", StringComparison.Ordinal))
            {
                var parts = l.Substring(6).Split(' ');
                ToolItem t = parts.Length > 1 ? Find(parts[1]) : null;
                if (parts[0] == "begin" && t != null) t.State = "running";
                else if (parts[0] == "end" && t != null) t.State = parts.Length > 2 && parts[2] == "ok" ? "ok" : "fail";
                Refresh();
                return;
            }
            log.AppendLine(line);
            Raise("LogText");
            Ui.ScrollLog(Window);
        }

        ToolItem Find(string id) { foreach (var t in Tools) if (t.Info.Id == id) return t; return null; }

        void Finish()
        {
            if (Mode == "done") return;
            if (proc != null) { proc.Dispose(); proc = null; }
            doneOk = true;
            foreach (var t in Tools)
            {
                if (t.IsPending || t.IsRunning) t.State = "fail";
                if (t.IsFail) doneOk = false;
                t.Installed = Manifest.InstalledVersion(t.Info);
            }
            if (AnyInstalled) Registration.Register(manifest); else Registration.Unregister();
            Mode = "done";
            if (!doneOk) showLog = true;
            Refresh();
        }

        void Refresh()
        {
            foreach (var t in Tools) t.RaiseAll();
            RaiseAll();
        }

        public bool Busy { get { return Mode == "work"; } }

        static void SaveAutoUpdate(bool on)
        {
            try
            {
                Directory.CreateDirectory(Program.Root);
                var lines = File.Exists(SettingsFile) ? new List<string>(File.ReadAllLines(SettingsFile)) : new List<string>();
                bool found = false;
                for (int i = 0; i < lines.Count; i++)
                    if (lines[i].Trim().StartsWith("AutoUpdate=", StringComparison.OrdinalIgnoreCase)) { lines[i] = "AutoUpdate=" + (on ? "true" : "false"); found = true; }
                if (!found) lines.Add("AutoUpdate=" + (on ? "true" : "false"));
                File.WriteAllLines(SettingsFile, lines.ToArray());
            }
            catch { }
        }

        // ---------------- self-test helpers
        public void Sample(string mode)
        {
            Mode = mode;
            if (mode == "work" || mode == "done")
            {
                RunMode = "install";
                string[] states = mode == "work" ? new[] { "ok", "running", "pending", "pending" } : new[] { "ok", "ok", "ok", "ok" };
                for (int i = 0; i < Tools.Count; i++) Tools[i].State = i < states.Length ? states[i] : "pending";
                log.AppendLine("==> Building Leanback Mode");
                log.AppendLine("    built C:\\Users\\you\\AppData\\Local\\LeanbackMode\\LeanbackMode.exe");
                showLog = mode == "work";
                doneOk = true;
            }
            Refresh();
        }

        public void SampleInstalled()
        {
            for (int i = 0; i < Tools.Count; i++) Tools[i].Installed = i == 0 ? "1.0" : i == 1 ? Tools[i].Info.Version : null;
            foreach (var t in Tools) t.SetQuiet(t.IsInstalled);
            Refresh();
        }
    }

    // ------------------------------------------------------------------ look

    static class Ui
    {
        public static bool Dark;

        public static object LoadXaml(string name)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (s == null) throw new FileNotFoundException("Setup was built without " + name + ".");
                return XamlReader.Load(s);
            }
        }

        public static void LoadTheme(Application app) { LoadTheme(app, SystemDark()); }

        public static void LoadTheme(Application app, bool dark)
        {
            if (app.Resources.MergedDictionaries.Count == 0) app.Resources.MergedDictionaries.Add((ResourceDictionary)LoadXaml("Theme.xaml"));
            Dark = dark;
            var r = app.Resources;
            string[] keys = { "BgPage", "BgWin", "BgSide", "BgCard", "Line", "Ink", "Ink2", "Mut", "Soft", "Off" };
            string[] light = { "#F9F9FB", "#F4F4F7", "#ECECF1", "#FFFFFF", "#E3E3EA", "#1B1B21", "#3B3B45", "#6B6B76", "#EEEEF3", "#C9C9D2" };
            string[] darkC = { "#26262D", "#1C1C21", "#222228", "#2B2B32", "#3A3A43", "#F2F2F5", "#D2D2DA", "#9C9CA8", "#35353D", "#56565F" };
            for (int i = 0; i < keys.Length; i++) r[keys[i]] = Solid(dark ? darkC[i] : light[i]);
            r["Accent"] = Solid(dark ? "#6E8FF8" : "#3B6CF6");
            r["AccentInk"] = Solid("#FFFFFF");
        }

        static bool SystemDark()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    return v is int && (int)v == 0;
                }
            }
            catch { return false; }
        }

        public static SolidColorBrush Solid(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        public static Brush Gradient(string a, string b)
        {
            var g = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(a), (Color)ColorConverter.ConvertFromString(b), new Point(0, 0), new Point(1, 1));
            g.Freeze();
            return g;
        }

        // Same colours as the tray app's tool cards.
        public static Brush ToolBrush(string id)
        {
            switch (id)
            {
                case "leanback": return Gradient("#3B6CF6", "#7C5CFF");
                case "mediatabjump": return Gradient("#FF8A5B", "#E5487A");
                case "autozoom": return Gradient("#D98E04", "#B5452A");
                case "toolkit": return Gradient("#0E8FA3", "#16A37B");
            }
            return Gradient("#5B6472", "#2A2F3A");
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public static Window BuildWindow(SetupVm vm)
        {
            var w = (Window)LoadXaml("Setup.xaml");
            w.DataContext = vm;
            vm.Window = w;
            try
            {
                string self = Process.GetCurrentProcess().MainModule.FileName;
                using (var ico = SD.Icon.ExtractAssociatedIcon(self))
                    w.Icon = Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            }
            catch { }
            w.SourceInitialized += delegate
            {
                var h = new WindowInteropHelper(w).Handle;
                int on = Dark ? 1 : 0;
                try { DwmSetWindowAttribute(h, 20, ref on, 4); } catch { } // dark title bar to match
            };
            w.Closing += delegate(object s, CancelEventArgs e)
            {
                if (vm.Busy)
                {
                    e.Cancel = true;
                    MessageBox.Show(w, "Setup is still working. It closes by itself if you wait a moment.", "Windows Unlocked Toolkit Setup");
                }
            };
            return w;
        }

        public static void ScrollLog(Window w)
        {
            if (w == null) return;
            var box = w.FindName("Log") as TextBox;
            if (box != null) box.ScrollToEnd();
        }

        // The app icon: an accent tile with an open padlock (the same mark as the website and tray app).
        public static SD.Bitmap DrawLogo(int size)
        {
            var bmp = new SD.Bitmap(size, size, SD.Imaging.PixelFormat.Format32bppArgb);
            using (var g = SD.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(SD.Color.Transparent);
                float s = size / 64f;
                using (var path = Rounded(1 * s, 1 * s, 62 * s, 62 * s, 15 * s))
                using (var fill = new SD.Drawing2D.LinearGradientBrush(new SD.PointF(0, 0), new SD.PointF(size, size), SD.Color.FromArgb(0x3B, 0x6C, 0xF6), SD.Color.FromArgb(0x7C, 0x5C, 0xFF)))
                    g.FillPath(fill, path);
                float stroke = Math.Max(1.5f, 4.2f * s);
                using (var pen = new SD.Pen(SD.Color.White, stroke))
                using (var body = Rounded(17 * s, 29 * s, 30 * s, 22 * s, 4 * s))
                using (var white = new SD.SolidBrush(SD.Color.White))
                {
                    pen.StartCap = pen.EndCap = SD.Drawing2D.LineCap.Round;
                    g.FillPath(white, body);
                    g.DrawArc(pen, 22 * s, 12 * s, 18 * s, 20 * s, 180, 150); // open shackle
                    g.DrawLine(pen, 22 * s, 22 * s, 22 * s, 29.5f * s);
                }
            }
            return bmp;
        }

        static SD.Drawing2D.GraphicsPath Rounded(float x, float y, float w, float h, float r)
        {
            var p = new SD.Drawing2D.GraphicsPath();
            float d = r * 2;
            p.AddArc(x, y, d, d, 180, 90);
            p.AddArc(x + w - d, y, d, d, 270, 90);
            p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            p.AddArc(x, y + h - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Settle()
        {
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(delegate { }));
        }
    }

    // ------------------------------------------------------------------ self-test (CI)

    static class SelfTest
    {
        class Collector : TraceListener
        {
            public readonly StringBuilder Text = new StringBuilder();
            public int Count;
            public override void Write(string message) { Text.Append(message); }
            public override void WriteLine(string message) { Text.AppendLine(message); Count++; }
        }

        public static int Run(string outDir)
        {
            var report = new StringBuilder();
            var errors = new Collector();
            int rc = 0;
            try
            {
                Directory.CreateDirectory(outDir);
                PresentationTraceSources.Refresh();
                PresentationTraceSources.DataBindingSource.Listeners.Add(errors);
                PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;

                var app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var m = Manifest.Load();
                report.AppendLine("manifest: build " + m.Build + ", " + m.Tools.Count + " tools: " + string.Join(", ", m.Ids.ToArray()));
                if (m.Tools.Count == 0) { report.AppendLine("no tools in manifest.ini"); rc = 1; }
                string script = Manifest.Resource("install.ps1");
                if (script.IndexOf("##WUT-POWERSHELL-BELOW##", StringComparison.Ordinal) < 0) { report.AppendLine("install.ps1 isn't the toolkit installer"); rc = 1; }

                foreach (var dark in new[] { false, true })
                {
                    Ui.LoadTheme(app, dark);
                    string suffix = dark ? "-dark" : "";
                    var vm = new SetupVm(m, false);
                    foreach (var t in vm.Tools) { t.Installed = null; t.SetQuiet(true); }
                    var root = Detach(Ui.BuildWindow(vm));
                    vm.Sample("choose");
                    Save(root, Path.Combine(outDir, "setup-choose" + suffix + ".png"), report);
                    if (dark) continue;
                    vm.SampleInstalled();
                    Save(root, Path.Combine(outDir, "setup-update.png"), report);
                    vm.LinkCommand.Execute(null); // Remove tools...
                    Save(root, Path.Combine(outDir, "setup-remove.png"), report);
                    vm.SecondaryCommand.Execute(null); // Back
                    vm.Sample("work");
                    Save(root, Path.Combine(outDir, "setup-working.png"), report);
                    vm.Sample("done");
                    Save(root, Path.Combine(outDir, "setup-done.png"), report);
                }
                using (var bmp = Ui.DrawLogo(256)) bmp.Save(Path.Combine(outDir, "setup-icon.png"));
            }
            catch (Exception ex)
            {
                report.AppendLine("FAILED: " + ex);
                rc = 1;
            }
            if (errors.Count > 0)
            {
                report.AppendLine("Binding problems:");
                report.Append(errors.Text);
                rc = 1;
            }
            report.AppendLine(rc == 0 ? "OK" : "FAILED");
            try { File.WriteAllText(Path.Combine(outDir, "selftest.txt"), report.ToString()); } catch { }
            return rc;
        }

        static FrameworkElement Detach(Window w)
        {
            var root = (FrameworkElement)w.Content;
            object data = w.DataContext;
            w.Content = null;
            root.DataContext = data;
            root.SetResourceReference(TextElement.FontFamilyProperty, "UiFont");
            root.SetResourceReference(TextElement.ForegroundProperty, "Ink");
            TextElement.SetFontSize(root, 14);
            return root;
        }

        static void Save(FrameworkElement root, string file, StringBuilder report)
        {
            for (int i = 0; i < 3; i++)
            {
                Ui.Settle();
                root.Measure(new Size(640, 720));
                root.Arrange(new Rect(0, 0, 640, 720));
                root.UpdateLayout();
            }
            var rtb = new RenderTargetBitmap(640, 720, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(root);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var f = File.Create(file)) enc.Save(f);
            report.AppendLine("rendered " + Path.GetFileName(file));
        }
    }
}
