using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Services;
using Mendix.StudioPro.ExtensionsAPI.UI.DockablePane;
using Mendix.StudioPro.ExtensionsAPI.UI.WebView;
using Mendix.StudioPro.ExtensionsAPI;
using System.Threading.Tasks;


namespace LowCodeConnect.CloudRestoreExtension
{
    public class CloudRestoreDockablePaneViewModel : WebViewDockablePaneViewModel
    {

        private readonly Uri _baseUri;
        private readonly Func<IModel?> _getCurrentApp;
        private readonly ILogService _logService;
        private readonly IConfigurationService _configurationService;
        private readonly RestoreUtil RestoreUtil;
        private readonly IHttpClientService _httpClientServer;
        private IWebView? webView;
        private Boolean active = false;

        public CloudRestoreDockablePaneViewModel(Uri baseUri, Func<IModel?> getCurrentApp, ILogService logService, IHttpClientService httpClientService, IConfigurationService configurationService
            )
        {
            _baseUri = baseUri;
            _getCurrentApp = getCurrentApp;
            _logService = logService;
            _httpClientServer = httpClientService;
            _configurationService = configurationService;
            RestoreUtil = new RestoreUtil(_httpClientServer, _logService, System.Threading.SynchronizationContext.Current);
            AppSettings appSettings = AppSettings.Load();
            if (appSettings != null)
            {
                RestoreUtil.SetAppSettings(appSettings);
            }
        }


