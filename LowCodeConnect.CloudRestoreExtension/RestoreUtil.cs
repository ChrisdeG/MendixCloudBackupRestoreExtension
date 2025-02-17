using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Text.Json;
using Npgsql;
using Mendix.StudioPro.ExtensionsAPI.UI.WebView;
using Mendix.StudioPro.ExtensionsAPI;
using Mendix.StudioPro.ExtensionsAPI.Services;

namespace LowCodeConnect.CloudRestoreExtension
{
    internal class RestoreUtil
    {
        private readonly string MXBACKUPAPIV1 = "https://deploy.mendix.com/api/1/apps";
        private readonly string MXBACKUPAPIV2 = "https://deploy.mendix.com/api/v2/apps";
        private readonly string MENDIX_USER_NAME = "Mendix-Username";
        private readonly string MENDIX_API_KEY = "Mendix-ApiKey";

        private List<AppInfo> apps;
        private readonly List<string> applist = [];
        private readonly List<Snapshot> backups = [];

        private readonly int MaxRetry = 20;
        private int PhaseStart = 0;
        private int PhaseEnd = 100;

        AppSettings AppSettings = new();

        private readonly List<EnvironmentInfo> environments = [];
        private IWebView? webView;
        private readonly IHttpClientService httpClientService;
        private readonly ILogService LogService;
        private readonly SynchronizationContext? SynchronizationContext;
        private Boolean active = false;
        private CancellationTokenSource cts;

        public RestoreUtil(IHttpClientService httpClientService,
            ILogService logService, SynchronizationContext? _synchronizationContext)
        {
            this.httpClientService = httpClientService;
            this.LogService = logService;
            if (_synchronizationContext != null)
            {
                this.SynchronizationContext = _synchronizationContext;
            }
            this.active = true;
            this.cts = new CancellationTokenSource();
            this.apps = [];
        }

        public void SetAppSettings(AppSettings newappsettings)
        {
            AppSettings = newappsettings;
        }

