using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Retexture
{
    public partial class Form1 : Form
    {
        // Theme colors
        private Color Ink = Color.FromArgb(10, 13, 19);
        private Color Ink2 = Color.FromArgb(15, 19, 27);
        private Color Glass = Color.FromArgb(30, 36, 49);
        private Color Glass2 = Color.FromArgb(36, 43, 58);
        private Color Glass3 = Color.FromArgb(22, 27, 38);
        private Color Field = Color.FromArgb(18, 22, 30);
        private Color White = Color.FromArgb(246, 248, 252);
        private Color Muted = Color.FromArgb(151, 163, 183);
        private Color Dim = Color.FromArgb(111, 122, 141);
        private Color Accent = Color.FromArgb(111, 101, 255);
        private Color Accent2 = Color.FromArgb(92, 196, 255);
        private Color Good = Color.FromArgb(102, 224, 168);
        private Color Warn = Color.FromArgb(255, 186, 92);
        private Color Danger = Color.FromArgb(255, 112, 132);

        // Paths
        private string AppRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Retexture Studio");
        private string LibraryRoot;
        private string BackupRoot;
        private string ScriptRoot;
        private string LogoPath;

        // UI state
        private dynamic CurrentItem;
        private string SelectedPresetPath;

        // Controls
        private FlowLayoutPanel LibraryFlow;
        private PictureBox SelectedPreview;
        private Label SelectedName;
        private Label SelectedInfo;
        private Button ApplyButton;
        private Button RestoreButton;
        private Button AccessButton;
        private Label StatusLabel;
        private Label VersionChip;
        private Label TargetTitle;
        private Label TargetHint;
        private TextBox SearchBox;
        private Label CountLabel;
        private Label Headline;

        private List<dynamic> Textures;
        private string[] SkyFaces = new[] { "bk", "dn", "ft", "lf", "rt", "up" };

        private Dictionary<Control, GlassMeta> glassMetaMap = new Dictionary<Control, GlassMeta>();
        private readonly Dictionary<Control, Color> baseControlColors = new Dictionary<Control, Color>();
        private readonly HashSet<Control> accentControls = new HashSet<Control>();
        private readonly HashSet<Control> applyHighlightControls = new HashSet<Control>();
        private Timer interactionTimer;
        private Panel activeNavigationPanel;
        private Panel pageHost;
        private Control texturePage;
        private Panel myPresetsPage;
        private bool lightTheme;
        private Action<string> refreshMyPresetsView;
        private string myPresetsCurrentFolder;
        private Panel sidebar;

        private class GlassMeta
        {
            public int Radius { get; set; }
            public Color Top { get; set; }
            public Color Bottom { get; set; }
            public Color DarkTop { get; set; }
            public Color DarkBottom { get; set; }
        }

        private class PresetEntry
        {
            public string Name { get; set; }
            public string FullPath { get; set; }
            public bool IsDirectory { get; set; }
            public override string ToString() { return Name; }
        }

        public Form1()
        {
            InitializeComponent();
            // Initialize folders
            LibraryRoot = Path.Combine(AppRoot, "Library");
            BackupRoot = Path.Combine(AppRoot, "Backups");
            Directory.CreateDirectory(LibraryRoot);
            Directory.CreateDirectory(BackupRoot);

            ScriptRoot = AppDomain.CurrentDomain.BaseDirectory;
            LogoPath = Path.Combine(ScriptRoot, "RetextureLogo.png");
            if (!File.Exists(LogoPath)) LogoPath = Path.Combine(ScriptRoot, "RetextureLogo.png.png");

            BuildTextures();
            MigrateLegacyPresetsToZip();
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            this.UpdateStyles();
            BuildUI();
            RefreshTargetView();
            this.Load += (s, e) =>
            {
                foreach (var pair in glassMetaMap.Keys.ToList())
                {
                    SetRoundedRegion(pair, glassMetaMap[pair].Radius);
                    pair.PerformLayout();
                    pair.Invalidate();
                }
                if (LibraryFlow != null)
                {
                    LibraryFlow.PerformLayout();
                    LibraryFlow.AutoScrollPosition = new Point(0, 0);
                }
            };
            this.Load += (s, e) => CheckForUpdatesAsync(true);
        }

        private void BuildTextures()
        {
            Textures = new List<dynamic>
            {
                new { Id = "cursor", Name = "Select Cursor", Icon = ">", Info = "The cursor that appears when you hover over clickable elements.", Size = "64 x 64 px", Target = PathCombine("content","textures","Cursors","KeyboardMouse","ArrowCursor.png"), File = "ArrowCursor.png", Sky=false },
                new { Id = "far", Name = "Floating Cursor", Icon = ">", Info = "The default floating cursor shown in-game.", Size = "64 x 64 px", Target = PathCombine("content","textures","Cursors","KeyboardMouse","ArrowFarCursor.png"), File = "ArrowFarCursor.png", Sky=false },
                new { Id = "emote", Name = "Emote Wheel", Icon = "o", Info = "The segmented circle UI for the emote wheel.", Size = "512 x 512 px", Target = PathCombine("content","textures","ui","Emotes","Large","SegmentedCircle.png"), File = "SegmentedCircle.png", Sky=false },
                new { Id = "skybox", Name = "Skybox", Icon = "*", Info = "Replace all six skybox face textures in-game.", Size = "1024 x 1024 px per face", Target = PathCombine("PlatformContent","pc","textures","sky"), File = "sky512_{face}.tex", Sky=true }
            };
            CurrentItem = Textures[0];
        }

        private static string PathCombine(params string[] parts) => Path.Combine(parts);

        private void MigrateLegacyPresetsToZip()
        {
            try
            {
                foreach (var item in Textures)
                {
                    var library = GetLibrary((string)item.Id);
                    if ((bool)item.Sky)
                    {
                        foreach (var dir in new DirectoryInfo(library).GetDirectories())
                        {
                            var faceFiles = dir.GetFiles().Where(f => IsTexEntry(f.Name)).ToArray();
                            if (faceFiles.Length == 0) continue;
                            var zipPath = Path.Combine(library, dir.Name + ".zip");
                            if (!File.Exists(zipPath))
                            {
                                using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                                {
                                    foreach (var face in faceFiles) zip.CreateEntryFromFile(face.FullName, face.Name);
                                }
                            }
                            Directory.Delete(dir.FullName, true);
                        }
                    }
                    else
                    {
                        foreach (var file in new DirectoryInfo(library).GetFiles().Where(f => IsImageEntry(f.Name)))
                        {
                            var zipPath = Path.Combine(library, Path.GetFileNameWithoutExtension(file.Name) + ".zip");
                            if (!File.Exists(zipPath))
                            {
                                using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                                {
                                    zip.CreateEntryFromFile(file.FullName, file.Name);
                                }
                            }
                            file.Delete();
                        }
                    }
                }
            }
            catch { /* best-effort */ }
        }

        #region Filesystem helpers
        private IEnumerable<DirectoryInfo> GetRobloxVersions()
        {
            var versions = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "Versions");
            if (!Directory.Exists(versions)) yield break;
            var dirs = new DirectoryInfo(versions).GetDirectories().OrderByDescending(d => d.LastWriteTime);
            foreach (var d in dirs) yield return d;
        }

        private IEnumerable<string> GetRobloxTargetPaths()
        {
            foreach (var v in GetRobloxVersions())
            {
                yield return Path.Combine(v.FullName, (string)CurrentItem.Target);
            }
        }

        private string GetLibrary(string id)
        {
            var p = Path.Combine(LibraryRoot, id);
            Directory.CreateDirectory(p);
            return p;
        }

        private string GetBackup(string target)
        {
            var key = Convert.ToBase64String(Encoding.UTF8.GetBytes(target)).Replace('/', '_').Replace('+', '-').TrimEnd('=');
            return Path.Combine(BackupRoot, key + ".bak");
        }

        private bool RobloxRunning()
        {
            try { return System.Diagnostics.Process.GetProcessesByName("RobloxPlayerBeta").Any(); }
            catch { return false; }
        }

        private void AddBackup(string target)
        {
            var backup = GetBackup(target);
            if (!File.Exists(backup)) File.Copy(target, backup, true);
        }

        private Image ReadImageSafe(string path)
        {
            try
            {
                using (var src = Image.FromFile(path))
                {
                    return new Bitmap(src);
                }
            }
            catch { return null; }
        }

        private bool IsImageEntry(string name)
        {
            var extension = Path.GetExtension(name).ToLowerInvariant();
            return extension == ".png" || extension == ".jpg" || extension == ".jpeg" || extension == ".bmp";
        }
        private bool IsTexEntry(string name)
        {
            return Path.GetExtension(name).Equals(".tex", StringComparison.OrdinalIgnoreCase);
        }

        private Image ReadPresetPreview(string zipPath)
        {
            try
            {
                using (var archive = ZipFile.OpenRead(zipPath))
                {
                    var imageEntry = archive.Entries
                        .Where(e => IsImageEntry(e.Name))
                        .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault();
                    if (imageEntry != null)
                    {
                        using (var stream = imageEntry.Open())
                        using (var src = Image.FromStream(stream))
                        {
                            return new Bitmap(src);
                        }
                    }

                    var texEntries = archive.Entries.Where(e => IsTexEntry(e.Name)).ToArray();
                    if (texEntries.Length > 0)
                    {
                        var rng = new Random();
                        var randomFace = texEntries[rng.Next(texEntries.Length)];
                        using (var stream = randomFace.Open())
                        using (var ms = new MemoryStream())
                        {
                            stream.CopyTo(ms);
                            return DecodeTexPreview(ms.ToArray());
                        }
                    }
                    return null;
                }
            }
            catch { return null; }
        }

        private Image DecodeTexPreview(byte[] data)
        {
            try
            {
                const int headerSize = 128;
                if (data.Length <= headerSize) return null;

                int width = BitConverter.ToInt32(data, 16);
                int height = BitConverter.ToInt32(data, 12);
                if (width <= 0 || height <= 0 || width > 8192 || height > 8192) { width = 1024; height = 1024; }

                int expectedBytes = width * height * 4;
                if (data.Length - headerSize < expectedBytes)
                {
                    return null;
                }

                var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                var rect = new Rectangle(0, 0, width, height);
                var bits = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    Marshal.Copy(data, headerSize, bits.Scan0, expectedBytes);
                }
                finally
                {
                    bmp.UnlockBits(bits);
                }
                return bmp;
            }
            catch { return null; }
        }

        private string GetPresetImageExtension(string zipPath)
        {
            try
            {
                using (var archive = ZipFile.OpenRead(zipPath))
                {
                    var entry = archive.Entries
                        .Where(e => IsImageEntry(e.Name))
                        .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault();
                    return entry != null ? Path.GetExtension(entry.Name).TrimStart('.').ToUpper() : "ZIP";
                }
            }
            catch { return "ZIP"; }
        }

        private void ConvertToDds(string source, string destination)
        {
            using (var image = Image.FromFile(source))
            using (var bmp = new Bitmap(1024, 1024, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Black);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(image, 0, 0, 1024, 1024);
                }

                var rect = new Rectangle(0, 0, 1024, 1024);
                var bits = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    int bytes = 1024 * 1024 * 4;
                    var pixels = new byte[bytes];
                    Marshal.Copy(bits.Scan0, pixels, 0, bytes);

                    var header = new byte[128];
                    Encoding.ASCII.GetBytes("DDS ").CopyTo(header, 0);
                    BitConverter.GetBytes((uint)124).CopyTo(header, 4);
                    BitConverter.GetBytes((uint)0x0000100F).CopyTo(header, 8);
                    BitConverter.GetBytes((uint)1024).CopyTo(header, 12);
                    BitConverter.GetBytes((uint)1024).CopyTo(header, 16);
                    BitConverter.GetBytes((uint)4096).CopyTo(header, 20);
                    BitConverter.GetBytes((uint)32).CopyTo(header, 76);
                    BitConverter.GetBytes((uint)0x41).CopyTo(header, 80);
                    BitConverter.GetBytes((uint)32).CopyTo(header, 88);
                    BitConverter.GetBytes((uint)0x00ff0000).CopyTo(header, 92);
                    BitConverter.GetBytes((uint)0x0000ff00).CopyTo(header, 96);
                    BitConverter.GetBytes((uint)0x000000ff).CopyTo(header, 100);
                    BitConverter.GetBytes((uint)0xff000000).CopyTo(header, 104);
                    BitConverter.GetBytes((uint)0x1000).CopyTo(header, 108);

                    using (var stream = File.Open(destination, FileMode.Create, FileAccess.Write))
                    {
                        stream.Write(header, 0, header.Length);
                        stream.Write(pixels, 0, pixels.Length);
                    }
                }
                finally
                {
                    bmp.UnlockBits(bits);
                }
            }
        }
        #endregion

        #region UI helpers
        private void SetRoundedRegion(Control control, int radius)
        {
            if (control == null || control.Width < 2 || control.Height < 2) return;
            var path = new GraphicsPath();
            int d = Math.Min(radius * 2, Math.Min(control.Width - 1, control.Height - 1));
            if (d < 2) { path.Dispose(); return; }
            try
            {
                path.AddArc(0, 0, d, d, 180, 90);
                path.AddArc(control.Width - d - 1, 0, d, d, 270, 90);
                path.AddArc(control.Width - d - 1, control.Height - d - 1, d, d, 0, 90);
                path.AddArc(0, control.Height - d - 1, d, d, 90, 90);
                path.CloseFigure();
                control.Region?.Dispose();
                control.Region = new Region(path);
            }
            finally { path.Dispose(); }
        }

        private Panel AddGlassPanel(Panel panel, int radius = 22, Color? top = null, Color? bottom = null)
        {
            var darkTop = top ?? Glass2;
            var darkBottom = bottom ?? Glass;
            var initialTop = lightTheme ? Color.FromArgb(255, 255, 255) : darkTop;
            var initialBottom = lightTheme ? Color.FromArgb(247, 248, 251) : darkBottom;
            panel.BackColor = initialBottom;
            var metaObj = new GlassMeta
            {
                Radius = radius,
                Top = initialTop,
                Bottom = initialBottom,
                DarkTop = darkTop,
                DarkBottom = darkBottom
            };
            // store meta in a dedicated map
            glassMetaMap[panel] = metaObj;
            panel.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                if (!glassMetaMap.TryGetValue(panel, out var meta)) return;
                var rect = new Rectangle(0, 0, Math.Max(1, panel.Width - 1), Math.Max(1, panel.Height - 1));
                var path = new GraphicsPath();
                int d = Math.Min(meta.Radius * 2, Math.Min(rect.Width, rect.Height));
                if (d >= 2)
                {
                    try
                    {
                        path.AddArc(0, 0, d, d, 180, 90);
                        path.AddArc(rect.Width - d, 0, d, d, 270, 90);
                        path.AddArc(rect.Width - d, rect.Height - d, d, d, 0, 90);
                        path.AddArc(0, rect.Height - d, d, d, 90, 90);
                        path.CloseFigure();
                        using (var brush = new LinearGradientBrush(rect, meta.Top, meta.Bottom, LinearGradientMode.Vertical))
                        {
                            e.Graphics.FillPath(brush, path);
                        }
                    }
                    finally { path.Dispose(); }
                }
            };
            panel.Resize += (s, e) => { SetRoundedRegion(panel, radius); panel.Invalidate(); };
            SetRoundedRegion(panel, radius);
            return panel;
        }

        private Button NewGlassButton(string text, int width = 120, int height = 36, bool accent = false, bool applyHighlight = false, int radius = 13)
        {
            var b = new Button();
            b.Text = text;
            b.Size = new Size(width, height);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.UseVisualStyleBackColor = false;
            var baseFill = accent ? Accent : Glass2;
            b.BackColor = baseFill;
            if (accent) accentControls.Add(b);
            if (applyHighlight) applyHighlightControls.Add(b);
            b.ForeColor = White;
            b.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            b.Cursor = Cursors.Hand;
            b.TabStop = false;
            b.FlatAppearance.MouseOverBackColor = accent ? Color.FromArgb(92, 145, 255) : (applyHighlight ? Accent : Color.FromArgb(39, 45, 60));
            b.FlatAppearance.MouseDownBackColor = accent ? Color.FromArgb(72, 120, 220) : (applyHighlight ? Color.FromArgb(72, 120, 220) : Color.FromArgb(31, 36, 49));
            b.Resize += (s, e) => SetRoundedRegion(b, radius);
            b.MouseEnter += (s, e) => AnimateButton(b, true);
            b.MouseLeave += (s, e) => AnimateButton(b, false);
            SetRoundedRegion(b, radius);
            return b;
        }

        private void OpenCommunity()
        {
            try
            {
                var url = "https://discord.gg/tx6QQvcrWa";
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                catch
                {
                    System.Diagnostics.Process.Start("explorer.exe", url);
                }
            }
            catch (Exception ex)
            {
                SetStatus("Could not open Community: " + ex.Message, Warn);
            }
        }

        private void ShowSettings()
        {
            using (var dialog = new Form())
            {
                dialog.Text = "Settings";
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.FormBorderStyle = FormBorderStyle.None;
                dialog.MinimizeBox = false;
                dialog.MaximizeBox = false;
                dialog.ShowInTaskbar = false;
                dialog.ClientSize = new Size(390, 270);
                dialog.BackColor = lightTheme ? Color.FromArgb(245, 247, 251) : Color.FromArgb(18, 20, 27);
                dialog.ForeColor = lightTheme ? Color.FromArgb(28, 31, 40) : White;
                dialog.Font = new Font("Segoe UI", 9);
                dialog.Padding = new Padding(1);
                dialog.Load += (s, e) => SetRoundedRegion(dialog, 18);
                dialog.Resize += (s, e) => SetRoundedRegion(dialog, 18);
                dialog.Paint += (s, e) =>
                {
                    using (var brush = new SolidBrush(dialog.BackColor)) e.Graphics.FillRectangle(brush, dialog.ClientRectangle);
                };

                var title = new Label { Text = "Appearance", Location = new Point(24, 20), AutoSize = true, Font = new Font("Segoe UI", 14, FontStyle.Bold), BackColor = Color.Transparent };
                dialog.Controls.Add(title);
                var modeLabel = new Label { Text = "Theme mode", Location = new Point(24, 64), AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold), BackColor = Color.Transparent };
                dialog.Controls.Add(modeLabel);
                var dark = new RadioButton { Text = "Dark", Location = new Point(24, 90), AutoSize = true, Checked = !lightTheme };
                var light = new RadioButton { Text = "Light", Location = new Point(100, 90), AutoSize = true, Checked = lightTheme };
                dialog.Controls.Add(dark); dialog.Controls.Add(light);

                var colorLabel = new Label { Text = "Accent color", Location = new Point(24, 130), AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold), BackColor = Color.Transparent };
                dialog.Controls.Add(colorLabel);
                var colors = new[]
                {
                    new { Name = "Purple", Value = Color.FromArgb(111, 101, 255) },
                    new { Name = "Blue", Value = Color.FromArgb(65, 137, 255) },
                    new { Name = "Pink", Value = Color.FromArgb(215, 82, 190) },
                    new { Name = "Green", Value = Color.FromArgb(49, 190, 139) }
                };
                var selectedAccent = Accent;
                var accentButtons = new List<Button>();
                int x = 24;
                foreach (var color in colors)
                {
                    var button = new Button { Text = color.Name, Tag = color.Value, Location = new Point(x, 158), Size = new Size(76, 32), FlatStyle = FlatStyle.Flat, FlatAppearance = { BorderSize = 0 }, BackColor = color.Value, ForeColor = Color.White, Cursor = Cursors.Hand };
                    button.Click += (s, e) =>
                    {
                        selectedAccent = (Color)((Button)s).Tag;
                        foreach (var other in accentButtons) other.FlatAppearance.BorderSize = 0;
                        ((Button)s).FlatAppearance.BorderSize = 2;
                        ((Button)s).FlatAppearance.BorderColor = Color.White;
                    };
                    accentButtons.Add(button);
                    button.Resize += (s, e) => SetRoundedRegion(button, 8);
                    SetRoundedRegion(button, 8);
                    dialog.Controls.Add(button);
                    x += 84;
                }

                var checkUpdates = new Button { Text = "Check for updates", Location = new Point(24, 215), Size = new Size(140, 32), FlatStyle = FlatStyle.Flat };
                checkUpdates.FlatAppearance.BorderSize = 0;
                checkUpdates.BackColor = lightTheme ? Color.FromArgb(225, 230, 238) : Color.FromArgb(43, 49, 63);
                checkUpdates.ForeColor = dialog.ForeColor;
                checkUpdates.Resize += (s, e) => SetRoundedRegion(checkUpdates, 8);
                SetRoundedRegion(checkUpdates, 8);
                checkUpdates.Click += (s, e) => CheckForUpdatesAsync(false);
                dialog.Controls.Add(checkUpdates);

                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(190, 215), Size = new Size(78, 32) };
                var save = new Button { Text = "Apply", DialogResult = DialogResult.OK, Location = new Point(278, 215), Size = new Size(86, 32), BackColor = Accent, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
                save.FlatAppearance.BorderSize = 0;
                cancel.FlatStyle = FlatStyle.Flat;
                cancel.FlatAppearance.BorderSize = 0;
                cancel.BackColor = lightTheme ? Color.FromArgb(225, 230, 238) : Color.FromArgb(43, 49, 63);
                cancel.ForeColor = dialog.ForeColor;
                cancel.Resize += (s, e) => SetRoundedRegion(cancel, 8);
                save.Resize += (s, e) => SetRoundedRegion(save, 8);
                SetRoundedRegion(cancel, 8);
                SetRoundedRegion(save, 8);
                dialog.Controls.Add(cancel); dialog.Controls.Add(save);
                dialog.AcceptButton = save; dialog.CancelButton = cancel;

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    lightTheme = light.Checked;
                    Accent = selectedAccent;
                    ApplyTheme();
                    SetStatus("Appearance updated.", Good);
                }
            }
        }

        private void ApplyTheme()
        {
            var oldWhite = White;
            var oldMuted = Muted;
            var oldDim = Dim;
            var oldAccent = Accent;

            if (lightTheme)
            {
                Ink = Color.FromArgb(239, 242, 247);
                Ink2 = Color.FromArgb(247, 248, 251);
                Glass = Color.FromArgb(255, 255, 255);
                Glass2 = Color.FromArgb(235, 239, 246);
                Glass3 = Color.FromArgb(247, 248, 251);
                Field = Color.FromArgb(228, 233, 241);
                White = Color.FromArgb(28, 31, 40);
                Muted = Color.FromArgb(82, 89, 106);
                Dim = Color.FromArgb(108, 116, 132);
            }
            else
            {
                Ink = Color.FromArgb(10, 13, 19);
                Ink2 = Color.FromArgb(15, 19, 27);
                Glass = Color.FromArgb(30, 36, 49);
                Glass2 = Color.FromArgb(36, 43, 58);
                Glass3 = Color.FromArgb(22, 27, 38);
                Field = Color.FromArgb(18, 22, 30);
                White = Color.FromArgb(246, 248, 252);
                Muted = Color.FromArgb(151, 163, 183);
                Dim = Color.FromArgb(111, 122, 141);
            }
            Accent2 = Color.FromArgb(Math.Min(255, Accent.R + 35), Math.Min(255, Accent.G + 35), Math.Min(255, Accent.B + 35));
            this.BackColor = Ink;
            ApplyThemeToControls(this, oldWhite, oldMuted, oldDim, oldAccent);
            foreach (var pair in glassMetaMap)
            {
                pair.Value.Top = lightTheme ? Color.FromArgb(255, 255, 255) : pair.Value.DarkTop;
                pair.Value.Bottom = lightTheme ? Color.FromArgb(247, 248, 251) : pair.Value.DarkBottom;
                pair.Key.BackColor = pair.Value.Bottom;
                pair.Key.Invalidate();
            }
            RefreshLibrary();
            refreshMyPresetsView?.Invoke(myPresetsCurrentFolder ?? LibraryRoot);
            this.Invalidate(true);
        }

        private void ApplyThemeToControls(Control parent, Color oldWhite, Color oldMuted, Color oldDim, Color oldAccent)
        {
            foreach (Control control in parent.Controls)
            {
                if (control == sidebar) continue;

                if (control.ForeColor == oldWhite) control.ForeColor = White;
                else if (control.ForeColor == oldMuted) control.ForeColor = Muted;
                else if (control.ForeColor == oldDim) control.ForeColor = Dim;
                if (control is Button button)
                {
                    bool isAccent = accentControls.Contains(control);
                    bool isApplyHighlight = applyHighlightControls.Contains(control);
                    button.BackColor = isAccent ? Accent : Glass2;
                    button.ForeColor = White;
                    button.FlatAppearance.MouseOverBackColor = isAccent
                        ? Color.FromArgb(Math.Min(255, Accent.R + 20), Math.Min(255, Accent.G + 20), Math.Min(255, Accent.B + 20))
                        : isApplyHighlight
                            ? Accent
                            : (lightTheme ? Color.FromArgb(218, 224, 234) : Color.FromArgb(44, 52, 69));
                    button.FlatAppearance.MouseDownBackColor = isAccent
                        ? Color.FromArgb(Math.Max(0, Accent.R - 20), Math.Max(0, Accent.G - 20), Math.Max(0, Accent.B - 20))
                        : isApplyHighlight
                            ? Color.FromArgb(Math.Max(0, Accent.R - 20), Math.Max(0, Accent.G - 20), Math.Max(0, Accent.B - 20))
                            : (lightTheme ? Color.FromArgb(204, 212, 225) : Color.FromArgb(31, 36, 49));
                }
                else if (control is TextBox) control.BackColor = Field;
                else if (!(control is Button) && !glassMetaMap.ContainsKey(control) && control.BackColor != Color.Transparent) control.BackColor = lightTheme ? Ink2 : Ink;
                ApplyThemeToControls(control, oldWhite, oldMuted, oldDim, oldAccent);
            }
        }

        private Panel NewDashboardCard(int radius = 20, Color? top = null, Color? bottom = null)
        {
            var panel = new Panel { BackColor = bottom ?? Glass3, Margin = new Padding(0) };
            AddGlassPanel(panel, radius, top ?? Color.FromArgb(30, 36, 49), bottom ?? Color.FromArgb(18, 21, 29));
            return panel;
        }

        private Panel NewNavigationItem(string text, string icon, bool selected = false)
        {
            var item = new Panel
            {
                Height = 38,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, 0, 5),
                Padding = new Padding(12, 0, 10, 0),
                BackColor = selected ? Color.FromArgb(39, 39, 47) : Color.Transparent,
                Cursor = Cursors.Hand,
                Tag = selected
            };
            SetRoundedRegion(item, 9);
            item.Resize += (s, e) => SetRoundedRegion(item, 9);
            item.MouseEnter += (s, e) => { if (item.BackColor != Color.FromArgb(39, 39, 47)) item.BackColor = Color.FromArgb(30, 31, 40); };
            item.MouseLeave += (s, e) => { if (item.BackColor != Color.FromArgb(39, 39, 47)) item.BackColor = Color.Transparent; };

            var iconLabel = new Label
            {
                Text = icon,
                Location = new Point(10, 0),
                Size = new Size(22, 38),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = selected ? White : Color.FromArgb(190, 194, 204),
                Font = new Font("Segoe UI Symbol", 10),
                BackColor = Color.Transparent
            };
            var textLabel = new Label
            {
                Text = text,
                Location = new Point(40, 0),
                Size = new Size(170, 38),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = selected ? White : Color.FromArgb(190, 194, 204),
                Font = new Font("Segoe UI", 9, selected ? FontStyle.Bold : FontStyle.Regular),
                BackColor = Color.Transparent
            };
            item.Controls.Add(iconLabel);
            item.Controls.Add(textLabel);
            return item;
        }

        private Label NewSectionLabel(string text)
        {
            return new Label
            {
                Text = text.ToUpperInvariant(),
                Height = 24,
                Dock = DockStyle.Top,
                Padding = new Padding(8, 8, 0, 0),
                ForeColor = Dim,
                Font = new Font("Segoe UI", 7, FontStyle.Bold),
                BackColor = Color.Transparent
            };
        }

        private void AnimateButton(Button button, bool enter)
        {
            if (button == null || button.Enabled == false) return;
            bool isAccent = accentControls.Contains(button);
            bool isApplyHighlight = applyHighlightControls.Contains(button);
            Color target;
            if (enter)
            {
                target = isAccent ? Color.FromArgb(94, 145, 255)
                       : isApplyHighlight ? Accent
                       : Color.FromArgb(42, 49, 65);
            }
            else
            {
                target = isAccent ? Accent : Glass2;
            }
            button.BackColor = target;
        }

        private void StartInteractionAnimation()
        {
            if (interactionTimer != null) return;
            interactionTimer = new Timer { Interval = 45 };
            interactionTimer.Tick += (s, e) =>
            {
                if (activeNavigationPanel != null)
                {
                    activeNavigationPanel.Invalidate();
                }
            };
            interactionTimer.Start();
        }

        private Label NewPillLabel(string text, int width = 140)
        {
            var p = new Label();
            p.Text = text;
            p.Size = new Size(width, 24);
            p.TextAlign = ContentAlignment.MiddleCenter;
            p.Font = new Font("Segoe UI", 8, FontStyle.Bold);
            p.ForeColor = Muted;
            p.BackColor = Field;
            p.Resize += (s, e) => SetRoundedRegion(p, 12);
            SetRoundedRegion(p, 12);
            return p;
        }

        private void SetStatus(string text, Color? color = null)
        {
            if (StatusLabel != null)
            {
                StatusLabel.Text = text;
                StatusLabel.ForeColor = color ?? Good;
            }
        }

        private void SetButtonEnabled(bool ready)
        {
            if (ApplyButton != null) ApplyButton.Enabled = ready;
            if (RestoreButton != null) RestoreButton.Enabled = ready;
            if (AccessButton != null) AccessButton.Enabled = ready;
        }
        #endregion

        #region Library
        private IEnumerable<FileSystemInfo> GetPresetItems()
        {
            var library = GetLibrary((string)CurrentItem.Id);
            var files = new DirectoryInfo(library).GetFiles("*.zip");
            foreach (var f in files) yield return f;
        }

        private void ClearLibrary()
        {
            if (LibraryFlow == null) return;
            foreach (Control c in LibraryFlow.Controls)
            {
                foreach (Control child in c.Controls)
                {
                    if (child is PictureBox pb && pb.Image != null) { pb.Image.Dispose(); pb.Image = null; }
                }
                c.Dispose();
            }
            LibraryFlow.Controls.Clear();
        }

        private void SelectPreset(string path, dynamic item = null)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            item = item ?? CurrentItem;
            SelectedPresetPath = path;
            SelectedName.Text = Path.GetFileNameWithoutExtension(path);
            SelectedInfo.Text = (bool)item.Sky ? "Skybox preset - 6 faces" : "Texture preset - " + GetPresetImageExtension(path);
            if (SelectedPreview.Image != null) { SelectedPreview.Image.Dispose(); SelectedPreview.Image = null; }
            SelectedPreview.Image = ReadPresetPreview(path);
            ApplyButton.Enabled = true;
        }

        private void DeletePreset(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            var label = Path.GetFileNameWithoutExtension(path);
            var res = MessageBox.Show($"Delete preset \"{label}\"?\r\n\r\nThis cannot be undone.", "Delete preset", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (res != DialogResult.OK) return;
            try
            {
                File.Delete(path);
                if (string.Equals(SelectedPresetPath, path, StringComparison.OrdinalIgnoreCase))
                {
                    SelectedPresetPath = null;
                    if (SelectedPreview.Image != null) { SelectedPreview.Image.Dispose(); SelectedPreview.Image = null; }
                    SelectedName.Text = "No preset selected";
                    SelectedInfo.Text = "Select a library card to preview it.";
                    ApplyButton.Enabled = false;
                }
                SetStatus($"Deleted preset - {label}", Good);
                RefreshLibrary();
            }
            catch (Exception ex)
            {
                SetStatus("Could not delete preset: " + ex.Message, Warn);
            }
        }

        private void RefreshLibrary()
        {
            ClearLibrary();
            LibraryFlow.AutoScrollPosition = new Point(0, 0);
            LibraryFlow.VerticalScroll.Value = 0;
            var items = GetPresetItems().OrderBy(i => i.Name).ToArray();
            CountLabel.Text = $"{items.Length} preset{(items.Length == 1 ? "" : "s")}";

            if (items.Length == 0)
            {
                var empty = new Panel { Size = new Size(520, 130), Margin = new Padding(4, 4, 16, 16) };
                AddGlassPanel(empty, 18, Glass, Glass3);
                var t = new Label();
                var hasSearch = SearchBox.Text != null && SearchBox.Text != "Search presets...";
                t.Text = hasSearch ? $"No presets match \"{SearchBox.Text}\"." : "Your library is empty.\r\nImport an image and it will appear here.";
                t.Dock = DockStyle.Fill; t.TextAlign = ContentAlignment.MiddleCenter; t.ForeColor = White; t.BackColor = Color.Transparent;
                t.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
                empty.Controls.Add(t);
                LibraryFlow.Controls.Add(empty);
                return;
            }

            foreach (var preset in items)
            {
                var card = new Panel { Size = new Size(220, 156), Margin = new Padding(0, 0, 12, 12) };
                AddGlassPanel(card, 20, Glass3, Glass);
                card.Tag = preset.FullName;

                var pic = new PictureBox { Location = new Point(10, 10), Size = new Size(178, 70), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Field };
                SetRoundedRegion(pic, 16);
                pic.Resize += (s, e) => SetRoundedRegion(pic, 16);
                var previewImage = ReadPresetPreview(preset.FullName);
                if (previewImage != null) pic.Image = previewImage;
                else pic.Controls.Add(new Label { Text = "*", Dock = DockStyle.Fill, ForeColor = Accent2, Font = new Font("Segoe UI Symbol", 20), TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent });
                card.Controls.Add(pic);

                var deleteButton = new Label
                {
                    Text = "\U0001F5D1",
                    Location = new Point(192, 10),
                    Size = new Size(22, 22),
                    TextAlign = ContentAlignment.MiddleCenter,
                    ForeColor = Danger,
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI Symbol", 11),
                    Cursor = Cursors.Hand,
                    Tag = preset.FullName
                };
                deleteButton.Click += (s, e) => DeletePreset((string)((Label)s).Tag);
                deleteButton.MouseEnter += (s, e) => deleteButton.ForeColor = Color.FromArgb(255, 150, 165);
                deleteButton.MouseLeave += (s, e) => deleteButton.ForeColor = Danger;
                card.Controls.Add(deleteButton);
                deleteButton.BringToFront();

                var name = new Label { Text = Path.GetFileNameWithoutExtension(preset.Name), Location = new Point(10, 86), Size = new Size(132, 20), ForeColor = White, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), AutoEllipsis = true, BackColor = Color.Transparent };
                card.Controls.Add(name);

                var meta = new Label { Text = (bool)CurrentItem.Sky ? "6 face skybox" : GetPresetImageExtension(preset.FullName), Location = new Point(10, 106), Size = new Size(90, 18), ForeColor = Dim, Font = new Font("Segoe UI", 7.5f), BackColor = Color.Transparent };
                card.Controls.Add(meta);

                var selectButton = NewGlassButton("Select", 60, 28, false);
                selectButton.Location = new Point(96, 120);
                selectButton.Tag = preset.FullName;
                selectButton.Click += (s, e) => SelectPreset((string)((Button)s).Tag);
                card.Controls.Add(selectButton);

                var apply = NewGlassButton("Apply", 60, 28, false, true);
                apply.Location = new Point(156, 120);
                apply.Tag = preset.FullName;
                apply.Click += (s, e) => { var p = (string)((Button)s).Tag; SelectPreset(p); ApplyPreset(p); };
                card.Controls.Add(apply);

                card.Click += (s, e) => SelectPreset((string)card.Tag);
                pic.Click += (s, e) => SelectPreset((string)card.Tag);
                name.Click += (s, e) => SelectPreset((string)card.Tag);

                LibraryFlow.Controls.Add(card);

                if (!string.IsNullOrEmpty(SelectedPresetPath) && SelectedPresetPath == preset.FullName) SelectPreset(preset.FullName);
            }
        }

        private void ImportPreset()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "Preset zip files|*.zip";
                dlg.FilterIndex = 1;
                dlg.DefaultExt = "zip";
                dlg.CheckFileExists = true;
                dlg.ValidateNames = true;
                dlg.Multiselect = false;
                dlg.Title = (bool)CurrentItem.Sky
                    ? "Import a skybox preset (.zip containing 6 .tex faces)"
                    : "Import a texture preset (.zip containing one image)";
                if (dlg.ShowDialog() != DialogResult.OK) return;

                var source = dlg.FileName;

                try
                {
                    using (var archive = ZipFile.OpenRead(source))
                    {
                        if ((bool)CurrentItem.Sky)
                        {
                            foreach (var face in SkyFaces)
                            {
                                var match = archive.Entries.Where(e => IsTexEntry(e.Name) &&
                                    Path.GetFileNameWithoutExtension(e.Name).ToLower().Split(new[] { '_', '-' }).Contains(face)).ToArray();
                                if (match.Length != 1)
                                {
                                    MessageBox.Show($"This zip doesn't have exactly one '{face}' face. A skybox preset needs six .tex files, each named so it contains bk, dn, ft, lf, rt, or up.", "Invalid skybox preset", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                    return;
                                }
                            }
                        }
                        else
                        {
                            if (!archive.Entries.Any(e => IsImageEntry(e.Name)))
                            {
                                MessageBox.Show("This zip doesn't contain an image (PNG, JPEG, or BMP).", "Invalid preset", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                return;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not read that zip file: " + ex.Message, "Invalid preset", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var library = GetLibrary((string)CurrentItem.Id);
                var baseName = Path.GetFileNameWithoutExtension(source);
                var destination = Path.Combine(library, baseName + ".zip");
                var suffix = 1;
                while (File.Exists(destination)) destination = Path.Combine(library, $"{baseName} ({suffix++}).zip");
                File.Copy(source, destination, false);
                SelectedPresetPath = destination;
                SetStatus($"Imported preset - {Path.GetFileName(destination)}", Good);
            }
            RefreshLibrary();
            SelectPreset(SelectedPresetPath);
        }

        private void ApplyPreset(string presetPath, dynamic item = null)
        {
            item = item ?? CurrentItem;
            if (string.IsNullOrEmpty(presetPath) || !File.Exists(presetPath)) { SetStatus("Select a preset first.", Warn); return; }
            if (RobloxRunning()) { SetStatus("Close Roblox completely before applying a texture.", Warn); return; }
            var versions = GetRobloxVersions().ToArray();
            if (versions.Length == 0) { SetStatus("Roblox was not found. Launch Roblox once, then reopen Retexture Studio.", Warn); return; }

            var label = Path.GetFileNameWithoutExtension(presetPath);
            var res = MessageBox.Show($"Apply \"{label}\" to {item.Name}?\r\n\r\nRetexture Studio will create a one-time backup of the original Roblox file(s).", "Apply preset", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (res != DialogResult.OK) return;

            string tempDir = null;
            try
            {
                tempDir = Path.Combine(Path.GetTempPath(), "RetextureStudio_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);

                int updated = 0;
                using (var archive = ZipFile.OpenRead(presetPath))
                {
                    if ((bool)item.Sky)
                    {
                        var faceExtracted = new Dictionary<string, string>();
                        foreach (var face in SkyFaces)
                        {
                            var entry = archive.Entries.FirstOrDefault(e => IsTexEntry(e.Name) &&
                                Path.GetFileNameWithoutExtension(e.Name).ToLower().Split(new[] { '_', '-' }).Contains(face));
                            if (entry == null) throw new Exception($"This preset is missing the '{face}' face (.tex).");
                            var extractedPath = Path.Combine(tempDir, face + ".tex");
                            entry.ExtractToFile(extractedPath, true);
                            faceExtracted[face] = extractedPath;
                        }
                        foreach (var version in versions)
                        {
                            var folder = Path.Combine(version.FullName, (string)item.Target);
                            var targets = SkyFaces.Select(f => Path.Combine(folder, ((string)item.File).Replace("{face}", f))).ToArray();
                            if (targets.Where(t => !File.Exists(t)).Any()) continue;
                            foreach (var face in SkyFaces)
                            {
                                var target = Path.Combine(folder, ((string)item.File).Replace("{face}", face));
                                AddBackup(target);
                                File.Copy(faceExtracted[face], target, true);
                            }
                            updated++;
                        }
                    }
                    else
                    {
                        var imageEntry = archive.Entries.Where(e => IsImageEntry(e.Name)).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                        if (imageEntry == null) throw new Exception("This preset does not contain an image.");
                        var extractedPath = Path.Combine(tempDir, "source" + Path.GetExtension(imageEntry.Name));
                        imageEntry.ExtractToFile(extractedPath, true);
                        foreach (var version in versions)
                        {
                            var target = Path.Combine(version.FullName, (string)item.Target);
                            if (!File.Exists(target)) continue;
                            AddBackup(target);
                            File.Copy(extractedPath, target, true);
                            updated++;
                        }
                    }
                }
                if (updated == 0) throw new Exception("No matching Roblox texture files were found in the version folders.");
                SetStatus($"{item.Name} applied to {updated} Roblox version folder(s).", Good);
            }
            catch (Exception ex)
            {
                SetStatus("Could not apply: " + ex.Message, Warn);
            }
            finally
            {
                if (tempDir != null && Directory.Exists(tempDir)) { try { Directory.Delete(tempDir, true); } catch { } }
            }
        }

        private void RestoreOriginal()
        {
            if (RobloxRunning()) { SetStatus("Close Roblox completely before restoring.", Warn); return; }
            var versions = GetRobloxVersions().ToArray();
            if (versions.Length == 0) { SetStatus("Roblox was not found.", Warn); return; }

            var res = MessageBox.Show($"Restore the original {CurrentItem.Name} texture?\r\n\r\nThe last saved backup will be restored.", "Restore original", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (res != DialogResult.OK) return;

            try
            {
                int restored = 0;
                foreach (var version in versions)
                {
                    string[] targets;
                    if ((bool)CurrentItem.Sky)
                    {
                        var folder = Path.Combine(version.FullName, (string)CurrentItem.Target);
                        targets = SkyFaces.Select(f => Path.Combine(folder, ((string)CurrentItem.File).Replace("{face}", f))).ToArray();
                    }
                    else
                    {
                        targets = new[] { Path.Combine(version.FullName, (string)CurrentItem.Target) };
                    }
                    foreach (var target in targets)
                    {
                        var backup = GetBackup(target);
                        if (!File.Exists(backup) || !File.Exists(target)) continue;
                        File.Copy(backup, target, true);
                        restored++;
                    }
                }
                if (restored == 0) throw new Exception("No backups were found in the Roblox version folders.");
                SetStatus($"Restored {restored} original texture file(s).", Good);
            }
            catch (Exception ex)
            {
                SetStatus("Could not restore: " + ex.Message, Warn);
            }
        }

        private void RefreshTargetView()
        {
            var versions = GetRobloxVersions().ToArray();
            if (versions.Length > 0)
            {
                VersionChip.Text = $"Roblox: {versions.Length} version(s)";
                VersionChip.ForeColor = Good;
            }
            else
            {
                VersionChip.Text = "Roblox not detected";
                VersionChip.ForeColor = Warn;
            }
            TargetTitle.Text = CurrentItem.Name;
            TargetHint.Text = (bool)CurrentItem.Sky ? "Applying a skybox replaces all six face files automatically." : "A preset is copied over the active Roblox texture after a backup is made.";
            SetButtonEnabled(GetRobloxVersions().Any());
            RefreshLibrary();
        }

        private bool IsPresetFile(string path)
        {
            return Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase);
        }

        private bool FolderContainsPreset(string folder)
        {
            try
            {
                return Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Any(IsPresetFile);
            }
            catch { return false; }
        }

        private dynamic GetLibraryItemFor(string path)
        {
            try
            {
                var full = Path.GetFullPath(path);
                var root = Path.GetFullPath(LibraryRoot);
                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
                var relative = full.Substring(root.Length).Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var topId = relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (topId == null) return null;
                return Textures.FirstOrDefault(t => string.Equals((string)t.Id, topId, StringComparison.OrdinalIgnoreCase));
            }
            catch { return null; }
        }

        private void AddPresetFolderNodes(TreeNode parent, string folder)
        {
            try
            {
                foreach (var directory in Directory.GetDirectories(folder).OrderBy(Path.GetFileName))
                {
                    var node = new TreeNode(Path.GetFileName(directory)) { Tag = directory };
                    parent.Nodes.Add(node);
                    AddPresetFolderNodes(node, directory);
                }
            }
            catch { }
        }

        private List<PresetEntry> GetPresetEntries(string folder, bool recursive)
        {
            var result = new List<PresetEntry>();
            try
            {
                var fileSearch = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                foreach (var file in Directory.GetFiles(folder, "*", fileSearch).Where(IsPresetFile))
                    result.Add(new PresetEntry { Name = Path.GetFileName(file), FullPath = file, IsDirectory = false });

                var dirs = recursive ? Directory.GetDirectories(folder, "*", SearchOption.AllDirectories) : Directory.GetDirectories(folder);
                foreach (var directory in dirs.Where(FolderContainsPreset))
                    result.Add(new PresetEntry { Name = Path.GetFileName(directory) + "  (folder)", FullPath = directory, IsDirectory = true });
            }
            catch { }
            return result.OrderBy(x => x.Name).ToList();
        }

        private string PromptForText(string title, string prompt, string initial = "")
        {
            using (var dialog = new Form { Text = title, ClientSize = new Size(340, 135), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false })
            {
                dialog.Font = new Font("Segoe UI", 9);
                var label = new Label { Text = prompt, Location = new Point(16, 16), AutoSize = true };
                var text = new TextBox { Text = initial, Location = new Point(16, 44), Width = 305 };
                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(160, 86), Width = 76 };
                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(245, 86), Width = 76 };
                dialog.Controls.Add(label); dialog.Controls.Add(text); dialog.Controls.Add(ok); dialog.Controls.Add(cancel);
                dialog.AcceptButton = ok; dialog.CancelButton = cancel;
                return dialog.ShowDialog(this) == DialogResult.OK ? text.Text.Trim() : null;
            }
        }

        private void ShowTexturePage()
        {
            if (texturePage != null) texturePage.Visible = true;
            if (myPresetsPage != null) myPresetsPage.Visible = false;
        }

        private void ShowMyPresetsPage()
        {
            if (pageHost == null || texturePage == null) return;
            if (myPresetsPage == null)
            {
                myPresetsPage = BuildMyPresetsPage();
                pageHost.Controls.Add(myPresetsPage);
            }
            texturePage.Visible = false;
            myPresetsPage.Visible = true;
            myPresetsPage.BringToFront();
        }

        private Panel BuildMyPresetsPage()
        {
            var page = new Panel
            {
                Dock = DockStyle.Top,
                Height = 610,
                BackColor = Color.Transparent,
                Padding = new Padding(0)
            };

            var heading = new Label { Text = "My Presets", Location = new Point(0, 2), AutoSize = true, ForeColor = White, Font = new Font("Segoe UI", 24, FontStyle.Bold), BackColor = Color.Transparent };
            page.Controls.Add(heading);
            page.Controls.Add(new Label { Text = "All your imported textures and skyboxes in one place.", Location = new Point(2, 48), AutoSize = true, ForeColor = Muted, Font = new Font("Segoe UI", 9.5f), BackColor = Color.Transparent });

            var folderCard = NewDashboardCard(18, Color.FromArgb(27, 31, 42), Color.FromArgb(16, 18, 24));
            folderCard.Location = new Point(0, 88);
            folderCard.Size = new Size(250, 470);
            folderCard.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            folderCard.Padding = new Padding(14);
            page.Controls.Add(folderCard);
            folderCard.Controls.Add(new Label { Text = "Folders", Location = new Point(14, 14), AutoSize = true, ForeColor = White, Font = new Font("Segoe UI", 11, FontStyle.Bold), BackColor = Color.Transparent });

            var folderFlow = new FlowLayoutPanel
            {
                Location = new Point(10, 44),
                Size = new Size(230, 350),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = Color.Transparent,
                Padding = new Padding(4)
            };
            folderCard.Controls.Add(folderFlow);
            var newFolder = NewGlassButton("+  New folder", 112, 32, true);
            newFolder.Location = new Point(14, 412);
            newFolder.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            folderCard.Controls.Add(newFolder);
            var rename = NewGlassButton("Rename", 82, 32, false);
            rename.Location = new Point(132, 412);
            rename.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            folderCard.Controls.Add(rename);

            var listCard = NewDashboardCard(18, Color.FromArgb(27, 31, 42), Color.FromArgb(16, 18, 24));
            listCard.Location = new Point(266, 88);
            listCard.Size = new Size(700, 470);
            listCard.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            listCard.Padding = new Padding(18);
            page.Controls.Add(listCard);
            listCard.Controls.Add(new Label { Text = "All presets", Location = new Point(18, 14), AutoSize = true, ForeColor = White, Font = new Font("Segoe UI", 11, FontStyle.Bold), BackColor = Color.Transparent });
            var move = NewGlassButton("Move selected", 120, 32, false);
            move.Location = new Point(0, 10);
            move.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            listCard.Resize += (s, e) => move.Left = listCard.ClientSize.Width - move.Width - 18;
            listCard.Controls.Add(move);

            var presetFlow = new FlowLayoutPanel
            {
                Location = new Point(14, 52),
                Size = new Size(668, 396),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                WrapContents = true,
                AutoScroll = true,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent,
                Padding = new Padding(4)
            };
            listCard.Controls.Add(presetFlow);

            string currentFolder = LibraryRoot;
            PresetEntry selectedEntry = null;
            Action<string> refresh = null;

            Func<string, Image> previewFor = path =>
            {
                if (Directory.Exists(path)) return null;
                return ReadPresetPreview(path);
            };

            Func<PresetEntry, string> displayNameFor = entry =>
                entry.IsDirectory ? Path.GetFileName(entry.FullPath) : Path.GetFileNameWithoutExtension(entry.FullPath);

            Func<PresetEntry, string> metadataFor = entry =>
            {
                if (entry.IsDirectory) return "Folder";
                var item = GetLibraryItemFor(entry.FullPath);
                if (item != null && (bool)item.Sky) return "6 face skybox";
                return GetPresetImageExtension(entry.FullPath);
            };

            Action<PresetEntry, Label, Panel> renamePreset = null;
            renamePreset = (entry, label, card) =>
            {
                var oldName = displayNameFor(entry);
                var newName = PromptForText("Rename preset", "Preset name:", oldName);
                if (string.IsNullOrWhiteSpace(newName) || newName.Equals(oldName, StringComparison.OrdinalIgnoreCase)) return;
                try
                {
                    if (entry.IsDirectory)
                    {
                        var newPath = Path.Combine(Path.GetDirectoryName(entry.FullPath), newName);
                        if (Directory.Exists(newPath)) return;
                        Directory.Move(entry.FullPath, newPath);
                        entry.FullPath = newPath;
                    }
                    else
                    {
                        var newPath = Path.Combine(Path.GetDirectoryName(entry.FullPath), newName + Path.GetExtension(entry.FullPath));
                        if (File.Exists(newPath)) return;
                        File.Move(entry.FullPath, newPath);
                        entry.FullPath = newPath;
                    }
                    entry.Name = newName;
                    label.Text = newName;
                    card.Tag = entry;
                    selectedEntry = entry;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not rename preset: " + ex.Message, "My Presets", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };

            Action<PresetEntry> deleteEntry = null;
            deleteEntry = entry =>
            {
                var label = displayNameFor(entry);
                var kind = entry.IsDirectory ? "folder" : "preset";
                var res = MessageBox.Show($"Delete {kind} \"{label}\"?\r\n\r\nThis cannot be undone.", "Delete " + kind, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
                if (res != DialogResult.OK) return;
                try
                {
                    if (entry.IsDirectory) Directory.Delete(entry.FullPath, true);
                    else File.Delete(entry.FullPath);
                    if (selectedEntry == entry) selectedEntry = null;
                    if (!entry.IsDirectory && string.Equals(SelectedPresetPath, entry.FullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        SelectedPresetPath = null;
                        if (SelectedPreview.Image != null) { SelectedPreview.Image.Dispose(); SelectedPreview.Image = null; }
                        SelectedName.Text = "No preset selected";
                        SelectedInfo.Text = "Select a library card to preview it.";
                        ApplyButton.Enabled = false;
                    }
                    SetStatus($"Deleted {kind} - {label}", Good);
                    refresh(currentFolder);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not delete " + kind + ": " + ex.Message, "My Presets", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };

            refresh = folder =>
            {
                currentFolder = folder;
                myPresetsCurrentFolder = folder;
                selectedEntry = null;
                folderFlow.Controls.Clear();
                presetFlow.Controls.Clear();

                var allCard = new Panel { Size = new Size(220, 48), Margin = new Padding(0, 0, 0, 8) };
                AddGlassPanel(allCard, 12, Glass2, Glass3);
                allCard.Controls.Add(new Label { Text = "▱  All presets", Location = new Point(14, 0), Size = new Size(190, 48), ForeColor = White, Font = new Font("Segoe UI", 9, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent });
                folderFlow.Controls.Add(allCard);
                allCard.Click += (s, e) => refresh(LibraryRoot);
                foreach (Control child in allCard.Controls) child.Click += (s, e) => refresh(LibraryRoot);

                foreach (var directory in Directory.GetDirectories(LibraryRoot).Where(FolderContainsPreset).OrderBy(Path.GetFileName))
                {
                    var folderCardItem = new Panel { Size = new Size(220, 48), Margin = new Padding(0, 0, 0, 8), Tag = directory };
                    AddGlassPanel(folderCardItem, 12, Glass3, Glass);
                    var folderLabel = new Label { Text = "▱  " + Path.GetFileName(directory), Location = new Point(14, 0), Size = new Size(190, 48), ForeColor = White, Font = new Font("Segoe UI", 9), TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent };
                    folderCardItem.Controls.Add(folderLabel);
                    folderFlow.Controls.Add(folderCardItem);
                    folderCardItem.Click += (s, e) => refresh((string)folderCardItem.Tag);
                    folderCardItem.DoubleClick += (s, e) => refresh((string)folderCardItem.Tag);
                    folderLabel.Click += (s, e) => refresh((string)folderCardItem.Tag);
                    folderLabel.DoubleClick += (s, e) => refresh((string)folderCardItem.Tag);
                }

                foreach (var entry in GetPresetEntries(folder, folder == LibraryRoot))
                {
                    var card = new Panel { Size = new Size(220, 156), Margin = new Padding(0, 0, 12, 12), Tag = entry };
                    AddGlassPanel(card, 20, Glass3, Glass);
                    var pic = new PictureBox { Location = new Point(10, 10), Size = new Size(178, 70), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Field };
                    SetRoundedRegion(pic, 16);
                    pic.Resize += (s, e) => SetRoundedRegion(pic, 16);
                    var image = previewFor(entry.FullPath);
                    if (image != null) pic.Image = image;
                    else pic.Controls.Add(new Label { Text = entry.IsDirectory ? "▱" : "◆", Dock = DockStyle.Fill, ForeColor = Accent2, Font = new Font("Segoe UI Symbol", 20), TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent });
                    card.Controls.Add(pic);

                    var deleteButton = new Label
                    {
                        Text = "\U0001F5D1",
                        Location = new Point(192, 10),
                        Size = new Size(22, 22),
                        TextAlign = ContentAlignment.MiddleCenter,
                        ForeColor = Danger,
                        BackColor = Color.Transparent,
                        Font = new Font("Segoe UI Symbol", 11),
                        Cursor = Cursors.Hand
                    };
                    deleteButton.Click += (s, e) => deleteEntry(entry);
                    deleteButton.MouseEnter += (s, e) => deleteButton.ForeColor = Color.FromArgb(255, 150, 165);
                    deleteButton.MouseLeave += (s, e) => deleteButton.ForeColor = Danger;
                    card.Controls.Add(deleteButton);
                    deleteButton.BringToFront();

                    var name = new Label { Text = displayNameFor(entry), Location = new Point(10, 86), Size = new Size(132, 20), ForeColor = White, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), AutoEllipsis = true, BackColor = Color.Transparent, Cursor = Cursors.Hand };
                    card.Controls.Add(name);
                    var meta = new Label { Text = metadataFor(entry), Location = new Point(10, 106), Size = new Size(90, 18), ForeColor = Dim, Font = new Font("Segoe UI", 7.5f), BackColor = Color.Transparent };
                    card.Controls.Add(meta);
                    name.Click += (s, e) => renamePreset(entry, name, card);
                    name.MouseEnter += (s, e) => name.ForeColor = Accent2;
                    name.MouseLeave += (s, e) => name.ForeColor = White;
                    var select = NewGlassButton(entry.IsDirectory ? "Open" : "Select", 60, 28, false);
                    select.Location = new Point(96, 120);
                    select.Click += (s, e) =>
                    {
                        selectedEntry = entry;
                        if (entry.IsDirectory) refresh(entry.FullPath);
                        else SelectPreset(entry.FullPath, GetLibraryItemFor(entry.FullPath));
                    };
                    card.Controls.Add(select);
                    var apply = NewGlassButton("Apply", 60, 28, false, true);
                    apply.Location = new Point(156, 120);
                    apply.Click += (s, e) =>
                    {
                        var applyItem = GetLibraryItemFor(entry.FullPath);
                        SelectPreset(entry.FullPath, applyItem);
                        ApplyPreset(entry.FullPath, applyItem);
                    };
                    card.Controls.Add(apply);
                    EventHandler choose = (s, e) => selectedEntry = entry;
                    card.Click += choose; pic.Click += choose; name.Click += choose; meta.Click += choose;
                    card.DoubleClick += (s, e) => { if (entry.IsDirectory) refresh(entry.FullPath); else SelectPreset(entry.FullPath, GetLibraryItemFor(entry.FullPath)); };
                    pic.DoubleClick += (s, e) => { if (entry.IsDirectory) refresh(entry.FullPath); else SelectPreset(entry.FullPath, GetLibraryItemFor(entry.FullPath)); };
                    presetFlow.Controls.Add(card);
                }
            };

            refreshMyPresetsView = refresh;

            newFolder.Click += (s, e) =>
            {
                var name = PromptForText("New folder", "Folder name:");
                if (string.IsNullOrWhiteSpace(name)) return;
                var path = Path.Combine(currentFolder, name);
                if (Directory.Exists(path)) return;
                Directory.CreateDirectory(path);
                refresh(LibraryRoot);
            };
            rename.Click += (s, e) =>
            {
                if (currentFolder == LibraryRoot) return;
                var name = PromptForText("Rename folder", "New folder name:", Path.GetFileName(currentFolder));
                if (string.IsNullOrWhiteSpace(name)) return;
                var newPath = Path.Combine(Path.GetDirectoryName(currentFolder), name);
                if (Directory.Exists(newPath)) return;
                Directory.Move(currentFolder, newPath);
                refresh(LibraryRoot);
            };
            move.Click += (s, e) =>
            {
                if (selectedEntry == null || currentFolder == Path.GetDirectoryName(selectedEntry.FullPath)) return;
                try
                {
                    var destination = Path.Combine(currentFolder, Path.GetFileName(selectedEntry.FullPath));
                    if (selectedEntry.IsDirectory) Directory.Move(selectedEntry.FullPath, destination); else File.Move(selectedEntry.FullPath, destination);
                    refresh(currentFolder);
                }
                catch (Exception ex) { MessageBox.Show("Could not move preset: " + ex.Message, "My Presets", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };
            refresh(LibraryRoot);
            return page;
        }

        private void ShowMyPresets()
        {
            Directory.CreateDirectory(LibraryRoot);
            using (var dialog = new Form { Text = "My Presets", StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(780, 500), MinimumSize = new Size(650, 420) })
            {
                dialog.Font = new Font("Segoe UI", 9);
                dialog.BackColor = lightTheme ? Color.FromArgb(245, 247, 251) : Color.FromArgb(18, 20, 27);
                dialog.ForeColor = lightTheme ? Color.FromArgb(28, 31, 40) : White;
                var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 235, Padding = new Padding(12) };
                var tree = new TreeView { Dock = DockStyle.Fill, HideSelection = false, BackColor = lightTheme ? Color.White : Field, ForeColor = dialog.ForeColor, BorderStyle = BorderStyle.None };
                var list = new ListBox { Dock = DockStyle.Fill, BackColor = lightTheme ? Color.White : Field, ForeColor = dialog.ForeColor, BorderStyle = BorderStyle.None, IntegralHeight = false };
                var root = new TreeNode("All presets") { Tag = LibraryRoot };
                tree.Nodes.Add(root);
                AddPresetFolderNodes(root, LibraryRoot);
                root.Expand();
                split.Panel1.Controls.Add(tree);

                var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 0, 0) };
                right.Controls.Add(list);
                var toolbar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
                var newFolder = new Button { Text = "New folder", Width = 92, Height = 30 };
                var rename = new Button { Text = "Rename folder", Width = 108, Height = 30 };
                var move = new Button { Text = "Move selected", Width = 108, Height = 30 };
                toolbar.Controls.Add(newFolder); toolbar.Controls.Add(rename); toolbar.Controls.Add(move);
                right.Controls.Add(toolbar);
                split.Panel2.Controls.Add(right);
                dialog.Controls.Add(split);

                Action refreshList = () =>
                {
                    var selectedNode = tree.SelectedNode ?? root;
                    var selectedFolder = (string)selectedNode.Tag;
                    list.Items.Clear();
                    foreach (var entry in GetPresetEntries(selectedFolder, selectedNode == root)) list.Items.Add(entry);
                };
                tree.AfterSelect += (s, e) => refreshList();
                refreshList();

                newFolder.Click += (s, e) =>
                {
                    var parent = (string)(tree.SelectedNode == null ? root : tree.SelectedNode).Tag;
                    var name = PromptForText("New folder", "Folder name:");
                    if (string.IsNullOrWhiteSpace(name)) return;
                    var path = Path.Combine(parent, name);
                    if (Directory.Exists(path)) { MessageBox.Show("That folder already exists.", "My Presets", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                    Directory.CreateDirectory(path);
                    tree.Nodes.Clear();
                    root = new TreeNode("All presets") { Tag = LibraryRoot };
                    tree.Nodes.Add(root); AddPresetFolderNodes(root, LibraryRoot); root.Expand(); tree.SelectedNode = root;
                    refreshList();
                };
                rename.Click += (s, e) =>
                {
                    var node = tree.SelectedNode;
                    if (node == null || node == root) return;
                    var oldPath = (string)node.Tag;
                    var name = PromptForText("Rename folder", "New folder name:", node.Text);
                    if (string.IsNullOrWhiteSpace(name)) return;
                    var newPath = Path.Combine(Path.GetDirectoryName(oldPath), name);
                    if (!Directory.Exists(newPath)) Directory.Move(oldPath, newPath);
                    tree.Nodes.Clear(); root = new TreeNode("All presets") { Tag = LibraryRoot }; tree.Nodes.Add(root); AddPresetFolderNodes(root, LibraryRoot); root.Expand(); tree.SelectedNode = root; refreshList();
                };
                move.Click += (s, e) =>
                {
                    var entry = list.SelectedItem as PresetEntry;
                    var destinationFolder = (string)(tree.SelectedNode == null ? root : tree.SelectedNode).Tag;
                    if (entry == null || destinationFolder == Path.GetDirectoryName(entry.FullPath)) return;
                    var destination = Path.Combine(destinationFolder, Path.GetFileName(entry.FullPath));
                    try
                    {
                        if (entry.IsDirectory) Directory.Move(entry.FullPath, destination); else File.Move(entry.FullPath, destination);
                        refreshList();
                    }
                    catch (Exception ex) { MessageBox.Show("Could not move preset: " + ex.Message, "My Presets", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                };
                dialog.ShowDialog(this);
            }
        }
        #endregion

        #region Auto Update
        // --- Auto-update configuration ---
        private const string UpdateManifestUrl = "https://raw.githubusercontent.com/jakedev2/retexture/main/version.json";
        private static readonly Version CurrentVersion = new Version(0, 5, 0);
        private bool updateCheckInProgress;

        private async void CheckForUpdatesAsync(bool silent)
        {
            if (updateCheckInProgress) return;
            updateCheckInProgress = true;
            try
            {
                string json;
                using (var client = new System.Net.WebClient())
                {
                    client.Headers.Add("User-Agent", "RetextureStudio");
                    json = await client.DownloadStringTaskAsync(UpdateManifestUrl);
                }

                var remoteVersionText = ExtractJsonValue(json, "version");
                var downloadUrl = ExtractJsonValue(json, "url");
                var notes = ExtractJsonValue(json, "notes");
                if (string.IsNullOrEmpty(remoteVersionText) || string.IsNullOrEmpty(downloadUrl))
                {
                    if (!silent) SetStatus("Could not read the update file.", Warn);
                    return;
                }

                if (!Version.TryParse(remoteVersionText, out var remoteVersion))
                {
                    if (!silent) SetStatus("Update file has an invalid version number.", Warn);
                    return;
                }

                if (remoteVersion <= CurrentVersion)
                {
                    if (!silent) SetStatus("You're already on the latest version.", Good);
                    return;
                }

                var message = $"Version {remoteVersion} is available (you're on {CurrentVersion}).";
                if (!string.IsNullOrEmpty(notes)) message += "\r\n\r\n" + notes;
                message += "\r\n\r\nUpdate now? Retexture Studio will close and reopen.";
                var res = MessageBox.Show(message, "Update available", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (res != DialogResult.Yes)
                {
                    if (!silent) SetStatus($"Version {remoteVersion} is available whenever you're ready.", Warn);
                    return;
                }

                await DownloadAndInstallUpdateAsync(downloadUrl);
            }
            catch (Exception ex)
            {
                if (!silent) SetStatus("Could not check for updates: " + ex.Message, Warn);
            }
            finally
            {
                updateCheckInProgress = false;
            }
        }

        private string ExtractJsonValue(string json, string key)
        {
            var match = System.Text.RegularExpressions.Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            if (!match.Success) return null;
            return match.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        private async System.Threading.Tasks.Task DownloadAndInstallUpdateAsync(string downloadUrl)
        {
            string tempZip = null;
            string stagingDir = null;
            try
            {
                SetStatus("Downloading update...", Accent2);
                tempZip = Path.Combine(Path.GetTempPath(), "RetextureStudioUpdate_" + Guid.NewGuid().ToString("N") + ".zip");
                using (var client = new System.Net.WebClient())
                {
                    client.Headers.Add("User-Agent", "RetextureStudio");
                    await client.DownloadFileTaskAsync(new Uri(downloadUrl), tempZip);
                }

                SetStatus("Extracting update...", Accent2);
                stagingDir = Path.Combine(Path.GetTempPath(), "RetextureStudioUpdate_" + Guid.NewGuid().ToString("N"));
                ZipFile.ExtractToDirectory(tempZip, stagingDir);

                var entries = Directory.GetFileSystemEntries(stagingDir);
                var sourceDir = stagingDir;
                if (entries.Length == 1 && Directory.Exists(entries[0]))
                {
                    sourceDir = entries[0];
                }

                var installDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
                var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                var currentPid = System.Diagnostics.Process.GetCurrentProcess().Id;
                var scriptPath = Path.Combine(Path.GetTempPath(), "RetextureStudioUpdate_" + Guid.NewGuid().ToString("N") + ".bat");

                var script = new StringBuilder();
                script.AppendLine("@echo off");
                script.AppendLine(":wait");
                script.AppendLine("tasklist /fi \"PID eq " + currentPid + "\" | find \"" + currentPid + "\" >nul");
                script.AppendLine("if not errorlevel 1 (timeout /t 1 /nobreak >nul & goto wait)");
                script.AppendLine("xcopy \"" + sourceDir + "\\*\" \"" + installDir + "\\\" /E /I /Y /Q");
                script.AppendLine("start \"\" \"" + exePath + "\"");
                script.AppendLine("rmdir /s /q \"" + stagingDir + "\"");
                script.AppendLine("del \"" + tempZip + "\"");
                script.AppendLine("(goto) 2>nul & del \"%~f0\"");
                File.WriteAllText(scriptPath, script.ToString());

                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = scriptPath,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                try
                {
                    System.Diagnostics.Process.Start(startInfo);
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    startInfo.Verb = "runas";
                    System.Diagnostics.Process.Start(startInfo);
                }

                Application.Exit();
            }
            catch (Exception ex)
            {
                SetStatus("Update failed: " + ex.Message, Warn);
                try { if (tempZip != null && File.Exists(tempZip)) File.Delete(tempZip); } catch { }
                try { if (stagingDir != null && Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true); } catch { }
            }
        }
        #endregion

        #region Build UI
        private void BuildUI()
        {
            this.Text = "Retexture Studio";
            this.AutoScaleMode = AutoScaleMode.None;
            this.ClientSize = new Size(1280, 820);
            this.MinimumSize = new Size(1050, 700);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Ink;
            this.ForeColor = White;
            this.FormBorderStyle = FormBorderStyle.None;
            this.Font = new Font("Segoe UI", 9);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            this.UpdateStyles();

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Ink,
                Padding = new Padding(0)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 224));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            this.Controls.Add(root);

            sidebar = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(12, 13, 17), Padding = new Padding(12, 14, 12, 12) };
            root.Controls.Add(sidebar, 0, 0);

            var brandPanel = new Panel { Dock = DockStyle.Top, Height = 62, BackColor = Color.Transparent };
            sidebar.Controls.Add(brandPanel);
            var brandIcon = new Panel { Location = new Point(8, 7), Size = new Size(34, 34) };
            AddGlassPanel(brandIcon, 9, Accent, Color.FromArgb(61, 74, 190));
            brandPanel.Controls.Add(brandIcon);
            var brandImage = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
            if (File.Exists(LogoPath)) brandImage.Image = ReadImageSafe(LogoPath);
            if (brandImage.Image != null) brandIcon.Controls.Add(brandImage);
            else brandIcon.Controls.Add(new Label { Text = "R", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = White, Font = new Font("Segoe UI", 11, FontStyle.Bold), BackColor = Color.Transparent });
            brandPanel.Controls.Add(new Label { Text = "Retexture", Location = new Point(52, 6), Size = new Size(150, 22), ForeColor = White, Font = new Font("Segoe UI", 11, FontStyle.Bold), BackColor = Color.Transparent });
            brandPanel.Controls.Add(new Label { Text = "for Roblox", Location = new Point(52, 29), Size = new Size(150, 17), ForeColor = Dim, Font = new Font("Segoe UI", 7.5f), BackColor = Color.Transparent });

            var nav = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 8, 0, 8)
            };
            sidebar.Controls.Add(nav);
            nav.BringToFront();

            var navItems = new List<Control>();

            var home = NewNavigationItem("Home", "⌂", true);
            navItems.Add(home);
            activeNavigationPanel = home;
            navItems.Add(NewSectionLabel("Textures"));
            var targetPanels = new List<Panel>();
            foreach (var item in Textures)
            {
                var targetPanel = NewNavigationItem(
                    (string)item.Name,
                    (string)item.Icon,
                    (string)item.Id == (string)CurrentItem.Id);
                targetPanel.Tag = item;
                EventHandler selectTarget = (s, e) =>
                {
                    foreach (var other in targetPanels)
                    {
                        other.Tag = other.Tag == CurrentItem;
                        other.BackColor = Color.Transparent;
                        foreach (Control child in other.Controls) child.ForeColor = Color.FromArgb(190, 194, 204);
                    }
                    var selected = targetPanel;
                    ShowTexturePage();
                    selected.BackColor = Color.FromArgb(39, 39, 47);
                    selected.Tag = true;
                    foreach (Control child in selected.Controls) child.ForeColor = White;
                    CurrentItem = Textures.First(t => t.Name == selected.Controls.OfType<Label>().Last().Text);
                    SelectedPresetPath = null;
                    if (SelectedPreview != null && SelectedPreview.Image != null) { SelectedPreview.Image.Dispose(); SelectedPreview.Image = null; }
                    if (SelectedName != null) SelectedName.Text = "No preset selected";
                    if (SelectedInfo != null) SelectedInfo.Text = "Select a library card to preview it.";
                    if (ApplyButton != null) ApplyButton.Enabled = false;
                    if (Headline != null) Headline.Text = "Retexture your Roblox, one click at a time.";
                    RefreshTargetView();
                };
                targetPanel.Click += selectTarget;
                foreach (Control child in targetPanel.Controls) child.Click += selectTarget;
                targetPanels.Add(targetPanel);
                navItems.Add(targetPanel);
            }
            navItems.Add(NewSectionLabel("Library"));
            var communityNav = NewNavigationItem("Community", "♙");
            EventHandler communityClick = (s, e) => OpenCommunity();
            communityNav.Click += communityClick;
            foreach (Control child in communityNav.Controls) child.Click += communityClick;
            navItems.Add(communityNav);
            var myPresetsNav = NewNavigationItem("My Presets", "▱");
            EventHandler myPresetsClick = (s, e) => ShowMyPresetsPage();
            myPresetsNav.Click += myPresetsClick;
            foreach (Control child in myPresetsNav.Controls) child.Click += myPresetsClick;
            navItems.Add(myPresetsNav);
            navItems.Add(NewSectionLabel("Account"));
            var notificationsNav = NewNavigationItem("Notifications", "♧");
            navItems.Add(notificationsNav);
            var settingsNav = NewNavigationItem("Settings", "⚙");
            EventHandler settingsClick = (s, e) => ShowSettings();
            settingsNav.Click += settingsClick;
            foreach (Control child in settingsNav.Controls) child.Click += settingsClick;
            navItems.Add(settingsNav);
            var moderationNav = NewNavigationItem("Moderation", "♢");
            navItems.Add(moderationNav);
            for (int i = navItems.Count - 1; i >= 0; i--) nav.Controls.Add(navItems[i]);
            StartInteractionAnimation();

            var right = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Ink,
                Padding = new Padding(0)
            };
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.Controls.Add(right, 1, 0);

            var titleBar = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(10, 11, 14), Padding = new Padding(20, 0, 18, 0) };
            right.Controls.Add(titleBar, 0, 0);
            VersionChip = NewPillLabel("Checking Roblox...", 205);
            VersionChip.Dock = DockStyle.Right;
            VersionChip.Margin = new Padding(0, 15, 36, 15);
            titleBar.Controls.Add(VersionChip);
            var minBtn = NewGlassButton("−", 32, 32, false, false, 8);
            minBtn.Dock = DockStyle.Right;
            minBtn.Margin = new Padding(0, 13, 8, 13);
            minBtn.Click += (s, e) => WindowState = FormWindowState.Minimized;
            titleBar.Controls.Add(minBtn);
            var closeBtn = NewGlassButton("×", 32, 32, false, false, 8);
            closeBtn.Dock = DockStyle.Right;
            closeBtn.Margin = new Padding(0, 13, 0, 13);
            closeBtn.Click += (s, e) => Close();
            titleBar.Controls.Add(closeBtn);
            EnableDrag(titleBar);

            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(8, 9, 12), Padding = new Padding(34, 28, 34, 40) };
            right.Controls.Add(scroll, 0, 1);
            pageHost = scroll;
            var content = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowOnly,
                Dock = DockStyle.Top,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = Color.Transparent,
                Padding = new Padding(0)
            };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 260));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 560));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
            content.MinimumSize = new Size(0, 260 + 82 + 560 + 88);
            texturePage = content;
            scroll.Controls.Add(content);

            var hero = NewDashboardCard(20, Color.FromArgb(25, 32, 46), Color.FromArgb(14, 15, 20));
            hero.Dock = DockStyle.Fill;
            hero.Margin = new Padding(0, 0, 0, 14);
            hero.Padding = new Padding(34, 28, 34, 24);
            content.Controls.Add(hero, 0, 0);
            var heroTag = new Label { Text = "●  CUSTOM TEXTURES", Location = new Point(34, 25), AutoSize = true, ForeColor = Accent2, Font = new Font("Segoe UI", 8, FontStyle.Bold), BackColor = Color.Transparent };
            hero.Controls.Add(heroTag);
            Headline = new Label { Text = "Retexture your Roblox,\r\none click at a time.", Location = new Point(34, 63), Size = new Size(760, 82), AutoSize = false, ForeColor = White, Font = new Font("Segoe UI", 22, FontStyle.Bold), BackColor = Color.Transparent };
            hero.Controls.Add(Headline);
            hero.Controls.Add(new Label { Text = "Browse your presets and swap in new cursors, emote wheels,\r\nand skyboxes. Just replace the file — Roblox does the rest.", Location = new Point(36, 145), Size = new Size(570, 42), ForeColor = Muted, Font = new Font("Segoe UI", 10), BackColor = Color.Transparent });
            var browseButton = NewGlassButton("Browse presets  ↗", 136, 36, true);
            browseButton.Location = new Point(34, 204);
            browseButton.Tag = "accent";
            browseButton.Click += (s, e) => SearchBox.Focus();
            hero.Controls.Add(browseButton);
            var communityButton = NewGlassButton("♙  Community", 116, 36, false);
            communityButton.Location = new Point(178, 204);
            communityButton.Click += (s, e) => OpenCommunity();
            hero.Controls.Add(communityButton);

            var targetCard = NewDashboardCard(16, Color.FromArgb(27, 31, 42), Color.FromArgb(16, 18, 24));
            targetCard.Dock = DockStyle.Fill;
            targetCard.Margin = new Padding(0, 0, 0, 14);
            content.Controls.Add(targetCard, 0, 1);
            TargetTitle = new Label { Text = CurrentItem.Name, Location = new Point(22, 17), AutoSize = true, ForeColor = White, Font = new Font("Segoe UI", 11, FontStyle.Bold), BackColor = Color.Transparent };
            targetCard.Controls.Add(TargetTitle);
            TargetHint = new Label { Text = "", Location = new Point(22, 43), AutoSize = true, ForeColor = Muted, Font = new Font("Segoe UI", 8), BackColor = Color.Transparent };
            targetCard.Controls.Add(TargetHint);

            var libraryCard = NewDashboardCard(18, Color.FromArgb(27, 31, 42), Color.FromArgb(16, 18, 24));
            libraryCard.Dock = DockStyle.Fill;
            libraryCard.Margin = new Padding(0, 0, 0, 14);
            libraryCard.Padding = new Padding(18, 14, 18, 14);
            libraryCard.MinimumSize = new Size(0, 560);
            content.Controls.Add(libraryCard, 0, 2);
            var libraryHeader = new Panel { Dock = DockStyle.Top, Height = 22, BackColor = Color.Transparent };
            libraryCard.Controls.Add(libraryHeader);
            libraryHeader.Controls.Add(new Label { Text = "Textures", Location = new Point(0, 2), AutoSize = true, ForeColor = White, Font = new Font("Segoe UI", 12, FontStyle.Bold), BackColor = Color.Transparent });
            CountLabel = new Label { Text = "0 presets", Location = new Point(82, 6), AutoSize = true, ForeColor = Dim, Font = new Font("Segoe UI", 8), BackColor = Color.Transparent };
            libraryHeader.Controls.Add(CountLabel);
            var importBtn = NewGlassButton("+  Import", 92, 20, true);
            importBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            importBtn.Location = new Point(0, 0);
            importBtn.Click += (s, e) => ImportPreset();
            libraryHeader.Resize += (s, e) => { importBtn.Left = libraryHeader.ClientSize.Width - importBtn.Width; };
            libraryHeader.Controls.Add(importBtn);
            SearchBox = new TextBox { Width = 170, Height = 25, BackColor = Field, ForeColor = Muted, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 8), Text = "Search presets...", Anchor = AnchorStyles.Top | AnchorStyles.Right };
            SearchBox.Location = new Point(0, 3);
            libraryHeader.Resize += (s, e) => SearchBox.Left = Math.Max(190, libraryHeader.ClientSize.Width - importBtn.Width - SearchBox.Width - 12);
            libraryHeader.Controls.Add(SearchBox);
            LibraryFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoScroll = true, FlowDirection = FlowDirection.LeftToRight, BackColor = Color.Transparent, Padding = new Padding(0, 24, 0, 24) };
            libraryCard.Controls.Add(LibraryFlow);

            var actionCard = NewDashboardCard(16, Color.FromArgb(27, 31, 42), Color.FromArgb(16, 18, 24));
            actionCard.Dock = DockStyle.Fill;
            content.Controls.Add(actionCard, 0, 3);
            SelectedPreview = new PictureBox { Location = new Point(18, 20), Size = new Size(48, 48), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Field };
            SetRoundedRegion(SelectedPreview, 12); actionCard.Controls.Add(SelectedPreview);
            SelectedName = new Label { Text = "No preset selected", Location = new Point(80, 19), AutoSize = true, ForeColor = White, Font = new Font("Segoe UI", 9, FontStyle.Bold), BackColor = Color.Transparent };
            actionCard.Controls.Add(SelectedName);
            SelectedInfo = new Label { Text = "Select a library card to preview it.", Location = new Point(80, 43), AutoSize = true, ForeColor = Dim, Font = new Font("Segoe UI", 7.5f), BackColor = Color.Transparent };
            actionCard.Controls.Add(SelectedInfo);
            ApplyButton = NewGlassButton("Apply selected", 126, 36, false, true); ApplyButton.Anchor = AnchorStyles.Top | AnchorStyles.Right; ApplyButton.Location = new Point(0, 25); ApplyButton.Enabled = false; ApplyButton.Click += (s, e) => ApplyPreset(SelectedPresetPath); actionCard.Resize += (s, e) => ApplyButton.Left = actionCard.ClientSize.Width - 390; actionCard.Controls.Add(ApplyButton);
            RestoreButton = NewGlassButton("Restore", 100, 36, false); RestoreButton.Anchor = AnchorStyles.Top | AnchorStyles.Right; RestoreButton.Location = new Point(0, 25); RestoreButton.Enabled = false; RestoreButton.Click += (s, e) => RestoreOriginal(); actionCard.Resize += (s, e) => RestoreButton.Left = actionCard.ClientSize.Width - 254; actionCard.Controls.Add(RestoreButton);
            AccessButton = NewGlassButton("Check access", 112, 36, false); AccessButton.Anchor = AnchorStyles.Top | AnchorStyles.Right; AccessButton.Location = new Point(0, 25); AccessButton.Enabled = false; AccessButton.Click += (s, e) => CheckAccess(); actionCard.Resize += (s, e) => AccessButton.Left = actionCard.ClientSize.Width - 136; actionCard.Controls.Add(AccessButton);
            StatusLabel = new Label { Text = "Ready - pick a target and choose a preset.", Location = new Point(20, 71), AutoSize = true, ForeColor = Good, Font = new Font("Segoe UI", 7.8f, FontStyle.Bold), BackColor = Color.Transparent };
            actionCard.Controls.Add(StatusLabel);

            SearchBox.GotFocus += (s, e) => { if (SearchBox.Text == "Search presets...") { SearchBox.Text = ""; SearchBox.ForeColor = White; } };
            SearchBox.LostFocus += (s, e) => { if (string.IsNullOrEmpty(SearchBox.Text)) { SearchBox.Text = "Search presets..."; SearchBox.ForeColor = Muted; } };
            SearchBox.TextChanged += (s, e) => { if (SearchBox.Text != "Search presets...") RefreshLibrary(); };
            this.FormClosed += (s, e) => { if (interactionTimer != null) { interactionTimer.Stop(); interactionTimer.Dispose(); interactionTimer = null; } };
            EnableDrag(brandPanel);
        }

        private IEnumerable<Control> ControlsRecursive()
        {
            var stack = new Stack<Control>(this.Controls.Cast<Control>());
            while (stack.Count > 0)
            {
                var c = stack.Pop();
                yield return c;
                foreach (Control child in c.Controls) stack.Push(child);
            }
        }

        private void EnableDrag(Control control)
        {
            bool down = false; Point last = Point.Empty;
            control.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { down = true; last = Cursor.Position; } };
            control.MouseMove += (s, e) => { if (down) { var p = Cursor.Position; this.Left += p.X - last.X; this.Top += p.Y - last.Y; last = p; } };
            control.MouseUp += (s, e) => { down = false; };
        }

        private void CheckAccess()
        {
            var targetPaths = GetRobloxTargetPaths().Where(p => File.Exists(p) || Directory.Exists(p)).ToArray();
            if (targetPaths.Length == 0) { SetStatus("Target files are missing. Launch Roblox once, then reopen Retexture Studio.", Warn); return; }
            try
            {
                string probePath = targetPaths[0];
                if ((bool)CurrentItem.Sky) probePath = Directory.GetFiles(probePath).First();
                using (var stream = File.Open(probePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read)) { }
                SetStatus("Access is ready - textures can be updated.", Good);
            }
            catch { SetStatus("Access is blocked - close Roblox or run Retexture Studio as Administrator.", Warn); }
        }
        #endregion
    }
}