// Mouse Gears: switchable speed profiles for any mouse, in the system tray.
// MIT License. One file, built with the C# compiler that ships with Windows (.NET Framework 4.8).
// Logic is documented in architecture/mouse-gears-sop.md - update that first when changing behaviour.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("Mouse Gears")]
[assembly: System.Reflection.AssemblyProduct("Mouse Gears")]
[assembly: System.Reflection.AssemblyDescription("Speed profiles for any mouse")]
[assembly: System.Reflection.AssemblyCopyright("MIT License")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]

namespace MouseGears
{
    // ---------------------------------------------------------------- settings

    public class Profile
    {
        public string name { get; set; }
        public int speed { get; set; }
        public bool precision { get; set; }
        public int scrollLines { get; set; }
        public Profile() { }
        public Profile(string n, int s, bool p, int l) { name = n; speed = s; precision = p; scrollLines = l; }
    }

    public class ButtonMap
    {
        public string Back { get; set; }
        public string Forward { get; set; }
        public string Middle { get; set; }
        public string Get(string b)
        {
            if (b == "Back") return Back;
            if (b == "Forward") return Forward;
            if (b == "Middle") return Middle;
            return "pass";
        }
        public void Set(string b, string v)
        {
            if (b == "Back") Back = v;
            else if (b == "Forward") Forward = v;
            else if (b == "Middle") Middle = v;
        }
    }

    public class Settings
    {
        public int version { get; set; }
        public int active { get; set; }
        public List<Profile> profiles { get; set; }
        public ButtonMap buttons { get; set; }
        public bool? showPopup { get; set; }
        public bool? startWithWindows { get; set; }

        public const int MaxProfiles = 9;
        public static readonly string[] ButtonNames = { "Back", "Forward", "Middle" };

        public static Settings Defaults()
        {
            Settings s = new Settings();
            s.version = 1;
            s.active = 1;
            s.profiles = new List<Profile>();
            s.profiles.Add(new Profile("Slow", 6, false, 3));
            s.profiles.Add(new Profile("Normal", 10, false, 3));
            s.profiles.Add(new Profile("Fast", 15, false, 5));
            s.buttons = new ButtonMap();
            s.Fix();
            return s;
        }

        // First run: Normal = the user's own Windows settings, Slow and Fast either side of it.
        public static Settings DefaultsFrom(MouseState w)
        {
            Settings s = Defaults();
            bool acc = w.mouse != null && w.mouse[2] != 0;
            s.profiles[0] = new Profile("Slow", Clamp(w.speed - 4, 1, 20), acc, w.lines);
            s.profiles[1] = new Profile("Normal", Clamp(w.speed, 1, 20), acc, w.lines);
            s.profiles[2] = new Profile("Fast", Clamp(w.speed + 5, 1, 20), acc, Clamp(w.lines + 2, 1, 100));
            s.Fix();
            return s;
        }

        public static bool ValidAction(string a, int count)
        {
            if (a == "pass" || a == "next" || a == "prev" || a == "holdSlow") return true;
            if (a != null && a.StartsWith("profile:"))
            {
                int n;
                return int.TryParse(a.Substring(8), out n) && n >= 1 && n <= count;
            }
            return false;
        }

        static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }

        public void Fix()
        {
            version = 1;
            if (profiles == null) profiles = new List<Profile>();
            profiles.RemoveAll(p => p == null);
            if (profiles.Count == 0) profiles = Defaults().profiles;
            if (profiles.Count > MaxProfiles) profiles.RemoveRange(MaxProfiles, profiles.Count - MaxProfiles);
            for (int i = 0; i < profiles.Count; i++)
            {
                Profile p = profiles[i];
                p.name = (p.name ?? "").Trim();
                if (p.name.Length == 0) p.name = "Profile " + (i + 1);
                if (p.name.Length > 24) p.name = p.name.Substring(0, 24);
                p.speed = Clamp(p.speed, 1, 20);
                p.scrollLines = Clamp(p.scrollLines, 1, 100);
            }
            active = Clamp(active, 0, profiles.Count - 1);
            if (buttons == null) buttons = new ButtonMap();
            foreach (string b in ButtonNames)
                if (!ValidAction(buttons.Get(b), profiles.Count)) buttons.Set(b, "pass");
            if (showPopup == null) showPopup = true;
            if (startWithWindows == null) startWithWindows = false;
        }

        static string Q(string s) { return new JavaScriptSerializer().Serialize(s ?? ""); }
        static string B(bool? b) { return b == true ? "true" : "false"; }

