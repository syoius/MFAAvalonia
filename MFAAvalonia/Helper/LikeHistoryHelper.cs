using System;
using System.Collections.Generic;

namespace MFAAvalonia.Helper;

public static class LikeHistoryHelper
{
    private const string ConfigName = "copilot_like_history";

    private class LikeHistoryModel
    {
        public HashSet<string> Ids { get; set; } = new();
    }

    private static LikeHistoryModel Load()
    {
        return JsonHelper.LoadConfig(ConfigName, new LikeHistoryModel());
    }

    private static void Save(LikeHistoryModel model)
    {
        JsonHelper.SaveConfig(ConfigName, model);
    }

    public static bool HasLiked(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        var model = Load();
        return model.Ids.Contains(id);
    }

    public static bool HasLiked(long id)
    {
        return HasLiked(id.ToString());
    }

    public static void MarkLiked(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        var model = Load();
        if (model.Ids.Add(id))
        {
            Save(model);
        }
    }

    public static void MarkLiked(long id)
    {
        MarkLiked(id.ToString());
    }
}

