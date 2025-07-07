using Avalonia.Collections;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using System;
using System.Linq;

namespace MFAAvalonia.ViewModels.UsersControls.Settings;

public partial class TimerSettingsUserControlModel : ViewModelBase
{
    [ObservableProperty] private bool _customConfig = Convert.ToBoolean(GlobalConfiguration.GetValue(ConfigurationKeys.CustomConfig, bool.FalseString));
    [ObservableProperty] private bool _forceScheduledStart = Convert.ToBoolean(GlobalConfiguration.GetValue(ConfigurationKeys.ForceScheduledStart, bool.FalseString));

    public TimerModel TimerModels { get; set; } = new();

    partial void OnCustomConfigChanged(bool value)
    {
        GlobalConfiguration.SetValue(ConfigurationKeys.CustomConfig, value.ToString());
    }

    partial void OnForceScheduledStartChanged(bool value)
    {
        GlobalConfiguration.SetValue(ConfigurationKeys.ForceScheduledStart, value.ToString());
    }

    public IAvaloniaReadOnlyList<MFAConfiguration> ConfigurationList { get; set; } = ConfigurationManager.Configs;

    public partial class TimerModel
    {
        public partial class TimerProperties : ObservableObject
        {
            public TimerProperties(int timeId, bool isOn, string time, string? timerConfig)
            {
                TimerId = timeId;
                _isOn = isOn;
                _time = TimeSpan.Parse(time);
                TimerName = $"{"Timer".ToLocalization()} {TimerId + 1}";
                if (timerConfig == null || !ConfigurationManager.Configs.Any(c => c.Name.Equals(timerConfig)))
                {
                    _timerConfig = ConfigurationManager.GetCurrentConfiguration();
                }
                else
                {
                    _timerConfig = timerConfig;
                }
                LanguageHelper.LanguageChanged += OnLanguageChanged;
            }

            public int TimerId { get; set; }

            [ObservableProperty] private string _timerName;

            private void OnLanguageChanged(object sender, EventArgs e)
            {
                TimerName = $"{"Timer".ToLocalization()} {TimerId + 1}";
            }

            private bool _isOn;

            /// <summary>
            /// Gets or sets a value indicating whether the timer is set.
            /// </summary>
            public bool IsOn
            {
                get => _isOn;
                set
                {
                    SetProperty(ref _isOn, value);
                    GlobalConfiguration.SetTimer(TimerId, value.ToString());
                }
            }

            private TimeSpan _time;

            /// <summary>
            /// Gets or sets the timer.
            /// </summary>
            public TimeSpan Time
            {
                get => _time;
                set
                {
                    SetProperty(ref _time, value);
                    GlobalConfiguration.SetTimerTime(TimerId, value.ToString(@"h\:mm"));
                }
            }

            private string? _timerConfig;

            /// <summary>
            /// Gets or sets the config of the timer.
            /// </summary>
            public string? TimerConfig
            {
                get => _timerConfig;
                set
                {
                    SetProperty(ref _timerConfig, value ?? ConfigurationManager.GetCurrentConfiguration());
                    GlobalConfiguration.SetTimerConfig(TimerId, _timerConfig);
                }
            }
        }

        public TimerProperties[] Timers { get; set; } = new TimerProperties[8];
        private readonly DispatcherTimer _dispatcherTimer;
        public TimerModel()
        {
            for (var i = 0; i < 8; i++)
            {
                Timers[i] = new TimerProperties(
                    i,
                    GlobalConfiguration.GetTimer(i, bool.FalseString) == bool.TrueString, GlobalConfiguration.GetTimerTime(i, $"{i * 3}:0"), GlobalConfiguration.GetTimerConfig(i, ConfigurationManager.GetCurrentConfiguration()));
            }
            _dispatcherTimer = new();
            _dispatcherTimer.Interval = TimeSpan.FromMinutes(1);
            _dispatcherTimer.Tick += CheckTimerElapsed;
            _dispatcherTimer.Start();
        }

        private void CheckTimerElapsed(object sender, EventArgs e)
        {
            var currentTime = DateTime.Now;
            foreach (var timer in Timers)
            {
                if (timer.IsOn && timer.Time.Hours == currentTime.Hour && timer.Time.Minutes == currentTime.Minute)
                {
                    ExecuteTimerTask(timer.TimerId);
                }
                if (timer.IsOn && timer.Time.Hours == currentTime.Hour && timer.Time.Minutes == currentTime.Minute + 2)
                {
                    SwitchConfiguration(timer.TimerId);
                }
            }
        }

        private void SwitchConfiguration(int timerId)
        {
            var timer = Timers.FirstOrDefault(t => t.TimerId == timerId, null);
            if (timer != null)
            {
                var config = timer.TimerConfig ?? ConfigurationManager.GetCurrentConfiguration();
                if (config != ConfigurationManager.GetCurrentConfiguration())
                {
                    ConfigurationManager.SetDefaultConfig(config);
                    if (Instances.RootViewModel.IsRunning)
                        Instances.TaskQueueViewModel.StopTask(() => Instances.RestartApplication(true, true));
                    else
                        Instances.RestartApplication(true, true);
                }
            }
        }

        private void ExecuteTimerTask(int timerId)
        {
            var timer = Timers.FirstOrDefault(t => t.TimerId == timerId, null);
            if (timer != null)
            {
                if (Instances.TimerSettingsUserControlModel.ForceScheduledStart && Instances.RootViewModel.IsRunning)
                    Instances.TaskQueueViewModel.StopTask(Instances.TaskQueueViewModel.StartTask);
                else
                    Instances.TaskQueueViewModel.StartTask();
            }
        }
    }
}
