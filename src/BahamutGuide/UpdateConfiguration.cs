using System.Linq;

namespace BahamutGuide
{
    internal static class UpdateConfiguration
    {
        public const string RepositoryOwner = "Roxyz0501";
        public const string RepositoryName = "bahamut-guide-act";
        public const string RepositoryUrl = "https://github.com/" + RepositoryOwner + "/" + RepositoryName;
        public const string PluginAssemblyName = "BahamutGuide";
        public const string UpdaterAssemblyName = "BahamutGuide.Updater";
        public const string PluginFileName = "BahamutGuide.dll";
        public const string UpdaterFileName = "BahamutGuide.Updater.exe";

        public static bool IsConfigured => IsSafeSegment(RepositoryOwner) && IsSafeSegment(RepositoryName);

        public static string ReleasesApiUrl => "https://api.github.com/repos/" + RepositoryOwner + "/" + RepositoryName + "/releases?per_page=10";

        private static bool IsSafeSegment(string value) => !string.IsNullOrWhiteSpace(value) &&
            value.All(x => char.IsLetterOrDigit(x) || x == '-' || x == '_' || x == '.');
    }
}
