﻿using Avalonia.Media;
using Avalonia.Threading;
using MaaFramework.Binding;
using MaaFramework.Binding.Notification;
using MFAAvalonia.Configuration;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.Converters;
using MFAAvalonia.Helper.ValueType;
using MFAAvalonia.Views.Windows;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MFAAvalonia.Extensions.MaaFW;
#pragma warning  disable CS4014 // 由于此调用不会等待，因此在此调用完成之前将会继续执行当前方法.
#pragma warning  disable CS1998 // 此异步方法缺少 "await" 运算符，将以同步方式运行。
#pragma warning disable CA1416 //  可在 'linux', 'macOS/OSX', 'windows' 上访问此调用站点。
public class MaaProcessor
{
    #region 属性

    private static Random Random = new();
    public static string Resource => Path.Combine(AppContext.BaseDirectory, "Resource");
    public static string ResourceBase => Path.Combine(Resource, "base");
    public static MaaProcessor Instance { get; } = new();
    public static MaaToolkit Toolkit { get; } = new();
    public static MaaUtility Utility { get; } = new();

    private static MaaInterface? _interface;

    public Dictionary<string, MaaNode> BaseNodes = new();

    public Dictionary<string, MaaNode> NodeDictionary = new();
    public ObservableQueue<MFATask> TaskQueue { get; } = new();

    public MaaProcessor()
    {
        TaskQueue.CountChanged += (_, args) =>
        {
            if (args.NewValue > 0)
                Instances.RootViewModel.IsRunning = true;
        };
    }

    public static MaaInterface? Interface
    {
        get => _interface;
        private set
        {
            _interface = value;

            foreach (var customResource in value?.Resource ?? Enumerable.Empty<MaaInterface.MaaInterfaceResource>())
            {
                var nameKey = customResource.Name?.Trim() ?? string.Empty;
                var paths = MaaInterface.ReplacePlaceholder(customResource.Path ?? new(), AppContext.BaseDirectory);

                value!.Resources[nameKey] = new MaaInterface.MaaInterfaceResource
                {
                    Name = LanguageHelper.GetLocalizedString(nameKey),
                    Path = paths
                };
            }
        }
    }

    public static MaaFWConfiguration Config { get; } = new();
    public MaaTasker? MaaTasker { get; set; }

    public void SetTasker(MaaTasker? maaTasker = null)
    {
        if (maaTasker == null)
        {
            _agentClient?.LinkStop();
            _agentClient?.Dispose();
            _agentClient = null;
            _agentStarted = false;
            _agentProcess?.Kill();
            _agentProcess?.Dispose();
            _agentProcess = null;
        }
        MaaTasker = maaTasker;
    }

    public MaaTasker? GetTasker(CancellationToken token = default)
    {
        var task = GetTaskerAsync(token);
        task.Wait(token);
        return task.Result;
    }

    public async Task<MaaTasker?> GetTaskerAsync(CancellationToken token = default)
    {
        MaaTasker ??= await InitializeMaaTasker(token);
        return MaaTasker;
    }
    public ObservableCollection<DragItemViewModel> TasksSource { get; private set; } =
        [];
    public AutoInitDictionary AutoInitDictionary { get; } = new();

    private MaaAgentClient? _agentClient;
    private bool _agentStarted;
    private Process? _agentProcess;

    #endregion

    #region MaaTasker初始化

