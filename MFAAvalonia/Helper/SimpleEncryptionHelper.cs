using NETCore.Encrypt;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;


namespace MFAAvalonia.Helper;

public static class SimpleEncryptionHelper
{
    public static string Generate()
    {
        // 跨平台系统特征参数
        var osDescription = RuntimeInformation.OSDescription;
        var osArchitecture = RuntimeInformation.OSArchitecture.ToString();
        var plainTextSpecificId = GetPlatformSpecificId();
        var machineName = Environment.MachineName;

        // 混合参数生成哈希
        var combinedString = $"{osDescription}_{osArchitecture}_{plainTextSpecificId}_{machineName}";
        return EncryptProvider.Sha256(combinedString);
    }

    public static string GetPlatformSpecificId()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // 使用WMI获取主板UUID
                using var searcher = new ManagementObjectSearcher("SELECT UUID FROM Win32_ComputerSystemProduct");
                foreach (ManagementObject obj in searcher.Get())
                    return obj["UUID"].ToString();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // 读取DMI产品UUID
                try
                {
                    if (File.Exists("/sys/class/dmi/id/product_uuid"))
                        return File.ReadAllText("/sys/class/dmi/id/product_uuid").Trim();
                }
                catch (UnauthorizedAccessException)
                {
                    LoggerHelper.Warning("权限不足，无法访问/sys/class/dmi/id/product_uuid，尝试备选方法");
                }
                catch (Exception ex)
                {
                    LoggerHelper.Warning($"读取Linux UUID失败: {ex.Message}");
                }

                // 备选方法：尝试读取机器ID
                try
                {
                    if (File.Exists("/etc/machine-id"))
                        return File.ReadAllText("/etc/machine-id").Trim();
                    if (File.Exists("/var/lib/dbus/machine-id"))
                        return File.ReadAllText("/var/lib/dbus/machine-id").Trim();
                }
                catch (Exception ex)
                {
                    LoggerHelper.Warning($"读取Linux机器ID失败: {ex.Message}");
                }

                // 备选方法：使用网络接口MAC地址
                try
                {
                    var nic = NetworkInterface.GetAllNetworkInterfaces()
                        .OrderByDescending(n => n.Speed)
                        .FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up && !n.Description.Contains("virtual", StringComparison.OrdinalIgnoreCase));
                    if (nic != null)
                    {
                        var mac = nic.GetPhysicalAddress();
                        return BitConverter.ToString(mac.GetAddressBytes()).Replace("-", "");
                    }
                }
                catch (Exception ex)
                {
                    LoggerHelper.Warning($"获取Linux MAC地址失败: {ex.Message}");
                }
            }

            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "ioreg",
                        Arguments = "-rd1 -c IOPlatformExpertDevice",
                        RedirectStandardOutput = true,
                        UseShellExecute = false
                    }
                };
                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                var match = Regex.Match(output, @"IOPlatformUUID"" = ""(.+?)""");
                return match.Success ? match.Groups[1].Value : string.Empty;
            }
        }
        catch (Exception e)
        {
            LoggerHelper.Error(e);
            return string.Empty;
        }
        return string.Empty;
    }
    private static string GetDeviceKeys()
    {
        var fingerprint = Generate();
        var key = fingerprint.Substring(0, 32);
        return key;
    }

    // 加密（自动绑定设备）
    public static string Encrypt(string plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
            return string.Empty;
        var key = GetDeviceKeys();
        var encryptedData = EncryptProvider.AESEncrypt(plainText, key);
        return encryptedData;
    }

    // 解密（仅当前设备可用）
    public static string Decrypt(string encryptedBase64)
    {
        try
        {
            var key = GetDeviceKeys();
            var decryptedData = string.IsNullOrWhiteSpace(encryptedBase64) ? encryptedBase64 : EncryptProvider.AESDecrypt(encryptedBase64, key);
            return decryptedData;
        }
        catch (Exception e)
        {
            LoggerHelper.Warning(e);
            return string.Empty;
        }
    }
}
