﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using System.Linq;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Security.Principal;
using Microsoft.VisualBasic.FileIO;
using System.Drawing;
using Newtonsoft.Json;
using System.Management;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text;
//using System.Windows.Forms.HtmlElement;

namespace RPS {
    public class Screensaver : ApplicationContext {
        [DllImport("kernel32.dll")]
        static extern int GetShortPathName(string longPath, StringBuilder buffer, int bufferSize);

        private int mouseX = -1, mouseY = -1;
        public static int CM_ALL = -1;

        private bool configInitialised = false;
        public bool applicationClosing = false;
        public bool clipboardReady = false;
        private IntPtr[] hwnds;
        private System.Windows.Forms.Keys previousKey;
        public bool configHidden = false;

        #if (DEBUG)
            public List<string> debugLog;
        #endif

        public int currentMonitor = CM_ALL;

        public enum Actions { Config, Preview, Screensaver, Slideshow, Test, Wallpaper };
        public Actions action;
        public Config config;
        public Monitor[] monitors;
        public Rectangle Desktop;
        public float desktopRatio;
        public FileNodes fileNodes;
        public bool readOnly;

        public Version version;
        public bool showUpdateStatus = false;

        private System.Windows.Forms.Timer mouseMoveTimer;

        #region Win32 API functions
        [DllImport("user32.dll")]
        public static extern void SwitchToThisWindow(IntPtr hWnd, bool turnon);
        #endregion

        private Screensaver(Actions action, bool readOnly, IntPtr[] hwnds) {
            #if (DEBUG)
                this.debugLog = new List<string>();
            #endif
            
            this.version = new Version(Application.ProductVersion);
            this.readOnly = readOnly;
            this.action = action;
            this.hwnds = hwnds;
            this.config = new Config(this);
            this.config.PreviewKeyDown += new System.Windows.Forms.PreviewKeyDownEventHandler(this.PreviewKeyDown);
            this.config.browser.PreviewKeyDown += new System.Windows.Forms.PreviewKeyDownEventHandler(this.PreviewKeyDown);

            this.config.browser.Navigate(new Uri(Constants.getDataFolder(Constants.ConfigHtmlFile)));
            this.config.browser.DocumentCompleted += new System.Windows.Forms.WebBrowserDocumentCompletedEventHandler(this.config.ConfigDocumentCompleted);
            if (this.action == Actions.Config) this.config.Show();
            else {
                if (this.action != Actions.Wallpaper) {
                    this.mouseMoveTimer = new System.Windows.Forms.Timer();
                    this.mouseMoveTimer.Interval = 1500;
                    this.mouseMoveTimer.Tick += mouseMoveTimer_Tick;
                }
            }
            // Wait for config document to load to complete initialisation: Config.ConfigDocumentCompleted()
        }

        public static bool checkBrowserVersionOk() {
            if ((new WebBrowser()).Version.Major < 8) {
                MessageBoxManager.Yes = "Upgrade";
                MessageBoxManager.No = "Continue";
                MessageBoxManager.Register();

                switch (MessageBox.Show("RPS requires Internet Explorer 8 or later" + Environment.NewLine + Environment.NewLine + "Open Internet Explorer download page?", "Upgrade Internet Explorer?", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning)) {
                    //this.ExitThread();
                    //Environment.Exit(1);
                    case DialogResult.Yes:
                        Process.Start("explorer.exe", "http://windows.microsoft.com/en-us/internet-explorer/download-ie");
                        Application.Exit();
                        return false;
                    break;
                    case DialogResult.Cancel:
                        Application.Exit();
                        return false;
                    break;
                }
                MessageBoxManager.Unregister();
            }
            return true;
        }

        public void appendDebugFile(int monitor, string log) {
            if (this.config.getPersistantBool("debugLog")) {
                string path = Constants.selectProgramAppDataFolder("debug_" + DateTime.Now.ToString("yyyyMMdd") + ".txt");
                try {
                    File.AppendAllText(path, DateTime.Now.ToString("yyyyMMddhhmmss") + "\t" + Convert.ToString(monitor + 1) + "\t" + log + Environment.NewLine);
                } catch (Exception e) {
                    this.monitors[monitor].showInfoOnMonitor("Error writing to debug log." + Environment.NewLine + e.Message);
                }
            }
        }

        // Return the number of monitors pre initialising monitors array
        public int getNrMonitors() {
            int nrMonitors = 1;
            if (this.action == Actions.Test || this.action == Actions.Preview) {
                nrMonitors = hwnds.Length;
            } else {
                nrMonitors = Screen.AllScreens.Length;
            }
            return nrMonitors;
        }

