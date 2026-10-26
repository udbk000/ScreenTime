using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("ScreenTime Setup")]
[assembly: AssemblyDescription("Standalone Installer für ScreenTime (Windows 10/11)")]
[assembly: AssemblyCompany("udbk000")]
[assembly: AssemblyProduct("ScreenTime")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace ScreenTimeSetup
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool uninstallMode = false;
            foreach (string a in args)
            {
                if (string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a, "/uninstall", StringComparison.OrdinalIgnoreCase))
                {
                    uninstallMode = true;
                }
            }

            Application.Run(new InstallerForm(uninstallMode));
        }
    }

    public sealed class InstallerForm : Form
    {
        private readonly TextBox _installPathBox;
        private readonly CheckBox _desktopShortcutCheck;
        private readonly CheckBox _startMenuShortcutCheck;
        private readonly CheckBox _launchAfterInstallCheck;
        private readonly ProgressBar _progressBar;
        private readonly Label _statusLabel;
        private readonly Button _installButton;
        private readonly Button _uninstallButton;

        public InstallerForm(bool startInUninstallMode)
        {
            this.Text = "ScreenTime – Installation (v1.1.0)";
            this.ClientSize = new Size(540, 360);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(20, 20, 24);
            this.ForeColor = Color.White;
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            try
            {
                this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
                // Ignore icon extraction errors
            }

            var titleLabel = new Label
            {
                Text = "ScreenTime für Windows",
                Font = new Font("Segoe UI", 18f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(24, 20),
                AutoSize = true
            };
            this.Controls.Add(titleLabel);

            var subLabel = new Label
            {
                Text = "Eigenständiges Installationsprogramm (enthält alle benötigten Laufzeitdateien)\nVersion 1.1.0 • GitHub: github.com/udbk000",
                Font = new Font("Segoe UI", 9.2f, FontStyle.Regular),
                ForeColor = Color.FromArgb(185, 190, 200),
                Location = new Point(26, 56),
                Size = new Size(490, 38)
            };
            this.Controls.Add(subLabel);

            var pathHeader = new Label
            {
                Text = "Installationsverzeichnis:",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(26, 108),
                AutoSize = true
            };
            this.Controls.Add(pathHeader);

            string defaultDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                "ScreenTime");

            _installPathBox = new TextBox
            {
                Text = defaultDir,
                Location = new Point(26, 132),
                Size = new Size(390, 26),
                BackColor = Color.FromArgb(34, 34, 40),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            this.Controls.Add(_installPathBox);

            var browseBtn = new Button
            {
                Text = "Wählen...",
                Location = new Point(424, 130),
                Size = new Size(90, 27),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 45, 52),
                ForeColor = Color.White
            };
            browseBtn.Click += OnBrowseClicked;
            this.Controls.Add(browseBtn);

            _desktopShortcutCheck = new CheckBox
            {
                Text = "Desktop-Verknüpfung erstellen",
                Checked = true,
                Location = new Point(26, 172),
                AutoSize = true,
                ForeColor = Color.White
            };
            this.Controls.Add(_desktopShortcutCheck);

            _startMenuShortcutCheck = new CheckBox
            {
                Text = "Eintrag im Windows-Startmenü erstellen",
                Checked = true,
                Location = new Point(26, 198),
                AutoSize = true,
                ForeColor = Color.White
            };
            this.Controls.Add(_startMenuShortcutCheck);

            _launchAfterInstallCheck = new CheckBox
            {
                Text = "ScreenTime direkt nach der Installation starten",
                Checked = true,
                Location = new Point(26, 224),
                AutoSize = true,
                ForeColor = Color.White
            };
            this.Controls.Add(_launchAfterInstallCheck);

            _statusLabel = new Label
            {
                Text = "Bereit zur Installation.",
                Location = new Point(26, 258),
                Size = new Size(488, 20),
                ForeColor = Color.FromArgb(180, 200, 230)
            };
            this.Controls.Add(_statusLabel);

            _progressBar = new ProgressBar
            {
                Location = new Point(26, 281),
                Size = new Size(488, 16),
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };
            this.Controls.Add(_progressBar);

            _uninstallButton = new Button
            {
                Text = "Deinstallieren",
                Location = new Point(26, 312),
                Size = new Size(130, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(48, 48, 55),
                ForeColor = Color.White
            };
            _uninstallButton.Click += OnUninstallClicked;
            this.Controls.Add(_uninstallButton);

            _installButton = new Button
            {
                Text = "Jetzt installieren",
                Location = new Point(344, 312),
                Size = new Size(170, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 120, 212),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
            };
            _installButton.Click += OnInstallClicked;
            this.Controls.Add(_installButton);

            if (startInUninstallMode)
            {
                this.Shown += (s, ev) => OnUninstallClicked(this, EventArgs.Empty);
            }
        }

        private void OnBrowseClicked(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Zielordner für ScreenTime auswählen";
                dlg.SelectedPath = _installPathBox.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _installPathBox.Text = dlg.SelectedPath;
                }
            }
        }

        private async void OnInstallClicked(object sender, EventArgs e)
        {
            string targetDir = _installPathBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(targetDir))
            {
                MessageBox.Show(this, "Bitte ein gültiges Installationsverzeichnis angeben.", "ScreenTime Setup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _installButton.Enabled = false;
            _uninstallButton.Enabled = false;
            _progressBar.Value = 5;
            _statusLabel.Text = "Beende laufende ScreenTime-Prozesse...";

            try
            {
                bool createDesktop = _desktopShortcutCheck.Checked;
                bool createStartMenu = _startMenuShortcutCheck.Checked;
                bool launchApp = _launchAfterInstallCheck.Checked;

                await Task.Run(() =>
                {
                    foreach (var proc in Process.GetProcessesByName("ScreenTime.App"))
                    {
                        try
                        {
                            proc.Kill();
                            proc.WaitForExit(3000);
                        }
                        catch
                        {
                            // Ignore
                        }
                    }

                    Directory.CreateDirectory(targetDir);

                    using (Stream zipStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
                    {
                        if (zipStream == null)
                        {
                            throw new InvalidOperationException("Das eingebettete Installationspaket (payload.zip) wurde nicht gefunden.");
                        }

                        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
                        {
                            int total = Math.Max(1, archive.Entries.Count);
                            int current = 0;

                            foreach (var entry in archive.Entries)
                            {
                                current++;
                                string fullPath = Path.Combine(targetDir, entry.FullName);
                                string dirName = Path.GetDirectoryName(fullPath);
                                if (!string.IsNullOrEmpty(dirName))
                                {
                                    Directory.CreateDirectory(dirName);
                                }

                                if (!string.IsNullOrEmpty(entry.Name))
                                {
                                    entry.ExtractToFile(fullPath, overwrite: true);
                                }

                                int pct = 10 + (int)((current / (double)total) * 80);
                                this.BeginInvoke((Action)(() =>
                                {
                                    _progressBar.Value = Math.Min(92, pct);
                                    _statusLabel.Text = string.Format("Entpacke Dateien ({0}/{1})...", current, total);
                                }));
                            }
                        }
                    }

                    string exePath = Path.Combine(targetDir, "ScreenTime.App.exe");
                    string icoPath = Path.Combine(targetDir, "Assets", "app.ico");

                    // Kopiere den Installer als Uninstaller in das Zielverzeichnis
                    string uninstallerPath = Path.Combine(targetDir, "ScreenTime-Uninstall.exe");
                    try
                    {
                        File.Copy(Application.ExecutablePath, uninstallerPath, overwrite: true);
                    }
                    catch
                    {
                        // Ignore
                    }

                    if (createDesktop)
                    {
                        string desktopLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "ScreenTime.lnk");
                        ShortcutHelper.CreateShortcut(desktopLink, exePath, targetDir, "ScreenTime – Aktive Bildschirmzeit-Analyse", icoPath);
                    }

                    if (createStartMenu)
                    {
                        string startMenuLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "ScreenTime.lnk");
                        ShortcutHelper.CreateShortcut(startMenuLink, exePath, targetDir, "ScreenTime – Aktive Bildschirmzeit-Analyse", icoPath);
                    }

                    RegisterUninstaller(targetDir, exePath, uninstallerPath);
                });

                _progressBar.Value = 100;
                _statusLabel.Text = "✓ Installation erfolgreich abgeschlossen!";

                string installedExe = Path.Combine(targetDir, "ScreenTime.App.exe");
                if (_launchAfterInstallCheck.Checked && File.Exists(installedExe))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = installedExe,
                        WorkingDirectory = targetDir,
                        UseShellExecute = true
                    });
                }

                MessageBox.Show(
                    this,
                    string.Format("ScreenTime wurde erfolgreich installiert unter:\n{0}", targetDir),
                    "Installation abgeschlossen",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                this.Close();
            }
            catch (Exception ex)
            {
                _statusLabel.Text = "Fehler bei der Installation.";
                MessageBox.Show(this, string.Format("Fehler bei der Installation:\n{0}", ex.Message), "ScreenTime Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _installButton.Enabled = true;
                _uninstallButton.Enabled = true;
            }
        }

        private void OnUninstallClicked(object sender, EventArgs e)
        {
            string targetDir = _installPathBox.Text.Trim();
            if (MessageBox.Show(
                this,
                string.Format("Möchtest du ScreenTime aus '{0}' wirklich deinstallieren?", targetDir),
                "ScreenTime deinstallieren",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            try
            {
                foreach (var proc in Process.GetProcessesByName("ScreenTime.App"))
                {
                    try
                    {
                        proc.Kill();
                        proc.WaitForExit(3000);
                    }
                    catch
                    {
                        // Ignore
                    }
                }

                string desktopLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "ScreenTime.lnk");
                if (File.Exists(desktopLink)) File.Delete(desktopLink);

                string startMenuLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "ScreenTime.lnk");
                if (File.Exists(startMenuLink)) File.Delete(startMenuLink);

                Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\ScreenTime", throwOnMissingSubKey: false);

                if (Directory.Exists(targetDir))
                {
                    try
                    {
                        Directory.Delete(targetDir, recursive: true);
                    }
                    catch
                    {
                        // Falls der Uninstaller selbst aus dem Ordner läuft
                    }
                }

                MessageBox.Show(this, "ScreenTime wurde erfolgreich deinstalliert.", "Deinstallation abgeschlossen", MessageBoxButtons.OK, MessageBoxIcon.Information);
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format("Fehler bei der Deinstallation:\n{0}", ex.Message), "ScreenTime Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void RegisterUninstaller(string installDir, string exePath, string uninstallerPath)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\ScreenTime"))
                {
                    if (key != null)
                    {
                        key.SetValue("DisplayName", "ScreenTime");
                        key.SetValue("DisplayVersion", "1.1.0");
                        key.SetValue("Publisher", "udbk000");
                        key.SetValue("DisplayIcon", exePath);
                        key.SetValue("InstallLocation", installDir);
                        key.SetValue("UninstallString", string.Format("\"{0}\" --uninstall", uninstallerPath));
                        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    }
                }
            }
            catch
            {
                // Ignore registry errors
            }
        }
    }

    internal static class ShortcutHelper
    {
        public static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory, string description, string iconPath)
        {
            try
            {
                IShellLinkW link = (IShellLinkW)new ShellLink();
                link.SetPath(targetPath);
                link.SetWorkingDirectory(workingDirectory);
                link.SetDescription(description);
                if (File.Exists(iconPath))
                {
                    link.SetIconLocation(iconPath, 0);
                }
                IPersistFile file = (IPersistFile)link;
                file.Save(shortcutPath, false);
            }
            catch
            {
                // Ignore shortcut creation errors
            }
        }

        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink
        {
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, out IntPtr pfd, int fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
            void Resolve(IntPtr hwnd, int fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }
    }
}
