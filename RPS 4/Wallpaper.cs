﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Data;
using System.IO;
using Microsoft.Win32;

namespace RPS {
    class Wallpaper {
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, string pvParam, uint fWinIni);
        
        private const uint SPI_SETDESKWALLPAPER = 20;
        private const uint SPIF_UPDATEINIFILE = 0x01;
        private const uint SPIF_SENDWININICHANGE = 0x02;

        private Screensaver screensaver;
        public Wallpaper(Screensaver screensaver) {
            this.screensaver = screensaver;
        }

        public void resetDefaultWallpaper() {
            SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, "", SPIF_UPDATEINIFILE | SPIF_SENDWININICHANGE);
//            this.setWallpaper();
        }

        public bool changeWallpaper() {
            switch(this.screensaver.config.getPersistantString("wallpaperChange")) {
                case "never": 
                    return false;
                break;
                case "daily":
                if (Convert.ToDateTime(this.screensaver.config.getPersistant("wallpaperLastChange")).Equals(DateTime.Today)) return false;
                break;
            }
            return true;
        }

        public void generateWallpaperFromSQL(string sql) {
            string[] paths = new string[Screen.AllScreens.Length];
            // Run SQL limit = 10 * Screen.AllScreens.Length;

            DataTable dt = this.screensaver.fileNodes.runSQLByPassFilter(sql + " ORDER BY RANDOM() LIMIT 10");
            int m = 0;
            int i = 0;
            while (dt.Rows.Count > i && m < Screen.AllScreens.Length && paths[m] == null) {
                paths[m] = Convert.ToString(dt.Rows[i]["path"]);
                if (!File.Exists(paths[m])) paths[m] = null;
                else {
                    if (Convert.ToString(this.screensaver.config.getPersistant("videoExtensions")).ToLower().IndexOf(new FileInfo(paths[m]).Extension.ToLower()) > -1) {
                        paths[m] = null;
                    } else m++;
                }
                i++;
            }
            this.generateWallpaper(paths);
        }

        public void generateWallpaper() {
            string[] paths = new string[Screen.AllScreens.Length];
            for (int i = 0; i < Screen.AllScreens.Length; i++) {
                int j = 0;
                DataRow dr;
                do {
                    dr = this.screensaver.fileNodes.getRandomImage(0);
                    paths[i] = Convert.ToString(dr["path"]);
                    // no videos
                    if (Convert.ToString(this.screensaver.config.getPersistant("videoExtensions")).ToLower().IndexOf(new FileInfo(paths[i]).Extension.ToLower()) > -1) {
                        paths[i] = null;
                    }
                    j++;
                } while (paths[i] == null && j < 10);
            }
            this.generateWallpaper(paths);
        }

        /**
         * Set single screen wallpaper path
         **/
        public void generateWallpaper(long monitor, string path) {
            string[] paths = new string[Screen.AllScreens.Length];
            for (int i = 0; i < Screen.AllScreens.Length; i++) {
                if (monitor == Screensaver.CM_ALL || monitor == i) {
                    paths[i] = path;
                }
            }
            this.generateWallpaper(monitor, paths);
        }

        public void generateWallpaper(string[] paths) {
            this.generateWallpaper(Screensaver.CM_ALL, paths);
        }

        public void generateWallpaper(long monitor, string[] paths) {
            // Wallpaper will have exact size of monitors so wallpaper style (Tile, Center, Stretch, Fit, Fill) shouldn't matter
            string wallpaperPath = Constants.selectProgramAppDataFolder(Constants.WallpaperFileName);
            Rectangle r = Constants.getDesktopBounds();
            Bitmap wallpaper  = new Bitmap(r.Width, r.Height);
            Graphics g = Graphics.FromImage(wallpaper);
            GraphicsUnit units = GraphicsUnit.Pixel;
            if (monitor != Screensaver.CM_ALL) {
                if (File.Exists(wallpaperPath)) {
                    try {
                        Image old = Image.FromFile(wallpaperPath);
                        g.DrawImage(old, old.GetBounds(ref units));
                        old.Dispose();
                    } catch (Exception ex) {
                        this.screensaver.showInfoOnMonitors(ex.Message);
                    }
                }
            }
            Color c = (Color)new ColorConverter().ConvertFromString(Convert.ToString(this.screensaver.config.getPersistant("wallpaperBackgroundColour")));
            System.Drawing.Brush fill = new System.Drawing.SolidBrush(c);

            for(int i = 0; i < Screen.AllScreens.Length; i++) {
                if (monitor == Screensaver.CM_ALL || monitor == i) {
                    bool readSuccess = false;
                    Image image = null;
                    try {
                        image = Image.FromFile(paths[i]);
                        readSuccess = true;
                    } catch (OutOfMemoryException ex) {
                        this.screensaver.monitors[i].showInfoOnMonitor("Out of memory reading '" + paths[i] + "' for wallpaper");
                    } catch (FileNotFoundException ex) {
                        this.screensaver.monitors[i].showInfoOnMonitor("File not found '" + paths[i] + "' for wallpaper");
                    } catch (System.ArgumentNullException ex) {
                        this.screensaver.monitors[i].showInfoOnMonitor("No file found for wallpaper");
                    }
                    if (readSuccess) {
                        float imgRatio = (float)image.Width / (float)image.Height;
                        string path = paths[i];
                        // Panorama
                        Rectangle bounds;
                        Rectangle backgroundBounds;
                        if (i == 0 && this.screensaver.config.getPersistantBool("stretchPanoramas") && imgRatio >= (this.screensaver.desktopRatio * (1 - Convert.ToDouble(this.screensaver.config.getPersistant("stretchPanoramaTolerance"))/100))) {
                            // ToDo: Stretch wallpaper parts to fit monitor(s)
                            bounds = this.screensaver.Desktop;
                            i = Screen.AllScreens.Length;
                        } else {
                            bounds = Screen.AllScreens[i].Bounds;
                        }

                        g.FillRectangle(fill, bounds);
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

                        // Draw faded background image or covered front image
                        if (this.screensaver.config.getPersistantBool("wallpaperBackgroundImage") || this.screensaver.config.getPersistantString("wallpaperFitTo") == "cover") {
                            ColorMatrix colorMatrix = new ColorMatrix();
                            if (this.screensaver.config.getPersistantString("wallpaperFitTo") == "cover") colorMatrix.Matrix33 = (float)1;
                            else colorMatrix.Matrix33 = (float)0.5;
                            ImageAttributes imageAttributes = new ImageAttributes();
                            imageAttributes.SetColorMatrix(colorMatrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);

                            // Get scaled union of screen and image
                            Rectangle source = Constants.FitIntoBounds(bounds, Rectangle.Round(image.GetBounds(ref units)), true, false);
                            Point[] dest = {bounds.Location, new Point(bounds.Right, bounds.Top), new Point(bounds.Left, bounds.Bottom)};
                            g.DrawImage(image, dest, source, GraphicsUnit.Pixel, imageAttributes, null);
                        }

                        // Draw font image only if not full cover
                        if (this.screensaver.config.getPersistantString("wallpaperFitTo") != "cover") {
                            g.DrawImage(image, Constants.FitIntoBounds(Rectangle.Round(image.GetBounds(ref units)), bounds, this.screensaver.config.getPersistantBool("wallpaperStretchSmall"), this.screensaver.config.getPersistantString("wallpaperFitTo") == "cover"), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel);
                        }
                        
                        if (this.screensaver.config.getPersistantBool("wallpaperFilenames")) {
                            // ToDo: Get font settings from config.html
                            Font font = new Font("Arial", 10);
                            g.DrawString(path, font, new SolidBrush(Color.Black), bounds.Left + 1, bounds.Top + 1);
                            g.DrawString(path, font, new SolidBrush(Color.White), bounds.Left, bounds.Top);
                        }
                    }
                }
            }
            if (!Directory.Exists(Path.GetDirectoryName(wallpaperPath))) {
                Cursor.Show();
                if (DialogResult.OK != MessageBox.Show("Create folder '"+Path.GetDirectoryName(wallpaperPath)+"'>\n\nOk: Creates folder for backgrounds\nCancel doesn't change background image.", "Installation folder for background not found!", MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation)) {
                    return;
                }
                Cursor.Hide();
                Directory.CreateDirectory(Path.GetDirectoryName(wallpaperPath));
            }
            //try {
            wallpaper.Save(wallpaperPath, System.Drawing.Imaging.ImageFormat.Bmp);
            //} catch(Exception ex) {}

            this.screensaver.config.setPersistant("wallpaperLastChange", Convert.ToString(DateTime.Today));
            Utils.RunTaskScheduler(@"SetWallpaper", Application.ExecutablePath, "/x \"" + wallpaperPath + "\"");
            //Wallpaper.setWallpaper(wallpaperPath);
        }

        public static void setWallpaper(string wallpaperPath) {
            if (File.Exists(wallpaperPath)) {
                SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, wallpaperPath, SPIF_UPDATEINIFILE | SPIF_SENDWININICHANGE);

                Registry.SetValue("HKEY_CURRENT_USER\\Control Panel\\Desktop", "TileWallpaper", "1");
                Registry.SetValue("HKEY_CURRENT_USER\\Control Panel\\Desktop", "WallpaperStyle", "0");
            }
        }
    }


}