        public void initForScreensaverAndWallpaper() {
            int nrMonitors = this.getNrMonitors();
            // Avoid double loading config from DB
            if (!config.persistantLoaded()) {
                this.config.loadPersistantConfig(nrMonitors);
            }
            this.configInitialised = true;
            this.fileNodes = new FileNodes(this.config, this);
            if (this.config.getPersistantBool("useFilter")) {
                try {
                    this.fileNodes.setFilterSQL(this.config.getPersistantString("filter"));
                } catch (Exception e) {
                    //this.showInfoOnMonitors(e.Message, true, true);
                    this.config.setPersistant("useFilter", false, true);
                    this.fileNodes.clearFilter();
                }
            }
            this.Desktop = Constants.getDesktopBounds();
            this.desktopRatio = Desktop.Width / Desktop.Height;
        }

//        private void ConfigDocumentCompleted(object sender, System.Windows.Forms.WebBrowserDocumentCompletedEventArgs e) {
        public void initializeMonitors() {
            #if (DEBUG)
                this.debugLog.Add("initializeMonitors");
            #endif

            //MessageBox.Show("ConfigDocumentCompleted:" + this.action.ToString());
            if (this.action != Actions.Config && this.action != Actions.Wallpaper) {
                // Complete initialisation when config.html is loaded.
                if (!this.configInitialised && this.config.browser.Url.Segments.Last().Equals(Constants.ConfigHtmlFile)) {
                    this.initForScreensaverAndWallpaper();
                    System.Drawing.Color backgroundColour = System.Drawing.ColorTranslator.FromHtml(this.config.getPersistantString("backgroundColour"));
                    int i = 0;
                    if (this.action == Actions.Test || this.action == Actions.Preview) {
                        int start = 0;
                        // Skip first monitor for preview
                        if (this.action == Actions.Preview) {
                            start = 1;
                        } else {
                            this.monitors = new Monitor[hwnds.Length];
                        } 

                        for (i = start; i < hwnds.Length; i++) {
                            this.monitors[i] = new Monitor(hwnds[i], i, this);
                            this.monitors[i].FormClosed += new FormClosedEventHandler(this.OnFormClosed);
                            this.monitors[i].PreviewKeyDown += new System.Windows.Forms.PreviewKeyDownEventHandler(this.PreviewKeyDown);
                            this.monitors[i].browser.PreviewKeyDown += new System.Windows.Forms.PreviewKeyDownEventHandler(this.PreviewKeyDown);
                            this.monitors[i].Show();
                        }
                    } else {
                        this.monitors = new Monitor[Screen.AllScreens.Length];
                        foreach (Screen screen in Screen.AllScreens) {
                            this.monitors[i] = new Monitor(screen.Bounds, i, this);
                            this.monitors[i].browser.PreviewKeyDown += new System.Windows.Forms.PreviewKeyDownEventHandler(this.PreviewKeyDown);
                            this.monitors[i].PreviewKeyDown += new System.Windows.Forms.PreviewKeyDownEventHandler(this.PreviewKeyDown);
                            // Avoid white flash by hiding browser and setting form background colour. (Focus on browser on DocumentCompleted to process keystrokes)
                            this.monitors[i].browser.Hide();
                            try {
                                this.monitors[i].BackColor = backgroundColour;
                            } catch (System.ArgumentException ae) { }
                            this.monitors[i].Show();
                            i++;
                        }
                    }
                    this.MonitorsAndConfigReady();
                }
            }
        }

        private void CleanUpOnException(object sender, UnhandledExceptionEventArgs args) {
            this.fileNodes.OnExitCleanUp();
        }

        private void MonitorsAndConfigReady() {
            if (this.action != Actions.Preview) {
                for (int i = 0; i < this.monitors.Length; i++) {
                    if (this.config.hasPersistantKey("historyM" + Convert.ToString(i)) && this.config.hasPersistantKey("historyOffsetM" + Convert.ToString(i))) {
                        try {
                            this.monitors[i].setHistory(JsonConvert.DeserializeObject<List<long>>((string)this.config.getPersistant("historyM" + Convert.ToString(i))), Convert.ToInt32((string)this.config.getPersistant("historyOffsetM" + Convert.ToString(i))));
                        } catch (Newtonsoft.Json.JsonSerializationException e) {
                            // Ignore old format
                        }
                    }
                }
            }
        }

        /***
         * Detect other RPS processes
         ***/
        private static bool singleProcess(Actions action) {
            Process currentProcess = Process.GetCurrentProcess();
            // Adapted from: http://stackoverflow.com/questions/504208/how-to-read-command-line-arguments-of-another-process-in-c
            string wmiQuery = string.Format("select Handle, ProcessId, CommandLine from Win32_Process where Name='{0}'", Path.GetFileName(currentProcess.MainModule.FileName));
            ManagementObjectSearcher searcher = new ManagementObjectSearcher(wmiQuery);
            ManagementObjectCollection retObjectCollection = searcher.Get();
            foreach (ManagementObject retObject in retObjectCollection) {
                if (currentProcess.Id != Convert.ToInt32(retObject["ProcessId"])) {
                    //Console.WriteLine("[{0}]", retObject["CommandLine"] + " " + retObject["ProcessId"] + " ~ " + currentProcess.Id);
                    // If both self and process as screensaver /s switch to existing and exit
                    if (!System.Diagnostics.Debugger.IsAttached) {
                        if (action == Actions.Screensaver && !retObject["CommandLine"].ToString().Contains("/c") && !retObject["CommandLine"].ToString().Contains("/p")) {
                            try {
                                Process p = Process.GetProcessById(Convert.ToInt32(retObject["ProcessId"]));
                                SwitchToThisWindow(p.MainWindowHandle, false);
                                Environment.Exit(0);
                            } catch (System.ArgumentException ae) {

                            }
                        }
                        // Make this version read only
                        return true;
                    }
                }
            }       
            return false;
        }

        private static void setAsCurrentScreensaver(string path) {
            if (File.Exists(path)) {
                StringBuilder buffer = new StringBuilder(512);
                GetShortPathName(path, buffer, buffer.Capacity);
                Registry.SetValue("HKEY_CURRENT_USER\\Control Panel\\Desktop", "SCRNSAVE.EXE", Convert.ToString(buffer));

                // https://msdn.microsoft.com/library/windows/desktop/ms724832.aspx
                RegistryValueKind rvkType = RegistryValueKind.String;
                if (Environment.OSVersion.Version.Major > 6 || (Environment.OSVersion.Version.Major == 6 &&  Environment.OSVersion.Version.Minor > 0)) {
                    rvkType = RegistryValueKind.DWord;
                }
                Registry.SetValue("HKEY_CURRENT_USER\\Control Panel\\Desktop", "ScreenSaveActive", 1, rvkType);
            }
        }

