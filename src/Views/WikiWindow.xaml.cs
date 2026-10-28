using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using SSF2ModManager.Services;

namespace SSF2ModManager.Views
{
    public partial class WikiWindow : Window
    {
        private readonly string? _installRoot;

        public WikiWindow(string? installRoot)
        {
            _installRoot = installRoot;
            InitializeComponent();
            Loaded += async (_, _) => await OpenAsync();
        }

        private async Task OpenAsync()
        {
            var docs = FindDocs(_installRoot);
            if (docs == null)
            {
                System.Windows.MessageBox.Show("Could not find the wiki. Look for runtime\\shell-dev\\docs.",
                    "Open Docs", MessageBoxButton.OK, MessageBoxImage.Warning);
                Close();
                return;
            }
            try
            {
                var data = Path.Combine(Path.GetTempPath(), "ssf2mm-wiki");
                var env = await CoreWebView2Environment.CreateAsync(null, data);
                await Browser.EnsureCoreWebView2Async(env);
                Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                Browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
                Browser.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "ssf2.wiki", docs, CoreWebView2HostResourceAccessKind.Allow);
                await Browser.CoreWebView2.CallDevToolsProtocolMethodAsync(
                    "Network.setCacheDisabled", "{\"cacheDisabled\":true}");
                Browser.CoreWebView2.AddHostObjectToScript("bridge", new WikiHost(docs, this));
                await Browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("""
                    (function () {
                      var b = chrome.webview.hostObjects.bridge;
                      function parse(p) { return p.then(function (s) { return JSON.parse(s); }); }
                      window.wiki = {
                        author: function () { return b.Author(); },
                        history: function (file) { return parse(b.History(file)); },
                        images: function () { return parse(b.Images()); },
                        pickImage: function () { return b.PickImage(); },
                        savePage: function (file, body, author, note) { return b.SavePage(file, body, author, note); },
                        packagePage: function (file, author) { return b.PackagePage(file, author); },
                        deletePage: function (file) { return b.DeletePage(file); },
                        downloadTutorial: function (id) { return b.DownloadTutorial(id); },
                        createTutorial: function (title) { return b.CreateTutorial(title); }
                      };
                    })();
                    """);
                Browser.CoreWebView2.Navigate("https://ssf2.wiki/_static/index.html");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, "Open Docs", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        private static string? FindDocs(string? installRoot)
        {
            var fromWalk = TutorialWikiImporter.FindDocs();
            if (fromWalk != null && Directory.Exists(Path.Combine(fromWalk, "_static")))
                return fromWalk;
            if (!string.IsNullOrWhiteSpace(installRoot))
            {
                var candidate = Path.Combine(installRoot, "runtime", "shell-dev", "docs");
                if (Directory.Exists(Path.Combine(candidate, "_static")))
                    return candidate;
            }
            var dev = ShellWorkspaceService.FindDevWorkspace();
            if (!string.IsNullOrWhiteSpace(dev))
            {
                var candidate = Path.Combine(dev, "docs");
                if (Directory.Exists(Path.Combine(candidate, "_static")))
                    return candidate;
            }
            return null;
        }
    }
}