        public override void InitWebView(IWebView webView)
        {
            this.webView = webView;
            //webView.ShowDevTools();
            AppSettings appSettings = AppSettings.Load();
            // automatically show settings the first time
            string page = appSettings != null && appSettings.MendixApiKey == null ? "settings.html" : "index.html";
            webView.Address = new Uri(_baseUri, page);
            SetTheme();
            RestoreUtil.SetWebview(webView);
            active = true;
            // getApps on start
            if (appSettings != null && appSettings.MendixApiKey != null)
            {
                Task task = FetchApps();
            }
            // handles all messages from the webview and do actions or show pages.
            webView.MessageReceived += (_, args) =>
            {
                if (_getCurrentApp() == null || !active) return;

                if (args != null && args.Message != null && args.Data != null && this.webView != null)
                {
                    Message? message = System.Text.Json.JsonSerializer.Deserialize<Message>(args.Data);
                    if (message != null)
                    {
                        _logService.Info(args.Message);
                        bool handled = false;
                        // all Message string are also in the index.html
                        if (args.Message == "getapps")
                        {
                            RestoreUtil.InitCancellationToken();
                            Task task = FetchApps();
                            handled = true;
                        }
                        if (args.Message == "getenvs" && message != null)
                        {
                            _logService.Info("getenvs" + message.Index);
                            RestoreUtil.InitCancellationToken();
                            Task task = FetchEnvironments(message.Index);
                            handled = true;
                        }
                        if (args.Message == "getbackups" && message != null)
                        {
                            if (message.Environment != null)
                            {
                                RestoreUtil.InitCancellationToken();
                                Task task = FetchBackups(message.Index, message.Environment);
                            }
                            handled = true;
                        }
                        if (args.Message == "download" && message != null && message.Environment != null)
                        {
                            EnableButton("download", false);
                            RestoreUtil.InitCancellationToken();
                            try
                            {
                                Task task = RestoreUtil.RestoreDatabase(message.Index, message.Backup, message.Environment, true, true, false);
                            }
                            catch { }
                            finally
                            {
                                EnableButton("download", true);
                            }
                            handled = true;
                        }
                        if (args.Message == "cancel")
                        {
                            RestoreUtil.Cancel();
                            handled = true;
                        }
                        if (args.Message == "settings" && this.webView != null)
                        {
                            this.webView.Address = new Uri(_baseUri, "settings.html");
                            SetTheme();
                            handled = true;
                        }
                        if (args.Message == "savesettings" && args.Data != null && this.webView != null)
                        {
                            // map same string to settings and save it.
                            AppSettings? appSettings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(args.Data);
                            if (appSettings != null)
                            {
                                if (appSettings.PostgresDirectory != null)
                                    appSettings.PostgresDirectory = appSettings.PostgresDirectory.Replace(@"\\", @"\");
                                if (appSettings.DownloadDirectory != null)
                                    appSettings.DownloadDirectory = appSettings.DownloadDirectory.Replace(@"\\", @"\");
                                appSettings.Save();
                                RestoreUtil.SetAppSettings(appSettings);
                            }
                            this.webView.Address = new Uri(_baseUri, "index.html");
                            SetTheme();
                            Task task = FetchApps();
                            handled = true;
                        }
                        if (args.Message == "cancelsettings" && this.webView != null)
                        {
                            this.webView.Address = new Uri(_baseUri, "index.html");
                            SetTheme();
                            handled = true;
                        }
                        if (args.Message == "settingsloaded" && this.webView != null)
                        {
                            ConsoleWriteLine($"Settingsloaded");
                            // load settings and send them to the screen
                            AppSettings? appSettings = AppSettings.Load();
                            if (appSettings != null)
                            {
                                this.webView.PostMessage("updateSettings", appSettings);
                            }
                            handled = true;
                        }
                        if (!handled)
                        {
                            ConsoleWriteLine("Unhandled message in CoreWebView2_WebMessageReceived: " + args.Message);
                        }
                    }
                }
            };
        }

        private void SetTheme()
        {
            if (_configurationService.Configuration.Theme.Equals(ThemeSupport.Dark))
            {
                webView?.PostMessage("setDark");
            }
        }
        // either enable or disable button
        private void EnableButton(String id, Boolean active)
        {
            webView?.PostMessage("disableButton", new Message { Key = id, Value = active.ToString() });
        }

        // add app to the list of apps
        public void AddApp(string optionValue, string optionText)
        {
            if (!active) return;
            Message message = new() { Key = optionValue, Value = optionText };
            webView?.PostMessage("addApp", message);
        }

        // write a message to the message box on screen
        internal void ConsoleWriteLine(string MessageText)
        {
            if (active)
            {
                Console.WriteLine(MessageText);
                Message message = new() { Text = MessageText };
                SynchronizationContext.Current?.Post((_) =>
                {
                    try
                    {
                        webView?.PostMessage("setMessage", message);
                    }
                    catch (ObjectDisposedException)
                    {
                        active = false;
                    }
                }, null);
            }
        }


        private async Task FetchApps()
        {
            if (!active) return;
            try
            {
                EnableButton("Download", false);
                var list = new List<string>();
                string filter = "";
                _logService.Info("get apps");
                await RestoreUtil.GetAppListAsync(list, filter);
                _logService.Info("get apps ready");
                webView?.PostMessage("clearAppList");
                foreach (var item in list)
                {
                    AddApp(item, item);
                }
                // for 1 app, get the environment directly
                if (list.Count == 1)
                {
                    Task task = FetchEnvironments(0);
                }
                EnableButton("download", list.Count > 0);
            }
            catch (Exception e)
            {
                _logService.Error("Error getting apps", e);
            }
        }

        public void AddOptionToEnvtList(string optionValue, string optionText)
        {
            if (!active) return;
            Message message = new() { Key = optionValue, Value = optionText };
            webView?.PostMessage("addEnvironment", message);
        }

        private async Task FetchEnvironments(int index)
        {
            if (!active) return;
            var list = new List<string>();
            webView?.PostMessage("clearEnvList");
            await RestoreUtil.GetEnvironmentList(index, list);
            bool first = true;
            foreach (var item in list)
            {
                AddOptionToEnvtList(item, item);
                if (first)
                {
                    await FetchBackups(0, item);
                    first = false;
                }
            }

        }

        public void AddOptionToBackupList(string optionValue, string optionText)
        {
            if (!active) return;
            Message message = new() { Key = optionValue, Value = optionText };
            webView?.PostMessage("addBackup", message);
        }

        private async Task FetchBackups(int index, string environment)
        {
            if (!active) return;
            var list = new List<string>();
            try
            {
                webView?.PostMessage("clearBackupList");
                EnableButton("download", false);
                await RestoreUtil.GetBackupList(index, environment, list);
                foreach (var item in list)
                {
                    AddOptionToBackupList(item, item);
                }
                EnableButton("download", list.Count > 0);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
        }


    }
    // generic message for all communication, including settings
    public class Message
    {
        public string? Type { get; set; }
        public string? Command { get; set; }
        public string? Key { get; set; }
        public string? Text { get; set; }
        public string? Value { get; set; }
        public int Index { get; set; }
        public string? Environment { get; set; }
        public int Backup { get; set; }
        public long Progress { get; set; }
        public string? MendixApiUser { get; set; }
        public string? MendixApiKey { get; set; }
        public string? PostgresUser { get; set; }
        public string? PostgresPassword { get; set; }
        public string? PostgresPort { get; set; }
        public string? DownloadDirectory { get; set; }
    }



}