        private void DoWorkDeleteFile(object sender, DoWorkEventArgs e) {
            //            Debug.WriteLine(this.config.getPersistant("folders"));
            BackgroundWorker worker = sender as BackgroundWorker;
            // Lower priority to ensure smooth working of main screensaver
            System.Diagnostics.Process.GetCurrentProcess().PriorityClass = System.Diagnostics.ProcessPriorityClass.BelowNormal;
            int monitorId = Convert.ToInt32(((object[])(e.Argument))[0]);
            string filename = Convert.ToString(((object[])(e.Argument))[1]);
            int i = 0;
            while (File.Exists(filename) && i < 100) {
                i++;
                try {
                    FileSystem.DeleteFile(filename, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.DoNothing);
                    this.fileNodes.deleteFromDB(filename);
                } catch (ArgumentNullException ane) {
                    this.monitors[i].showInfoOnMonitor("Nothing to delete");
                } catch (Exception ex) {
                    if (this.monitors[i].imagePath() == filename) this.monitors[i].showInfoOnMonitor("Deleting\n"+Path.GetFileName(filename));
				    Thread.Sleep(1000);
                }
            }
        }

        public void showInfoOnMonitors(string info) {
            this.showInfoOnMonitors(info, false);
        }

        public void showInfoOnMonitors(string info, bool highPriority) {
            this.showInfoOnMonitors(info, highPriority, false);
        }

        public void showUpdateInfo(string info) {
            if (this.action == Actions.Config) {
                this.config.showUpdateInfo(info);
            } else {
                this.monitors[0].showUpdateInfo(info);
            }
        }

        public void hideUpdateInfo() {
            if (this.action == Actions.Config) {
                //this.config.hideUpdateInfo();
            } else {
                this.monitors[0].hideUpdateInfo();
            }
        }

        public void showInfoOnMonitors(string info, bool highPriority, bool fade) {
            if (this.action == Actions.Config) {
                this.config.showUpdateInfo(info);
            } else {
                if (this.monitors != null) for (int i = 0; i < this.monitors.Length; i++) {
                    if ((this.currentMonitor == CM_ALL) || (this.currentMonitor == i)) {
                        this.monitors[i].showInfoOnMonitor(info, highPriority, fade);
                        //this.monitors[i].browser.Document.InvokeScript("showInfo", new String[] { info });
                    }
                }
            }
        }

        public void showAllUpToDate() {
            this.showInfoOnMonitors("All up to date" + Environment.NewLine + "(" + this.version.ToString() + ")", true, true);
        }

        public void pauseAll(bool showInfo) {
            for (int i = 0; i < this.monitors.Length; i++) {
                this.monitors[i].timer.Enabled = false;
                if (showInfo) this.monitors[i].showInfoOnMonitor("||");
            }
        }

        public void resumeAll(bool showInfo) {
            for (int i = 0; i < this.monitors.Length; i++) {
                //this.monitors[i].timer.Enabled = true;
                this.monitors[i].startTimer();
                if (showInfo) this.monitors[i].showInfoOnMonitor("|>");
            }
        }

        public void startTimers() {
            //for (int i = (this.monitors.Length - 1); i >= 0 ; i--) {
            for (int i = 0; i < this.monitors.Length; i++) {
    //            if (this.currentMonitor == CM_ALL || this.currentMonitor == i) {
                    ///this.monitors[i].setTimerInterval();
                    this.monitors[i].startTimer();
                    //this.monitors[i].timer.Enabled = !this.monitors[i].paused;
                    /*if (!this.monitors[i].paused) {
                        this.monitors[i].timer.Start();
                    }*/
                    //
      //          }
            }
        }

        public void stopTimers() {
            for (int i = 0; i < this.monitors.Length; i++) {
//                if (this.currentMonitor == CM_ALL || this.currentMonitor == i) {
                    //this.monitors[i].timer.Stop();
                    this.monitors[i].timer.Enabled = false;
  //              }
            }
        }

        public void actionNext(int step) {
            this.stopTimers();
            bool panoramaShownPreviously = this.monitors[0].isMonitor0PanoramaImage(false);
            for (int i = 0; i < this.monitors.Length; i++) {
                int firstStep = step;
                if (this.currentMonitor == CM_ALL || this.currentMonitor == i) {
                    this.monitors[i].timer.Stop();
                    string s = "";
                    if (step > 1) s = " x " + step;
                    this.monitors[i].showInfoOnMonitor(">>" + s);
                    this.monitors[i].nextImage(step, panoramaShownPreviously);
                    /*if (Utils.hasKeyMessage()) {
                        Console.WriteLine("Skip:" + i + " " + this.monitors[i].currentImage["path"]);
                        Console.Beep();
                        //this.monitors[i].showInfoOnMonitor(">>>" + s);
                    } else {
                        Console.WriteLine("Show:" + i + " " + this.monitors[i].currentImage["path"]);*/
                        this.monitors[i].showImage(this.config.getPersistantBool("useTransitionsOnInput"));
                    //}
                }
            }
            this.startTimers();
        }

