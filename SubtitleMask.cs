using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace SubtitleMask
{
    public static class Native
    {
        [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr h, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    }

    public class Preferences
    {
        public int X, Y, Width = 900, Height = 120;
        public bool HasPosition, Locked;
        public int ColorArgb = Color.Black.ToArgb();
        public static Preferences Load(string path)
        {
            try { using (var stream = File.OpenRead(path)) return (Preferences)new XmlSerializer(typeof(Preferences)).Deserialize(stream); }
            catch { return new Preferences(); }
        }
        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            using (var stream = File.Create(tmp)) new XmlSerializer(typeof(Preferences)).Serialize(stream, this);
            if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
        }
    }

    public sealed class MaskForm : Form
    {
        private bool locked, dragging;
        private int edges;
        private Point dragStart;
        private Rectangle original;
        public event Action GeometryChanged;
        public bool Locked
        {
            get { return locked; }
            set { locked = value; if (IsHandleCreated) RecreateHandle(); }
        }
        public MaskForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.Black;
            MinimumSize = new Size(120, 24);
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            Opacity = 0.999;
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x00000080; if (locked) cp.ExStyle |= 0x00080000 | 0x00000020; return cp; }
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0021) { m.Result = new IntPtr(3); return; }
            base.WndProc(ref m);
        }
        private int Edges(Point p)
        {
            return (p.X < 10 ? 1 : p.X >= ClientSize.Width - 10 ? 2 : 0) |
                   (p.Y < 10 ? 4 : p.Y >= ClientSize.Height - 10 ? 8 : 0);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (locked || e.Button != MouseButtons.Left) return;
            dragging = true; edges = Edges(e.Location); original = Bounds; dragStart = Cursor.Position; Capture = true;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (locked) return;
            if (!dragging)
            {
                int b = Edges(e.Location);
                Cursor = b == 5 || b == 10 ? Cursors.SizeNWSE : b == 6 || b == 9 ? Cursors.SizeNESW :
                    (b & 3) != 0 ? Cursors.SizeWE : (b & 12) != 0 ? Cursors.SizeNS : Cursors.SizeAll;
                return;
            }
            int dx = Cursor.Position.X - dragStart.X, dy = Cursor.Position.Y - dragStart.Y;
            Rectangle r = original;
            if (edges == 0) r.Offset(dx, dy);
            else
            {
                int left = r.Left, right = r.Right, top = r.Top, bottom = r.Bottom;
                if ((edges & 1) != 0) left = Math.Min(right - 120, left + dx);
                if ((edges & 2) != 0) right = Math.Max(left + 120, right + dx);
                if ((edges & 4) != 0) top = Math.Min(bottom - 24, top + dy);
                if ((edges & 8) != 0) bottom = Math.Max(top + 24, bottom + dy);
                r = Rectangle.FromLTRB(left, top, right, bottom);
            }
            Bounds = r;
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!dragging) return;
            dragging = false; Capture = false;
            if (GeometryChanged != null) GeometryChanged();
        }
    }

    public sealed class ControlForm : Form
    {
        private readonly MaskForm mask = new MaskForm();
        private readonly Preferences prefs;
        private readonly string config;
        private readonly bool testMode;
        private readonly NotifyIcon tray = new NotifyIcon();
        private bool shortcutActive, pauseRegistered, closing, synchronizing;
        private Button toggle, pause;
        private Label state, shortcutState;
        private CheckBox lockBox;
        private NumericUpDown widthBox, heightBox;
        private ToolStripMenuItem trayToggle, trayPause, trayLock;
        private readonly Color ink = Color.FromArgb(231, 237, 248), muted = Color.FromArgb(158, 171, 194);
        public ControlForm(string settingsPath, bool testing)
        {
            config = settingsPath; testMode = testing; prefs = Preferences.Load(config);
            Text = "字幕遮挡 · Subtitle Mask";
            Font = new Font("Microsoft YaHei UI", 10F);
            BackColor = Color.FromArgb(19, 25, 37); ForeColor = ink;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(560, 596);
            FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Icon = SystemIcons.Application;
            BuildUI(); BuildMenu();
            mask.BackColor = Color.FromArgb(255, Color.FromArgb(prefs.ColorArgb));
            Rectangle desired = prefs.HasPosition ? new Rectangle(prefs.X, prefs.Y, Math.Max(120, prefs.Width), Math.Max(24, prefs.Height)) : DefaultBounds(Screen.PrimaryScreen);
            mask.Bounds = FitToScreen(desired);
            mask.Locked = prefs.Locked; lockBox.Checked = prefs.Locked;
            mask.GeometryChanged += delegate { SyncDimensions(); SavePreferences(); };
            Resize += delegate { if (WindowState == FormWindowState.Minimized) Hide(); };
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplayChanged;
            SyncDimensions();
        }
        private Label TextAt(string text, int x, int y, int w, int h, float size, Color color)
        {
            var label = new Label { Text = text, Location = new Point(x, y), Size = new Size(w, h), ForeColor = color, Font = new Font(Font.FontFamily, size), BackColor = Color.Transparent };
            Controls.Add(label); return label;
        }
        private Button ButtonAt(string text, int x, int y, int w, EventHandler action, bool primary)
        {
            var b = new Button { Text = text, Location = new Point(x, y), Size = new Size(w, 42), FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Color.FromArgb(104, 221, 185) : Color.FromArgb(36, 46, 64), ForeColor = primary ? Color.FromArgb(16, 36, 31) : ink, Cursor = Cursors.Hand };
            b.FlatAppearance.BorderSize = 0; b.Click += action; Controls.Add(b); return b;
        }
        private void BuildUI()
        {
            TextAt("SUBTITLE MASK", 28, 22, 300, 22, 9, Color.FromArgb(104, 221, 185));
            TextAt("让字幕，按需出现。", 26, 55, 506, 47, 25, ink);
            TextAt("先听懂，再按 C 看答案。", 28, 111, 500, 28, 11, muted);
            state = TextAt("", 28, 157, 504, 29, 13, ink);
            toggle = ButtonAt("", 28, 199, 504, delegate { ToggleMask(); }, true);
            TextAt("遮挡区域", 28, 267, 140, 25, 11, ink);
            TextAt("拖动黑条移动位置 · 拖动边缘调整大小", 28, 297, 504, 26, 10, muted);
            TextAt("宽", 28, 338, 30, 28, 10, muted);
            widthBox = DimensionBox(62, 334, 120, 30000);
            TextAt("高", 215, 338, 30, 28, 10, muted);
            heightBox = DimensionBox(249, 334, 24, 30000);
            ButtonAt("重置位置", 392, 330, 140, delegate { mask.Bounds = DefaultBounds(Screen.FromPoint(Cursor.Position)); SyncDimensions(); SavePreferences(); }, false);
            lockBox = new CheckBox { Text = "锁定位置（鼠标可穿透遮挡条）", Location = new Point(28, 390), Size = new Size(365, 30), ForeColor = ink };
            lockBox.CheckedChanged += delegate { mask.Locked = lockBox.Checked; UpdateStatus(); SavePreferences(); };
            Controls.Add(lockBox);
            ButtonAt("遮挡颜色", 392, 384, 140, delegate
            {
                using (var dialog = new ColorDialog { Color = mask.BackColor, FullOpen = true })
                    if (dialog.ShowDialog(this) == DialogResult.OK) { mask.BackColor = dialog.Color; SavePreferences(); }
            }, false);
            shortcutState = TextAt("", 28, 441, 504, 26, 10, muted);
            pause = ButtonAt("", 28, 482, 244, delegate { SetShortcut(!shortcutActive); }, false);
            ButtonAt("收起到托盘", 288, 482, 244, delegate { Hide(); }, false);
            TextAt("C 为全局热键，启用时不能输入字母 C。关闭窗口退出程序。", 28, 548, 508, 32, 9, muted);
        }
        private NumericUpDown DimensionBox(int x, int y, int min, int max)
        {
            var box = new NumericUpDown { Location = new Point(x, y), Size = new Size(125, 30), Minimum = min, Maximum = max, Value = min,
                BackColor = Color.FromArgb(36, 46, 64), ForeColor = ink, BorderStyle = BorderStyle.FixedSingle, Increment = 10 };
            box.ValueChanged += delegate
            {
                if (synchronizing || widthBox == null || heightBox == null) return;
                mask.Size = new Size((int)widthBox.Value, (int)heightBox.Value); SavePreferences();
            };
            Controls.Add(box); return box;
        }
        private void BuildMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("打开控制面板", null, delegate { ShowPanel(); });
            trayToggle = (ToolStripMenuItem)menu.Items.Add("切换字幕", null, delegate { ToggleMask(); });
            trayLock = (ToolStripMenuItem)menu.Items.Add("锁定位置", null, delegate { lockBox.Checked = !lockBox.Checked; });
            trayPause = (ToolStripMenuItem)menu.Items.Add("暂停 C 热键", null, delegate { SetShortcut(!shortcutActive); });
            menu.Items.Add("重置遮挡位置", null, delegate { mask.Bounds = DefaultBounds(Screen.FromPoint(Cursor.Position)); SyncDimensions(); SavePreferences(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { Close(); });
            mask.ContextMenuStrip = menu;
            tray.Icon = SystemIcons.Application; tray.Text = "字幕遮挡 · C 切换"; tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { ShowPanel(); };
        }
        private void ShowPanel() { Show(); WindowState = FormWindowState.Normal; Activate(); }
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            mask.Show(); tray.Visible = !testMode;
            if (!testMode) pauseRegistered = Native.RegisterHotKey(Handle, 2, 0x4003, (uint)Keys.C);
            SetShortcut(true);
        }
        private void SetShortcut(bool enabled)
        {
            if (shortcutActive && !testMode) Native.UnregisterHotKey(Handle, 1);
            shortcutActive = false;
            if (enabled) shortcutActive = testMode || Native.RegisterHotKey(Handle, 1, 0x4000, (uint)Keys.C);
            UpdateStatus();
            if (enabled && !shortcutActive) shortcutState.Text = "C 热键被其他程序占用。可用按钮切换，或稍后重试。";
        }
        private void ToggleMask()
        {
            if (mask.Visible) mask.Hide(); else mask.Show();
            UpdateStatus();
        }
        private void UpdateStatus()
        {
            if (state == null || trayToggle == null) return;
            state.Text = mask.Visible ? "●  字幕已遮挡" : "○  字幕已显示（遮挡条已隐藏）";
            toggle.Text = mask.Visible ? "显示字幕    C" : "遮挡字幕    C";
            pause.Text = shortcutActive ? "暂停 C 热键" : "启用 C 热键";
            shortcutState.Text = shortcutActive ? (pauseRegistered || testMode ? "全局 C 热键已启用 · Ctrl + Alt + C 暂停 / 恢复" : "全局 C 热键已启用 · 可通过按钮或托盘暂停") : "C 热键已暂停，可以正常打字。";
            trayToggle.Text = mask.Visible ? "显示字幕" : "遮挡字幕";
            trayPause.Text = pause.Text; trayLock.Checked = mask.Locked;
            tray.Text = mask.Visible ? "字幕遮挡 · 当前已遮挡" : "字幕遮挡 · 当前已显示";
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312)
            {
                if (m.WParam.ToInt32() == 1 && shortcutActive) ToggleMask();
                else if (m.WParam.ToInt32() == 2) SetShortcut(!shortcutActive);
                return;
            }
            base.WndProc(ref m);
        }
        private void SyncDimensions()
        {
            synchronizing = true;
            widthBox.Value = Math.Max(widthBox.Minimum, Math.Min(widthBox.Maximum, mask.Width));
            heightBox.Value = Math.Max(heightBox.Minimum, Math.Min(heightBox.Maximum, mask.Height));
            synchronizing = false;
        }
        public static Rectangle DefaultBounds(Screen screen)
        {
            Rectangle r = screen.Bounds;
            int w = Math.Min(1000, (int)(r.Width * 0.78)), h = Math.Min(130, Math.Max(60, r.Height / 9));
            return new Rectangle(r.Left + (r.Width - w) / 2, r.Bottom - h - Math.Max(45, r.Height / 15), w, h);
        }
        public static Rectangle FitToScreen(Rectangle desired)
        {
            Rectangle s = Screen.FromRectangle(desired).Bounds;
            int w = Math.Min(s.Width, Math.Max(120, desired.Width)), h = Math.Min(s.Height, Math.Max(24, desired.Height));
            return new Rectangle(Math.Max(s.Left, Math.Min(desired.X, s.Right - w)), Math.Max(s.Top, Math.Min(desired.Y, s.Bottom - h)), w, h);
        }
        private void DisplayChanged(object sender, EventArgs e)
        {
            if (!closing && IsHandleCreated) BeginInvoke((Action)delegate { mask.Bounds = FitToScreen(mask.Bounds); SyncDimensions(); SavePreferences(); });
        }
        private void SavePreferences()
        {
            if (testMode) return;
            prefs.X = mask.Left; prefs.Y = mask.Top; prefs.Width = mask.Width; prefs.Height = mask.Height;
            prefs.HasPosition = true; prefs.Locked = mask.Locked; prefs.ColorArgb = mask.BackColor.ToArgb();
            try { prefs.Save(config); } catch (Exception ex) { shortcutState.Text = "设置未能保存：" + ex.Message; }
        }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            closing = true; SavePreferences();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplayChanged;
            if (shortcutActive && !testMode) Native.UnregisterHotKey(Handle, 1);
            if (pauseRegistered) Native.UnregisterHotKey(Handle, 2);
            tray.Visible = false; tray.Dispose(); mask.Dispose();
            base.OnFormClosed(e);
        }
        public void RunChecks(string folder)
        {
            Directory.CreateDirectory(folder);
            Show(); Application.DoEvents();
            Check(mask.Visible, "Mask is initially visible");
            Native.SendMessage(Handle, 0x0312, new IntPtr(1), IntPtr.Zero);
            Check(!mask.Visible, "C hotkey hides mask");
            Native.SendMessage(Handle, 0x0312, new IntPtr(1), IntPtr.Zero);
            Check(mask.Visible, "C hotkey restores mask");
            Native.SendMessage(Handle, 0x0312, new IntPtr(2), IntPtr.Zero);
            Native.SendMessage(Handle, 0x0312, new IntPtr(1), IntPtr.Zero);
            Check(mask.Visible && !shortcutActive, "Paused C does not toggle");
            Native.SendMessage(Handle, 0x0312, new IntPtr(2), IntPtr.Zero);
            Check(shortcutActive, "Ctrl Alt C resumes shortcut");
            lockBox.Checked = true; Check(mask.Locked && mask.Visible, "Lock preserves visibility");
            lockBox.Checked = false;
            widthBox.Value = 800; heightBox.Value = 100;
            Check(mask.Size == new Size(800, 100), "Dimensions update mask");
            Rectangle recovered = FitToScreen(new Rectangle(-99999, -99999, 100000, 100000));
            Check(Screen.FromRectangle(recovered).Bounds.Contains(recovered), "Offscreen bounds recover");
            string temp = Path.Combine(folder, "settings-test.xml");
            var p = new Preferences { X = -600, Y = 30, Width = 880, Height = 80, HasPosition = true, Locked = true };
            p.Save(temp); p.Width = 900; p.Save(temp);
            var loaded = Preferences.Load(temp);
            Check(loaded.X == -600 && loaded.Width == 900 && loaded.Locked, "Settings round trip and atomic replacement");
            File.WriteAllText(temp, "invalid"); Check(Preferences.Load(temp).Width == 900, "Invalid settings recover"); File.Delete(temp);
            bool registered = Native.RegisterHotKey(Handle, 7, 0x4000, (uint)Keys.C);
            if (registered) Native.UnregisterHotKey(Handle, 7);
            File.WriteAllText(Path.Combine(folder, "test-result.txt"), "PASS: 10 behavioral checks.\r\nActual OS C hotkey registration: " + registered);
            using (var bitmap = new Bitmap(Width, Height)) { DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size)); bitmap.Save(Path.Combine(folder, "preview.png")); }
            Close();
        }
        private static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); }
    }

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Native.SetProcessDPIAware();
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            bool test = args.Length == 2 && args[0] == "--self-test";
            string config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SubtitleMask", "settings.xml");
            try
            {
                if (test) { using (var form = new ControlForm(config, true)) form.RunChecks(args[1]); return 0; }
                bool first;
                using (var singleton = new System.Threading.Mutex(true, "Local\\SubtitleMask.Desktop.v1", out first))
                {
                    if (!first) { MessageBox.Show("程序已经运行，请在系统托盘中打开字幕遮挡工具。", "字幕遮挡"); return 0; }
                    Application.Run(new ControlForm(config, false));
                }
                return 0;
            }
            catch (Exception ex)
            {
                if (test) { Directory.CreateDirectory(args[1]); File.WriteAllText(Path.Combine(args[1], "test-result.txt"), ex.ToString()); }
                else MessageBox.Show(ex.Message, "字幕遮挡启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}