        public async Task GetAppListAsync(List<string> list, string filter)
        {
            try
            {
                using IHttpClient client = httpClientService.CreateHttpClient();

                HttpRequestMessage request = new (HttpMethod.Get, MXBACKUPAPIV1);
                AddAuthenticationHeaders(request);
                var response = await client.SendAsync(request, this.cts.Token);
                response.EnsureSuccessStatusCode();

                if (response.IsSuccessStatusCode)
                {
                    string appsJSON = await response.Content.ReadAsStringAsync();
                    if (appsJSON != null)
                    {
                        var _apps = JsonConvert.DeserializeObject<List<AppInfo>>(appsJSON);
                        if (_apps != null)
                        {
                            apps = _apps;
                        }
                        ParseJsonAndSortToList(list, filter);
                    }
                }
                else
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        ShowErrorMessage("User or API key invalid");
                    }
                    else
                    {
                        ShowErrorMessage("Error fetching applications: " + response.StatusCode);
                    }
                }
            }
            catch (Exception e)
            {
                if (e.Message.Contains("401 (Un"))
                {
                    ShowErrorMessage("User or API key invalid");
                }
                else
                {
                    ShowErrorMessage(e.Message);
                }
            }
        }

        /// <summary>
        /// Return the environments when received from the cloud.
        /// </summary>
        /// <param name="selectionCount"></param>
        /// <param name="environmentList">result parameter with list of environments</param>
        /// <returns></returns>
        public async Task GetEnvironmentList(int selectionCount, List<string> environmentList)
        {
            if (selectionCount >= 0)
            {
                using IHttpClient client = httpClientService.CreateHttpClient();
                string? appId = AppIdByListIndex(selectionCount);
                if (appId == null)
                {
                    ShowErrorMessage("Unable to get appId");
                    return;
                }
                HttpRequestMessage request = new(HttpMethod.Get, $"{MXBACKUPAPIV1}/{appId}/environments");
                AddAuthenticationHeaders(request);

                var response = await client.SendAsync(request, this.cts.Token);

                if (response.IsSuccessStatusCode)
                {
                    string environmentsJSON = await response.Content.ReadAsStringAsync();
                    if (environmentsJSON != null)
                    {
                        JArray jArray = JArray.Parse(environmentsJSON);
                        List<EnvironmentInfo>? envs = jArray.ToObject<List<EnvironmentInfo>>();
                        if (envs != null)
                        {
                            // save full data for later request 
                            environments.Clear();
                            environments.AddRange(envs);
                            // environments is just a list of strings.
                            environmentList.Clear();
                            var sorted = new List<string>();
                            if (environments != null)
                            {
                                foreach (var environment in environments)
                                {
                                    string? mode = environment.Mode;
                                    if (mode != null && !mode.ToLower().Equals("sandbox"))
                                    {
                                        sorted.Add(mode);
                                    }
                                }
                            }
                            sorted.Sort();
                            environmentList.AddRange(sorted);
                        }
                    }
                    else
                    {
                        ShowErrorMessage("Error: Unable to retrieve environments");
                    }
                }
                else
                {
                    ShowErrorMessage("Error: Unable to retrieve environments");
                }
            }
        }

        public void Cancel()
        {
            this.cts.Cancel();
        }

        public void InitCancellationToken()
        {
            this.cts = new CancellationTokenSource();
        }


        public async Task GetBackupList(int selectionCount, string environment, List<string> backupList)
        {
            if (selectionCount >= 0)
            {
                try
                {
                    using IHttpClient client = httpClientService.CreateHttpClient();

                    int appIndex = AppIndexByListIndex(selectionCount);
                    if (appIndex >= 0 && appIndex < apps.Count)
                    {
                        string? appid = apps[appIndex].AppId;
                        string? environmentId = FindEnvironmentId(environment);
                        string? projectid = apps[appIndex].ProjectId;
                        if (appid == null || environmentId == null || projectid == null)
                        {
                            ShowErrorMessage("Error with internal ids");
                            return;
                        }
                        HttpRequestMessage request = new(HttpMethod.Get, $"{MXBACKUPAPIV2}/{projectid}/environments/{environmentId}/snapshots");
                        AddAuthenticationHeaders(request);
                        var response = await client.SendAsync(request, this.cts.Token);
                        string result = response.Content.ReadAsStringAsync().Result;
                        backupList.Clear();
                        if (response.IsSuccessStatusCode)
                        {
                            var backupResult = JsonConvert.DeserializeObject<BackupResult>(result);
                            if (backupResult != null && backupResult.Snapshots != null)
                            {
                                backups.Clear();
                                // store for later use
                                backups.AddRange(backupResult.Snapshots);
                                //var dt1 = new DateTimeFormatInfo { FullDateTimePattern = "yyyy-MM-dd HH:mm" };
                                foreach (var snapshot in backupResult.Snapshots)
                                {
                                    backupList.Add(snapshot.CreatedAtString);
                                }
                            }
                            else
                            {
                                ShowErrorMessage("No snapshots found.");
                            }
                        }
                        else
                        {
                            ShowErrorMessage("Error getting backups.");
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    SetProgress(0);
                    throw;
                }
                catch (Exception e)
                {
                    ShowErrorMessage("Error listing backups: " + e.Message);
                }
            }
        }

        private string? FindEnvironmentId(string environment)
        {
            return environments
                .FirstOrDefault(env => env?.Mode == environment)
                ?.EnvironmentId;
        }

        private int AppIndexByListIndex(int listIndex)
        {
            if (listIndex < 0 || listIndex >= applist.Count)
                return -1;

            string appName = applist[listIndex];
            return apps.FindIndex(app => app.Name == appName);
        }

        private string? ProjectIdByListIndex(int listIndex)
        {
            var appIndex = AppIndexByListIndex(listIndex);
            if (listIndex >= 0 && listIndex < applist.Count && appIndex >= 0)
            {
                return apps[appIndex].ProjectId;
            }
            return null;
        }

        private string? AppIdByListIndex(int listIndex)
        {
            var appIndex = AppIndexByListIndex(listIndex);
            if (listIndex >= 0 && listIndex < applist.Count && appIndex >= 0)
            {
                return apps[appIndex].AppId;
            }
            return null;
        }

        private string? AppNameByListIndex(int listIndex)
        {
            var appIndex = AppIndexByListIndex(listIndex);
            if (listIndex >= 0 && listIndex < applist.Count && appIndex >= 0)
            {
                return apps[appIndex].Name;
            }
            return null;
        }

        private string? EnvironmentIdByEnvironmentName(string environment)
        {
            for (int i = 0; i < environments.Count; i++)
            {
                if (environments[i].Mode == environment)
                {
                    return environments[i].EnvironmentId;
                }
            }
            return null;
        }



        private void ParseJsonAndSortToList(List<string> list, string filter)
        {
            list.Clear();
            applist.Clear();
            if (this.apps != null)
            {

                foreach (var app in apps)
                {
                    // Skip if the app name doesn't match the filter
                    if (!string.IsNullOrEmpty(filter) && app.Name != null && !app.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Skip if the app URL contains "mxapps.io" or is null/empty and skip mobile projects
                    if (string.IsNullOrEmpty(app.Url) || string.IsNullOrEmpty(app.Name) || app.Url.Contains("mxapps.io"))
                        continue;

                    // Add to the list if all checks pass
                    applist.Add(app.Name);
                }
                applist.Sort();
                list.AddRange(applist);
            }
        }

        public async Task RestoreDatabase(int selectedAppIndex, int selectedBackupIndex,
           string environment, bool doDownload, bool doRestore, bool includeDocuments)
        {
            if (string.IsNullOrEmpty(environment))
            {
                ShowMessage("Select a valid environment.");
                return;
            }

            if (apps == null) return;

            string? appId = ProjectIdByListIndex(selectedAppIndex);
            string? appName = AppNameByListIndex(selectedAppIndex);
            string? projectId = apps[AppIndexByListIndex(selectedAppIndex)].ProjectId;
            string? snapshotId = backups[selectedBackupIndex].SnapshotId;
            string? environmentId = EnvironmentIdByEnvironmentName(environment);

            if (appId == null || appName == null || projectId == null || snapshotId == null || environmentId == null)
            {
                ShowErrorMessage("One of more internal ids could not be retrieved");
                return;
            }
            string scope = includeDocuments ? "files_and_database" : "database_only";
            string backupUrl = $"{MXBACKUPAPIV2}/{projectId}/environments/{environmentId}/snapshots/{snapshotId}/archives?data_type={scope}";
            using IHttpClient client = httpClientService.CreateHttpClient();
            HttpRequestMessage request = new(HttpMethod.Post, backupUrl);
            AddAuthenticationHeaders(request);
            if (AppSettings != null && snapshotId != null)
            {
                try
                {
                    var response = await client.SendAsync(request, this.cts.Token);
                    response.EnsureSuccessStatusCode();

                    var result = await response.Content.ReadAsStringAsync();
                    var archiveId = JsonDocument.Parse(result).RootElement.GetProperty("archive_id").GetString();

                    string? saveDir = AppSettings.DownloadDirectory;
                    if (saveDir != null)
                    {
                        if (archiveId != null && appId != null && appName != null && environmentId != null)
                        {
                            string filename = await DownloadFile(appName, projectId, environment, environmentId, snapshotId, archiveId, saveDir, doDownload, includeDocuments);

                            if (!string.IsNullOrEmpty(filename))
                            {
                                string dbname = GetTargetDatabaseName(appName, environment, backups[selectedBackupIndex].CreatedAt);
                                if (doRestore)
                                {
                                    ShowMessage("Restoring");
                                    int restoreState = await RestoreDatabase(filename, dbname, includeDocuments);
                                    if (restoreState >= 0)
                                    {
                                        ShowMessage("The backup is restored to " + dbname);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    ShowMessage("");
                    SetProgress(0);
                    throw;
                }
                catch (Exception ex)
                {
                    ShowMessage($"Error getting backup link: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Add the user name and api key to a request.
        /// </summary>

        private void AddAuthenticationHeaders(HttpRequestMessage request)
        {
            if (AppSettings != null)
            {
                request.Headers.Add(MENDIX_USER_NAME, AppSettings.MendixApiUser);
                request.Headers.Add(MENDIX_API_KEY, AppSettings.MendixApiKey);
            }
        }

        private async Task<string> DownloadFile(string appName, string projectId, string environment, string environmentId,
            string snapshotId, string archiveId, string saveDir, bool doDownload, bool _includeDocuments)
        {
            string result = string.Empty;
            string fileName = $"mendixbackup-{appName}-{environment}-{archiveId}.backup";
            //string ext = includeDocuments ? "-doc" : string.Empty;
            long contentLength = 0L;


            string statusUrl = $"{MXBACKUPAPIV2}/{projectId}/environments/{environmentId}/snapshots/{snapshotId}/archives/{archiveId}";

            try
            {
                SetPhaseStartEnd(0, 5);
                Uri? archiveUri = await GetArchiveStatus(statusUrl);
                if (archiveUri != null)
                {
                    using IHttpClient client = httpClientService.CreateHttpClient();
                    HttpRequestMessage request = new (HttpMethod.Get, archiveUri);
                    AddAuthenticationHeaders(request);
                    SetPhaseStartEnd(5, 30);

                    string saveFilePath = Path.Combine(saveDir, fileName);
                    int totalBytesRead = 0;
                    if (doDownload)
                    {
                        try
                        {
                            // use a default HttpClient to get the file size.
                            using HttpClient httpClient = new();
                            // Send the request and read the headers
                            using HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, this.cts.Token);
                            response.EnsureSuccessStatusCode();
                            if (response.Content.Headers.ContentLength.HasValue)
                            {
                                contentLength = response.Content.Headers.ContentLength.Value;
                                ShowMessage($"File size {contentLength} bytes");
                            }
                            using Stream stream = await response.Content.ReadAsStreamAsync();
                            using FileStream fileStream = new(saveFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
                            // Buffer size for reading/writing data
                            ShowMessage("Start download");
                            byte[] buffer = new byte[81920]; // 80 KB buffer size
                            int bytesRead;
                            int c = 0;

                            while ((bytesRead = await stream.ReadAsync(buffer, this.cts.Token)) > 0)
                            {
                                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), this.cts.Token);
                                totalBytesRead += bytesRead;
                                if (c++ == 10)
                                {
                                    c = 0;
                                    long prog = Math.Min(100, totalBytesRead);
                                    if (contentLength > 0)
                                    {
                                        SetProgress(1.0 * totalBytesRead / contentLength);
                                    }
                                }
                            }
                            if (this.cts.Token.IsCancellationRequested)
                            {
                                ShowErrorMessage("File download canceled");
                            }
                            else
                            {
                                ShowMessage("File downloaded");
                            }
                        }
                        catch (HttpRequestException e)
                        {
                            ShowErrorMessage(e.Message);
                        }
                        catch (TaskCanceledException e)
                        {
                            ShowErrorMessage(e.Message);
                        }
                    }

                    result = saveFilePath;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is IOException)
            {
                ShowErrorMessage($"Error: {ex.Message}");
            }

            return result;
        }



        public async Task<Uri?> GetArchiveStatus(string statusUrl)
        {
            using IHttpClient client = httpClientService.CreateHttpClient();
            try
            {
                HttpRequestMessage request = new(HttpMethod.Get, statusUrl);
                AddAuthenticationHeaders(request);
                int retryCount = 0;
                HttpResponseMessage response;
                string statusResult;
                JObject resultJson;
                string status;
                int timeout = 1;

                response = await client.SendAsync(request, this.cts.Token);
                statusResult = await response.Content.ReadAsStringAsync();
                resultJson = JObject.Parse(statusResult);

                JToken? StateToken = resultJson["state"];
                if (StateToken != null)
                {
                    status = StateToken.ToString();
                }
                else
                {
                    ShowErrorMessage("Can not read state from JSON");
                    status = "unknown";
                }
                while ((status == "queued" || status == "running") && retryCount++ < MaxRetry)
                {
                    ShowMessage($"Backup is prepared in the cloud, retrying in {timeout} seconds...");
                    await Task.Delay(TimeSpan.FromSeconds(timeout), cts.Token);
                    SetProgress(retryCount * 1.0 / MaxRetry);
                    if (cts.IsCancellationRequested)
                    {
                        return null;
                    }
                    if (timeout < 32)
                    {
                        timeout *= 2;
                    }

                    HttpRequestMessage statusRequest = new(HttpMethod.Get, statusUrl);
                    AddAuthenticationHeaders(statusRequest);
                    response = await client.SendAsync(statusRequest);
                    statusResult = await response.Content.ReadAsStringAsync();
                    resultJson = JObject.Parse(statusResult);
                    // TODO HANDLE "[UNAUTHORIZED:Please provide valid credentials or set up a Mendix user session]","[""errorMessage"": ""UNAUTHORIZED:Please provide valid credentials or set up a Mendix user session"", ""errorCode"": ""UNAUTHORIZED""]","",[UNAUTHORIZED],Newtonsoft.Json.Linq.JToken+LineInfoAnnotation,"[""errorMessage"": ""UNAUTHORIZED:Please provide valid credentials or set up a Mendix user session"", ""errorCode"": ""UNAUTHORIZED""]","[""errorMessage"": ""UNAUTHORIZED:Please provide valid credentials or set up a Mendix user session"", ""errorCode"": ""UNAUTHORIZED""]",Property,True,[UNAUTHORIZED],"",errorMessage,[],[]
                    JToken? jToken = resultJson["state"];
                    if (jToken != null)
                    {
                        status = jToken.ToString();
                    }
                    else
                    {
                        status = "unknown";
                        ShowErrorMessage("Can not read state from JSON");
                    }
                }

                if (status == "failed" || retryCount >= MaxRetry)
                {
                    ShowMessage("Retry failed.");
                    return null;
                }

                if (resultJson.ContainsKey("url"))
                {
                    JToken? jToken = resultJson["url"];
                    if (jToken != null)
                    {
                        return new Uri(jToken.ToString());
                    }
                }

            }
            catch (HttpRequestException e)
            {
                ShowErrorMessage(e.Message);
            }
            catch (TaskCanceledException)
            {
                throw new OperationCanceledException();
            }
            return null;
        }



        private static string GetTargetDatabaseName(string appName, string environment, DateTime? createdOn)
        {
            // Implement the logic to determine the target database name
            // max length is 63
            string name = $"{appName}_{environment}";
            if (name.Length > 43) { name = name[..43]; }
            return $"{name}_{createdOn?.ToString("yyyyMMddHHmmss")}";
        }

        /// <summary>
        /// Restores a database asynchronous
        /// </summary>
        /// <param name="filename">filename of the downloaded backup</param>
        /// <param name="dbname">name of the target database. Should be an empty database.</param>
        /// <param name="includeDocuments">Export the documents to a folder, not supported yet.</param>
        /// <returns></returns>
        public async Task<int> RestoreDatabase(string filename, string dbname, bool _includeDocuments)
        {
            string dbFilename = filename;

            // create a unique name
            string dbext = "";
            int count = 1;
            while (DatabaseExists($"{dbname}{dbext}"))
            {
                dbext = "_" + count++;
            }
            dbname += dbext;
            int exitCodeCreate = RunPostgresSQL("postgres", $"Create Database \"{dbname}\"");
            if (exitCodeCreate != 0)
            {
                ShowErrorMessage("A database could not be created, check the postgres password, port and server running");
                return -2;
            }
            int restorestate1 = 0;
            try
            {
                string commandList = $"\"{AppSettings.PostgresDirectory}/bin/pg_restore.exe\"";
                String commandArgs = $" -l \"{dbFilename}\"";
                var state = new CustomState { ShowProgress = false, RestoreUtil = this };
                SetPhaseStartEnd(30, 39);
                restorestate1 = await RunCommand(commandList, commandArgs, state);
                if (restorestate1 < 0)
                {
                    return restorestate1;
                }
                // restoring will take 60% of the time
                SetPhaseStartEnd(40, 100);
                string commandRestore = $"\"{AppSettings.PostgresDirectory}/bin/pg_restore.exe\"";
                string argsRestore = $"-d \"postgresql://{AppSettings.PostgresUser}:{AppSettings.PostgresPassword}@localhost:{AppSettings.PostgresPort}/{dbname}\" --jobs 2 --verbose --no-owner \"{dbFilename}\" ";
                state.ShowProgress = true;
                restorestate1 = await RunCommand(commandRestore, argsRestore, state);
            }
            finally
            {
                if (restorestate1 < 0)
                {
                    // wait 2 seconds to give the command a chance to end the connection with the database.
                    await Task.Delay(TimeSpan.FromSeconds(1));
                    // kill all connections
                    RunPostgresSQL("postgres", $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{dbname}'");
                    // delete the database which is not correctly filled.
                    RunPostgresSQL("postgres", $"Drop Database \"{dbname}\"");
                    // set full progress to 0
                    SetPhaseStartEnd(0, 100);
                    SetProgress(0);
                }
                else
                {
                    SetProgress(1);
                }
            }
            return restorestate1;
        }

        private string GetConnectionString(string database)
        {
            return $"Host=localhost;Port = {AppSettings.PostgresPort};Database = {database};User Id = {AppSettings.PostgresUser};Password = {AppSettings.PostgresPassword}; ";
        }

        private int RunPostgresSQL(string database, string sql)
        {
            string connectionString = GetConnectionString(database);
            using NpgsqlConnection connection = new(connectionString);
            try
            {
                using NpgsqlCommand cmd = new(sql, connection);

                connection.Open();
                cmd.ExecuteNonQuery();
                return 0;
            }
            catch (Exception ex)
            {
                ShowErrorMessage(ex.Message);
                return -1;
            }
        }

        /// <summary>
        /// Checks the existence of a postgres database to prevent overwriting. Uses the port and sa account of postgres.
        /// </summary>
        /// <param name="database">
        /// Name of the database
        /// </param>
        /// <returns>True if database exists</returns>
        private bool DatabaseExists(string database)
        {
            string connectionString = GetConnectionString("postgres");
            using var connection = new NpgsqlConnection(connectionString);
            try
            {
                connection.Open();
                string cmdText = $"SELECT 1 FROM pg_database WHERE datname='{database}'";
                using NpgsqlCommand cmd = new(cmdText, connection);
                bool exists = cmd.ExecuteScalar() != null;
                return exists;
            }
            catch (Exception ex)
            {
                ShowMessage(ex.Message);
                return false;
            }
        }



        /// <summary>
        /// Runs a os command asynchronous. 
        /// </summary>
        /// <param name="command"></param>
        /// full path to the executable
        /// <param name="arguments"></param>
        /// Command line arguments
        /// <param name="state"></param>
        /// State is used for progress or preparing the progress
        /// <returns></returns>
        private async Task<int> RunCommand(string command, string arguments, CustomState state)
        {
            try
            {
                var processStartInfo = new ProcessStartInfo(command, arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var process = new Process { StartInfo = processStartInfo };
                process.OutputDataReceived += (sender, e) => MyProcOutputHandler(sender, e, state);
                process.ErrorDataReceived += (sender, e) => MyProcOutputHandler(sender, e, state);
                process.Start();

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync(this.cts.Token);
                return process.ExitCode;
            }
            catch (Exception ex)
            {
                if (!this.cts.Token.IsCancellationRequested)
                {
                    ShowErrorMessage($"{ex.Message} {command}");
                }
                else
                {
                    // remove the restoring message
                    ShowMessage("");
                }
            }
            return -1;
        }

        private void MyProcOutputHandler(object _sendingProcess,
            DataReceivedEventArgs outLine, CustomState state)
        {
            // Collect the sort command output. 
            if (!String.IsNullOrEmpty(outLine.Data))
            {
                if (state.ShowProgress)
                {
                    state.LineCount += 1;
                    SetProgress(1.0 * state.LineCount / state.TotalLineCount);
                }
                else
                {
                    // the restore lines is about 3 messages per list line.
                    state.TotalLineCount += 3;
                    // just a shot in the dark progress, 
                    SetProgress(state.TotalLineCount / 200.0);
                }
            }
        }


        private static string ExtractTarGz(string filename)
        {
            // Implement the logic to extract .tar.gz file TODO
            return "path/to/extracted/directory" + filename; // Return the path to the extracted directory
        }

        internal void SetWebview(IWebView webView21)
        {
            webView = webView21;
        }
        // set value 0-100
        internal void SetPhaseStartEnd(int PhaseStart, int PhaseEnd)
        {
            this.PhaseStart = PhaseStart;
            this.PhaseEnd = PhaseEnd;
            SetProgress(1.0 * PhaseStart / 100);
        }
        // progres 0-1
        internal void SetProgress(double progress)
        {
            if (!active) return;
            long p = Convert.ToInt64(PhaseStart + (PhaseEnd - PhaseStart) * Math.Min(progress, 1));
            Message message = new() { Progress = p };
            this.SynchronizationContext?.Post((_) =>
            {
                try
                {
                    webView?.PostMessage("setProgress", message);
                }
                catch (ObjectDisposedException)
                {
                    active = false;
                    this.cts.Cancel(true);
                }
            }, null);
        }

        internal void ShowMessage(string MessageText)
        {
            if (!active) return;
            Console.WriteLine(MessageText);
            Message message = new() { Text = MessageText };
            this.SynchronizationContext?.Post((_) =>
            {
                try
                {
                    webView?.PostMessage("setMessage", message);
                }
                catch (ObjectDisposedException)
                {
                    active = false;
                    this.cts.Cancel(true);
                }
            }, null);
        }

        internal void ShowErrorMessage(string MessageText)
        {
            if (!active) return;
            LogService.Error(MessageText);
            Console.WriteLine(MessageText);
            Message message = new() { Text = MessageText };
            this.SynchronizationContext?.Post((_) =>
            {
                try
                {
                    webView?.PostMessage("setErrorMessage", message);
                }
                catch (ObjectDisposedException)
                {
                    active = false;
                    this.cts.Cancel(true);
                }
            }, null);
        }


        public class AppInfo
        {
            public string? AppId { get; set; }
            public string? Name { get; set; }
            public string? Url { get; set; }
            public string? ProjectId { get; set; }
        }


        public class EnvironmentInfo
        {
            public string? Url { get; set; }
            public string? Mode { get; set; }
            public string? Status { get; set; }
            public string? ModelVersion { get; set; }
            public string? MendixVersion { get; set; }
            public bool Production { get; set; }
            public int Instances { get; set; }
            public int MemoryPerInstance { get; set; }
            public int TotalMemory { get; set; }
            public string? EnvironmentId { get; set; }
            public string? RuntimeLayer { get; set; }
        }

        public class Snapshot
        {
            [JsonProperty("snapshot_id")]
            public string? SnapshotId { get; set; }

            [JsonProperty("model_version")]
            public string? ModelVersion { get; set; }

            [JsonProperty("comment")]
            public string? Comment { get; set; }

            [JsonProperty("expires_at")]
            public DateTime? ExpiresAt { get; set; }

            [JsonProperty("state")]
            public string? State { get; set; }

            [JsonProperty("status_message")]
            public string? StatusMessage { get; set; }

            [JsonProperty("created_at")]
            public DateTime CreatedAt { get; set; }

            [JsonProperty("finished_at")]
            public DateTime? FinishedAt { get; set; }

            [JsonProperty("updated_at")]
            public DateTime? UpdatedAt { get; set; }

            public String CreatedAtString
            {
                get
                {
                    return CreatedAt.ToString("yyyy-MM-dd HH:mm");
                }
            }
        }

        public class BackupResult
        {
            [JsonProperty("total")]
            public int Total { get; set; }

            [JsonProperty("snapshots")]
            public List<Snapshot>? Snapshots { get; set; }
        }

        private class CustomState
        {
            public long TotalLineCount { get; set; }
            public long LineCount { get; set; }
            public System.Boolean ShowProgress { get; set; }
            public RestoreUtil? RestoreUtil { get; set; }
        }
    }

    // Custom EventArgs to carry text data
    public class TextEventArgs(string text) : EventArgs
    {
        public string Text { get; } = text;
    }
}


