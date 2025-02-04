using System.Configuration;

namespace PinballScores.Services
{
    public class ConfigService
    {
        public static DateTime LastRun
        {
            get => GetValue<DateTime>("LastRun");
            set => SetValue("LastRun", value);
        }
        public static string? nvramPath => ConfigurationManager.AppSettings.Get("nvramPath");
        public static string? PINemHiPath => ConfigurationManager.AppSettings.Get("PINemHiPath");
        public static string? stgPath => ConfigurationManager.AppSettings.Get("stgPath");
        public static string? firebaseUrl => ConfigurationManager.AppSettings.Get("firebaseUrl");
        public static bool enableLogging => ConfigurationManager.AppSettings.Get("enableLogging") == "true";

        private static T? GetValue<T>(string key) where T:IConvertible
        {
            var v = ConfigurationManager.AppSettings.Get(key);
            if (v == null)
                return default(T);
            return (T?)Convert.ChangeType(v, typeof(T));
        }

        private static void SetValue<T>(string key, T? value) where T:IConvertible
        {
            var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            config.AppSettings.Settings.Remove(key);
            if (value != null)
                config.AppSettings.Settings.Add(key, (string)Convert.ChangeType(value, typeof(string)));
            config.Save();
        }
    }
}