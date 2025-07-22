using MFAAvalonia.Helper.Converters;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace MFAAvalonia.Extensions.MaaFW;

public partial class MaaInterface
{
    public class MaaInterfaceOptionCase
    {
        [JsonProperty("name")]
        public string? Name { get; set; }

        [JsonProperty("pipeline_override")]
        public Dictionary<string, JToken>? PipelineOverride { get; set; }

        public override string? ToString()
        {
            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Ignore
            };
            return JsonConvert.SerializeObject(PipelineOverride, settings);
        }
    }

    public class MaaInterfaceOption
    {
        [JsonIgnore]
        public string? Name { get; set; } = string.Empty;
        [JsonProperty("cases")]
        public List<MaaInterfaceOptionCase>? Cases { get; set; }
        [JsonProperty("default_case")]
        public string? DefaultCase { get; set; }
        
        [JsonProperty("doc")]
        [JsonConverter(typeof(GenericSingleOrListConverter<string>))]
        public List<string>? Document { get; set; }
    }
    
    public class MaaInterfaceSelectAdvanced
    {
        [JsonProperty("name")]
        public string? Name { get; set; }
        
        [JsonProperty("data")]
        public Dictionary<string, string?> Data = new();
    
        [JsonIgnore]
        public string PipelineOverride = "{}";
        
        public override string? ToString()
        {
            return Name ?? string.Empty;
        }
    }
 
    public class MaaInterfaceSelectOption
    {
        [JsonProperty("name")]
        public string? Name { get; set; }

        [JsonProperty("index")]
        public int? Index { get; set; }

        public override string? ToString()
        {
            return Name ?? string.Empty;
        }
    }

    public class MaaInterfaceTask
    {
        [JsonProperty("name")] public string? Name;
        [JsonProperty("entry")] public string? Entry;
        [JsonConverter(typeof(GenericSingleOrListConverter<string>))] [JsonProperty("doc")]
        public List<string>? Document;
        [JsonProperty("check", 
            NullValueHandling = NullValueHandling.Include,
            DefaultValueHandling = DefaultValueHandling.Include)] public bool? Check = false;
        [JsonProperty("repeatable")] public bool? Repeatable;
        [JsonProperty("repeat_count")] public int? RepeatCount;
        [JsonProperty("advanced")] public List<MaaInterfaceSelectAdvanced>? Advanced;
        [JsonProperty("option")] public List<MaaInterfaceSelectOption>? Option;
        
        [JsonProperty("pipeline_override")] public Dictionary<string, JToken>? PipelineOverride;

        public override string ToString()
        {
            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Ignore
            };

            return JsonConvert.SerializeObject(this, settings);
        }

        /// <summary>
        /// Creates a deep copy of the current <see cref="MaaInterfaceTask"/> instance.
        /// </summary>
        /// <returns>A new <see cref="MaaInterfaceTask"/> instance that is a deep copy of the current instance.</returns>
        public MaaInterfaceTask Clone()
        {
            return JsonConvert.DeserializeObject<MaaInterfaceTask>(ToString()) ?? new MaaInterfaceTask();
        }
    }

    public class MaaInterfaceResource
    {
        [JsonProperty("name")]
        public string? Name { get; set; }

        [JsonConverter(typeof(GenericSingleOrListConverter<string>))]
        [JsonProperty("path")]
        public List<string>? Path { get; set; }
    }

    public class MaaResourceVersion
    {
        [JsonProperty("name")]
        public string? Name { get; set; }
        [JsonProperty("version")]
        public string? Version { get; set; }
        [JsonProperty("url")]
        public string? Url { get; set; }


        public override string? ToString()
        {
            return Version ?? string.Empty;
        }
    }

    public class MaaResourceControllerAdb
    {
        [JsonProperty("input")]
        public long? Input { get; set; }
        [JsonProperty("screencap")]
        public long? ScreenCap { get; set; }
        [JsonProperty("config")]
        public object? Adb { get; set; }
    }

    public class MaaResourceControllerWin32
    {
        [JsonProperty("class_regex")]
        public string? ClassRegex { get; set; }
        [JsonProperty("window_regex")]
        public string? WindowRegex { get; set; }
        [JsonProperty("input")]
        public long? Input { get; set; }
        [JsonProperty("screencap")]
        public long? ScreenCap { get; set; }
    }

    public class MaaInterfaceAgent
    {
        [JsonProperty("child_exec")]
        public string? ChildExec { get; set; }
        [JsonProperty("child_args")]
        [JsonConverter(typeof(GenericSingleOrListConverter<string>))]
        public List<string>? ChildArgs { get; set; }
        [JsonProperty("identifier")]
        public string? Identifier { get; set; }
    }

    public class MaaResourceController
    {
        [JsonProperty("name")]
        public string? Name { get; set; }
        [JsonProperty("type")]
        public string? Type { get; set; }
        [JsonProperty("adb")]
        public MaaResourceControllerAdb? Adb { get; set; }
        [JsonProperty("win32")]
        public MaaResourceControllerWin32? Win32 { get; set; }
    }

    [JsonProperty("mirrorchyan_rid")]
    public string? RID { get; set; }

    [JsonProperty("mirrorchyan_multiplatform")]
    public bool? Multiplatform { get; set; }

    [JsonProperty("name")]
    public string? Name { get; set; }

    [JsonProperty("version")]
    public string? Version { get; set; }
    
    [JsonProperty("mfa_max_version")]
    public string? MFAMaxVersion { get; set; }
    
    [JsonProperty("mfa_min_version")]
    public string? MFAMinVersion { get; set; }

    [JsonProperty("message")]
    public string? Message { get; set; }

    [JsonProperty("url")]   
    public string? Url { get; set; }

    [JsonProperty("custom_title")]
    public string? CustomTitle { get; set; }

    [JsonProperty("default_controller")]
    public string? DefaultController { get; set; }

    [JsonProperty("lock_controller")]
    public bool LockController { get; set; }

    [JsonProperty("controller")]
    public List<MaaResourceController>? Controller { get; set; }
    [JsonProperty("resource")]
    public List<MaaInterfaceResource>? Resource { get; set; }
    [JsonProperty("task")]
    public List<MaaInterfaceTask>? Task { get; set; }

    [JsonProperty("agent")]
    public MaaInterfaceAgent? Agent { get; set; }

    [JsonProperty("advanced")]
    public Dictionary<string, MaaInterfaceAdvancedOption>? Advanced { get; set; }
    
    [JsonProperty("option")]
    public Dictionary<string, MaaInterfaceOption>? Option { get; set; }

    [JsonExtensionData]
    public Dictionary<string, object> AdditionalData { get; set; } = new();


    [JsonIgnore]
    public Dictionary<string, MaaInterfaceResource> Resources { get; } = new();

    // 替换单个字符串中的 "{PROJECT_DIR}" 为指定的替换值
    public static string? ReplacePlaceholder(string? input, string? replacement)
    {
        return string.IsNullOrEmpty(input) ? string.Empty : input.Replace("{PROJECT_DIR}", replacement);
    }

    // 替换字符串列表中的每个字符串中的 "{PROJECT_DIR}"
    public static List<string> ReplacePlaceholder(IEnumerable<string>? inputs, string? replacement)
    {
        if (inputs == null) return new List<string>();

        return inputs.ToList().ConvertAll(input => ReplacePlaceholder(input, replacement));
    }

    public override string? ToString()
    {
        var settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore
        };

        return JsonConvert.SerializeObject(this, settings);
    }
}