        public string ToJson()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\n  \"version\": 1,\n  \"active\": ").Append(active).Append(",\n  \"profiles\": [\n");
            for (int i = 0; i < profiles.Count; i++)
            {
                Profile p = profiles[i];
                sb.Append("    { \"name\": ").Append(Q(p.name))
                  .Append(", \"speed\": ").Append(p.speed)
                  .Append(", \"precision\": ").Append(p.precision ? "true" : "false")
                  .Append(", \"scrollLines\": ").Append(p.scrollLines)
                  .Append(i < profiles.Count - 1 ? " },\n" : " }\n");
            }
            sb.Append("  ],\n  \"buttons\": { \"Back\": ").Append(Q(buttons.Back))
              .Append(", \"Forward\": ").Append(Q(buttons.Forward))
              .Append(", \"Middle\": ").Append(Q(buttons.Middle)).Append(" },\n");
            sb.Append("  \"showPopup\": ").Append(B(showPopup)).Append(",\n");
            sb.Append("  \"startWithWindows\": ").Append(B(startWithWindows)).Append("\n}\n");
            return sb.ToString();
        }
    }

    // ---------------------------------------------------------------- Windows mouse settings

    public struct MouseState
    {
        public int speed;
        public int[] mouse;   // threshold1, threshold2, acceleration
        public int lines;
        public bool Same(MouseState o)
        {
            return speed == o.speed && lines == o.lines && mouse != null && o.mouse != null &&
                   mouse[0] == o.mouse[0] && mouse[1] == o.mouse[1] && mouse[2] == o.mouse[2];
        }
        public override string ToString()
        {
            return "speed " + speed + ", accel " + (mouse == null ? "?" : mouse[0] + "/" + mouse[1] + "/" + mouse[2]) + ", scroll " + lines;
        }
    }

    public static class WinMouse
    {
        const uint SPI_GETMOUSE = 0x0003, SPI_SETMOUSE = 0x0004;
        const uint SPI_GETWHEELSCROLLLINES = 0x0068, SPI_SETWHEELSCROLLLINES = 0x0069;
        const uint SPI_GETMOUSESPEED = 0x0070, SPI_SETMOUSESPEED = 0x0071;
        const uint SPIF_SENDCHANGE = 0x0002;

        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
        static extern bool SpiPtr(uint action, uint uParam, IntPtr pv, uint flags);
        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
        static extern bool SpiInt(uint action, uint uParam, ref int pv, uint flags);
        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
        static extern bool SpiArr(uint action, uint uParam, [In, Out] int[] pv, uint flags);

        public static MouseState Read()
        {
            MouseState s = new MouseState();
            SpiInt(SPI_GETMOUSESPEED, 0, ref s.speed, 0);
            s.mouse = new int[3];
            SpiArr(SPI_GETMOUSE, 0, s.mouse, 0);
            SpiInt(SPI_GETWHEELSCROLLLINES, 0, ref s.lines, 0);
            return s;
        }

        // Session-only: never SPIF_UPDATEINIFILE, so the user's saved settings stay untouched.
        public static void Write(MouseState s)
        {
            SpiPtr(SPI_SETMOUSESPEED, 0, new IntPtr(s.speed), 0);
            SpiArr(SPI_SETMOUSE, 0, (int[])s.mouse.Clone(), 0);
            int cur = 0;
            SpiInt(SPI_GETWHEELSCROLLLINES, 0, ref cur, 0);
            if (cur != s.lines) SpiPtr(SPI_SETWHEELSCROLLLINES, (uint)s.lines, IntPtr.Zero, SPIF_SENDCHANGE);
        }

        public static MouseState FromProfile(Profile p)
        {
            MouseState s = new MouseState();
            s.speed = p.speed;
            s.mouse = p.precision ? new int[] { 6, 10, 1 } : new int[] { 0, 0, 0 };
            s.lines = p.scrollLines;
            return s;
        }

        static int Reg(string key, string name, int fallback)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(key))
                {
                    if (k == null) return fallback;
                    int v;
                    object o = k.GetValue(name);
                    return (o != null && int.TryParse(o.ToString(), out v)) ? v : fallback;
                }
            }
            catch { return fallback; }
        }

        // The user's own saved Windows settings.
        public static MouseState ReadSaved()
        {
            MouseState s = new MouseState();
            s.speed = Reg(@"Control Panel\Mouse", "MouseSensitivity", 10);
            s.mouse = new int[] {
                Reg(@"Control Panel\Mouse", "MouseThreshold1", 6),
                Reg(@"Control Panel\Mouse", "MouseThreshold2", 10),
                Reg(@"Control Panel\Mouse", "MouseSpeed", 1) };
            s.lines = Reg(@"Control Panel\Desktop", "WheelScrollLines", 3);
            return s;
        }
    }

    // ---------------------------------------------------------------- engine

    public class Engine
    {
        public Settings S;
        public readonly string Dir, FilePath;
        public bool Sync;            // apply on the calling thread (self-test)
        public bool Paused { get; private set; }
        public string LoadNote;      // set when settings.json was unreadable
        public event Action<string> Changed;   // popup text, or null

        int holdReturn = -1;
        bool stopped;
        readonly object pendLock = new object(), writeLock = new object();
        MouseState pending;
        bool hasPending;

        public Engine(string dir) { Dir = dir; FilePath = Path.Combine(dir, "settings.json"); }

        public void Load()
        {
            Directory.CreateDirectory(Dir);
            Settings s = null;
            if (File.Exists(FilePath))
            {
                try
                {
                    s = new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(FilePath));
                    if (s == null) throw new Exception("empty file");
                }
                catch (Exception ex)
                {
                    LoadNote = "settings.json could not be read (" + ex.Message + "). A copy was kept as settings.json.bak and the defaults were loaded.";
                    try { File.Copy(FilePath, FilePath + ".bak", true); } catch { }
                    s = null;
                }
            }
            bool fresh = s == null;
            if (fresh) s = Settings.DefaultsFrom(WinMouse.ReadSaved());
            s.Fix();
            S = s;
            if (fresh) Save();
        }

        public bool Save()
        {
            try
            {
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, S.ToJson(), new UTF8Encoding(false));
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
                return true;
            }
            catch { return false; }
        }

        public Profile Active { get { return S.profiles[S.active]; } }

        public void ApplyCurrent() { Push(Paused ? WinMouse.ReadSaved() : WinMouse.FromProfile(Active)); }

        // Latest wins: the hook thread never waits on a settings broadcast.
        void Push(MouseState st)
        {
            if (Sync) { lock (writeLock) WinMouse.Write(st); return; }
            lock (pendLock) { pending = st; hasPending = true; }
            ThreadPool.QueueUserWorkItem(delegate
            {
                lock (writeLock)
                {
                    MouseState p;
                    lock (pendLock)
                    {
                        if (!hasPending || stopped) return;
                        p = pending;
                        hasPending = false;
                    }
                    WinMouse.Write(p);
                }
            });
        }

        // Quit: put the user's saved Windows settings back, synchronously.
        public void Shutdown()
        {
            lock (writeLock)
            {
                lock (pendLock) { stopped = true; hasPending = false; }
                WinMouse.Write(WinMouse.ReadSaved());
            }
        }

        void Fire(string text) { Action<string> h = Changed; if (h != null) h(text); }

        public void SetActive(int i, bool persist, bool popup)
        {
            int n = S.profiles.Count;
            S.active = ((i % n) + n) % n;
            Paused = false;
            ApplyCurrent();
            if (persist) Save();
            Fire(popup ? Active.name : null);
        }

        public void Next() { holdReturn = -1; SetActive(S.active + 1, true, true); }
        public void Prev() { holdReturn = -1; SetActive(S.active - 1, true, true); }

        public void SetPaused(bool p)
        {
            Paused = p;
            holdReturn = -1;
            ApplyCurrent();
            Fire(p ? "Paused" : Active.name);
        }

        public string ActionFor(string button)
        {
            if (Paused) return "pass";
            return S.buttons.Get(button) ?? "pass";
        }

        public void DoAction(string act, bool down)
        {
            if (act == "holdSlow")
            {
                if (down)
                {
                    if (holdReturn < 0) { int r = S.active; SetActive(0, false, true); holdReturn = r; }
                }
                else if (holdReturn >= 0)
                {
                    int r = holdReturn; holdReturn = -1; SetActive(r, false, true);
                }
                return;
            }
            if (!down) return;
            if (act == "next") Next();
            else if (act == "prev") Prev();
            else if (act.StartsWith("profile:"))
            {
                int n;
                if (int.TryParse(act.Substring(8), out n)) { holdReturn = -1; SetActive(n - 1, true, true); }
            }
        }

        public void Replace(Settings ns)
        {
            ns.Fix();
            S = ns;
            holdReturn = -1;
            Paused = false;
            ApplyCurrent();
            Save();
            Fire(null);
        }
    }

    // ---------------------------------------------------------------- mouse hook

    public class MouseHook : IDisposable
    {
        delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int id, HookProc fn, IntPtr mod, uint threadId);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);

        [StructLayout(LayoutKind.Sequential)]
        struct MSLLHOOKSTRUCT { public int x, y; public uint mouseData, flags, time; public IntPtr extra; }

        const int WH_MOUSE_LL = 14;
        const int WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208, WM_XBUTTONDOWN = 0x020B, WM_XBUTTONUP = 0x020C;

        readonly Engine engine;
        readonly Control invoker;
        HookProc proc;      // kept in a field so the GC never collects it
        IntPtr handle = IntPtr.Zero;
        public int Swallowed;

        public MouseHook(Engine e, Control inv) { engine = e; invoker = inv; }

        public bool Install()
        {
            proc = Proc;
            handle = SetWindowsHookEx(WH_MOUSE_LL, proc, GetModuleHandle(null), 0);
            return handle != IntPtr.Zero;
        }

        public static string ButtonOf(int msg, uint mouseData, out bool down)
        {
            down = false;
            if (msg == WM_XBUTTONDOWN || msg == WM_XBUTTONUP)
            {
                down = msg == WM_XBUTTONDOWN;
                int x = (int)(mouseData >> 16);
                return x == 1 ? "Back" : (x == 2 ? "Forward" : null);
            }
            if (msg == WM_MBUTTONDOWN || msg == WM_MBUTTONUP)
            {
                down = msg == WM_MBUTTONDOWN;
                return "Middle";
            }
            return null;
        }

        IntPtr Proc(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                MSLLHOOKSTRUCT m = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                bool down;
                string b = ButtonOf(wParam.ToInt32(), m.mouseData, out down);
                if (b != null)
                {
                    string act = engine.ActionFor(b);
                    if (act != "pass")
                    {
                        Swallowed++;
                        bool d = down;
                        invoker.BeginInvoke((Action)delegate { engine.DoAction(act, d); });
                        return new IntPtr(1);
                    }
                }
            }
            return CallNextHookEx(handle, code, wParam, lParam);
        }

        public void Dispose()
        {
            if (handle != IntPtr.Zero) { UnhookWindowsHookEx(handle); handle = IntPtr.Zero; }
        }
    }

    // ---------------------------------------------------------------- artwork (black + gold)

    public static class Art
    {
        public static readonly Color Gold = Color.FromArgb(212, 175, 55);
        public static readonly Color Ink = Color.FromArgb(11, 11, 11);
        public static readonly Color Panel = Color.FromArgb(18, 18, 18);
        public static readonly Color Field = Color.FromArgb(32, 32, 32);
        public static readonly Color Text = Color.FromArgb(235, 235, 235);
        public static readonly Color Dim = Color.FromArgb(150, 150, 150);

        public static Bitmap Gear(int size, string label, bool dim)
        {
            Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                float c = size / 2f, rOut = size * 0.49f, rIn = size * 0.37f;
                int teeth = 8, steps = teeth * 4;
                PointF[] pts = new PointF[steps];
                for (int i = 0; i < steps; i++)
                {
                    double a = (i + 0.5) * Math.PI * 2 / steps - Math.PI / 2;
                    float r = (i % 4 == 0 || i % 4 == 1) ? rOut : rIn;
                    pts[i] = new PointF(c + (float)(Math.Cos(a) * r), c + (float)(Math.Sin(a) * r));
                }
                Color fill = dim ? Color.FromArgb(120, 120, 120) : Gold;
                using (SolidBrush b = new SolidBrush(fill)) g.FillPolygon(b, pts);
                float hole = size * 0.29f;
                using (SolidBrush b = new SolidBrush(Ink)) g.FillEllipse(b, c - hole, c - hole, hole * 2, hole * 2);
                if (!string.IsNullOrEmpty(label))
                {
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    using (Font f = new Font("Segoe UI", size * 0.36f, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (SolidBrush b = new SolidBrush(fill))
                    using (StringFormat sf = new StringFormat())
                    {
                        sf.Alignment = StringAlignment.Center;
                        sf.LineAlignment = StringAlignment.Center;
                        g.DrawString(label, f, b, new RectangleF(0, size * 0.02f, size, size), sf);
                    }
                }
                else
                {
                    float dot = size * 0.09f;
                    using (SolidBrush b = new SolidBrush(fill)) g.FillEllipse(b, c - dot, c - dot, dot * 2, dot * 2);
                }
            }
            return bmp;
        }

        // Multi-size .ico with PNG frames (Windows Vista and later read these).
        public static void WriteIco(string path)
        {
            int[] sizes = { 16, 24, 32, 48, 64, 256 };
            List<byte[]> frames = new List<byte[]>();
            foreach (int s in sizes)
                using (Bitmap b = Gear(s, null, false))
                using (MemoryStream ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); frames.Add(ms.ToArray()); }
            using (BinaryWriter w = new BinaryWriter(File.Create(path)))
            {
                w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)0); w.Write((byte)0);
                    w.Write((ushort)1); w.Write((ushort)32);
                    w.Write(frames[i].Length); w.Write(offset);
                    offset += frames[i].Length;
                }
                foreach (byte[] f in frames) w.Write(f);
            }
        }
    }

    // ---------------------------------------------------------------- popup near the cursor

    class Popup : Form
    {
        readonly Label lbl;
        readonly System.Windows.Forms.Timer timer;
        int ticks;

        public Popup()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Art.Gold;              // 1 px gold border around the label
            Padding = new Padding(1);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            lbl = new Label();
            lbl.AutoSize = true;
            lbl.BackColor = Art.Ink;
            lbl.ForeColor = Art.Gold;
            lbl.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            lbl.Padding = new Padding(10, 5, 10, 5);
            Controls.Add(lbl);
            timer = new System.Windows.Forms.Timer();
            timer.Interval = 40;
            timer.Tick += OnTick;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 | 0x00000080 | 0x00000020;  // no-activate, tool window, click-through
                return cp;
            }
        }

        public void ShowText(string text)
        {
            lbl.Text = text;
            if (!Visible) { Opacity = 0; Show(); }
            PerformLayout();
            Point p = Cursor.Position;
            Rectangle wa = Screen.FromPoint(p).WorkingArea;
            int x = Math.Min(p.X + 18, wa.Right - Width);
            int y = Math.Min(p.Y + 18, wa.Bottom - Height);
            Location = new Point(Math.Max(wa.Left, x), Math.Max(wa.Top, y));
            Opacity = 0.96;
            ticks = 0;
            timer.Start();
        }

        void OnTick(object s, EventArgs e)
        {
            ticks++;
            if (ticks < 18) return;
            Opacity -= 0.12;
            if (Opacity <= 0.05) { timer.Stop(); Hide(); }
        }
    }

    // ---------------------------------------------------------------- settings window

    class SettingsForm : Form
    {
        class Row
        {
            public TextBox name;
            public TrackBar speed;
            public Label value;
            public CheckBox accel;
            public NumericUpDown lines;
            public Button remove;
        }

        readonly Engine engine;
        readonly TableLayoutPanel grid;
        readonly List<Row> rows = new List<Row>();
        readonly ComboBox[] combos = new ComboBox[3];
        readonly List<string>[] comboCodes = new List<string>[3];
        readonly CheckBox chkPopup, chkStart;
        readonly Button addBtn;
        readonly float k;

        int Px(int v) { return (int)Math.Round(v * k); }

        public SettingsForm(Engine e, Icon icon)
        {
            engine = e;
            using (Graphics g = CreateGraphics()) k = g.DpiX / 96f;
            Text = "Mouse Gears";
            Icon = icon;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Art.Panel;
            ForeColor = Art.Text;
            Font = new Font("Segoe UI", 9.5f);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(Px(14));

            FlowLayoutPanel root = new FlowLayoutPanel();
            root.FlowDirection = FlowDirection.TopDown;
            root.WrapContents = false;
            root.AutoSize = true;

            root.Controls.Add(Heading("Speed profiles"));
            root.Controls.Add(Hint("Left-click the tray icon to step through them. Speed 10 is the Windows default."));

            grid = new TableLayoutPanel();
            grid.AutoSize = true;
            grid.ColumnCount = 6;
            root.Controls.Add(grid);

            addBtn = MakeButton("+ Add profile");
            addBtn.Click += delegate { AddRow(new Profile("Profile " + (rows.Count + 1), 10, false, 3)); Rebuild(); };
            root.Controls.Add(addBtn);

            root.Controls.Add(Heading("Mouse buttons"));
            root.Controls.Add(Hint("A button set to anything but Normal stops doing its usual job (Back and Forward in browsers)."));
            TableLayoutPanel bt = new TableLayoutPanel();
            bt.AutoSize = true;
            bt.ColumnCount = 2;
            string[] labels = { "Back button (side, rear)", "Forward button (side, front)", "Middle button (wheel click)" };
            for (int i = 0; i < 3; i++)
            {
                Label l = new Label();
                l.Text = labels[i];
                l.AutoSize = true;
                l.Anchor = AnchorStyles.Left;
                l.Margin = new Padding(0, Px(4), Px(12), Px(4));
                ComboBox cb = new ComboBox();
                cb.DropDownStyle = ComboBoxStyle.DropDownList;
                cb.Width = Px(230);
                cb.BackColor = Art.Field;
                cb.ForeColor = Art.Text;
                cb.FlatStyle = FlatStyle.Flat;
                combos[i] = cb;
                comboCodes[i] = new List<string>();
                bt.Controls.Add(l, 0, i);
                bt.Controls.Add(cb, 1, i);
            }
            root.Controls.Add(bt);

            root.Controls.Add(Heading("Options"));
            chkPopup = MakeCheck("Show the profile name when it changes", e.S.showPopup == true);
            chkStart = MakeCheck("Start with Windows", Startup.IsOn());
            root.Controls.Add(chkPopup);
            root.Controls.Add(chkStart);

            FlowLayoutPanel actions = new FlowLayoutPanel();
            actions.AutoSize = true;
            actions.FlowDirection = FlowDirection.LeftToRight;
            actions.Margin = new Padding(0, Px(14), 0, 0);
            Button save = MakeButton("Save");
            Button cancel = MakeButton("Cancel");
            save.Click += delegate { DoSave(); };
            cancel.Click += delegate { Close(); };
            actions.Controls.Add(save);
            actions.Controls.Add(cancel);
            root.Controls.Add(actions);
            AcceptButton = save;
            CancelButton = cancel;

            Controls.Add(root);

            foreach (Profile p in e.S.profiles) AddRow(p);
            Rebuild();
            for (int i = 0; i < 3; i++) Select(i, e.S.buttons.Get(Settings.ButtonNames[i]));
            Load += delegate { CenterToScreen(); };   // centre after auto-size has measured the window
        }

        Label Heading(string t)
        {
            Label l = new Label();
            l.Text = t;
            l.AutoSize = true;
            l.ForeColor = Art.Gold;
            l.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            l.Margin = new Padding(0, Px(10), 0, Px(2));
            return l;
        }

        Label Hint(string t)
        {
            Label l = new Label();
            l.Text = t;
            l.AutoSize = true;
            l.MaximumSize = new Size(Px(560), 0);
            l.ForeColor = Art.Dim;
            l.Margin = new Padding(0, 0, 0, Px(6));
            return l;
        }

        Button MakeButton(string t)
        {
            Button b = new Button();
            b.Text = t;
            b.AutoSize = true;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Art.Gold;
            b.BackColor = Art.Ink;
            b.ForeColor = Art.Gold;
            b.Padding = new Padding(Px(6), Px(2), Px(6), Px(2));
            b.Margin = new Padding(0, Px(4), Px(8), Px(4));
            return b;
        }

        CheckBox MakeCheck(string t, bool on)
        {
            CheckBox c = new CheckBox();
            c.Text = t;
            c.AutoSize = true;
            c.Checked = on;
            c.ForeColor = Art.Text;
            return c;
        }

        void AddRow(Profile p)
        {
            Row r = new Row();
            r.name = new TextBox();
            r.name.Text = p.name;
            r.name.MaxLength = 24;
            r.name.Width = Px(110);
            r.name.BackColor = Art.Field;
            r.name.ForeColor = Art.Text;
            r.name.BorderStyle = BorderStyle.FixedSingle;
            r.name.TextChanged += delegate { RefreshCombos(); };

            r.speed = new TrackBar();
            r.speed.Minimum = 1;
            r.speed.Maximum = 20;
            r.speed.TickFrequency = 1;
            r.speed.Value = p.speed;
            r.speed.Width = Px(180);
            r.speed.BackColor = Art.Panel;

            r.value = new Label();
            r.value.AutoSize = true;
            r.value.ForeColor = Art.Gold;
            r.value.Anchor = AnchorStyles.Left;
            r.value.Text = p.speed.ToString();
            Row rr = r;
            r.speed.ValueChanged += delegate { rr.value.Text = rr.speed.Value.ToString(); };

            r.accel = MakeCheck("Acceleration", p.precision);
            r.accel.Anchor = AnchorStyles.Left;

            r.lines = new NumericUpDown();
            r.lines.Minimum = 1;
            r.lines.Maximum = 100;
            r.lines.Value = p.scrollLines;
            r.lines.Width = Px(56);
            r.lines.BackColor = Art.Field;
            r.lines.ForeColor = Art.Text;

            r.remove = MakeButton("Remove");
            r.remove.Click += delegate
            {
                if (rows.Count <= 1) return;
                rows.Remove(rr);
                Rebuild();
            };
            rows.Add(r);
        }

        Label Head(string t)
        {
            Label l = new Label();
            l.Text = t;
            l.AutoSize = true;
            l.ForeColor = Art.Dim;
            return l;
        }

        void Rebuild()
        {
            grid.SuspendLayout();
            grid.Controls.Clear();
            grid.RowCount = rows.Count + 1;
            grid.Controls.Add(Head("Name"), 0, 0);
            grid.Controls.Add(Head("Pointer speed"), 1, 0);
            grid.Controls.Add(Head(""), 2, 0);
            grid.Controls.Add(Head(""), 3, 0);
            grid.Controls.Add(Head("Scroll lines"), 4, 0);
            for (int i = 0; i < rows.Count; i++)
            {
                Row r = rows[i];
                r.name.Anchor = AnchorStyles.Left;
                r.lines.Anchor = AnchorStyles.Left;
                grid.Controls.Add(r.name, 0, i + 1);
                grid.Controls.Add(r.speed, 1, i + 1);
                grid.Controls.Add(r.value, 2, i + 1);
                grid.Controls.Add(r.accel, 3, i + 1);
                grid.Controls.Add(r.lines, 4, i + 1);
                grid.Controls.Add(r.remove, 5, i + 1);
                r.remove.Enabled = rows.Count > 1;
            }
            grid.ResumeLayout();
            addBtn.Enabled = rows.Count < Settings.MaxProfiles;
            RefreshCombos();
        }

        void RefreshCombos()
        {
            for (int i = 0; i < 3; i++)
            {
                ComboBox cb = combos[i];
                if (cb == null) return;
                string keep = cb.SelectedIndex >= 0 && cb.SelectedIndex < comboCodes[i].Count ? comboCodes[i][cb.SelectedIndex] : null;
                cb.BeginUpdate();
                cb.Items.Clear();
                comboCodes[i].Clear();
                string first = rows.Count > 0 ? rows[0].name.Text.Trim() : "first";
                AddItem(i, "Normal (no change)", "pass");
                AddItem(i, "Next profile", "next");
                AddItem(i, "Previous profile", "prev");
                AddItem(i, "Hold for " + (first.Length > 0 ? first : "profile 1"), "holdSlow");
                for (int p = 0; p < rows.Count; p++)
                {
                    string n = rows[p].name.Text.Trim();
                    AddItem(i, "Go to " + (p + 1) + (n.Length > 0 ? " (" + n + ")" : ""), "profile:" + (p + 1));
                }
                cb.EndUpdate();
                if (keep != null) Select(i, keep);
            }
        }

        void AddItem(int i, string label, string code) { combos[i].Items.Add(label); comboCodes[i].Add(code); }

        void Select(int i, string code)
        {
            int idx = comboCodes[i].IndexOf(code ?? "pass");
            combos[i].SelectedIndex = idx >= 0 ? idx : 0;
        }

        void DoSave()
        {
            Settings ns = new Settings();
            ns.profiles = new List<Profile>();
            foreach (Row r in rows)
                ns.profiles.Add(new Profile(r.name.Text, r.speed.Value, r.accel.Checked, (int)r.lines.Value));
            ns.active = Math.Min(engine.S.active, rows.Count - 1);
            ns.buttons = new ButtonMap();
            for (int i = 0; i < 3; i++)
            {
                string code = combos[i].SelectedIndex >= 0 ? comboCodes[i][combos[i].SelectedIndex] : "pass";
                ns.buttons.Set(Settings.ButtonNames[i], code);
            }
            ns.showPopup = chkPopup.Checked;
            ns.startWithWindows = chkStart.Checked;
            try { Startup.Set(chkStart.Checked); } catch { }
            engine.Replace(ns);
            Close();
        }
    }

    // ---------------------------------------------------------------- start with Windows

    static class Startup
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "Mouse Gears";

        public static bool IsOn()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Key))
                return k != null && k.GetValue(Name) is string;
        }

        public static void Set(bool on)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Key))
            {
                if (on) k.SetValue(Name, "\"" + Application.ExecutablePath + "\"");
                else k.DeleteValue(Name, false);
            }
        }
    }

    // ---------------------------------------------------------------- tray app

    class TrayApp : ApplicationContext
    {
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        readonly Engine engine;
        readonly NotifyIcon tray;
        readonly MouseHook hook;
        readonly Popup popup;
        readonly Control invoker;
        readonly Icon appIcon;
        IntPtr iconHandle = IntPtr.Zero;
        SettingsForm form;
        bool quitting;

        public TrayApp(string dir)
        {
            invoker = new Control();
            IntPtr force = invoker.Handle;   // the hook posts actions through this handle

            engine = new Engine(dir);
            engine.Load();
            engine.Changed += OnChanged;

            using (Bitmap b = Art.Gear(64, null, false)) { IntPtr h = b.GetHicon(); appIcon = (Icon)Icon.FromHandle(h).Clone(); DestroyIcon(h); }

            popup = new Popup();
            tray = new NotifyIcon();
            tray.ContextMenuStrip = new ContextMenuStrip();
            tray.ContextMenuStrip.Opening += delegate { BuildMenu(); };
            BuildMenu();
            tray.MouseClick += (s, a) => { if (a.Button == MouseButtons.Left) engine.Next(); };

            hook = new MouseHook(engine, invoker);
            bool hooked = hook.Install();

            engine.ApplyCurrent();
            UpdateIcon();
            tray.Visible = true;

            if (!hooked)
                tray.ShowBalloonTip(5000, "Mouse Gears", "Mouse buttons could not be watched, so only the tray icon will switch profiles.", ToolTipIcon.Warning);
            else if (engine.LoadNote != null)
                tray.ShowBalloonTip(6000, "Mouse Gears", engine.LoadNote, ToolTipIcon.Warning);

            SystemEvents.PowerModeChanged += OnPower;
            SystemEvents.SessionSwitch += OnSession;
            Application.ApplicationExit += delegate { Quit(); };

            if (ShowSignal != null)
            {
                Thread t = new Thread(delegate ()
                {
                    while (true)
                    {
                        ShowSignal.WaitOne();
                        if (quitting) return;
                        try { invoker.BeginInvoke((Action)ShowSettings); } catch { return; }
                    }
                });
                t.IsBackground = true;
                t.Start();
            }
        }

        public static EventWaitHandle ShowSignal;

        void OnPower(object s, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Resume) engine.ApplyCurrent(); }
        void OnSession(object s, SessionSwitchEventArgs e) { if (e.Reason == SessionSwitchReason.SessionUnlock) engine.ApplyCurrent(); }

        void OnChanged(string text)
        {
            UpdateIcon();
            if (text != null && engine.S.showPopup == true) popup.ShowText(text);
        }

        void UpdateIcon()
        {
            int size = Math.Max(16, SystemInformation.SmallIconSize.Width);
            using (Bitmap b = Art.Gear(size * 2, engine.Paused ? null : (engine.S.active + 1).ToString(), engine.Paused))
            using (Bitmap small = new Bitmap(b, size, size))
            {
                IntPtr h = small.GetHicon();
                tray.Icon = Icon.FromHandle(h);
                if (iconHandle != IntPtr.Zero) DestroyIcon(iconHandle);
                iconHandle = h;
            }
            string t = "Mouse Gears: " + (engine.Paused ? "paused" : engine.Active.name);
            tray.Text = t.Length > 63 ? t.Substring(0, 63) : t;
        }

        void BuildMenu()
        {
            ContextMenuStrip m = tray.ContextMenuStrip;
            m.Items.Clear();
            for (int i = 0; i < engine.S.profiles.Count; i++)
            {
                int idx = i;
                ToolStripMenuItem it = new ToolStripMenuItem((i + 1) + "   " + engine.S.profiles[i].name);
                it.Checked = i == engine.S.active && !engine.Paused;
                it.Click += delegate { engine.SetActive(idx, true, true); };
                m.Items.Add(it);
            }
            m.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem settings = new ToolStripMenuItem("Settings...");
            settings.Font = new Font(settings.Font, FontStyle.Bold);
            settings.Click += delegate { ShowSettings(); };
            m.Items.Add(settings);
            ToolStripMenuItem pause = new ToolStripMenuItem("Pause (use Windows settings)");
            pause.Checked = engine.Paused;
            pause.Click += delegate { engine.SetPaused(!engine.Paused); };
            m.Items.Add(pause);
            ToolStripMenuItem start = new ToolStripMenuItem("Start with Windows");
            start.Checked = Startup.IsOn();
            start.Click += delegate
            {
                bool on = !Startup.IsOn();
                try { Startup.Set(on); engine.S.startWithWindows = on; engine.Save(); } catch { }
            };
            m.Items.Add(start);
            m.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem quit = new ToolStripMenuItem("Quit");
            quit.Click += delegate { Quit(); };
            m.Items.Add(quit);
        }

        void ShowSettings()
        {
            if (form != null && !form.IsDisposed) { form.Activate(); return; }
            form = new SettingsForm(engine, appIcon);
            form.TopMost = true;          // a tray app can't take focus, so lift it above other windows once
            form.Show();
            form.Activate();
            form.TopMost = false;
        }

        void Quit()
        {
            if (quitting) return;
            quitting = true;
            hook.Dispose();
            engine.Shutdown();
            SystemEvents.PowerModeChanged -= OnPower;
            SystemEvents.SessionSwitch -= OnSession;
            tray.Visible = false;
            tray.Dispose();
            if (iconHandle != IntPtr.Zero) DestroyIcon(iconHandle);
            ExitThread();
        }
    }

    // ---------------------------------------------------------------- self-test

    static class SelfTest
    {
        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)]
        struct INPUT { public uint type; public MOUSEINPUT mi; }
        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint n, INPUT[] inputs, int size);

        static StringBuilder log;
        static int fails;

        static void Check(string name, bool ok, string detail)
        {
            log.AppendLine((ok ? "PASS  " : "FAIL  ") + name + (detail != null ? "   [" + detail + "]" : ""));
            if (!ok) fails++;
        }

        static void SendX(int button, bool down)
        {
            INPUT[] i = new INPUT[1];
            i[0].type = 0;
            i[0].mi.mouseData = (uint)button;
            i[0].mi.dwFlags = down ? 0x0080u : 0x0100u;
            SendInput(1, i, Marshal.SizeOf(typeof(INPUT)));
        }

        static void Pump(int ms)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(5); }
        }

        public static int Run(string outFile)
        {
            log = new StringBuilder();
            fails = 0;
            string dir = Path.Combine(Path.GetTempPath(), "MouseGears-selftest-" + Guid.NewGuid().ToString("N"));
            MouseState before = WinMouse.Read();
            log.AppendLine("live before:   " + before);
            log.AppendLine("saved Windows: " + WinMouse.ReadSaved());
            MouseHook hook = null;
            try
            {
                Engine e = new Engine(dir);
                e.Sync = true;
                e.Load();
                Check("fresh start writes settings.json", File.Exists(e.FilePath), null);
                Check("defaults: 3 profiles, every button Normal", e.S.profiles.Count == 3 &&
                      e.S.buttons.Back == "pass" && e.S.buttons.Forward == "pass" && e.S.buttons.Middle == "pass", null);
                Check("first-run Normal = your saved Windows settings",
                      WinMouse.FromProfile(e.S.profiles[1]).Same(WinMouse.ReadSaved()) || WinMouse.ReadSaved().mouse[2] != 0 &&
                      e.S.profiles[1].speed == WinMouse.ReadSaved().speed && e.S.profiles[1].precision &&
                      e.S.profiles[1].scrollLines == WinMouse.ReadSaved().lines,
                      "Normal: speed " + e.S.profiles[1].speed + ", accel " + e.S.profiles[1].precision + ", scroll " + e.S.profiles[1].scrollLines);

                for (int i = 0; i < e.S.profiles.Count; i++)
                {
                    e.SetActive(i, false, false);
                    MouseState want = WinMouse.FromProfile(e.S.profiles[i]), got = WinMouse.Read();
                    Check("apply profile " + (i + 1) + " (" + e.S.profiles[i].name + ")", got.Same(want), "want " + want + " / got " + got);
                }
                Profile accel = new Profile("Accel", 12, true, 7);
                WinMouse.Write(WinMouse.FromProfile(accel));
                Check("acceleration on + odd scroll applies", WinMouse.Read().Same(WinMouse.FromProfile(accel)), WinMouse.Read().ToString());

                e.SetPaused(true);
                Check("Pause puts saved Windows settings back", WinMouse.Read().Same(WinMouse.ReadSaved()), WinMouse.Read().ToString());
                e.SetActive(1, false, false);
                Check("picking a profile un-pauses", !e.Paused && WinMouse.Read().Same(WinMouse.FromProfile(e.S.profiles[1])), null);

                e.S.profiles[1].name = "Norm \"quoted\" é";
                e.S.buttons.Forward = "profile:3";
                e.S.active = 2;
                e.Save();
                Engine e2 = new Engine(dir); e2.Load();
                Check("save and reload round trip", e2.LoadNote == null && e2.S.profiles[1].name == e.S.profiles[1].name &&
                      e2.S.buttons.Forward == "profile:3" && e2.S.active == 2, e2.LoadNote);

                File.WriteAllText(e.FilePath, "{ this is not json");
                Engine e3 = new Engine(dir); e3.Load();
                Check("broken file -> defaults + .bak kept", e3.LoadNote != null && e3.S.profiles.Count == 3 &&
                      File.Exists(e.FilePath + ".bak") && File.ReadAllText(e.FilePath + ".bak").StartsWith("{ this"), null);

                File.WriteAllText(e.FilePath, "{\"active\":99,\"profiles\":[{\"name\":\"\",\"speed\":50,\"precision\":true,\"scrollLines\":0}],\"buttons\":{\"Back\":\"explode\",\"Middle\":\"profile:4\"}}");
                Engine e4 = new Engine(dir); e4.Load();
                Profile p4 = e4.S.profiles[0];
                Check("bad values clamped", e4.LoadNote == null && p4.speed == 20 && p4.scrollLines == 1 && p4.name == "Profile 1" &&
                      e4.S.active == 0 && e4.S.buttons.Back == "pass" && e4.S.buttons.Middle == "pass" && e4.S.showPopup == true,
                      "speed " + p4.speed + " lines " + p4.scrollLines + " back " + e4.S.buttons.Back);

                bool d;
                Check("route Back down", MouseHook.ButtonOf(0x020B, 1u << 16, out d) == "Back" && d, null);
                Check("route Forward up", MouseHook.ButtonOf(0x020C, 2u << 16, out d) == "Forward" && !d, null);
                Check("route Middle down", MouseHook.ButtonOf(0x0207, 0, out d) == "Middle" && d, null);
                Check("left click ignored", MouseHook.ButtonOf(0x0201, 0, out d) == null, null);

                Control inv = new Control();
                IntPtr force = inv.Handle;
                e.S.buttons.Back = "next";
                e.S.buttons.Forward = "holdSlow";
                e.SetActive(0, false, false);
                hook = new MouseHook(e, inv);
                Check("mouse hook installs", hook.Install(), null);

                SendX(1, true); SendX(1, false); Pump(400);
                Check("real Back press -> next profile, press swallowed", e.S.active == 1 && hook.Swallowed == 2,
                      "active " + e.S.active + ", swallowed " + hook.Swallowed);
                Check("live speed follows the button", WinMouse.Read().Same(WinMouse.FromProfile(e.S.profiles[1])), WinMouse.Read().ToString());

                e.SetActive(2, false, false);
                SendX(2, true); Pump(300);
                bool held = e.S.active == 0;
                SendX(2, false); Pump(300);
                Check("hold Forward -> profile 1 while held, back after", held && e.S.active == 2, "held " + held + ", after " + e.S.active);

                e.SetPaused(true);
                Check("paused -> every button Normal", e.ActionFor("Back") == "pass" && e.ActionFor("Forward") == "pass", null);
            }
            catch (Exception ex)
            {
                Check("no exceptions", false, ex.ToString());
            }
            finally
            {
                if (hook != null) hook.Dispose();
                WinMouse.Write(before);
                MouseState after = WinMouse.Read();
                Check("your live mouse settings put back", after.Same(before), after.ToString());
                try { Directory.Delete(dir, true); } catch { }
            }
            log.AppendLine(fails == 0 ? "ALL PASS" : fails + " FAILED");
            File.WriteAllText(outFile, log.ToString());
            return fails;
        }
    }

    // ---------------------------------------------------------------- entry

    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static int Main(string[] args)
        {
            string config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mouse Gears");
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--write-icon" && i + 1 < args.Length) { Art.WriteIco(args[i + 1]); return 0; }
                if (args[i] == "--selftest" && i + 1 < args.Length) return SelfTest.Run(args[i + 1]);
                if (args[i] == "--config" && i + 1 < args.Length) config = args[++i];
            }

            SetProcessDPIAware();
            bool created;
            using (EventWaitHandle show = new EventWaitHandle(false, EventResetMode.AutoReset, "MouseGears.ShowSettings"))
            using (Mutex m = new Mutex(true, "MouseGears.SingleInstance.v1", out created))
            {
                if (!created) { show.Set(); return 0; }   // already running: ask it to open Settings
                TrayApp.ShowSignal = show;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, a) =>
                {
                    try { WinMouse.Write(WinMouse.ReadSaved()); } catch { }
                    MessageBox.Show("Mouse Gears hit an error and has put your Windows mouse settings back.\n\n" + a.Exception.Message,
                                    "Mouse Gears", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    Application.Exit();
                };
                Application.Run(new TrayApp(config));
            }
            return 0;
        }
    }
}
