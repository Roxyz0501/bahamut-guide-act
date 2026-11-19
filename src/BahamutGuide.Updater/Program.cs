using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace BahamutGuide.Updater
{
    internal static class Program
    {
        private const string PluginFileName = "BahamutGuide.dll";

        private static int Main(string[] args)
        {
            try
            {
                var values = Parse(args);
                var processId = int.Parse(Required(values, "--wait-pid"));
                var source = Path.GetFullPath(Required(values, "--source"));
                var target = Path.GetFullPath(Required(values, "--target"));
                var expectedHash = Required(values, "--sha256");
                var version = Required(values, "--version");
                ValidatePaths(source, target);
                if (!string.Equals(Hash(source), expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Staged DLL hash mismatch.");
                var assembly = System.Reflection.AssemblyName.GetAssemblyName(source);
                if (assembly.Name != "BahamutGuide" || assembly.Version.ToString(3) != version || FileVersionInfo.GetVersionInfo(source).CompanyName != "Roxyz0501") throw new InvalidDataException("InvalidPackage");
                WaitForExit(processId, long.Parse(Required(values, "--start"), System.Globalization.CultureInfo.InvariantCulture));
                using (new FileStream(target + ".update-lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose))
                {
                    if (!string.Equals(Hash(target), Required(values, "--original"), StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Installed DLL changed since the update was requested.");
                    ReplaceWithBackup(source, target, expectedHash, version);
                }
                return 0;
            }
            catch (Exception ex)
            {
                try
                {
                    var log = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BahamutGuide.update-error.log");
                    File.AppendAllText(log, DateTime.Now.ToString("O") + " " + ex + Environment.NewLine);
                }
                catch { }
                return 1;
            }
        }

        internal static void ReplaceWithBackup(string source, string target, string expectedHash, string version)
        {
            ReplaceWithBackup(source, target, expectedHash, version, File.Replace);
        }

        internal static void ReplaceWithBackup(string source, string target, string expectedHash, string version,
            Action<string, string, string> replace)
        {
            var suffix = Guid.NewGuid().ToString("N");
            var backup = target + ".backup-" + suffix;
            var incoming = target + ".update-" + suffix;
            try
            {
                File.Copy(source, incoming);
                if (!string.Equals(Hash(incoming), expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Copied DLL hash mismatch.");
                replace(incoming, target, backup);
                if (!string.Equals(Hash(target), expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Installed DLL hash mismatch.");
            }
            catch
            {
                if (File.Exists(backup))
                {
                    if (File.Exists(target)) File.Replace(backup, target, null);
                    else File.Move(backup, target);
                }
                throw;
            }
            finally { if (File.Exists(incoming)) File.Delete(incoming); }
        }

        private static void ValidatePaths(string source, string target)
        {
            if (!File.Exists(source)) throw new FileNotFoundException("Staged DLL was not found.", source);
            if (!string.Equals(Path.GetFileName(source), PluginFileName, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetFileName(target), PluginFileName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Updater only accepts BahamutGuide.dll.");
            if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Source and target must be different files.");
        }

        private static void WaitForExit(int processId, long expectedStart)
        {
            try
            {
                using (var process = Process.GetProcessById(processId))
                {
                    if (process.StartTime.ToUniversalTime().Ticks == expectedStart) process.WaitForExit();
                }
            }
            catch (ArgumentException) { }
        }

        private static Dictionary<string, string> Parse(string[] args)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i + 1 < args.Length; i += 2) result[args[i]] = args[i + 1];
            return result;
        }

        private static string Required(Dictionary<string, string> values, string key)
        {
            string value;
            if (!values.TryGetValue(key, out value) || string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Missing argument " + key);
            return value;
        }

        private static string Hash(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