        public void actionPrevious(int step) {
            this.stopTimers();
            bool panoramaShownPreviously = this.monitors[0].isMonitor0PanoramaImage(false);
            for (int i = 0; i < this.monitors.Length; i++) {
                //for (int i = (this.monitors.Length - 1); i >= 0 ; i--) {
                if (this.currentMonitor == CM_ALL || this.currentMonitor == i) {
                    this.monitors[i].timer.Stop();
                    //if (i > 0) this.monitors[i].imageSettings["pano"] = false;
                    string s = "";
                    if (step > 1) s = " x " + step;
                    this.monitors[i].showInfoOnMonitor("<<" + s);
                    // Note check for same / slide nextImage is performed in Monitor.cs
                    if (this.currentMonitor == CM_ALL && i != 0 && this.config.getPersistantString("mmImages") == "same") {
                        this.monitors[i].currentImage = this.monitors[0].currentImage;
                        this.monitors[i].readMetadataImage();
                    } else if (this.currentMonitor == CM_ALL && i != 0 && this.config.getPersistantString("mmImages") == "slide") {
                        int extra;
                        if (this.config.getOrder() == Config.Order.Random) extra = 0; else extra = -1;
                        this.monitors[i].currentImage = this.monitors[0].previousImage(i + extra, panoramaShownPreviously, false);
                        this.monitors[i].readMetadataImage();
                    } else {
                        this.monitors[i].previousImage(step, panoramaShownPreviously);
                    }
                    this.monitors[i].showImage(this.config.getPersistantBool("useTransitionsOnInput"));
                }
            }
            this.startTimers();
        }

        public int getStep(PreviewKeyDownEventArgs e) {
            if (e.Shift) return 5;
            if (e.Control) return 25;
            if (e.Alt) return 100;
            return 1;
        }

