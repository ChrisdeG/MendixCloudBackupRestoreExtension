
using System.Text.Json;
using Redpoint.ThirdParty.CredentialManagement;
using Newtonsoft.Json.Linq;

namespace LowCodeConnect.CloudRestoreExtension
{
    // class to maintain app settings
    public class AppSettings
    {
        // Constants for field names
        private const string MendixApiUserKey = "CloudBackupRestore.MendixApiUser";
        private const string PostgresUserKey = "CloudBackupRestore.PostgresUser";

        public string? MendixApiUser { get; set; }
        public string? MendixApiKey { get; set; }
        public string? PostgresUser { get; set; }
        public string? PostgresPassword { get; set; }
        public string? PostgresPort { get; set; }
        public string? PostgresDirectory { get; set; }
        public string? DownloadDirectory { get; set; }

        private const string SettingsFileName = "settings.json";


        public AppSettings()
        {

        }


        private static string GetAppDataPath()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appFolder = Path.Combine(appDataPath, "MendixBackupRestore");
            if (!Directory.Exists(appFolder))
            {
                Directory.CreateDirectory(appFolder);
            }
            return appFolder;
        }

        public void Save()
        {
            string appDataPath = GetAppDataPath();
            string filePath = Path.Combine(appDataPath, SettingsFileName);
            JObject jo = JObject.FromObject(this);
            if (jo != null)
            {
                RemoveAttribute(jo, "MendixApiUser");
                RemoveAttribute(jo, "MendixApiKey");
                RemoveAttribute(jo, "PostgresUser");
                RemoveAttribute(jo, "PostgresPassword");

                File.WriteAllText(filePath, jo.ToString());
            }

            SavePasswords();
        }

        private static void RemoveAttribute(JObject jo, string attribute)
        {
            if (jo != null)
            {
                var attr = jo[attribute];
                attr?.Parent?.Remove();
            }
        }

        public static AppSettings Load()
        {
            AppSettings defaultappsettings = new()
            {
                PostgresPort = "5432",
                DownloadDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads"
            };

            string appDataPath = GetAppDataPath();
            string filePath = Path.Combine(appDataPath, SettingsFileName);

            if (!File.Exists(filePath))
            {
                return defaultappsettings; // Return default settings if the file does not exist
            }

            string jsonString = File.ReadAllText(filePath);

            AppSettings? appSettings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(jsonString);
            if (appSettings != null)
            {
                LoadPasswords(appSettings);
                return appSettings;
            }
            return defaultappsettings;
        }
 
        // Save individual fields to the Credential Manager
        private bool SavePasswords()
        {
            if (MendixApiUser != null && PostgresUser != null)
            {
                return SaveField(MendixApiUserKey, MendixApiUser, MendixApiKey ?? "") &&
                       SaveField(PostgresUserKey, PostgresUser, PostgresPassword ?? "");
            }
            return false;
        }

        // Load all fields from the Credential Manager
        public static bool LoadPasswords(AppSettings appSettings)
        {
            var cred_MendixApi = new Credential { Target = MendixApiUserKey };

            if (cred_MendixApi.Load())
            {
                appSettings.MendixApiUser = cred_MendixApi.Username;
                appSettings.MendixApiKey = cred_MendixApi.Password;
            }
            var cred_Postgres = new Credential { Target = PostgresUserKey };

            if (cred_Postgres.Load())
            {
                appSettings.PostgresUser = cred_Postgres.Username;
                appSettings.PostgresPassword = cred_Postgres.Password;
            }
            return true;
        }

        // Helper method to save a single field
        private static bool SaveField(string target, string username, string password)
        {
            var credential = new Credential
            {
                Target = target,
                Username = username,
                Password = password,
                PersistanceType = PersistanceType.LocalComputer
            };

            return credential.Save();
        }


    }


}
