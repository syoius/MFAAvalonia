using System;
using System.Collections.Generic;
using System.Linq;
using MFAAvalonia.Extensions.MaaFW;

namespace MFAAvalonia.Helper;

/// <summary>
/// Creates Copilot task state from the resource definition and Copilot's own saved options.
/// Main-page task instances must never be used as the source.
/// </summary>
public static class CopilotTaskFactory
{
    public const string DefaultTaskName = "✨ 自动抄作业V3";

    public static MaaInterface.MaaInterfaceTask? Create(
        MaaInterface? maaInterface, MaaInterface.MaaInterfaceTask? savedTask)
    {
        var definition = maaInterface?.Task?.FirstOrDefault(task =>
            string.Equals(task.Name, DefaultTaskName, StringComparison.OrdinalIgnoreCase))
            ?? maaInterface?.Task?.FirstOrDefault(task =>
                string.Equals(LanguageHelper.GetLocalizedString(task.DisplayName), DefaultTaskName, StringComparison.OrdinalIgnoreCase));
        if (definition == null)
            return null;

        var task = definition.Clone();
        // Clone saved state as well: controls and execution must not mutate persisted state by reference.
        var saved = savedTask?.Clone();
        task.Check = true;
        task.RepeatCount = saved?.RepeatCount ?? task.RepeatCount;
        RestoreOptions(maaInterface, task.Option, saved?.Option);

        if (task.Advanced != null)
        {
            foreach (var option in task.Advanced)
            {
                var previous = saved?.Advanced?.FirstOrDefault(item => item.Name == option.Name);
                if (previous != null)
                    option.Data = previous.Data;
                // Regenerate from the current definition when building execution parameters.
                option.PipelineOverride = "{}";
            }
        }

        return task;
    }

    private static void RestoreOptions(MaaInterface? maaInterface,
        List<MaaInterface.MaaInterfaceSelectOption>? options,
        List<MaaInterface.MaaInterfaceSelectOption>? savedOptions)
    {
        if (options == null)
            return;

        foreach (var option in options)
        {
            TaskLoader.SetDefaultOptionValue(maaInterface, option);
            var previous = savedOptions?.FirstOrDefault(item => item.Name == option.Name);
            if (previous == null)
                continue;

            if (maaInterface?.Option?.TryGetValue(option.Name ?? string.Empty, out var definition) == true)
            {
                if (previous.Index is int index && definition.Cases is { Count: > 0 }
                    && index >= 0 && index < definition.Cases.Count)
                    option.Index = index;

                if (definition.IsInput && previous.Data != null)
                {
                    option.Data ??= new Dictionary<string, string?>();
                    foreach (var (key, value) in previous.Data)
                        option.Data[key] = value;
                }
            }

            option.SubOptions = previous.SubOptions;
            option.PipelineOverride = "{}";
        }
    }
}
