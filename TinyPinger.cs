using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Media;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace TrayPingerApp
{
    public class PingerApplicationContext : ApplicationContext
    {
        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;
        private FormLog logWindow;
        private System.Windows.Forms.Timer pingTimer;
        private int pingInProgress = 0;
        private string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");
        private List<string> hosts = new List<string>();
        private int pingThresholdMS = 150;
        private int pingIntervalMS = 3000;
        private int pingTimeoutMS = 1000;
        private bool soundEnabled = true;
        private string currentStatus = "GOOD";
        private DateTime lastConfigLogTime = DateTime.MinValue;

        public PingerApplicationContext()
        {
            LoadConfiguration();
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Показать лог", null, ShowLog_Click);
            trayMenu.Items.Add("Редактировать список хостов", null, EditConfig_Click);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("Выход", null, Exit_Click);
            trayIcon = new NotifyIcon()
            {
                Icon = CreateColorIcon(Color.Green),
                ContextMenuStrip = trayMenu,
                Text = "Pinger: Мониторинг запущен",
                Visible = true
            };
            trayIcon.DoubleClick += ShowLog_Click;
            logWindow = new FormLog();
            pingTimer = new System.Windows.Forms.Timer();
            pingTimer.Interval = pingIntervalMS;
            pingTimer.Tick += PingTimer_Tick;
            pingTimer.Start();
            PingTimer_Tick(null, null);
        }

        private void LoadConfiguration()
        {
            try
            {
                if (!File.Exists(configPath))
                {
                    File.WriteAllLines(configPath, new[] {
                        "# --- НАСТРОЙКИ ПИНГЕРА ---",
                        "# Порог задержки в миллисекундах",
                        "ThresholdMS=150",
                        "# Интервал проверки в миллисекундах",
                        "IntervalMS=3000",
                        "# Звуковой сигнал при переходе GOOD -> BAD",
                        "SoundEnabled=true",
                        "",
                        "# --- СПИСОК IP-АДРЕСОВ ИЛИ ХОСТОВ ---",
                        "8.8.8.8",
                        "1.1.1.1",
                        "yandex.ru"
                    });
                }

                string[] lines = File.ReadAllLines(configPath);
                List<string> newHosts = new List<string>();
                int newThresholdMS = pingThresholdMS;
                int newIntervalMS = pingIntervalMS;
                bool newSoundEnabled = soundEnabled;

                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;
                    if (trimmed.StartsWith("ThresholdMS=", StringComparison.OrdinalIgnoreCase))
                    {
                        int value;
                        if (int.TryParse(trimmed.Substring(12).Trim(), out value) && value > 0) newThresholdMS = value;
                        continue;
                    }
                    if (trimmed.StartsWith("IntervalMS=", StringComparison.OrdinalIgnoreCase))
                    {
                        int value;
                        if (int.TryParse(trimmed.Substring(11).Trim(), out value) && value >= 500) newIntervalMS = value;
                        continue;
                    }
                    if (trimmed.StartsWith("SoundEnabled=", StringComparison.OrdinalIgnoreCase))
                    {
                        bool value;
                        if (bool.TryParse(trimmed.Substring(13).Trim(), out value)) newSoundEnabled = value;
                        continue;
                    }
                    newHosts.Add(trimmed);
                }

                pingThresholdMS = newThresholdMS;
                pingIntervalMS = newIntervalMS;
                soundEnabled = newSoundEnabled;
                hosts = newHosts;
                if (pingTimer != null && pingTimer.Interval != pingIntervalMS) pingTimer.Interval = pingIntervalMS;
            }
            catch (Exception ex)
            {
                if (logWindow != null && !logWindow.IsDisposed)
                    logWindow.AppendLog(new List<string> { "[SYSTEM] Ошибка чтения config.txt: " + ex.Message });
            }
        }

        private async void PingTimer_Tick(object sender, EventArgs e)
        {
            if (Interlocked.Exchange(ref pingInProgress, 1) == 1) return;
            try
            {
                LoadConfiguration();
                if (hosts.Count == 0)
                {
                    SetTrayIcon(Color.Orange);
                    trayIcon.Text = "Pinger: Список хостов пуст!";
                    return;
                }

                bool allGood = true;
                string currentTime = DateTime.Now.ToString("HH:mm:ss");
                List<string> results = new List<string>();
                results.Add(currentTime);

                if ((DateTime.Now - lastConfigLogTime).TotalMinutes > 5)
                {
                    results.Add(string.Format("[SYSTEM] Monitoring {0} hosts, threshold {1}ms", hosts.Count, pingThresholdMS));
                    lastConfigLogTime = DateTime.Now;
                }

                using (Ping pinger = new Ping())
                {
                    foreach (string host in hosts)
                    {
                        try
                        {
                            PingReply reply = await pinger.SendPingAsync(host, pingTimeoutMS);
                            if (reply.Status == IPStatus.Success)
                            {
                                long rtt = reply.RoundtripTime;
                                if (rtt > pingThresholdMS)
                                {
                                    results.Add(string.Format("  {0,-10} WARN  {1}ms", host, rtt));
                                    allGood = false;
                                }
                                else results.Add(string.Format("  {0,-10} OK    {1}ms", host, rtt));
                            }
                            else
                            {
                                results.Add(string.Format("  {0,-10} FAIL", host));
                                allGood = false;
                            }
                        }
                        catch { results.Add(string.Format("  {0,-10} FAIL", host)); allGood = false; }
                    }
                }

                if (!allGood)
                {
                    if (currentStatus == "GOOD")
                    {
                        if (soundEnabled) SystemSounds.Exclamation.Play();
                        currentStatus = "BAD";
                    }
                    SetTrayIcon(Color.Red);
                    trayIcon.Text = "Pinger: Проблемы со связью!";
                }
                else
                {
                    currentStatus = "GOOD";
                    SetTrayIcon(Color.Green);
                    trayIcon.Text = "Pinger: Все узлы доступны";
                }

                if (logWindow != null && !logWindow.IsDisposed) logWindow.AppendLog(results);
            }
            finally { Interlocked.Exchange(ref pingInProgress, 0); }
        }

        // Заменяет иконку в трее и корректно освобождает предыдущую,
        // иначе Icon (и обёрнутый в ней GDI-хэндл) утекает на каждом тике таймера.
        private void SetTrayIcon(Color color)
        {
            Icon newIcon = CreateColorIcon(color);
            Icon oldIcon = trayIcon.Icon;
            trayIcon.Icon = newIcon;
            if (oldIcon != null) oldIcon.Dispose();
        }

        private Icon CreateColorIcon(Color color)
        {
            using (Bitmap bitmap = new Bitmap(16, 16))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.Clear(color);
                    using (Pen pen = new Pen(Color.White, 1)) g.DrawRectangle(pen, 1, 1, 13, 13);
                }
                IntPtr hIcon = bitmap.GetHicon();
                try
                {
                    using (Icon tempIcon = Icon.FromHandle(hIcon))
                        return (Icon)tempIcon.Clone();
                }
                finally
                {
                    // GetHicon() создаёт GDI-объект, за освобождение которого
                    // отвечает вызывающий код - Icon.FromHandle им не владеет.
                    DestroyIcon(hIcon);
                }
            }
        }

        private void ShowLog_Click(object sender, EventArgs e)
        {
            if (logWindow == null || logWindow.IsDisposed) logWindow = new FormLog();
            logWindow.Show();
            logWindow.WindowState = FormWindowState.Normal;
            logWindow.Activate();
        }

        private void EditConfig_Click(object sender, EventArgs e)
        {
            try { Process.Start("notepad.exe", configPath); }
            catch (Exception ex) { MessageBox.Show("Не удалось открыть Блокнот: " + ex.Message, "Pinger", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void Exit_Click(object sender, EventArgs e)
        {
            if (pingTimer != null) { pingTimer.Stop(); pingTimer.Dispose(); pingTimer = null; }
            if (trayIcon != null) { trayIcon.Visible = false; trayIcon.Dispose(); trayIcon = null; }
            if (trayMenu != null) { trayMenu.Dispose(); trayMenu = null; }
            if (logWindow != null && !logWindow.IsDisposed)
            {
                // Явно сохраняем позицию/размер перед выходом: при выходе через
                // трей CloseReason будет ApplicationExitCall, а не UserClosing,
                // так что полагаться только на обработчик FormClosing нельзя.
                logWindow.PersistWindowState();
                logWindow.Dispose();
            }
            ExitThread();
        }
    }

    public class FormLog : Form
    {
        private TextBox txtLog;
        private CheckBox chkTopMost;
        private Label lblTopMost;
        private string windowConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "window.txt");
        private const int DefaultWidth = 500;
        private const int DefaultHeight = 380;

        public FormLog()
        {
            this.Text = "Pinger — Мониторинг";
            this.KeyPreview = true;
            LoadWindowState(); // выставляет Size, Location и StartPosition

            txtLog = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                BackColor = Color.Black,
                ForeColor = Color.LightGreen,
                Font = new Font("Consolas", 10)
            };

            Panel bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 28 };
            chkTopMost = new CheckBox { AutoSize = true, Location = new Point(6, 5) };
            lblTopMost = new Label { Text = "поверх", AutoSize = true, Location = new Point(25, 6) };
            lblTopMost.Cursor = Cursors.Hand;
            lblTopMost.Click += (s, e) => chkTopMost.Checked = !chkTopMost.Checked;

            chkTopMost.CheckedChanged += (s, e) => this.TopMost = chkTopMost.Checked;
            bottomPanel.Controls.Add(chkTopMost);
            bottomPanel.Controls.Add(lblTopMost);

            this.Controls.Add(txtLog);
            this.Controls.Add(bottomPanel);

            this.FormClosing += (s, e) =>
            {
                // Сохраняем состояние при ЛЮБОЙ причине закрытия (крестик,
                // Alt+F4, программный Application.Exit и т.д.), а не только
                // при UserClosing - иначе выход через трей "Выход" ничего
                // не сохранит, т.к. его CloseReason = ApplicationExitCall.
                SaveWindowState();

                if (e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    this.Hide();
                }
            };
            this.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) { this.Hide(); e.Handled = true; }
            };
        }

        // Раньше сохранялись только Width/Height (по событию Resize), а X/Y
        // вообще никуда не писались и StartPosition был жёстко CenterScreen -
        // поэтому положение окна никогда не восстанавливалось.
        private void LoadWindowState()
        {
            int width = DefaultWidth, height = DefaultHeight;
            int x = 0, y = 0;
            bool hasSavedPosition = false;

            try
            {
                if (File.Exists(windowConfigPath))
                {
                    string[] parts = File.ReadAllText(windowConfigPath).Trim().Split(';');
                    if (parts.Length == 4
                        && int.TryParse(parts[0], out x)
                        && int.TryParse(parts[1], out y)
                        && int.TryParse(parts[2], out width)
                        && int.TryParse(parts[3], out height))
                    {
                        hasSavedPosition = true;
                    }
                }
            }
            catch { }

            if (width < 300) width = DefaultWidth;
            if (height < 200) height = DefaultHeight;
            this.Size = new Size(width, height);

            // Если сохранённых координат нет, либо они указывают за пределы
            // всех подключённых мониторов (например, монитор отключили),
            // открываем окно по центру, а не за пределами видимой области.
            Rectangle savedRect = new Rectangle(x, y, width, height);
            bool onScreen = hasSavedPosition
                && Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(savedRect));

            if (onScreen)
            {
                this.StartPosition = FormStartPosition.Manual;
                this.Location = new Point(x, y);
            }
            else
            {
                this.StartPosition = FormStartPosition.CenterScreen;
            }
        }

        // Сохраняем состояние один раз - при закрытии/скрытии окна,
        // а не на каждый тик события Resize (которое к тому же не ловит
        // перемещение окна без изменения размера).
        // Публичный - чтобы можно было явно вызвать при выходе из всего
        // приложения (см. Exit_Click), не полагаясь только на FormClosing.
        public void PersistWindowState()
        {
            SaveWindowState();
        }

        private void SaveWindowState()
        {
            if (this.WindowState == FormWindowState.Normal && this.Width > 0 && this.Height > 0)
            {
                try
                {
                    File.WriteAllText(windowConfigPath,
                        string.Format("{0};{1};{2};{3}", this.Location.X, this.Location.Y, this.Width, this.Height));
                }
                catch { }
            }
        }

        public void AppendLog(List<string> lines)
        {
            if (this.IsDisposed || this.Disposing) return;
            if (txtLog.InvokeRequired) { txtLog.BeginInvoke(new Action(() => AppendLog(lines))); return; }

            foreach (string line in lines) txtLog.AppendText(line + Environment.NewLine);
            txtLog.AppendText("-------------" + Environment.NewLine + Environment.NewLine);
            txtLog.SelectionStart = txtLog.TextLength;
            txtLog.ScrollToCaret();
        }
    }

    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new PingerApplicationContext());
        }
    }
}