        /// <summary>
        /// The function checks whether the current process is run as administrator.
        /// In other words, it dictates whether the primary access token of the 
        /// process belongs to user account that is a member of the local 
        /// Administrators group and it is elevated.
        /// </summary>
        /// <returns>
        /// Returns true if the primary access token of the process belongs to user 
        /// account that is a member of the local Administrators group and it is 
        /// elevated. Returns false if the token does not.
        /// </returns>
        static bool IsRunAsAdmin() {
            WindowsIdentity id = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(id);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        public void PreviewKeyDown(object sender, PreviewKeyDownEventArgs e) {
            // Ignore shortcut keys when Config screen is visible
            // Ignore repeated keys
            if (this.previousKey == e.KeyCode) {
                this.previousKey = 0;
                //Console.WriteLine("PreviousKey:" + e.KeyCode.ToString());
            } else {
                //Console.WriteLine("Key:" + e.KeyCode.ToString());
                if (this.config.ActiveControl != null) Console.WriteLine(this.config.ActiveControl.Name);
                else Console.WriteLine("null");
                if (this.config.Visible && this.config.WindowState != FormWindowState.Minimized) {
                    switch (e.KeyCode) {
                        case Keys.Escape:
                            if (this.config.WindowState == FormWindowState.Minimized) {
                                this.config.WindowState = FormWindowState.Normal;
                            } else {
                                if (this.action == Actions.Config) {
                                    this.config.Config_FormClosing(this, null);
                                    //this.OnExit();
                                } else {
                                    this.configHidden = true;
                                    this.config.Hide();
                                }
                            }
                        break;
                        #if (DEBUG)
                        case Keys.F12:
                            this.config.saveDebug();   
                            MessageBox.Show(this.config.jsonAllPersistant());
                            /*string log = "HTML saved";
                            foreach(string s in this.debugLog) {
                                log += s + Environment.NewLine;
                            }
                            MessageBox.Show(log, "Debug log:");*/
                        break;
                        #endif
                        case Keys.S:
                            if (this.config != Form.ActiveForm) {
                                this.config.Activate();
                            }
                        break;
                    }
                } else {
		            Keys KeyCode = e.KeyCode;
		            // fix German keyboard codes for [ ]
		            if (e.Alt && e.Control) {
			            switch (e.KeyCode) {
				            case Keys.D8: 
                                KeyCode = Keys.OemOpenBrackets;
				            break;
				            case Keys.D9:
					            KeyCode = Keys.OemCloseBrackets;
				            break;
			            }
		            }
                    if (e.Control && KeyCode >= Keys.D0 && KeyCode <= Keys.D5) {
                        // Control + 0 ... 5 set Rating
                        this.stopTimers();
                        int rating = KeyCode - Keys.D0;

                        for (int i = 0; i < this.monitors.Length; i++) {
                            if (this.currentMonitor == CM_ALL || this.currentMonitor == i) {
                                this.monitors[i].rateImage(rating);
                            }
                        }
                        this.fileNodes.resetFilter();
                        this.startTimers();
                    } else switch (KeyCode) {
                        case Keys.Escape:
                            if (!this.configHidden) {
                                this.OnExit();
                            }
                            this.configHidden = false;
                        break;
                        case Keys.A:
                            Utils.RunTaskScheduler(@"OpenUrl", "http://www.abscreensavers.com");                    
                        break;
                        case Keys.B:
                            Utils.RunTaskScheduler(@"OpenUrl", "http://www.abscreensavers.com/random-photo-screensaver/version-information/");
                            //this.monitors[i].showInfoOnMonitor("Opened in Explorer Window", false, true);
                            //Process.Start("http://www.abscreensavers.com");
                        break;
                        case Keys.C:
                            string c;
                            if (!e.Control && this.clipboardReady ) c = Clipboard.GetText();
                            else c = "";
                            for (int i = 0; i < this.monitors.Length; i++) {
                                if (this.currentMonitor == CM_ALL || this.currentMonitor == i) {
                                    if (this.monitors[i].imagePath() != null) {
                                        c += this.monitors[i].imagePath() + Environment.NewLine;
                                        if (e.Control) {
                                            c += this.monitors[i].quickMetadata.getAsString() + Environment.NewLine + Environment.NewLine;
                                            this.monitors[i].showInfoOnMonitor("Metadata copied to clipboard");
                                        } else this.monitors[i].showInfoOnMonitor("Image path added to clipboard");
                                    }
                                }
                            }
                            if (c != "") {
                                Clipboard.SetText(c);
                                this.clipboardReady = true;
                            }
                        break;
                        case Keys.E:
                            for (int i = 0; i < this.monitors.Length; i++) {
                                if (this.currentMonitor == CM_ALL || this.currentMonitor == i) {
                                    if (this.monitors[i].imagePath() != null) {
                                        if (e.Control) {
                                            if (!File.Exists(Convert.ToString(this.config.getPersistant("externalEditor")))) {
                                                this.monitors[i].showInfoOnMonitor("External editor: '" + this.config.getPersistant("externalEditor") + "' not found.", true, true);
                                            } else {
                                                if (Utils.RunTaskScheduler(@"OpenInEditor" + Convert.ToString(i), Convert.ToString(this.config.getPersistant("externalEditor")), "\"" + this.monitors[i].imagePath() + "\"")) {
                                                    this.monitors[i].showInfoOnMonitor("Opened in external editor", false, true);
                                                }
                                            }
                                        } else {
                                            if (Utils.RunTaskScheduler(@"OpenInExplorer" + Convert.ToString(i), "explorer.exe", "/e,/select,\"" + this.monitors[i].imagePath() + "\"")) { 
                                                this.monitors[i].showInfoOnMonitor("Opened in Explorer Window", false, true);
                                            }
                                        }
                                    }
                                }
                            }
                            if (Convert.ToBoolean(this.config.getPersistantBool("closeAfterImageLocate"))) this.OnExit();
                        break;
                        case Keys.F:
                        case Keys.NumPad7:
                            for (int i = 0; i < this.monitors.Length; i++) {
                                if (this.currentMonitor == CM_ALL || this.currentMonitor == i) {
                                    this.config.setPersistant("showFilenameM" + (i + 1), Convert.ToString(this.monitors[i].InvokeScript("toggle", new string[] { "#filename" })));
                                }
                            }
                        break;
                        case Keys.H:
                            for (int i = 0; i < this.monitors.Length; i++) {
                                if (this.currentMonitor == CM_ALL || this.currentMonitor == i) {
                                    if (this.monitors[i].imagePath() != null) {
                                        FileAttributes attributes = File.GetAttributes(this.monitors[i].imagePath());
                                        if ((attributes & FileAttributes.Hidden) == FileAttributes.Hidden) {
                                            attributes = attributes & ~FileAttributes.Hidden;
                                            this.monitors[i].showInfoOnMonitor("Showing image<br/>(Hidden file attribute cleared)", false, true);
                                        } else {
                                            attributes = attributes | FileAttributes.Hidden;
                                            this.monitors[i].showInfoOnMonitor("Hiding image<br/>(Hidden file attribute set and removed from DB)", false, true);
                                            this.fileNodes.deleteFromDB(this.monitors[i].imagePath());
                                        }
                                        File.SetAttributes(this.monitors[i].imagePath(), attributes);
                                    }
                                }
                            }
                            if (Convert.ToBoolean(this.config.getPersistantBool("closeAfterImageLocate"))) this.OnExit();
                        break;
                        case Keys.I:
                            for (int i = 0; i < this.monitors.Length; i++) {
                                if (this.currentMonitor == CM_ALL || this.currentMonitor == i) {
                                    this.monitors[i].browser.Document.InvokeScript("identify");
                                }
                            }
                        break;
                        case Keys.M:
                        case Keys.N:
                            for (int i = 0; i < this.monitors.Length; i++) {
                                if (this.currentMonitor == CM_ALL || this.currentMonitor == i) {
                                    this.config.setPersistant("showQuickMetadataM" + (i + 1), Convert.ToString(this.monitors[i].InvokeScript("toggle", new string[] { "#quickMetadata" })));
                                }
                            }
                        break;
                        case Keys.P:
                            for (int i = 0; i < this.monitors.Length; i++) {
                                if (this.currentMonitor == CM_ALL || this.currentMonitor == i) {
                                    if (!this.config.syncMonitors() || i == 0) {
                                        this.monitors[i].paused = !this.monitors[i].paused;
                                        this.monitors[i].timer.Enabled = !this.monitors[i].paused;
                                    } else {
                                        this.monitors[i].paused = this.monitors[0].paused;
                                        this.monitors[i].timer.Enabled = !this.monitors[0].paused;
                                    }
                                    if (this.monitors[i].timer.Enabled) this.monitors[i].showInfoOnMonitor("|>");
                                    else this.monitors[i].showInfoOnMonitor("||");
                                }
                            }
                        break;
                        case Keys.R: case Keys.NumPad1:
                            if (this.config.changeOrder() == Config.Order.Random) {
                                this.showInfoOnMonitors("Randomising");
                            } else {
                                int monitor = this.currentMonitor;
                                if (this.currentMonitor == CM_ALL) monitor = 0;
                                if (this.monitors[monitor].currentImage != null) {
                                    this.fileNodes.currentSequentialSeedId = Convert.ToInt32(this.monitors[monitor].currentImage["id"]);
                                }
                                this.showInfoOnMonitors("Sequential");
                            };
                        break;
                        case Keys.S:
                            // Don't hide config screen if application is in Config mode
                            if (this.action != Actions.Config) {
                                if (this.config.Visible && this.config.WindowState != FormWindowState.Minimized)  this.config.Hide();
                                else {
                                    this.config.Activate();
                                    this.config.Show();
                                    this.config.WindowState = FormWindowState.Normal;
                                }
                            }
                        break;
                        case Keys.T: case Keys.NumPad5:
                            for (int i = 0; i < this.monitors.Length; i++) {
                                if (this.currentMonitor == CM_ALL || this.currentMonitor == i) {
                                    string display = "true";
                                    string clockType = "current";
                                    switch(Convert.ToString(this.config.getPersistant("clockM" + (i + 1)))) {
                                        case "none":
                                            this.config.setPersistant("currentClockM" + (i + 1), "checked");
                                            clockType = "current";
                                        break;
                                        case "current":
                                            this.config.setPersistant("elapsedClockM" + (i + 1), "checked");
                                            clockType = "elapsed";
                                        break;
                                        case "elapsed":
                                            this.config.setPersistant("noClockM" + (i + 1), "checked");
                                            clockType = "none";
                                            display = "false";
                                        break;
                                    }
                                    this.config.setPersistant("clockM" + (i + 1), clockType);
                                    this.monitors[i].InvokeScript("setClockType", new string[] { clockType  });
                                    this.monitors[i].InvokeScript("toggle", new string[] { "#clock", display });
                                    this.monitors[i].InvokeScript("setClockFormat", new string[] { this.config.getPersistantString("clockFormatM" + (i + 1)) });
                                }
                            }
                        break;
                        case Keys.U:
                            string updateFilename = this.config.updateFilename();
                            if (updateFilename == null) {
                                this.showUpdateStatus = true;
                                this.showInfoOnMonitors("Checking for updates.", true, true);
                                this.config.timerCheckUpdates_Tick(this, null);
                            } else {
                                if (e.Control) {
                                    string update = this.config.getUpdateVersion();
                                    if (update != null) {
                                        this.config.setPersistant("ignoreVersion", update);
                                        this.config.setPersistant("ignoreUpdate", Convert.ToString(true));
                                        this.hideUpdateInfo();
                                        this.showInfoOnMonitors("Update " + update + " will be ignored.", true, true);
                                    }
                                } else {
                                    switch (this.config.isUpdateNewer()) { 
                                        case true:
                                            this.showInfoOnMonitors("Activating update", true, true);
                                            if (File.Exists(updateFilename) && Utils.VerifyMD5(updateFilename, this.config.updateFileMD5())) {
                                                // Keep call to explorer.exe otherwise update won't start!
                                                Utils.RunTaskScheduler(@"Run", "explorer.exe", updateFilename);
                                            } else {
                                                Utils.RunTaskScheduler(@"Run", "explorer.exe", this.config.updateDownloadUrl());
                                            }
                                            this.OnExit();
                                        break;
                                        case false:
                                            this.showAllUpToDate();
                                        break;
                                        case null:
                                        break;
                                    }
                                }
                            }
                        break;
                        case Keys.W:
                            string[] paths = new string[this.monitors.Length];
                            for (int i = 0; i < this.monitors.Length; i++) {
                                paths[i] = Convert.ToString(this.monitors[i].imagePath());
                                if (this.currentMonitor == CM_ALL || this.currentMonitor == i) {
                                    this.monitors[i].showInfoOnMonitor("Setting as wallpaper");
                                }
                            }
                            Wallpaper wallpaper = new Wallpaper(this);
                            wallpaper.generateWallpaper(this.currentMonitor, paths);
                        break;
                        case Keys.X:
                            this.hideUpdateInfo();
                        break;
                        case Keys.D0:
                            this.currentMonitor = CM_ALL;
                            for (int i = 0; i < this.monitors.Length; i++) {
                                this.monitors[i].browser.Document.InvokeScript("identify");
                                this.monitors[i].showInfoOnMonitor("Offset (" + this.monitors[i].offset + ")");
                            }
                            break;
                        case Keys.D1:case Keys.D2:case Keys.D3:
                        case Keys.D4:case Keys.D5:case Keys.D6:
                        case Keys.D7:case Keys.D8:case Keys.D9:
                            int monitorId = e.KeyValue-49;
                            if (monitorId < this.monitors.Length) {
                                this.currentMonitor = monitorId;
                                this.monitors[monitorId].browser.Document.InvokeScript("identify");
                                this.monitors[monitorId].showInfoOnMonitor("Offset (" + this.monitors[monitorId].offset + ")");
                            }
                        break;
                        case Keys.NumPad4: case Keys.Left:
                            this.actionPrevious(this.getStep(e));
                        break;
                        case Keys.NumPad6: case Keys.Right:
                            this.actionNext(this.getStep(e));
                        break;
                        case Keys.NumPad2: case Keys.Down:
                            this.stopTimers();
                            for (int i = 0; i < this.monitors.Length; i++) {
                                if (this.currentMonitor == CM_ALL || this.currentMonitor == i) {
                                    this.monitors[i].timer.Stop();
                                    this.monitors[i].offsetImage(this.getStep(e));
                                    this.monitors[i].showImage(this.config.getPersistantBool("useTransitionsOnInput"));
                                    this.monitors[i].showInfoOnMonitor("v (" + this.monitors[i].offset + ")");
                                    this.monitors[i].startTimer();
                                }
                            }
                            this.startTimers();
                        break;
                        case Keys.NumPad8: case Keys.Up:
                            this.stopTimers();
                            for (int i = 0; i < this.monitors.Length; i++) {
                                if (this.currentMonitor == CM_ALL || this.currentMonitor == i) {
                                    this.monitors[i].timer.Stop();
                                    this.monitors[i].offsetImage(this.getStep(e)*-1);
                                    this.monitors[i].showImage(this.config.getPersistantBool("useTransitionsOnInput"));
                                    this.monitors[i].showInfoOnMonitor("^ (" + this.monitors[i].offset + ")");
                                    this.monitors[i].startTimer();
                                }
                            }
                            this.startTimers();
                        break;
                        case Keys.F2:
                        for (int i = 0; i < this.monitors.Length; i++) {
                            if (this.currentMonitor == CM_ALL || this.currentMonitor == i) {
                                this.monitors[i].renameFile();
                            }
                        }
                        break;
                        case Keys.F12:
                            for (int i = 0; i < this.monitors.Length; i++) {
                                if (this.currentMonitor == CM_ALL || this.currentMonitor == i) {
                                    string path = this.monitors[i].saveDebug();
                                    if (e.Control) {
                                        if (Utils.RunTaskScheduler(@"OpenInExplorer" + Convert.ToString(i), "explorer.exe", "/e,/select,\"" + path + "\"")) {
                                            this.monitors[i].showInfoOnMonitor("Opened in Explorer Window", false, true);
                                        }
                                    }
                                }
                            }
                        break;
                        case Keys.OemOpenBrackets:
                        case Keys.OemCloseBrackets: 
                        case Keys.Oemplus:
                            this.stopTimers();
                            int deg = 0;
                            string message = "";
                            switch (KeyCode) {
                                case Keys.OemOpenBrackets: 
                                    deg = 270;
                                    message = "Rotating 270° clock wise";
                                break;
                                case Keys.OemCloseBrackets: 
                                    deg = 90;
                                    message = "Rotating 90° clock wise";
                                break;
                                case Keys.Oemplus: 
                                    deg = 180;
                                    message = "Upside down you're turning me";
                                break;
                            }

                            for (int i = 0; i < this.monitors.Length; i++) {
                                if (this.currentMonitor == CM_ALL || this.currentMonitor == i) {
                                    this.monitors[i].info = message;
                                    this.monitors[i].showInfoOnMonitor(message, true, true);
                                    this.monitors[i].rotateImage(deg);
                                }
                            }
                            //this.fileNodes.toggleMetadataTransaction();
                            this.fileNodes.resetFilter();
                            this.startTimers();
                        break;
                        case Keys.Delete:
                        if (this.config.getPersistantBool("deleteKey")) {
                                this.pauseAll(false);
                                for (int i = 0; i < this.monitors.Length; i++) {
                                    if (this.currentMonitor == CM_ALL || this.currentMonitor == i) {
                                        bool deleteFile = true;
                                        string filename = this.monitors[i].imagePath();
                                        if (filename != null && filename.Length > 0 && File.Exists(filename)) {
                                            Cursor.Show();
                                            this.monitors[i].Focus();
                                            if (DialogResult.Yes == MessageBox.Show("Are you sure you want to delete '" + Path.GetFileName(filename) + "'?", "Confirm File Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation)) {
                                                deleteFile = true;
                                            } else {
                                                deleteFile = false;
                                            }
                                            Cursor.Hide();
                                            if (deleteFile) {
                                                BackgroundWorker bgwDeleteFile = new BackgroundWorker();
                                                bgwDeleteFile.DoWork += new DoWorkEventHandler(DoWorkDeleteFile);
                                                bgwDeleteFile.RunWorkerAsync(new Object[] {i, filename});
                                            }
                                        }
                                    }
                                }
                                this.resumeAll(false);
                            }
                        break;
                        default:
        		            if (!e.Alt && !e.Control && !e.Shift) {
                                if (!this.config.getPersistantBool("onlyEscapeExits")) {
                                    this.OnExit();
                                }
                            }
                        break;
                    }
                    this.previousKey = e.KeyCode;
                }
            } 
        }

        public void OnExit() {
            Cursor.Show();
            if (this.fileNodes != null) {
                this.fileNodes.CancelBackgroundWorker();
                this.config.setPersistant("sequentialStartImageId", this.fileNodes.currentSequentialSeedId.ToString());
            }
            this.config.savePersistantConfig();
            if (this.fileNodes != null) this.fileNodes.OnExitCleanUp();
            // Manually call config close to ensure it will not cancel the close.
            this.applicationClosing = true;
            Application.Exit();
        }

        private void OnFormClosed(object sender, EventArgs e) {
            this.OnExit();
        }

        class MouseMessageFilter : IMessageFilter {
            public static event MouseEventHandler MouseMove = delegate { };
            public static event MouseEventHandler MouseClick = delegate { };
            const int WM_MOUSEMOVE = 0x0200;
            const int WM_LBUTTONDOWN = 0x0201;
            const int WM_LBUTTONUP = 0x0202;
            const int WM_MOUSEWHEEL = 0x020A;
            const int WM_RBUTTONDOWN = 0x0204;
            const int WM_RBUTTONUP = 0x0205;

            public bool PreFilterMessage(ref Message m) {
                switch (m.Msg) { 
                    case WM_MOUSEMOVE:
                        MouseMove(null, new MouseEventArgs(MouseButtons.None, 0, Control.MousePosition.X, Control.MousePosition.Y, 0));
                    break;
                    case WM_LBUTTONUP: case WM_RBUTTONUP:
                        MouseButtons mb = MouseButtons.Left;
                        switch(m.Msg) {
                            case WM_LBUTTONUP: 
                                mb = MouseButtons.Left;
                            break;
                            case WM_RBUTTONUP:
                                mb = MouseButtons.Right;
                            break;
                        }
                        MouseClick(null, new MouseEventArgs(mb, 1, Control.MousePosition.X, Control.MousePosition.Y, 0));
                    break;


                }
                return false;
            }
        }

        private void MouseClick(object sender, MouseEventArgs e) {
            if (!this.config.Visible) {
                //if (this.config.hiding) return null;
                if (this.config.getPersistantBool("browseMouse")) {
                    switch (e.Button) {
                        case MouseButtons.Left:
                            this.actionNext(1);
                            break;
                        case MouseButtons.Right:
                            this.actionPrevious(1);
                            break;
                    }
                } else {
                    if (!this.config.getPersistantBool("ignoreMouseClick")) this.OnExit();
                }
            }
        }

        public void resetMouseMove() {
            this.mouseX = -1;
            this.mouseY = -1;

        }

        public void MouseMove(object sender, MouseEventArgs e) {
            if (this.config.Visible) {
                this.resetMouseMove();
            } else {
                if (this.mouseX != e.X && this.mouseY != e.Y && this.mouseX != -1 && this.mouseY != -1) {
                    Cursor.Show();
                    this.mouseMoveTimer.Stop();
                    this.mouseMoveTimer.Start();
                    this.mouseMoveTimer.Enabled = true;
                }
                if (this.mouseX == -1) this.mouseX = e.X;
                if (this.mouseY == -1) this.mouseY = e.Y;
                int sensitivity = 0;
                switch (this.config.getPersistantString("mouseSensitivity")) {
                    case "high":
                        sensitivity = 0;
                    break;
                    case "medium":
                        sensitivity = 10;
                    break;
                    case "low":
                        sensitivity = 50;
                    break;
                    case "none":
                        this.mouseX = e.X;
                        this.mouseY = e.Y;
                    break;
                }
                if (e.X > (this.mouseX + sensitivity) || e.X < (this.mouseX - sensitivity) ||
                    e.Y > (this.mouseY + sensitivity) || e.Y < (this.mouseY - sensitivity)) {
                        this.OnExit();
                }
                this.mouseX = e.X;
                this.mouseY = e.Y;
            }
        }

        void mouseMoveTimer_Tick(object sender, EventArgs e) {
            Cursor.Hide();
            this.mouseMoveTimer.Enabled = false;
        }
   
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args) {
            //MessageBox.Show(String.Join(" ", args));
            IntPtr previewHwnd = IntPtr.Zero;
            IntPtr[] hwnds;
            Actions action = Actions.Screensaver;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            hwnds = null;
            if (args.Length > 0) {
                string arg1 = args[0].ToLower().Trim();
                string arg2 = null;
                if (arg1.Length > 2) {
                    arg2 = arg1.Substring(3).Trim();
                    arg1 = arg1.Substring(0, 2);
                } else if (args.Length > 1) {
                    arg2 = args[1];
                }
                switch (arg1[1]) {
                    case 'a':
                        string path="";
                        for (int i = 1; i < args.Length; i++) path += args[i] + " ";
                        path = path.Trim();
                        //MessageBox.Show(path);
                        Screensaver.setAsCurrentScreensaver(path);
                        /*
                        MessageBox.Show("IsAdmin: " + Convert.ToString(Screensaver.IsRunAsAdmin()) + Environment.NewLine + Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                            Constants.AppFolderName,
                            Constants.DataFolder));*/
//                        MessageBox.Show("IsAdmin: " + Convert.ToString(Screensaver.IsRunAsAdmin()));
                        Application.Exit();
                        return;
                    break;
                    case 'c':
                        action = Actions.Config;
                    break;
                    case 't':
                    case 'p':
                        if (arg1[1] == 't') action = Actions.Test;
                        else action = Actions.Preview;
                        //action = Actions.Test;
                        hwnds = new IntPtr[args.Length - 1];
                        for (int i = 1; i < args.Length; i++) {
                            hwnds[i - 1] = new IntPtr(long.Parse(args[i]));
                        }
                        //previewHwnd = new IntPtr(long.Parse(arg2));
                    break;
                    case 'o':
                        string setting = "0";
                        string value = args[0].Trim("-/\\".ToCharArray());
                        if (string.Compare(value, "on", true) == 0) setting = "1";
                        string oldValue = "on";
                        if (Convert.ToString(Registry.GetValue("HKEY_CURRENT_USER\\Control Panel\\Desktop", "ScreenSaveActive", null)) == "0") oldValue = "off";
                        Registry.SetValue("HKEY_CURRENT_USER\\Control Panel\\Desktop", "ScreenSaveActive", setting);
                        MessageBox.Show("Your screensaver has been truned " + value + "." + Environment.NewLine + "(It was " + oldValue + ")", Constants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information );
                        Application.Exit();
                        return;
                    break;
                    case 'w':
                        action = Actions.Wallpaper;
                    break;
                    case 'x':
                        string wallpaperPath = args[1].Trim("-/\\".ToCharArray());
                        Wallpaper.setWallpaper(wallpaperPath);
                        Application.Exit();
                        return;
                    break;

                }
            }
            bool readOnly = Screensaver.singleProcess(action);
            if (!Screensaver.checkBrowserVersionOk()) {
                Application.Exit();
                return;
            }

            Screensaver screensaver = new Screensaver(action, readOnly, hwnds);
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(screensaver.CleanUpOnException);
            switch (action) {
                case Actions.Config:
                    Application.Run(screensaver);
                break;
                case Actions.Preview:
                    //MessageBox.Show(hwnds[0].ToString());
                    screensaver.monitors = new Monitor[hwnds.Length];
                    screensaver.monitors[0] = new Monitor(hwnds[0], 0, screensaver);
                    screensaver.monitors[0].FormClosed += new FormClosedEventHandler(screensaver.OnFormClosed);
                    screensaver.monitors[0].PreviewKeyDown += new System.Windows.Forms.PreviewKeyDownEventHandler(screensaver.PreviewKeyDown);
                    screensaver.monitors[0].browser.PreviewKeyDown += new System.Windows.Forms.PreviewKeyDownEventHandler(screensaver.PreviewKeyDown);
                    Application.Run(screensaver.monitors[0]);
                break;
                default:
                    Cursor.Hide();
                    Application.AddMessageFilter(new MouseMessageFilter());
                    MouseMessageFilter.MouseMove += new MouseEventHandler(screensaver.MouseMove);
                    MouseMessageFilter.MouseClick += new MouseEventHandler(screensaver.MouseClick);

                    Application.Run(screensaver);
                break;
            }
        }
    }
}
