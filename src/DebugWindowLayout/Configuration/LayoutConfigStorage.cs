using EnvDTE80;
using System.IO;

namespace DebugWindowLayout
{
    internal static class LayoutConfigStorage
    {
        public const string FileName = ".vsdebuglayout.json";

        public static string GetConfigPath(DTE2 dte)
        {
            var solutionFile = dte?.Solution?.FullName;
            if (string.IsNullOrWhiteSpace(solutionFile))
                return null;

            var directory = Path.GetDirectoryName(solutionFile);
            return string.IsNullOrWhiteSpace(directory)
                ? null
                : Path.Combine(directory, FileName);
        }

        public static LayoutConfig Load(DTE2 dte)
        {
            return LayoutConfig.LoadOrDefault(GetConfigPath(dte));
        }

        public static bool TrySave(DTE2 dte, LayoutConfig config)
        {
            var path = GetConfigPath(dte);
            if (string.IsNullOrWhiteSpace(path) || config == null)
                return false;

            config.Save(path);
            return true;
        }
    }
}
