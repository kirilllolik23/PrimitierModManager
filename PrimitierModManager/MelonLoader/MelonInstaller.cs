using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace PrimitierModManager.MelonLoader
{
    public static class MelonLoaderVersions
    {
        public static readonly string V0_5_3 = "v0.5.3";
        public static readonly string V0_6_6 = "v0.6.6";
        public static readonly string V0_7_2 = "v0.7.2";
    }

    public static class MelonInstaller
    {
        public static string MelonLoaderUpdateSourceUri = "https://github.com/LavaGang/MelonLoader/releases/download";
        private static readonly string[] s_proxyNames = { "version", "winmm", "winhttp" };
        private static readonly HttpClient s_httpClient = new HttpClient();

        public static string Status;
        public static string Error;

        public static bool GetIsInstalled()
        {
            string melonLoaderFolder = Path.Combine(ConfigFile.Config.PrimitierInstallPath, "MelonLoader");
            return Directory.Exists(melonLoaderFolder);
        }

        public static void Uninstall(string destination)
        {
            Status = "";
            Error = "";

            Status = "Uninstalling MelonLoader...";
            try
            {
                string melonLoaderFolder = Path.Combine(destination, "MelonLoader");
                if (Directory.Exists(melonLoaderFolder))
                {
                    ThreadHandler.RecursiveFuncRun(recurse =>
                    {
                        try { Directory.Delete(melonLoaderFolder, true); }
                        catch (Exception ex)
                        {
                            if (ex is not UnauthorizedAccessException and not IOException)
                                throw;
                            Error = "Unable to remove Existing MelonLoader Folder! Make sure the Unity Game is not running or try running the Installer as Administrator.";
                        }
                    });
                }

                if (GetExistingProxyPath(destination, out string proxyPath))
                {
                    ThreadHandler.RecursiveFuncRun(recurse =>
                    {
                        try { File.Delete(proxyPath); }
                        catch (Exception ex)
                        {
                            if (ex is not UnauthorizedAccessException and not IOException)
                                throw;
                            Error = "Unable to remove Existing Proxy Module! Make sure the Unity Game is not running or try running the Installer as Administrator.";
                        }
                    });
                }

                ThreadHandler.RecursiveFuncRun(recurse =>
                {
                    try { ExtraCleanupCheck(destination); }
                    catch (Exception ex)
                    {
                        if (ex is not UnauthorizedAccessException and not IOException)
                            throw;
                        Error = "Couldn't do Extra File Cleanup! Make sure the Unity Game is not running or try running the Installer as Administrator.";
                    }
                });

                ExtraCleanupCheck(destination);

                foreach (var dir in new[] { "Mods", "Plugins", "UserData", "UserLibs" })
                {
                    try { Directory.Delete(Path.Combine(destination, dir), true); } catch { }
                }
            }
            catch (Exception ex)
            {
                Error = ex.ToString();
            }
        }

        public static void Install(string? destination, string selectedVersion, bool isX86, bool legacyVersion)
        {
            if (destination == null)
            {
                Error = "Install destination is null";
                return;
            }

            Status = "";
            Error = "";

            string arch = (!legacyVersion && isX86) ? "x86" : "x64";
            string downloadUrl = $"{MelonLoaderUpdateSourceUri}/{selectedVersion}/MelonLoader.{arch}.zip";
            string tempPath = TempFileCache.CreateFile();

            Status = "Downloading MelonLoader...";
            try
            {
                var response = s_httpClient.GetAsync(downloadUrl).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();
                var fileBytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                File.WriteAllBytes(tempPath, fileBytes);
            }
            catch (Exception ex)
            {
                Error = $"Failed to download MelonLoader: {ex.Message}";
                return;
            }

            // Try to verify hash
            string hashUrl = $"{MelonLoaderUpdateSourceUri}/{selectedVersion}/MelonLoader.{arch}.sha512";
            try
            {
                var hashResponse = s_httpClient.GetAsync(hashUrl).GetAwaiter().GetResult();
                if (hashResponse.IsSuccessStatusCode)
                {
                    string repoHash = hashResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult()?.Trim();
                    if (!string.IsNullOrEmpty(repoHash))
                    {
                        using var sha512 = SHA512.Create();
                        byte[] checksum = sha512.ComputeHash(File.ReadAllBytes(tempPath));
                        string fileHash = BitConverter.ToString(checksum).Replace("-", "");
                        if (!fileHash.Equals(repoHash, StringComparison.OrdinalIgnoreCase))
                        {
                            Error = "SHA512 Hash from Temp File does not match Repo Hash!";
                            return;
                        }
                    }
                }
            }
            catch
            {
                // Hash file not available — skip verification
            }

            Status = "Extracting MelonLoader...";
            try
            {
                string melonLoaderFolder = Path.Combine(destination, "MelonLoader");
                if (Directory.Exists(melonLoaderFolder))
                {
                    ThreadHandler.RecursiveFuncRun(recurse =>
                    {
                        try { Directory.Delete(melonLoaderFolder, true); }
                        catch (Exception ex)
                        {
                            if (ex is not UnauthorizedAccessException and not IOException)
                                throw;
                            Error = "Unable to remove Existing MelonLoader Folder! Make sure the Unity Game is not running or try running the Installer as Administrator.";
                        }
                    });
                }

                if (GetExistingProxyPath(destination, out string proxyPath))
                {
                    ThreadHandler.RecursiveFuncRun(recurse =>
                    {
                        try { File.Delete(proxyPath); }
                        catch (Exception ex)
                        {
                            if (ex is not UnauthorizedAccessException and not IOException)
                                throw;
                            Error = "Unable to remove Existing Proxy Module! Make sure the Unity Game is not running or try running the Installer as Administrator.";
                        }
                    });
                }

                using FileStream stream = new FileStream(tempPath, FileMode.Open, FileAccess.Read);
                using ZipArchive zip = new ZipArchive(stream);
                foreach (var entry in zip.Entries)
                {
                    string fullPath = Path.Combine(destination, entry.FullName);
                    if (!fullPath.StartsWith(destination))
                        throw new IOException("Extracting Zip entry would have resulted in a file outside the specified destination directory.");
                    string filename = Path.GetFileName(fullPath);
                    if (filename.Length != 0)
                    {
                        if (!legacyVersion && filename.Equals("version.dll"))
                        {
                            foreach (string proxyName in s_proxyNames)
                            {
                                string newProxyPath = Path.Combine(destination, proxyName + ".dll");
                                if (File.Exists(newProxyPath))
                                    continue;
                                fullPath = newProxyPath;
                                break;
                            }
                        }
                        string directoryPath = Path.GetDirectoryName(fullPath);
                        if (!Directory.Exists(directoryPath))
                            Directory.CreateDirectory(directoryPath);
                        using FileStream targetStream = new FileStream(fullPath, FileMode.OpenOrCreate, FileAccess.Write);
                        using Stream entryStream = entry.Open();
                        ThreadHandler.RecursiveFuncRun(recurse =>
                        {
                            try { entryStream.CopyTo(targetStream); }
                            catch (Exception ex)
                            {
                                if (ex is not UnauthorizedAccessException and not IOException)
                                    throw;
                                Error = $"Couldn't extract file {filename}! Make sure the Unity Game is not running or try running the Installer as Administrator.";
                            }
                        });
                    }
                    else
                    {
                        if (entry.Length != 0)
                            throw new IOException("Zip entry name ends in directory separator character but contains data.");
                        if (!Directory.Exists(fullPath))
                            Directory.CreateDirectory(fullPath);
                    }
                }

                ExtraDirectoryChecks(destination);
                ThreadHandler.RecursiveFuncRun(recurse =>
                {
                    try { ExtraCleanupCheck(destination); }
                    catch (Exception ex)
                    {
                        if (ex is not UnauthorizedAccessException and not IOException)
                            throw;
                        Error = "Couldn't do Extra File Cleanup! Make sure the Unity Game is not running or try running the Installer as Administrator.";
                    }
                });
            }
            catch (Exception ex)
            {
                Error = ex.ToString();
                return;
            }

            // Pre-cache UnityDependencies so MelonLoader doesn't need to download them at runtime
            Status = "Caching Unity dependencies...";
            CacheUnityDependencies(destination);

            TempFileCache.ClearCache();
        }

        public static void CacheUnityDependencies(string gameDir)
        {
            // Detect Unity version from UnityPlayer.dll
            string unityPlayerPath = Path.Combine(gameDir, "UnityPlayer.dll");
            if (!File.Exists(unityPlayerPath))
            {
                Error = "UnityPlayer.dll not found — cannot determine Unity version for dependency caching.";
                return;
            }

            string unityVersion = null;
            try
            {
                var versionInfo = FileVersionInfo.GetVersionInfo(unityPlayerPath);
                string productVersion = versionInfo.ProductVersion;
                if (!string.IsNullOrEmpty(productVersion))
                {
                    // Extract "2022.3.22" from "2022.3.22f1 (887be4894c44)"
                    var match = Regex.Match(productVersion, @"^(\d+\.\d+\.\d+)");
                    if (match.Success)
                        unityVersion = match.Groups[1].Value;
                }
            }
            catch { }

            if (string.IsNullOrEmpty(unityVersion))
            {
                Error = "Could not detect Unity version from UnityPlayer.dll";
                return;
            }

            string depsDir = Path.Combine(gameDir, "MelonLoader", "Dependencies", "Il2CppAssemblyGenerator");
            string depsFile = Path.Combine(depsDir, $"UnityDependencies_{unityVersion}.zip");

            // If already cached, update config and return
            if (File.Exists(depsFile))
            {
                UpdateConfigCfg(depsDir, unityVersion);
                return;
            }

            // Try to download from primary source
            string downloadUrl = $"https://github.com/LavaGang/Unity-Runtime-Libraries/raw/master/{unityVersion}.zip";
            bool downloaded = false;

            try
            {
                Directory.CreateDirectory(depsDir);
                Status = $"Downloading Unity dependencies ({unityVersion})...";
                var response = s_httpClient.GetAsync(downloadUrl).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();
                var fileBytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                File.WriteAllBytes(depsFile, fileBytes);
                downloaded = true;
            }
            catch (Exception ex)
            {
                // Download failed — provide manual instructions
                Error = $"Could not download Unity dependencies automatically ({ex.Message}).\n\n" +
                        $"Manual fix:\n" +
                        $"1. Download '{unityVersion}.zip' from:\n" +
                        $"   {downloadUrl}\n" +
                        $"2. Place it in:\n" +
                        $"   {depsDir}\n" +
                        $"   as 'UnityDependencies_{unityVersion}.zip'\n" +
                        $"3. Launch the game again.";
            }

            if (downloaded)
                UpdateConfigCfg(depsDir, unityVersion);
        }

        private static void UpdateConfigCfg(string depsDir, string unityVersion)
        {
            string configPath = Path.Combine(depsDir, "Config.cfg");
            try
            {
                string[] lines = File.ReadAllLines(configPath);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].StartsWith("UnityVersion"))
                    {
                        lines[i] = $"UnityVersion = \"{unityVersion}.0\"";
                        break;
                    }
                }
                File.WriteAllLines(configPath, lines);
            }
            catch { }
        }

        private static bool GetExistingProxyPath(string destination, out string proxyPath)
        {
            proxyPath = null;
            foreach (string proxy in s_proxyNames)
            {
                string candidate = Path.Combine(destination, proxy + ".dll");
                if (!File.Exists(candidate))
                    continue;
                FileVersionInfo fileInfo = FileVersionInfo.GetVersionInfo(candidate);
                if (fileInfo != null && !string.IsNullOrEmpty(fileInfo.LegalCopyright) && fileInfo.LegalCopyright.Contains("Microsoft"))
                    continue;
                proxyPath = candidate;
                return true;
            }
            return false;
        }

        private static void ExtraDirectoryChecks(string destination)
        {
            foreach (var dir in new[] { "Plugins", "Mods", "UserData" })
            {
                string path = Path.Combine(destination, dir);
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
            }
        }

        private static void ExtraCleanupCheck(string destination)
        {
            foreach (var file in new[] { "MelonLoader.dll", "MelonLoader.ModHandler.dll" })
            {
                foreach (var subDir in new[] { "", "Mods", "Plugins", "UserData" })
                {
                    string path = Path.Combine(destination, subDir, file);
                    if (File.Exists(path))
                        File.Delete(path);
                }
            }
            string logsPath = Path.Combine(destination, "Logs");
            if (Directory.Exists(logsPath))
                Directory.Delete(logsPath, true);
        }
    }
}