    async private Task<MaaTasker?> InitializeMaaTasker(CancellationToken token) // 添加 async 和 token
    {
        AutoInitDictionary.Clear();
        LoggerHelper.Info("LoadingResources".ToLocalization());

        MaaResource maaResource = null;
        try
        {
            var resources = Instances.TaskQueueViewModel.CurrentResources
                    .FirstOrDefault(c => c.Name == Instances.TaskQueueViewModel.CurrentResource)?.Path
                ?? [];
            LoggerHelper.Info($"Resource: {string.Join(",", resources)}");


            maaResource = await TaskManager.RunTaskAsync(() =>
            {
                token.ThrowIfCancellationRequested();
                return new MaaResource(resources);
            }, token, catchException: true, shouldLog: false, handleError: exception => HandleInitializationError(exception, "LoadResourcesFailed".ToLocalization()));

            maaResource.SetOptionInferenceDevice(Instances.PerformanceUserControlModel.GpuOption);
            LoggerHelper.Info($"GPU acceleration: {Instances.PerformanceUserControlModel.GpuOption}");
        }
        catch (OperationCanceledException)
        {
            LoggerHelper.Warning("Resource loading was canceled");
            return null;
        }
        catch (Exception)
        {
            return null;
        }

        // 初始化控制器部分同理
        MaaController controller = null;
        try
        {
            controller = await TaskManager.RunTaskAsync(() =>
            {
                token.ThrowIfCancellationRequested();
                return InitializeController(Instances.TaskQueueViewModel.CurrentController == MaaControllerTypes.Adb);
            }, token, catchException: true, shouldLog: false, handleError: exception => HandleInitializationError(exception,
                "ConnectingEmulatorOrWindow".ToLocalization()
                    .FormatWith(Instances.TaskQueueViewModel.CurrentController == MaaControllerTypes.Adb
                        ? "Emulator".ToLocalization()
                        : "Window".ToLocalization()), true,
                "InitControllerFailed".ToLocalization()));
        }
        catch (OperationCanceledException)
        {
            LoggerHelper.Warning("Controller initialization was canceled");
            return null;
        }
        catch (Exception)
        {
            return null;
        }
        try
        {
            token.ThrowIfCancellationRequested();


            var tasker = new MaaTasker
            {
                Controller = controller,
                Resource = maaResource,
                Utility = Utility,
                Toolkit = Toolkit,
                DisposeOptions = DisposeOptions.All,
            };

            // 获取代理配置（假设Interface在UI线程中访问）
            var agentConfig = Interface?.Agent;
            if (agentConfig is { ChildExec: not null } && !_agentStarted)
            {
                RootView.AddLogByKey("StartingAgent");
                if (_agentClient != null)
                {
                    _agentClient.LinkStop();
                    _agentClient.Dispose();
                    _agentClient = null;
                    _agentProcess?.Kill();
                    _agentProcess?.Dispose();
                    _agentProcess = null;
                }
                _agentClient = new MaaAgentClient
                {
                    Resource = maaResource,
                    DisposeOptions = DisposeOptions.All
                };
                var chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
                var identifier = string.IsNullOrWhiteSpace(Interface?.Agent?.Identifier) ? new string(Enumerable.Repeat(chars, 8).Select(c => c[Random.Next(c.Length)]).ToArray()) : Interface.Agent.Identifier;
                var socket = _agentClient.CreateSocket(identifier);
                if (string.IsNullOrWhiteSpace(socket))
                {
                    throw new Exception("Socket creation failed");
                }

                try
                {
                    if (!Directory.Exists($"{AppContext.BaseDirectory}"))
                        Directory.CreateDirectory($"{AppContext.BaseDirectory}");
                    var program = MaaInterface.ReplacePlaceholder(agentConfig.ChildExec, AppContext.BaseDirectory);
                    var args = $"{string.Join(" ", MaaInterface.ReplacePlaceholder(agentConfig.ChildArgs ?? Enumerable.Empty<string>(), AppContext.BaseDirectory))} {socket}";
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = program,
                        WorkingDirectory = $"{AppContext.BaseDirectory}",
                        Arguments = $"{(program.Contains("python") && args.Contains(".py") && !args.Contains("-u ") ? "-u " : "")}{args}",
                        UseShellExecute = false,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        CreateNoWindow = true
                    };

                    _agentProcess = new Process
                    {
                        StartInfo = startInfo
                    };

                    _agentProcess.OutputDataReceived += (sender, args) =>
                    {
                        if (!string.IsNullOrEmpty(args.Data))
                        {
                            DispatcherHelper.PostOnMainThread(() =>
                            {
                                RootView.AddLog($"{args.Data}");
                            });
                        }
                    };

                    _agentProcess.ErrorDataReceived += (sender, args) =>
                    {
                        if (!string.IsNullOrEmpty(args.Data))
                        {
                            DispatcherHelper.PostOnMainThread(() =>
                            {
                                RootView.AddLog($"{args.Data}");
                            });
                        }
                    };

                    _agentProcess.Start();
                    LoggerHelper.Info(
                        $"Agent启动: {MaaInterface.ReplacePlaceholder(agentConfig.ChildExec, AppContext.BaseDirectory)} {string.Join(" ", MaaInterface.ReplacePlaceholder(agentConfig.ChildArgs ?? Enumerable.Empty<string>(), AppContext.BaseDirectory))} {socket} "
                        + $"socket_id: {socket}");
                    _agentProcess.BeginOutputReadLine();
                    _agentProcess.BeginErrorReadLine();

                    TaskManager.RunTaskAsync(async () => await _agentProcess.WaitForExitAsync(token), token);

                }
                catch (Exception ex)
                {
                    LoggerHelper.Error($"Agent启动失败: {ex.Message}");
                }

                _agentClient?.LinkStart();
                _agentStarted = true;
            }
            // RegisterCustomRecognitionsAndActions(tasker);
            Instances.TaskQueueViewModel.SetConnected(true);
            tasker.Utility.SetOptionRecording(ConfigurationManager.Maa.GetValue(ConfigurationKeys.Recording, false));
            tasker.Utility.SetOptionSaveDraw(ConfigurationManager.Maa.GetValue(ConfigurationKeys.SaveDraw, false));
            tasker.Utility.SetOptionShowHitDraw(ConfigurationManager.Maa.GetValue(ConfigurationKeys.ShowHitDraw, false));
            tasker.Callback += (_, args) =>
            {
                var jObject = JObject.Parse(args.Details);
                var name = jObject["name"]?.ToString() ?? string.Empty;
                if (args.Message.StartsWith(MaaMsg.Node.Action.Prefix))
                {
                    if (NodeDictionary.TryGetValue(name, out var taskModel))
                    {
                        DisplayFocus(taskModel, args.Message);
                    }
                }
            };
            return tasker;
        }
        catch (OperationCanceledException)
        {
            LoggerHelper.Warning("Tasker initialization was canceled");
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }
    private void DisplayFocus(MaaNode taskModel, string message)
    {
        switch (message)
        {
            case MaaMsg.Node.Action.Succeeded:
                if (taskModel.FocusSucceeded != null)
                {
                    for (int i = 0; i < taskModel.FocusSucceeded.Count; i++)
                    {
                        IBrush brush = null;
                        var tip = taskModel.FocusSucceeded[i];
                        try
                        {
                            if (taskModel.FocusSucceededColor != null && taskModel.FocusSucceededColor.Count > i)
                                brush = BrushHelper.ConvertToBrush(taskModel.FocusSucceededColor[i]);
                        }
                        catch (Exception e)
                        {
                            LoggerHelper.Error(e);
                        }

                        RootView.AddLog(HandleStringsWithVariables(tip), brush);
                    }
                }
                break;
            case MaaMsg.Node.Action.Failed:
                if (taskModel.FocusFailed != null)
                {
                    for (int i = 0; i < taskModel.FocusFailed.Count; i++)
                    {
                        IBrush brush = null;
                        var tip = taskModel.FocusFailed[i];
                        try
                        {
                            if (taskModel.FocusFailedColor != null && taskModel.FocusFailedColor.Count > i)
                                brush = BrushHelper.ConvertToBrush(taskModel.FocusFailedColor[i]);
                        }
                        catch (Exception e)
                        {
                            LoggerHelper.Error(e);
                        }

                        RootView.AddLog(HandleStringsWithVariables(tip), brush);
                    }
                }
                break;
            case MaaMsg.Node.Action.Starting:
                if (!string.IsNullOrWhiteSpace(taskModel.FocusToast))
                {
                    ToastNotification.Show(taskModel.FocusToast);
                }
                if (taskModel.FocusTip != null)
                {
                    for (int i = 0; i < taskModel.FocusTip.Count; i++)
                    {
                        IBrush? brush = null;
                        var tip = taskModel.FocusTip[i];
                        try
                        {
                            if (taskModel.FocusTipColor != null && taskModel.FocusTipColor.Count > i)
                            {
                                brush = BrushHelper.ConvertToBrush(taskModel.FocusTipColor[i]);
                            }
                        }
                        catch (Exception e)
                        {
                            LoggerHelper.Error(e);
                        }

                        RootView.AddLog(HandleStringsWithVariables(tip), brush);
                    }
                }
                break;
        }
    }
    public static string HandleStringsWithVariables(string content)
    {
        try
        {
            return Regex.Replace(content, @"\{(\+\+|\-\-)?(\w+)(\+\+|\-\-)?([\+\-\*/]\w+)?\}", match =>
            {
                var prefix = match.Groups[1].Value;
                var counterKey = match.Groups[2].Value;
                var suffix = match.Groups[3].Value;
                var operation = match.Groups[4].Value;

                int value = Instance.AutoInitDictionary.GetValueOrDefault(counterKey, 0);

                // 前置操作符7
                if (prefix == "++")
                {
                    value = ++Instance.AutoInitDictionary[counterKey];
                }
                else if (prefix == "--")
                {
                    value = --Instance.AutoInitDictionary[counterKey];
                }

                // 后置操作符
                if (suffix == "++")
                {
                    value = Instance.AutoInitDictionary[counterKey]++;
                }
                else if (suffix == "--")
                {
                    value = Instance.AutoInitDictionary[counterKey]--;
                }

                // 算术操作
                if (!string.IsNullOrEmpty(operation))
                {
                    string operationType = operation[0].ToString();
                    string operandKey = operation.Substring(1);

                    if (Instance.AutoInitDictionary.TryGetValue(operandKey, out var operandValue))
                    {
                        value = operationType switch
                        {
                            "+" => value + operandValue,
                            "-" => value - operandValue,
                            "*" => value * operandValue,
                            "/" => value / operandValue,
                            _ => value
                        };
                    }
                }

                return value.ToString();
            });
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            ErrorView.ShowException(e);
            return content;
        }
    }

    private void HandleInitializationError(Exception e,
        string message,
        bool hasWarning = false,
        string waringMessage = "")
    {
        ToastHelper.Error(message);
        if (hasWarning)
            LoggerHelper.Warning(waringMessage);
        LoggerHelper.Error(e.ToString());
    }

    private MaaController InitializeController(bool isAdb)
    {
        if (isAdb)
        {
            LoggerHelper.Info($"AdbPath: {Config.AdbDevice.AdbPath}");
            LoggerHelper.Info($"AdbSerial: {Config.AdbDevice.AdbSerial}");
            LoggerHelper.Info($"ScreenCap: {Config.AdbDevice.ScreenCap}");
            LoggerHelper.Info($"Input: {Config.AdbDevice.Input}");
            LoggerHelper.Info($"Config: {Config.AdbDevice.Config}");
        }
        else
        {
            LoggerHelper.Info($"HWnd: {Config.DesktopWindow.HWnd}");
            LoggerHelper.Info($"ScreenCap: {Config.DesktopWindow.ScreenCap}");
            LoggerHelper.Info($"Input: {Config.DesktopWindow.Input}");
            LoggerHelper.Info($"Link: {Config.DesktopWindow.Link}");
            LoggerHelper.Info($"Check: {Config.DesktopWindow.Check}");
        }
        return isAdb
            ? new MaaAdbController(
                Config.AdbDevice.AdbPath,
                Config.AdbDevice.AdbSerial,
                Config.AdbDevice.ScreenCap, Config.AdbDevice.Input,
                !string.IsNullOrWhiteSpace(Config.AdbDevice.Config) ? Config.AdbDevice.Config : "{}")
            : new MaaWin32Controller(
                Config.DesktopWindow.HWnd,
                Config.DesktopWindow.ScreenCap, Config.DesktopWindow.Input,
                Config.DesktopWindow.Link,
                Config.DesktopWindow.Check);
    }

