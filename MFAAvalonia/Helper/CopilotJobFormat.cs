using MFAAvalonia.Extensions.MaaFW;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MFAAvalonia.Helper;

internal static class CopilotJobFormat
{
    internal static JsonNode? Parse(string json)
    {
        try
        {
            return JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static bool IsValidPipeline(JsonNode? node) =>
        node is JsonObject { Count: > 0 } pipeline &&
        pipeline.All(entry => entry.Key.StartsWith('$') || entry.Value is JsonObject);

    internal static bool IsRecording(JsonNode? node) =>
        node is JsonObject job &&
        job["actions"] is JsonObject { Count: > 0 } actions &&
        actions.All(entry => entry.Value is JsonArray);

    internal static JsonObject? GetPipeline(JsonObject job)
    {
        if (job["actions"] is JsonObject actions && IsValidPipeline(actions))
            return actions;

        return IsValidPipeline(job) ? job : null;
    }

    internal static bool IsValidActiveJobFile(string path) => IsValidPipeline(Parse(File.ReadAllText(path)));

    internal static void QuarantineInvalidActiveJobs(IEnumerable<string> resources, Action<string> notify)
    {
        var invalidDir = Path.Combine(MaaProcessor.Resource, "copilot-cache", "_invalid");
        foreach (var resource in resources)
        {
            try
            {
                var activeDir = Path.Combine(resource, "pipeline", "copilot");
                if (!Directory.Exists(activeDir))
                    continue;

                foreach (var file in Directory.EnumerateFiles(activeDir, "*", SearchOption.AllDirectories))
                {
                    var extension = Path.GetExtension(file);
                    var name = Path.GetFileName(file);
                    if ((!extension.Equals(".json", StringComparison.OrdinalIgnoreCase) &&
                         !extension.Equals(".jsonc", StringComparison.OrdinalIgnoreCase)) ||
                        name.Equals("copilot_config.json", StringComparison.OrdinalIgnoreCase))
                        continue;

                    try
                    {
                        if (IsValidActiveJobFile(file))
                            continue;

                        Directory.CreateDirectory(invalidDir);
                        var destination = Path.Combine(invalidDir, name);
                        try
                        {
                            File.Move(file, destination);
                        }
                        catch (IOException) when (File.Exists(destination))
                        {
                            var stem = Path.GetFileNameWithoutExtension(name);
                            destination = Path.Combine(invalidDir, $"{stem}-{DateTime.Now:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}{extension}");
                            File.Move(file, destination);
                        }

                        LoggerHelper.Warning($"已隔离格式不正确的作业文件：{file} -> {destination}");
                        notify($"已移除格式不正确的作业文件：{name}，请在作业列表重新加载");
                    }
                    catch (Exception ex)
                    {
                        LoggerHelper.Warning($"检查或隔离作业文件失败：{file}，{ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerHelper.Warning($"检查作业目录失败：{resource}，{ex.Message}");
            }
        }
    }
}
