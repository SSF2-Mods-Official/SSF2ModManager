using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

namespace SSF2ModManager.Services
{
    public class ThemeManager
    {
        private readonly string _themesFolder;
        private readonly List<string> _themes = new();
        private FileSystemWatcher? _watcher;
        private string? _currentThemePath;

        public event Action? ThemesChanged;
        public event Action? ThemeApplied;

        public ThemeManager()
        {
            _themesFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Themes");
            if (!Directory.Exists(_themesFolder)) Directory.CreateDirectory(_themesFolder);
            LoadThemes();
            StartWatcher();
        }

        private void StartWatcher()
        {
            try
            {
                _watcher = new FileSystemWatcher(_themesFolder, "*.xaml")
                {
                    EnableRaisingEvents = true,
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
                };
                _watcher.Created += (s, e) => { System.Windows.Application.Current.Dispatcher.Invoke(() => { LoadThemes(); ThemesChanged?.Invoke(); }); };
                _watcher.Deleted += (s, e) => { System.Windows.Application.Current.Dispatcher.Invoke(() => { LoadThemes(); ThemesChanged?.Invoke(); }); };
                _watcher.Renamed += (s, e) => { System.Windows.Application.Current.Dispatcher.Invoke(() => { LoadThemes(); ThemesChanged?.Invoke(); }); };
            }
            catch { }
        }

        private void LoadThemes()
        {
            _themes.Clear();
            try
            {
                var files = Directory.GetFiles(_themesFolder, "*.xaml").OrderBy(f => f).ToList();
                foreach (var f in files)
                {
                    _themes.Add(Path.GetFileNameWithoutExtension(f));
                }
            }
            catch { }
        }

        public List<string> GetAvailableThemes() => _themes.ToList();

        public void ApplyTheme(string themeName)
        {
            try
            {
                var path = Path.Combine(_themesFolder, themeName + ".xaml");
                if (!File.Exists(path)) return;

                // Remove previously-applied theme dictionary(s) from Themes folder
                var app = System.Windows.Application.Current;
                var md = app.Resources.MergedDictionaries;
                var toRemove = md.Cast<ResourceDictionary>()
                    .Where(rd => rd.Source != null && rd.Source.IsFile &&
                                 Path.GetDirectoryName(rd.Source.LocalPath)?.Equals(Path.GetDirectoryName(path), StringComparison.OrdinalIgnoreCase) == true)
                    .ToList();
                foreach (var rd in toRemove)
                    md.Remove(rd);

                var dict = new ResourceDictionary();
                dict.Source = new Uri(path, UriKind.Absolute);
                md.Insert(0, dict);
                // Also copy theme entries into the application resources to overwrite existing keys
                try
                {
                    foreach (var key in dict.Keys)
                    {
                        try
                        {
                            app.Resources[key] = dict[key];
                        }
                        catch { }
                    }
                }
                catch { }
                _currentThemePath = path;
                // Notify listeners that a theme was applied so UI can refresh programmatic brushes
                try { ThemeApplied?.Invoke(); } catch { }
            }
            catch (Exception ex)
            {
                try { File.AppendAllText("ssf2mm-debug.log", $"[ThemeManager] ApplyTheme EX: {ex}\n"); } catch { }
            }
        }
    }
}