    public class MaaFWConfiguration
    {
        public AdbDeviceCoreConfig AdbDevice { get; set; } = new();
        public DesktopWindowCoreConfig DesktopWindow { get; set; } = new();
    }

    public class DesktopWindowCoreConfig
    {
        public string Name { get; set; } = string.Empty;
        public nint HWnd { get; set; }

        public Win32InputMethod Input { get; set; } = Win32InputMethod.SendMessage;

        public Win32ScreencapMethod ScreenCap { get; set; } = Win32ScreencapMethod.FramePool;
        public LinkOption Link { get; set; } = LinkOption.Start;
        public CheckStatusOption Check { get; set; } = CheckStatusOption.ThrowIfNotSucceeded;
    }

    public class AdbDeviceCoreConfig
    {
        public string Name { get; set; } = string.Empty;
        public string AdbPath { get; set; } = "adb";
        public string AdbSerial { get; set; } = "";
        public string Config { get; set; } = "{}";
        public AdbInputMethods Input { get; set; } = AdbInputMethods.Maatouch;
        public AdbScreencapMethods ScreenCap { get; set; } = AdbScreencapMethods.Default;
    }

    public static (string Name, string Version, string CustomTitle) ReadInterface()
    {
        if (!File.Exists($"{AppContext.BaseDirectory}/interface.json"))
        {
            LoggerHelper.Info("未找到interface文件，生成interface.json...");
            Interface = new MaaInterface
                {
                    Version = "1.0",
                    Name = "Debug",
                    Task = [],
                    Resource =
                    [
                        new MaaInterface.MaaInterfaceResource()
                        {
                            Name = "默认",
                            Path =
                            [
                                "{PROJECT_DIR}/resource/base",
                            ],
                        },
                    ],
                    Controller =
                    [
                        new MaaInterface.MaaResourceController()
                        {
                            Name = "adb 默认方式",
                            Type = "adb"
                        },
                    ],
                    Option = new Dictionary<string, MaaInterface.MaaInterfaceOption>
                    {
                        {
                            "测试", new MaaInterface.MaaInterfaceOption()
                            {
                                Cases =
                                [

                                    new MaaInterface.MaaInterfaceOptionCase
                                    {
                                        Name = "测试1",
                                        PipelineOverride = new Dictionary<string, MaaNode>()
                                    },
                                    new MaaInterface.MaaInterfaceOptionCase
                                    {
                                        Name = "测试2",
                                        PipelineOverride = new Dictionary<string, MaaNode>()
                                    }
                                ]
                            }
                        }
                    }
                }
                ;
            JsonHelper.SaveJson(Path.Combine(AppContext.BaseDirectory, "interface.json"),
                Interface, new MaaInterfaceSelectOptionConverter(true));

        }
        else
        {
            Interface =
                JsonHelper.LoadJson(Path.Combine(AppContext.BaseDirectory, "interface.json"),
                    new MaaInterface(),
                    new MaaInterfaceSelectOptionConverter(false));
        }

        return (Interface?.Name ?? string.Empty, Interface?.Version ?? string.Empty, Interface?.CustomTitle ?? string.Empty);

    }

    public bool InitializeData(Collection<DragItemViewModel>? dragItem = null)
    {
        var (name, version, customTitle) = ReadInterface();
        if (!string.IsNullOrWhiteSpace(name) && !name.Equals("debug", StringComparison.OrdinalIgnoreCase))
            Instances.RootViewModel.ShowResourceName(name);
        if (!string.IsNullOrWhiteSpace(version) && !version.Equals("debug", StringComparison.OrdinalIgnoreCase))
        {
            Instances.RootViewModel.ShowResourceVersion(version);
            Instances.VersionUpdateSettingsUserControlModel.ResourceVersion = version;
        }
        if (!string.IsNullOrWhiteSpace(customTitle))
            Instances.RootViewModel.ShowCustomTitle(customTitle);

        if (Interface != null)
        {
            AppendVersionLog(Interface.Version);
            TasksSource.Clear();
            LoadTasks(Interface.Task ?? new List<MaaInterface.MaaInterfaceTask>(), dragItem);
        }
        ConnectToMAA();

        return LoadTask();
    }

