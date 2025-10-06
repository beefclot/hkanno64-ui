using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace HKAnnoGui
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
    }

    public class MainForm : Form
    {
    private Button selectButton = null!;
    private Label fileLabel = null!;
    private Button dumpButton = null!;
    private Button updateButton = null!;
    private TextBox editor = null!;
    private StatusStrip statusStrip = null!;
    private ToolStripStatusLabel statusLabel = null!;
    private ToolStripProgressBar progressBar = null!;

    private string? selectedHkx;
    private string? tempAnnoPath;
    private string tempWorkDir = string.Empty;
    private string hkannoDir = string.Empty;
    private string hkannoExePath = string.Empty;
        public MainForm()
        {
            Text = "HKX Annotation Editor (Standalone)";
            Width = 1000;
            Height = 750;

            // Try to set window/taskbar icon (prefers ICO, falls back to PNG converted to ICO at runtime)
            TrySetAppIcon();

            // Extract hkanno resources to temp dir at startup
            tempWorkDir = Path.Combine(Path.GetTempPath(), "HKAnnoGui_" + Guid.NewGuid().ToString("N"));
            hkannoDir = Path.Combine(tempWorkDir, "hkanno");
            Directory.CreateDirectory(hkannoDir);
            ExtractResourceFolder("hkanno/", hkannoDir);
            hkannoExePath = Path.Combine(hkannoDir, "hkanno64.exe");

            BuildUi();
            // Apply dark mode if the system is set to dark
            if (IsSystemDarkMode())
            {
                ApplyDarkTheme(this);
            }
            UpdateUiState();

            AllowDrop = true;
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;

            // Keyboard shortcuts
            KeyPreview = true;
            KeyDown += OnKeyDown;

            FormClosing += OnFormClosing;
        }

        private void BuildUi()
        {
            var topPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 4,
                RowCount = 1,
                AutoSize = true,
                Padding = new Padding(8),
            };
            topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            selectButton = new Button { Text = "Select HKX..." };
            selectButton.Click += (s, e) => SelectHkx();
            topPanel.Controls.Add(selectButton, 0, 0);

            fileLabel = new Label { Text = "Drop an .hkx here or click Select", AutoSize = true };
            topPanel.Controls.Add(fileLabel, 1, 0);

            dumpButton = new Button { Text = "Dump", AutoSize = true };
            dumpButton.Click += (s, e) => DumpAnnotations();
            topPanel.Controls.Add(dumpButton, 2, 0);

            updateButton = new Button { Text = "Update", AutoSize = true };
            updateButton.Click += (s, e) => UpdateHkx();
            topPanel.Controls.Add(updateButton, 3, 0);

            editor = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Both,
                Font = new System.Drawing.Font("Consolas", 10),
                Dock = DockStyle.Fill,
                AcceptsTab = true,
                WordWrap = false,
            };
            editor.TextChanged += (s, e) => { statusLabel.Text = "Modified"; };

            statusStrip = new StatusStrip();
            statusLabel = new ToolStripStatusLabel("Ready");
            progressBar = new ToolStripProgressBar
            {
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30,
                Visible = false,
                Alignment = ToolStripItemAlignment.Right,
                Width = 120
            };
            statusStrip.Items.Add(statusLabel);
            statusStrip.Items.Add(progressBar);

            var container = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
            };
            container.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            container.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            container.Controls.Add(topPanel, 0, 0);
            container.Controls.Add(editor, 0, 1);

            Controls.Add(container);
            Controls.Add(statusStrip);
        }

        private void UpdateUiState()
        {
            bool hasFile = !string.IsNullOrEmpty(selectedHkx);
            dumpButton.Enabled = hasFile;
            updateButton.Enabled = hasFile && !string.IsNullOrEmpty(editor.Text);
            // no Save button anymore
        }

        private void SelectHkx()
        {
            using var ofd = new OpenFileDialog
            {
                Title = "Select HKX Animation File",
                Filter = "HKX files (*.hkx)|*.hkx|All files (*.*)|*.*",
                RestoreDirectory = true
            };
            if (ofd.ShowDialog(this) == DialogResult.OK)
            {
                selectedHkx = ofd.FileName;
                fileLabel.Text = Path.GetFileName(selectedHkx);
                statusLabel.Text = "HKX selected";
                UpdateUiState();
                // Auto-dump annotations after selecting a file
                DumpAnnotations();
            }
        }

        private void DumpAnnotations()
        {
            StartBusy("Dumping annotations...");
            if (string.IsNullOrEmpty(selectedHkx))
            {
                statusLabel.Text = "No HKX selected";
                EndBusy();
                return;
            }

            EnsureTempAnno();

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = hkannoExePath,
                    Arguments = $"dump -o \"{tempAnnoPath}\" \"{selectedHkx}\"",
                    WorkingDirectory = hkannoDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var proc = Process.Start(psi)!;
                string stdout = proc.StandardOutput.ReadToEnd();
                string stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit();

                if (proc.ExitCode == 0)
                {
                    editor.Text = File.ReadAllText(tempAnnoPath!);
                    statusLabel.Text = "Annotations loaded";
                }
                else
                {
                    statusLabel.Text = "Dump failed";
                }
            }
            catch (Exception ex)
            {
                statusLabel.Text = $"Dump error: {ex.Message}";
            }
            finally
            {
                UpdateUiState();
                EndBusy();
            }
        }

        // Save button removed; saving happens automatically during update

        private void UpdateHkx()
        {
            StartBusy("Updating HKX...");
            if (string.IsNullOrEmpty(selectedHkx)) { EndBusy(); return; }
            if (string.IsNullOrEmpty(tempAnnoPath)) EnsureTempAnno();

            // No prompts; always save current editor content and update
            if (string.IsNullOrEmpty(tempAnnoPath)) EnsureTempAnno();
            // Write without UTF-8 BOM to avoid confusing the CLI parser
            try { File.WriteAllText(tempAnnoPath!, editor.Text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)); }
            catch (Exception ex)
            {
                statusLabel.Text = $"Save error: {ex.Message}";
                return;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = hkannoExePath,
                    Arguments = $"update -i \"{tempAnnoPath}\" \"{selectedHkx}\"",
                    WorkingDirectory = hkannoDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var proc = Process.Start(psi)!;
                string stdout = proc.StandardOutput.ReadToEnd();
                string stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit();

                if (proc.ExitCode == 0)
                {
                    statusLabel.Text = "HKX updated; re-dumping...";
                    // Auto-dump after a successful update
                    DumpAnnotations();
                    statusLabel.Text = "HKX updated and reloaded";
                }
                else
                {
                    statusLabel.Text = "Update failed";
                }
            }
            catch (Exception ex)
            {
                statusLabel.Text = $"Update error: {ex.Message}";
            }
            finally
            {
                EndBusy();
            }
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.S)
            {
                // Ctrl+S triggers update
                UpdateHkx();
                e.SuppressKeyPress = true;
            }
            else if (e.Control && e.KeyCode == Keys.R)
            {
                // Ctrl+R re-dumps the current HKX
                DumpAnnotations();
                e.SuppressKeyPress = true;
            }
        }

        private void StartBusy(string message)
        {
            statusLabel.Text = message;
            progressBar.Visible = true;
            Application.DoEvents();
        }

        private void EndBusy(string? finalMessage = null)
        {
            if (!string.IsNullOrEmpty(finalMessage))
                statusLabel.Text = finalMessage!;
            progressBar.Visible = false;
        }

        private void EnsureTempAnno()
        {
            if (string.IsNullOrEmpty(tempAnnoPath))
            {
                tempAnnoPath = Path.Combine(tempWorkDir, "anno.txt");
            }
        }

        private void OnDragEnter(object? sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
                e.Effect = files.Any(f => f.EndsWith(".hkx", StringComparison.OrdinalIgnoreCase)) ? DragDropEffects.Copy : DragDropEffects.None;
            }
        }

        private void OnDragDrop(object? sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
                var hkx = files.FirstOrDefault(f => f.EndsWith(".hkx", StringComparison.OrdinalIgnoreCase));
                if (hkx != null)
                {
                    selectedHkx = hkx;
                    fileLabel.Text = Path.GetFileName(selectedHkx);
                    statusLabel.Text = "HKX selected";
                    UpdateUiState();
                    // Auto-dump annotations after dropping a file
                    DumpAnnotations();
                }
            }
        }

        private void OnFormClosing(object? sender, FormClosingEventArgs e)
        {
            try
            {
                if (Directory.Exists(tempWorkDir))
                {
                    Directory.Delete(tempWorkDir, true);
                }
            }
            catch
            {
                // ignore cleanup errors
            }
        }

        private void TrySetAppIcon()
        {
            try
            {
                string baseDir = AppContext.BaseDirectory;
                string assetsDir = Path.Combine(baseDir, "Assets");
                string icoPath = Path.Combine(assetsDir, "app.ico");
                string pngPath = Path.Combine(assetsDir, "app.png");

                if (File.Exists(icoPath))
                {
                    this.Icon = new System.Drawing.Icon(icoPath);
                    return;
                }
                if (File.Exists(pngPath))
                {
                    using var ms = PngToIco(File.ReadAllBytes(pngPath));
                    this.Icon = new System.Drawing.Icon(ms);
                    return;
                }

                // Fallback to executable's associated icon (compiled via ApplicationIcon)
                var exePath = Application.ExecutablePath;
                var assoc = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                if (assoc != null) this.Icon = assoc;
            }
            catch
            {
                // non-fatal if icon can't be set
            }
        }

        private static MemoryStream PngToIco(byte[] pngBytes)
        {
            // Minimal ICO writer that embeds the PNG as-is (supported on modern Windows)
            // Determines width/height from PNG IHDR to populate directory entry (0 means 256)
            int width = 0, height = 0;
            try
            {
                using var br = new BinaryReader(new MemoryStream(pngBytes, writable: false));
                // PNG signature (8 bytes)
                br.ReadBytes(8);
                // First chunk should be IHDR
                int ihdrLen = ReadInt32BE(br);
                var type = new string(br.ReadChars(4));
                if (type == "IHDR" && ihdrLen >= 8)
                {
                    width = ReadInt32BE(br);
                    height = ReadInt32BE(br);
                }
            }
            catch { }

            var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            {
                // ICONDIR
                bw.Write((ushort)0);     // reserved
                bw.Write((ushort)1);     // type = icon
                bw.Write((ushort)1);     // count = 1

                // ICONDIRENTRY
                byte bWidth = (byte)(width >= 256 || width == 0 ? 0 : Math.Min(255, width));
                byte bHeight = (byte)(height >= 256 || height == 0 ? 0 : Math.Min(255, height));
                bw.Write(bWidth);        // width (0 means 256)
                bw.Write(bHeight);       // height (0 means 256)
                bw.Write((byte)0);       // color count
                bw.Write((byte)0);       // reserved
                bw.Write((ushort)1);     // planes
                bw.Write((ushort)32);    // bit count
                bw.Write(pngBytes.Length);  // bytes in resource
                bw.Write(6 + 16);        // image data offset (ICONDIR + ICONDIRENTRY)

                // Image data (PNG)
                bw.Write(pngBytes);
            }
            ms.Position = 0;
            return ms;
        }

        private static int ReadInt32BE(BinaryReader br)
        {
            var bytes = br.ReadBytes(4);
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            return BitConverter.ToInt32(bytes, 0);
        }

        // --- Simple Dark Mode Helpers ---
        private static bool IsSystemDarkMode()
        {
            try
            {
                // Windows stores app theme preference in registry: 0 = dark, 1 = light
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
                if (key != null)
                {
                    object? val = key.GetValue("AppsUseLightTheme");
                    if (val is int i)
                    {
                        return i == 0; // dark mode
                    }
                }
            }
            catch { }
            return false;
        }

        private static void ApplyDarkTheme(Control root)
        {
            var bg = System.Drawing.Color.FromArgb(32, 32, 32);
            var panelBg = System.Drawing.Color.FromArgb(40, 40, 40);
            var fg = System.Drawing.Color.Gainsboro;

            void StyleControl(Control c)
            {
                switch (c)
                {
                    case TextBox tb:
                        tb.BackColor = System.Drawing.Color.FromArgb(24, 24, 24);
                        tb.ForeColor = fg;
                        tb.BorderStyle = BorderStyle.FixedSingle;
                        break;
                    case Button b:
                        b.BackColor = System.Drawing.Color.FromArgb(64, 64, 64);
                        b.ForeColor = fg;
                        b.FlatStyle = FlatStyle.System; // lean on system for better visuals
                        break;
                    case StatusStrip ss:
                        ss.BackColor = panelBg;
                        ss.ForeColor = fg;
                        foreach (ToolStripItem item in ss.Items)
                        {
                            item.ForeColor = fg;
                        }
                        break;
                    case TableLayoutPanel tlp:
                        tlp.BackColor = panelBg;
                        break;
                    default:
                        c.BackColor = bg;
                        c.ForeColor = fg;
                        break;
                }
                foreach (Control child in c.Controls)
                {
                    StyleControl(child);
                }
            }

            StyleControl(root);
        }

        private static void ExtractResourceFolder(string resourcePrefix, string targetDir)
        {
            var asm = Assembly.GetExecutingAssembly();
            // Resource names may be prefixed by the assembly's root namespace. We'll find matches that contain our logical names.
            var all = asm.GetManifestResourceNames();
            var matches = all.Where(n => n.Contains(resourcePrefix, StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var name in matches)
            {
                // Determine the relative path by taking the substring starting at the resourcePrefix
                int idx = name.IndexOf(resourcePrefix, StringComparison.OrdinalIgnoreCase);
                string rel = name.Substring(idx + resourcePrefix.Length);
                string outPath = Path.Combine(targetDir, rel.Replace('/', Path.DirectorySeparatorChar));

                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                using var s = asm.GetManifestResourceStream(name)!;
                using var fs = File.Create(outPath);
                s.CopyTo(fs);
            }
        }
    }
}
