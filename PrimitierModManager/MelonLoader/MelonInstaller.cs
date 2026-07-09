using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;

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

            // Try to verify hash — MelonLoader v0.5.x used .sha512, v0.6+ may not ship them
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
                // Hash file not available — skip verification (HTTPS transport is still secure)
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

            TempFileCache.ClearCache();
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