    private bool LoadTask()
    {
        try
        {
            var taskDictionary = new Dictionary<string, MaaNode>();
            if (Instances.TaskQueueViewModel.CurrentResources.Count > 0)
            {
                if (string.IsNullOrWhiteSpace(Instances.TaskQueueViewModel.CurrentResource) && !string.IsNullOrWhiteSpace(Instances.TaskQueueViewModel.CurrentResources[0].Name))
                    Instances.TaskQueueViewModel.CurrentResource = Instances.TaskQueueViewModel.CurrentResources[0].Name;
            }
            if (Instances.TaskQueueViewModel.CurrentResources.Any(r => r.Name == Instances.TaskQueueViewModel.CurrentResource))
            {
                var resources = Instances.TaskQueueViewModel.CurrentResources.FirstOrDefault(r => r.Name == Instances.TaskQueueViewModel.CurrentResource);
                if (resources?.Path != null)
                {
                    foreach (var resourcePath in resources.Path)
                    {
                        if (!Path.Exists($"{resourcePath}/pipeline/"))
                            break;
                        var jsonFiles = Directory.GetFiles($"{resourcePath}/pipeline/", "*.json", SearchOption.AllDirectories);
                        var taskDictionaryA = new Dictionary<string, MaaNode>();
                        foreach (var file in jsonFiles)
                        {
                            var content = File.ReadAllText(file);

                            var taskData = JsonConvert.DeserializeObject<Dictionary<string, MaaNode>>(content);
                            if (taskData == null || taskData.Count == 0)
                                break;
                            foreach (var task in taskData)
                            {
                                if (!taskDictionaryA.TryAdd(task.Key, task.Value))
                                {
                                    ToastHelper.Error("DuplicateTaskError".ToLocalizationFormatted(false, task.Key));
                                    return false;
                                }
                            }
                        }

                        taskDictionary = taskDictionary.MergeMaaNodes(taskDictionaryA);
                    }
                }
            }

            if (taskDictionary.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace($"{ResourceBase}/pipeline") && !Directory.Exists($"{ResourceBase}/pipeline"))
                {
                    try
                    {
                        Directory.CreateDirectory($"{ResourceBase}/pipeline");
                    }
                    catch (Exception ex)
                    {
                        LoggerHelper.Error(ex);
                    }
                }

                if (!File.Exists($"{ResourceBase}/pipeline/sample.json"))
                {
                    try
                    {
                        File.WriteAllText($"{ResourceBase}/pipeline/sample.json",
                            JsonConvert.SerializeObject(new Dictionary<string, MaaNode>
                            {
                                {
                                    "MFAWPF", new MaaNode
                                    {
                                        Action = "DoNothing"
                                    }
                                }
                            }, new JsonSerializerSettings()
                            {
                                Formatting = Formatting.Indented,
                                NullValueHandling = NullValueHandling.Ignore,
                                DefaultValueHandling = DefaultValueHandling.Ignore
                            }));
                    }
                    catch (Exception ex)
                    {
                        LoggerHelper.Error(ex);
                    }
                }
            }

            PopulateTasks(taskDictionary);

            return true;
        }
        catch (Exception ex)
        {
            ToastHelper.Error("PipelineLoadError".ToLocalizationFormatted(false, ex.Message)
            );
            Console.WriteLine(ex);
            LoggerHelper.Error(ex);
            return false;
        }
    }

    private void PopulateTasks(Dictionary<string, MaaNode> taskDictionary)
    {
        BaseNodes = taskDictionary;
        foreach (var task in taskDictionary)
        {
            task.Value.Name = task.Key;
            ValidateTaskLinks(taskDictionary, task);
        }
    }

    private void ValidateTaskLinks(Dictionary<string, MaaNode> taskDictionary,
        KeyValuePair<string, MaaNode> task)
    {
        ValidateNextTasks(taskDictionary, task.Value.Next);
        ValidateNextTasks(taskDictionary, task.Value.OnError, "on_error");
        ValidateNextTasks(taskDictionary, task.Value.Interrupt, "interrupt");
    }

    private void ValidateNextTasks(Dictionary<string, MaaNode> taskDictionary,
        object? nextTasks,
        string name = "next")
    {
        if (nextTasks is List<string> tasks)
        {
            foreach (var task in tasks)
            {
                if (!taskDictionary.ContainsKey(task))
                {
                    ToastHelper.Error("Error".ToLocalization(), "TaskNotFoundError".ToLocalizationFormatted(false, name, task));
                }
            }
        }
    }

    public void ConnectToMAA()
    {
        ConfigureMaaProcessorForADB();
        ConfigureMaaProcessorForWin32();
    }

    private void ConfigureMaaProcessorForADB()
    {
        if (Instances.TaskQueueViewModel.CurrentController == MaaControllerTypes.Adb)
        {
            var adbInputType = ConfigureAdbInputTypes();
            var adbScreenCapType = ConfigureAdbScreenCapTypes();

            Config.AdbDevice.Input = adbInputType;
            Config.AdbDevice.ScreenCap = adbScreenCapType;

            LoggerHelper.Info(
                $"{"AdbInputMode".ToLocalization()}{adbInputType},{"AdbCaptureMode".ToLocalization()}{adbScreenCapType}");
        }
    }

    public static string ScreenshotType()
    {
        if (Instances.TaskQueueViewModel.CurrentController == MaaControllerTypes.Adb)
            return ConfigureAdbScreenCapTypes().ToString();
        return ConfigureWin32ScreenCapTypes().ToString();
    }


    private static AdbInputMethods ConfigureAdbInputTypes()
    {
        return Instances.ConnectSettingsUserControlModel.AdbControlInputType;
    }

    private static AdbScreencapMethods ConfigureAdbScreenCapTypes()
    {
        return Instances.ConnectSettingsUserControlModel.AdbControlScreenCapType;
    }

    private void ConfigureMaaProcessorForWin32()
    {
        if (Instances.TaskQueueViewModel.CurrentController == MaaControllerTypes.Win32)
        {
            var win32InputType = ConfigureWin32InputTypes();
            var winScreenCapType = ConfigureWin32ScreenCapTypes();

            Config.DesktopWindow.Input = win32InputType;
            Config.DesktopWindow.ScreenCap = winScreenCapType;

            LoggerHelper.Info(
                $"{"AdbInputMode".ToLocalization()}{win32InputType},{"AdbCaptureMode".ToLocalization()}{winScreenCapType}");
        }
    }

    private static Win32ScreencapMethod ConfigureWin32ScreenCapTypes()
    {
        return Instances.ConnectSettingsUserControlModel.Win32ControlScreenCapType;
    }

    private static Win32InputMethod ConfigureWin32InputTypes()
    {
        return Instances.ConnectSettingsUserControlModel.Win32ControlInputType;
    }

    private bool FirstTask = true;

    private void LoadTasks(List<MaaInterface.MaaInterfaceTask> tasks, IList<DragItemViewModel>? oldDrags = null)
    {
        var items = ConfigurationManager.Current.GetValue(ConfigurationKeys.TaskItems, new List<MaaInterface.MaaInterfaceTask>()) ?? [];
        var drags = (oldDrags?.ToList() ?? []).Union(items.Select(interfaceItem => new DragItemViewModel(interfaceItem))).ToList();

        if (FirstTask)
        {
            InitializeResources();
            FirstTask = false;
        }


        var (updateList, removeList) = SynchronizeTaskItems(drags, tasks);
        updateList.RemoveAll(d => removeList.Contains(d));

        UpdateViewModels(updateList, tasks);
    }

    private void InitializeResources()
    {
        Instances.TaskQueueViewModel.CurrentResources =
            Interface?.Resources.Values.Count > 0
                ? new ObservableCollection<MaaInterface.MaaInterfaceResource>(Interface.Resources.Values.ToList())
                :
                [
                    new MaaInterface.MaaInterfaceResource
                    {
                        Name = "Default",
                        Path = [ResourceBase]
                    }
                ];
        if (Instances.TaskQueueViewModel.CurrentResources.Count > 0 && Instances.TaskQueueViewModel.CurrentResources.All(r => r.Name != Instances.TaskQueueViewModel.CurrentResource))
            Instances.TaskQueueViewModel.CurrentResource = Instances.TaskQueueViewModel.CurrentResources[0].Name ?? "Default";
    }

    private (List<DragItemViewModel> updateList, List<DragItemViewModel> removeList) SynchronizeTaskItems(
        IList<DragItemViewModel> drags,
        List<MaaInterface.MaaInterfaceTask> tasks)
    {
        var newDict = tasks.ToDictionary(t => (t.Name, t.Entry)); // 使用 (Name, Entry) 作为键
        var removeList = new List<DragItemViewModel>();
        var updateList = new List<DragItemViewModel>();

        if (drags.Count == 0)
        {
            updateList.AddRange(tasks.Select(t => new DragItemViewModel(t)).ToList());
        }
        else
        {
            foreach (var oldItem in drags)
            {
                if (newDict.TryGetValue((oldItem.Name, oldItem.InterfaceItem?.Entry), out var newItem))
                {
                    UpdateExistingItem(oldItem, newItem);
                    updateList.Add(oldItem);
                }
                else
                {
                    var sameNameTasks = tasks.Where(t => t.Entry == oldItem.InterfaceItem?.Entry).ToList();
                    if (sameNameTasks.Any())
                    {
                        var firstTask = sameNameTasks.First();
                        UpdateExistingItem(oldItem, firstTask);
                        updateList.Add(oldItem);
                    }
                    else
                    {
                        removeList.Add(oldItem);
                    }
                }
            }
        }
        return (updateList, removeList);
    }

    private void UpdateExistingItem(DragItemViewModel oldItem, MaaInterface.MaaInterfaceTask newItem)
    {
        if (oldItem.InterfaceItem == null) return;
        oldItem.InterfaceItem.Entry = newItem.Entry;
        oldItem.InterfaceItem.PipelineOverride = newItem.PipelineOverride;
        oldItem.InterfaceItem.Document = newItem.Document;
        oldItem.InterfaceItem.Repeatable = newItem.Repeatable;

        if (newItem.Option == null) return;

        var tempDict = oldItem.InterfaceItem.Option?.ToDictionary(t => t.Name) ?? new Dictionary<string, MaaInterface.MaaInterfaceSelectOption>();
        var maaInterfaceSelectOptions = JsonConvert.DeserializeObject<List<MaaInterface.MaaInterfaceSelectOption>>(JsonConvert.SerializeObject(newItem.Option));
        oldItem.InterfaceItem.Option = maaInterfaceSelectOptions.Select(opt =>
        {
            if (tempDict.TryGetValue(opt.Name ?? string.Empty, out var existing))
            {
                opt.Index = existing.Index;
            }
            else
            {
                SetDefaultOptionValue(opt);
            }
            return opt;
        }).ToList();
    }

    private void SetDefaultOptionValue(MaaInterface.MaaInterfaceSelectOption option)
    {
        if (!(Interface?.Option?.TryGetValue(option.Name ?? string.Empty, out var interfaceOption) ?? false)) return;

        var defaultIndex = interfaceOption.Cases?
                .FindIndex(c => c.Name == interfaceOption.DefaultCase)
            ?? -1;
        if (defaultIndex != -1) option.Index = defaultIndex;
    }

    private void UpdateViewModels(IList<DragItemViewModel> drags, List<MaaInterface.MaaInterfaceTask> tasks)
    {
        var newItems = tasks.Select(t => new DragItemViewModel(t)).ToList();
        foreach (var item in newItems)
        {
            if (item.InterfaceItem?.Option != null && !drags.Any())
            {
                item.InterfaceItem.Option.ForEach(SetDefaultOptionValue);
            }
        }

        TasksSource.AddRange(newItems);
        
        if (!Instances.TaskQueueViewModel.TaskItemViewModels.Any())
        {
            Instances.TaskQueueViewModel.TaskItemViewModels = new ObservableCollection<DragItemViewModel>(drags);
        }
    }

    public static void AppendVersionLog(string? resourceVersion)
    {
        if (resourceVersion is null)
        {
            return;
        }
        string debugFolderPath = Path.Combine(AppContext.BaseDirectory, "debug");
        if (!Directory.Exists(debugFolderPath))
        {
            Directory.CreateDirectory(debugFolderPath);
        }

        string logFilePath = Path.Combine(debugFolderPath, "maa.log");
        string timeStamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string formattedLogMessage =
            $"[{timeStamp}][INF][Px14600][Tx16498][Parser.cpp][L56][MaaNS::ProjectInterfaceNS::Parser::parse_interface] ";
        var logMessage = $"MFAWPF Version: [mfa.version={Instances.VersionUpdateSettingsUserControlModel.MfaVersion}] "
            + $"Interface Version: [data.version=v{resourceVersion.Replace("v", "")}] ";
        LoggerHelper.Info(logMessage);

        try
        {
            File.AppendAllText(logFilePath, formattedLogMessage + logMessage);
        }
        catch (Exception)
        {
            Console.WriteLine("尝试写入失败！");
        }
    }

    #endregion

    #region 开始任务

    static void MeasureExecutionTime(Action methodToMeasure)
    {
        var stopwatch = Stopwatch.StartNew();

        methodToMeasure();

        stopwatch.Stop();
        long elapsedMilliseconds = stopwatch.ElapsedMilliseconds;

        switch (elapsedMilliseconds)
        {
            case >= 800:
                RootView.AddLogByKey("ScreencapErrorTip", BrushHelper.ConvertToBrush("DarkGoldenrod"), false, elapsedMilliseconds.ToString(),
                    ScreenshotType());
                break;

            case >= 400:
                RootView.AddLogByKey("ScreencapWarningTip", BrushHelper.ConvertToBrush("DarkGoldenrod"), false, elapsedMilliseconds.ToString(),
                    ScreenshotType());
                break;

            default:
                RootView.AddLogByKey("ScreencapCost", null, false, elapsedMilliseconds.ToString(),
                    ScreenshotType());
                break;
        }
    }

    async static Task MeasureExecutionTimeAsync(Func<Task> methodToMeasure)
    {
        const int sampleCount = 4;
        long totalElapsed = 0;

        long min = 10000;
        long max = 0;
        for (int i = 0; i < sampleCount; i++)
        {
            var sw = Stopwatch.StartNew();
            await methodToMeasure();
            sw.Stop();
            min = Math.Min(min, sw.ElapsedMilliseconds);
            max = Math.Max(max, sw.ElapsedMilliseconds);
            totalElapsed += sw.ElapsedMilliseconds;
        }

        var avgElapsed = totalElapsed / sampleCount;

        switch (avgElapsed)
        {
            case >= 800:
                RootView.AddLogByKey("ScreencapErrorTip", BrushHelper.ConvertToBrush("DarkGoldenrod"), false, avgElapsed.ToString(),
                    ScreenshotType());
                break;

            case >= 400:
                RootView.AddLogByKey("ScreencapWarningTip", BrushHelper.ConvertToBrush("DarkGoldenrod"), false, avgElapsed.ToString(),
                    ScreenshotType());
                break;

            default:
                RootView.AddLogByKey("ScreencapCost", null, false, avgElapsed.ToString(),
                    ScreenshotType());
                break;
        }
    }

    public async Task RestartAdb()
    {
        var adbPath = Config.AdbDevice.AdbPath;

        if (string.IsNullOrEmpty(adbPath))
        {
            return;
        }

        ProcessStartInfo processStartInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            UseShellExecute = false,
        };

        Process process = new Process
        {
            StartInfo = processStartInfo,
        };

        process.Start();
        await process.StandardInput.WriteLineAsync($"{adbPath} kill-server");
        await process.StandardInput.WriteLineAsync($"{adbPath} start-server");
        await process.StandardInput.WriteLineAsync("exit");
        await process.WaitForExitAsync();
    }

    public async Task ReconnectByAdb()
    {
        var adbPath = Config.AdbDevice.AdbPath;
        var address = Config.AdbDevice.AdbSerial;

        if (string.IsNullOrEmpty(adbPath) || adbPath == "adb")
        {
            return;
        }

        ProcessStartInfo processStartInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            UseShellExecute = false,
        };

        var process = new Process
        {
            StartInfo = processStartInfo,
        };

        process.Start();
        await process.StandardInput.WriteLineAsync($"{adbPath} disconnect {address}");
        await process.StandardInput.WriteLineAsync("exit");
        await process.WaitForExitAsync();
    }

    public async Task HardRestartAdb()
    {
        var adbPath = Config.AdbDevice.AdbPath;
        if (string.IsNullOrEmpty(adbPath)) return;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            WindowsKillAdbProcesses(adbPath);
        }
        else
        {
            UnixKillAdbProcesses(adbPath);
        }
    }

    [SupportedOSPlatform("windows")]
    private void WindowsKillAdbProcesses(string adbPath)
    {
        const string WmiQueryString = "SELECT ProcessId, ExecutablePath, CommandLine FROM Win32_Process";
        using var searcher = new ManagementObjectSearcher(WmiQueryString);
        using var results = searcher.Get();

        var query = from p in Process.GetProcesses()
                    join mo in results.Cast<ManagementObject>()
                        on p.Id equals (int)(uint)mo["ProcessId"]
                    where ((string)mo["ExecutablePath"])?.Equals(adbPath, StringComparison.OrdinalIgnoreCase) == true
                    select p;

        KillProcesses(query);
    }

    private static void UnixKillAdbProcesses(string adbPath)
    {
        var processes = Process.GetProcessesByName("adb")
            .Where(p =>
            {
                try
                {
                    return GetUnixProcessPath(p.Id)?.Equals(adbPath, StringComparison.Ordinal) == true;
                }
                catch
                {
                    return false;
                }
            });

        KillProcesses(processes);
    }

    private static void KillProcesses(IEnumerable<Process> processes)
    {
        foreach (var process in processes)
        {
            try
            {
                process.Kill();
                process.WaitForExit();
            }
            catch
            {
                // 记录日志或忽略异常
            }
        }
    }

    private static string GetUnixProcessPath(int pid)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var exePath = $"/proc/{pid}/exe";
            return File.Exists(exePath) ? new FileInfo(exePath).LinkTarget : null;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var output = ExecuteShellCommand($"ps -p {pid} -o comm=");
            return string.IsNullOrWhiteSpace(output) ? null : output.Trim();
        }
        return null;
    }


    /// <summary>
    /// 跨平台终止指定进程
    /// </summary>
    /// <param name="processName">进程名称（不带后缀）</param>
    /// <param name="commandLineKeyword">命令行关键词（可选）</param>
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    public static void CloseProcessesByName(string processName, string? commandLineKeyword = null)
    {
        var processes = Process.GetProcesses()
            .Where(p => IsTargetProcess(p, processName, commandLineKeyword))
            .ToList();

        foreach (var process in processes)
        {
            SafeTerminateProcess(process);
        }
    }

    #region 跨平台核心逻辑

    private static bool IsTargetProcess(Process process, string processName, string? keyword)
    {
        try
        {
            // 验证进程名称
            if (!IsProcessNameMatch(process, processName))
                return false;

            // 验证命令行关键词
            return string.IsNullOrWhiteSpace(keyword) || GetCommandLine(process).Contains(keyword, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false; // 忽略无法访问的进程
        }
    }

    private static bool IsProcessNameMatch(Process process, string targetName)
    {
        var actualName = Path.GetFileNameWithoutExtension(process.ProcessName);
        return actualName.Equals(targetName, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region 命令行获取（平台相关）

    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static string GetCommandLine(Process process)
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? GetWindowsCommandLine(process) : GetUnixCommandLine(process.Id);
    }

    [SupportedOSPlatform("windows")]
    private static string GetWindowsCommandLine(Process process)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {process.Id}");
            return searcher.Get()
                    .Cast<ManagementObject>()
                    .FirstOrDefault()?["CommandLine"]?.ToString()
                ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static string GetUnixCommandLine(int pid)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            try
            {
                var cmdlinePath = $"/proc/{pid}/cmdline";
                return File.Exists(cmdlinePath) ? File.ReadAllText(cmdlinePath, Encoding.UTF8).Replace('\0', ' ') : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
        else // macOS
        {
            var output = ExecuteShellCommand($"ps -p {pid} -o command=");
            return output?.Trim() ?? string.Empty;
        }
    }

    #endregion

    #region 进程终止（带权限处理）

    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static void SafeTerminateProcess(Process process)
    {
        try
        {
            if (process.HasExited) return;

            if (NeedElevation(process))
            {
                ElevateKill(process.Id);
            }
            else
            {
                process.Kill();
                process.WaitForExit(5000);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Error] 终止进程失败: {process.ProcessName} ({process.Id}) - {ex.Message}");
        }
        finally
        {
            process.Dispose();
        }
    }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static bool NeedElevation(Process process)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;

        try
        {
            var uid = GetUnixUserId();
            var processUid = GetProcessUid(process.Id);
            return uid != processUid;
        }
        catch
        {
            return true; // 无法获取时默认需要提权
        }
    }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static void ElevateKill(int pid)
    {
        ExecuteShellCommand($"sudo kill -9 {pid}");
    }

    #endregion

    #region Unix辅助方法

    [DllImport("libc", EntryPoint = "getuid")]
    private static extern uint GetUid();

    private static uint GetUnixUserId() => GetUid();

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static uint GetProcessUid(int pid)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var statusPath = $"/proc/{pid}/status";
            var uidLine = File.ReadLines(statusPath)
                .FirstOrDefault(l => l.StartsWith("Uid:"));
            return uint.Parse(uidLine?.Split('\t')[1] ?? "0");
        }
        else // macOS
        {
            var output = ExecuteShellCommand($"ps -p {pid} -o uid=");
            return uint.TryParse(output?.Trim(), out var uid) ? uid : 0;
        }
    }

    private static string? ExecuteShellCommand(string command)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"{command}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false
            };

            using var process = Process.Start(psi);
            return process?.StandardOutput.ReadToEnd();
        }
        catch
        {
            return null;
        }
    }

    #endregion
    public async Task TestConnecting()
    {
        await GetTaskerAsync();
        var task = MaaTasker?.Controller?.LinkStart();
        task?.Wait();
        Instances.TaskQueueViewModel.SetConnected(task?.Status == MaaJobStatus.Succeeded);
    }

    public void Start(bool onlyStart = false, bool checkUpdate = false)
    {
        if (InitializeData())
        {
            var tasks = Instances.TaskQueueViewModel.TaskItemViewModels.ToList().FindAll(task => task.IsChecked);
            ConnectToMAA();
            StartTask(tasks, onlyStart, checkUpdate);
        }
    }

    public CancellationTokenSource? CancellationTokenSource { get; set; } = new();

    private DateTime? _startTime;

    public async Task StartTask(List<DragItemViewModel>? tasks, bool onlyStart = false, bool checkUpdate = false)
    {
        CancellationTokenSource = new CancellationTokenSource();

        _startTime = DateTime.Now;

        var token = CancellationTokenSource.Token;

        if (!onlyStart)
        {
            tasks ??= new List<DragItemViewModel>();
            var taskAndParams = tasks.Select(CreateNodeAndParam).ToList();
            InitializeConnectionTasksAsync(token);
            AddCoreTasksAsync(taskAndParams, token);
        }
        
        AddPostTasksAsync(onlyStart, checkUpdate, token);
        await TaskManager.RunTaskAsync(async () =>
        {
            var runSuccess = await ExecuteTasks(token);
            if (runSuccess)
            {
                Stop(true, onlyStart);
            }
        }, token, name: "启动任务");

    }

    async private Task<bool> ExecuteTasks(CancellationToken token)
    {
        while (TaskQueue.Count > 0 && !token.IsCancellationRequested)
        {
            var task = TaskQueue.Dequeue();
            if (!await task.Run(token))
            {
                return false;
            }
        }
        return !token.IsCancellationRequested;
    }

    public class NodeAndParam
    {
        public string? Name { get; set; }
        public string? Entry { get; set; }
        public int? Count { get; set; }

        public Dictionary<string, MaaNode>? Tasks
        {
            get;
            set;
        }
        public string? Param { get; set; }
    }


    private void UpdateTaskDictionary(ref Dictionary<string, MaaNode> taskModels,
        List<MaaInterface.MaaInterfaceSelectOption>? options)
    {
        Instance.NodeDictionary = Instance.NodeDictionary.MergeMaaNodes(taskModels);
        if (options == null) return;

        foreach (var selectOption in options)
        {
            if (Interface?.Option?.TryGetValue(selectOption.Name ?? string.Empty,
                    out var interfaceOption)
                == true
                && selectOption.Index is int index
                && interfaceOption.Cases is { } cases
                && cases[index]?.PipelineOverride != null)
            {
                var param = interfaceOption.Cases[selectOption.Index.Value].PipelineOverride;
                Instance.NodeDictionary = Instance.NodeDictionary.MergeMaaNodes(param);
                taskModels = taskModels.MergeMaaNodes(param);
            }
        }
    }

    private string SerializeTaskParams(Dictionary<string, MaaNode> taskModels)
    {
        var settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore
        };

        try
        {
            return JsonConvert.SerializeObject(taskModels, settings);
        }
        catch (Exception)
        {
            return "{}";
        }
    }

    private NodeAndParam CreateNodeAndParam(DragItemViewModel task)
    {
        var taskModels = task.InterfaceItem?.PipelineOverride ?? new Dictionary<string, MaaNode>();
        Console.WriteLine(string.Join(",", taskModels.Keys));
        UpdateTaskDictionary(ref taskModels, task.InterfaceItem?.Option);

        var taskParams = SerializeTaskParams(taskModels);
        var settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore
        };
        var json = JsonConvert.SerializeObject(Instance.BaseNodes, settings);

        var tasks = JsonConvert.DeserializeObject<Dictionary<string, MaaNode>>(json, settings);
        tasks = tasks.MergeMaaNodes(taskModels);
        return new NodeAndParam
        {
            Name = task.InterfaceItem?.Name,
            Entry = task.InterfaceItem?.Entry,
            Count = task.InterfaceItem?.Repeatable == true ? (task.InterfaceItem?.RepeatCount ?? 1) : 1,
            Tasks = tasks,
            Param = taskParams
        };
    }

    private void InitializeConnectionTasksAsync(CancellationToken token)
    {
        TaskQueue.Enqueue(CreateMFATask("启动脚本", async () =>
        {
            await TaskManager.RunTaskAsync(() => RunScript(), token);
        }));

        TaskQueue.Enqueue(CreateMFATask("连接设备", async () =>
        {
            await HandleDeviceConnectionAsync(token);
        }));

        TaskQueue.Enqueue(CreateMFATask("性能基准", async () =>
        {
            await MeasureScreencapPerformanceAsync(token);
        }));
    }

    public async Task MeasureScreencapPerformanceAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await MeasureExecutionTimeAsync(async () => await TaskManager.RunTaskAsync(() => MaaTasker?.Controller.Screencap().Wait(), token));
    }

    async private Task HandleDeviceConnectionAsync(CancellationToken token)
    {
        var controllerType = Instances.TaskQueueViewModel.CurrentController;
        var isAdb = controllerType == MaaControllerTypes.Adb;

        RootView.AddLogByKey("ConnectingTo", null, true, isAdb ? "Emulator" : "Window");
        if (Instances.TaskQueueViewModel.CurrentDevice == null)
            Instances.TaskQueueViewModel.TryReadAdbDeviceFromConfig(false, true);
        var connected = await TryConnectAsync(token);

        if (!connected && isAdb)
        {
            connected = await HandleAdbConnectionAsync(token);
        }

        if (!connected)
        {
            HandleConnectionFailureAsync(isAdb, token);
            throw new Exception("Connection failed after all retries");
        }

        Instances.TaskQueueViewModel.SetConnected(true);
    }

    async private Task<bool> HandleAdbConnectionAsync(CancellationToken token)
    {
        bool connected = false;
        var retrySteps = new List<Func<CancellationToken, Task<bool>>>
        {
            async t => await RetryConnectionAsync(t, StartSoftware, "TryToStartEmulator", Instances.ConnectSettingsUserControlModel.RetryOnDisconnected, () => Instances.TaskQueueViewModel.TryReadAdbDeviceFromConfig(false, true)),
            async t => await RetryConnectionAsync(t, ReconnectByAdb, "TryToReconnectByAdb"),
            async t => await RetryConnectionAsync(t, RestartAdb, "RestartAdb", Instances.ConnectSettingsUserControlModel.AllowAdbRestart),
            async t => await RetryConnectionAsync(t, HardRestartAdb, "HardRestartAdb", Instances.ConnectSettingsUserControlModel.AllowAdbHardRestart)
        };

        foreach (var step in retrySteps)
        {
            if (token.IsCancellationRequested) break;
            connected = await step(token);
            if (connected) break;
        }

        return connected;
    }

    async private Task<bool> RetryConnectionAsync(CancellationToken token, Func<Task> action, string logKey, bool enable = true, Action? other = null)
    {
        if (!enable) return false;
        token.ThrowIfCancellationRequested();
        RootView.AddLog("ConnectFailed".ToLocalization() + "\n" + logKey.ToLocalization());
        await action();
        if (token.IsCancellationRequested)
        {
            Stop();
            return false;
        }
        other?.Invoke();
        return await TryConnectAsync(token);
    }

    async private Task<bool> TryConnectAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var instance = await GetTaskerAsync(token);
        return instance is { Initialized: true };
    }

    private void HandleConnectionFailureAsync(bool isAdb, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        LoggerHelper.Warning("ConnectFailed".ToLocalization());
        RootView.AddLogByKey("ConnectFailed");
        Instances.TaskQueueViewModel.SetConnected(false);
        ToastHelper.Warn("Warning_CannotConnect".ToLocalizationFormatted(true, isAdb ? "Emulator" : "Window"));
        Stop();
    }

    private void AddCoreTasksAsync(List<NodeAndParam> taskAndParams, CancellationToken token)
    {
        foreach (var task in taskAndParams)
        {
            TaskQueue.Enqueue(CreateMaaFWTask(task.Name,
                async () =>
                {
                    token.ThrowIfCancellationRequested();
                    if (task.Tasks != null)
                        NodeDictionary = task.Tasks;
                    await TryRunTasksAsync(MaaTasker, task.Entry, task.Param, token);
                }, task.Count ?? 1
            ));
        }
    }

    async private Task TryRunTasksAsync(MaaTasker? maa, string? task, string? param, CancellationToken token)
    {
        if (maa == null || task == null) return;

        var job = maa.AppendTask(task, param ?? "{}");
        await TaskManager.RunTaskAsync(() => job.Wait().ThrowIfNot(MaaJobStatus.Succeeded), token, catchException: true, shouldLog: false);
    }

    public void RunScript(string str = "Prescript")
    {
        bool enable = str switch
        {
            "Prescript" => !string.IsNullOrWhiteSpace(ConfigurationManager.Current.GetValue(ConfigurationKeys.Prescript, string.Empty)),
            "Post-script" => !string.IsNullOrWhiteSpace(ConfigurationManager.Current.GetValue(ConfigurationKeys.Postscript, string.Empty)),
            _ => false,
        };
        if (!enable)
        {
            return;
        }

        Func<bool> func = str switch
        {
            "Prescript" => () => ExecuteScript(ConfigurationManager.Current.GetValue(ConfigurationKeys.Prescript, string.Empty)),
            "Post-script" => () => ExecuteScript(ConfigurationManager.Current.GetValue(ConfigurationKeys.Postscript, string.Empty)),
            _ => () => false,
        };

        if (!func())
        {
            LoggerHelper.Error($"Failed to execute the {str}.");
        }
    }

    private static bool ExecuteScript(string scriptPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(scriptPath))
            {
                return false;
            }

            string fileName;
            string arguments;

            if (scriptPath.StartsWith('\"'))
            {
                var parts = scriptPath.Split("\"", 3);
                fileName = parts[1];
                arguments = parts.Length > 2 ? parts[2] : string.Empty;
            }
            else
            {
                fileName = scriptPath;
                arguments = string.Empty;
            }

            bool createNoWindow = arguments.Contains("-noWindow");
            bool minimized = arguments.Contains("-minimized");

            if (createNoWindow)
            {
                arguments = arguments.Replace("-noWindow", string.Empty).Trim();
            }

            if (minimized)
            {
                arguments = arguments.Replace("-minimized", string.Empty).Trim();
            }

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    WindowStyle = minimized ? ProcessWindowStyle.Minimized : ProcessWindowStyle.Normal,
                    CreateNoWindow = createNoWindow,
                    UseShellExecute = !createNoWindow,
                },
            };
            process.Start();
            process.WaitForExit();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
    private void AddPostTasksAsync(bool onlyStart, bool checkUpdate, CancellationToken token)
    {
        if (!onlyStart)
        {
            TaskQueue.Enqueue(CreateMFATask("结束脚本", async () =>
            {
                await TaskManager.RunTaskAsync(() => RunScript("Post-script"), token);
            }));
        }
        if (checkUpdate)
        {
            TaskQueue.Enqueue(CreateMFATask("检查更新", async () =>
            {
                VersionChecker.Check();
            }));
        }
    }
    private MFATask CreateMaaFWTask(string? name, Func<Task> action, int count = 1)
    {
        return new MFATask
        {
            Name = name,
            Count = count,
            Type = MFATask.MFATaskType.MAAFW,
            Action = action
        };
    }

    private MFATask CreateMFATask(string? name, Func<Task> action)
    {
        return new MFATask
        {
            Name = name,
            Type = MFATask.MFATaskType.MFA,
            Action = action
        };
    }

    #endregion

    #region 停止任务

    public void Stop(bool finished = false, bool onlyStart = false)
    {
        try
        {
            if (!ShouldProcessStop(finished))
            {
                ToastHelper.Warn("NoTaskToStop".ToLocalization());

                TaskQueue.Clear();
                return;
            }

            CancelOperations();

            TaskQueue.Clear();

            Instances.RootViewModel.IsRunning = false;

            ExecuteStopCore(finished, () =>
            {
                var stopResult = AbortCurrentTasker();
                HandleStopResult(finished, stopResult, onlyStart);
            });

        }
        catch (Exception ex)
        {
            HandleStopException(ex);
        }
    }

    private void CancelOperations()
    {
        if (!_agentStarted)
        {
            _agentProcess?.Kill();
            _agentProcess?.Dispose();
            _agentProcess = null;
        }
        _emulatorCancellationTokenSource?.SafeCancel();
        CancellationTokenSource.SafeCancel();
    }

    private bool ShouldProcessStop(bool finished)
    {
        return CancellationTokenSource?.IsCancellationRequested == false
            || finished;
    }

    private void ExecuteStopCore(bool finished, Action stopAction)
    {
        TaskManager.RunTaskAsync(() =>
        {
            if (!finished)
                RootView.AddLogByKey("Stopping");

            stopAction.Invoke();

            Instances.RootViewModel.Idle = true;
        }, null, "停止任务");
    }

    private bool AbortCurrentTasker()
    {
        return MaaTasker == null || MaaTasker.Abort().Wait() == MaaJobStatus.Succeeded;
    }

    private void HandleStopResult(bool finished, bool success, bool onlyStart)
    {
        if (success)
        {
            DisplayTaskCompletionMessage(finished, onlyStart);
        }
        else
        {
            ToastHelper.Error("StoppingFailed".ToLocalization());
        }
    }

    private void DisplayTaskCompletionMessage(bool finished, bool onlyStart = false)
    {
        if (!finished)
        {
            ToastHelper.Info("TaskStopped".ToLocalization());
            RootView.AddLogByKey("TaskAbandoned");
        }
        else
        {
            if (!onlyStart)
                ToastNotification.Show("TaskCompleted".ToLocalization());
            if (_startTime != null)
            {
                var elapsedTime = DateTime.Now - (DateTime)_startTime;
                RootView.AddLogByKey("TaskAllCompletedWithTime", null, true, ((int)elapsedTime.TotalHours).ToString(),
                    ((int)elapsedTime.TotalMinutes % 60).ToString(), ((int)elapsedTime.TotalSeconds % 60).ToString());
            }
            else
            {
                RootView.AddLogByKey("TaskAllCompleted");
            }
            if (!onlyStart)
            {
                ExternalNotificationHelper.ExternalNotificationAsync();
                HandleAfterTaskOperation();
            }
        }

        _startTime = null;
    }

    public void HandleAfterTaskOperation()
    {
        var afterTask = ConfigurationManager.Current.GetValue(ConfigurationKeys.AfterTask, "None");
        switch (afterTask)
        {
            case "CloseMFA":
                Instances.ShutdownApplication();
                break;
            case "CloseEmulator":
                CloseSoftware();
                break;
            case "CloseEmulatorAndMFA":
                CloseSoftwareAndMFA();
                break;
            case "ShutDown":
                Instances.ShutdownApplication();
                break;
            case "CloseEmulatorAndRestartMFA":
                CloseSoftwareAndRestartMFA();
                break;
            case "RestartPC":
                Instances.RestartSystem();
                break;
        }
    }

    public static void CloseSoftwareAndRestartMFA()
    {
        CloseSoftware();
        Instances.RestartApplication();
    }

    public static void CloseSoftware(Action? action = null)
    {
        if (Instances.TaskQueueViewModel.CurrentController == MaaControllerTypes.Adb)
        {
            EmulatorHelper.KillEmulatorAsync();
        }
        else
        {
            if (_softwareProcess != null && !_softwareProcess.HasExited)
            {
                _softwareProcess.Kill();
            }
            else
            {
                CloseProcessesByName(Config.DesktopWindow.Name, ConfigurationManager.Current.GetValue(ConfigurationKeys.EmulatorConfig, string.Empty));
                _softwareProcess = null;
            }

        }
        action?.Invoke();
    }
    public static void CloseSoftwareAndMFA()
    {
        CloseSoftware(Instances.ShutdownApplication);
    }

    private void HandleStopException(Exception ex)
    {
        LoggerHelper.Error($"Stop operation failed: {ex.Message}");
        ToastHelper.Error("StopOperationFailed".ToLocalization());
    }

    #endregion

    #region 启动软件

    public async Task WaitSoftware()
    {
        if (ConfigurationManager.Current.GetValue(ConfigurationKeys.BeforeTask, "None").Contains("Startup", StringComparison.OrdinalIgnoreCase))
        {
            await StartSoftware();
        }

        Instances.TaskQueueViewModel.TryReadAdbDeviceFromConfig(false);
    }

    private CancellationTokenSource? _emulatorCancellationTokenSource;

    private static Process? _softwareProcess;

    public async Task StartSoftware()
    {
        _emulatorCancellationTokenSource = new CancellationTokenSource();
        await StartRunnableFile(ConfigurationManager.Current.GetValue(ConfigurationKeys.SoftwarePath, string.Empty),
            ConfigurationManager.Current.GetValue(ConfigurationKeys.WaitSoftwareTime, 60.0), _emulatorCancellationTokenSource.Token);
    }

    async private Task StartRunnableFile(string exePath, double waitTimeInSeconds, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            return;
        var processName = Path.GetFileNameWithoutExtension(exePath);
        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = true,
            CreateNoWindow = false
        };
        if (Process.GetProcessesByName(processName).Length == 0)
        {
            if (!string.IsNullOrWhiteSpace(ConfigurationManager.Current.GetValue(ConfigurationKeys.EmulatorConfig, string.Empty)))
            {
                startInfo.Arguments = ConfigurationManager.Current.GetValue(ConfigurationKeys.EmulatorConfig, string.Empty);
                _softwareProcess =
                    Process.Start(startInfo);
            }
            else
                _softwareProcess = Process.Start(startInfo);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(ConfigurationManager.Current.GetValue(ConfigurationKeys.EmulatorConfig, string.Empty)))
            {
                startInfo.Arguments = ConfigurationManager.Current.GetValue(ConfigurationKeys.EmulatorConfig, string.Empty);
                _softwareProcess = Process.Start(startInfo);
            }
            else
                _softwareProcess = Process.Start(startInfo);
        }

        for (double remainingTime = waitTimeInSeconds + 1; remainingTime > 0; remainingTime -= 1)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            if (remainingTime % 10 == 0)
            {
                RootView.AddLogByKey("WaitSoftwareTime", null, true,
                    Instances.TaskQueueViewModel.CurrentController == MaaControllerTypes.Adb
                        ? "Emulator"
                        : "Window",
                    remainingTime.ToString()
                );
            }
            else if (remainingTime == waitTimeInSeconds)
            {
                RootView.AddLogByKey("WaitSoftwareTime", null, true,
                    Instances.TaskQueueViewModel.CurrentController == MaaControllerTypes.Adb
                        ? "Emulator"
                        : "Window",
                    remainingTime.ToString()
                );
            }


            await Task.Delay(1000, token);
        }

    }

    #endregion
}
