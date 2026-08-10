using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace DebugWindowLayout
{
    public sealed class DebugWindowLayoutOptionsPage : DialogPage
    {
        [Category("General")]
        [DisplayName("Enabled")]
        [Description("Enables or disables automatic window arrangement.")]
        [DefaultValue(true)]
        public bool Enabled { get; set; } = true;

        [Category("General")]
        [DisplayName("Auto arrange on debug")]
        [Description("Automatically arranges debug windows when a debug session starts.")]
        [DefaultValue(true)]
        public bool AutoArrangeOnDebug { get; set; } = true;

        [Category("Layout")]
        [DisplayName("Target monitor")]
        [Description("Windows monitor number (DISPLAY1, DISPLAY2, ...) where windows are arranged by default.")]
        [DefaultValue(2)]
        public int TargetMonitor { get; set; } = 2;

        [Category("Layout")]
        [DisplayName("Margin")]
        [Description("Inner spacing in pixels between windows and monitor edges.")]
        [DefaultValue(8)]
        public int Margin { get; set; } = 8;

        [Category("Layout")]
        [DisplayName("Use working area")]
        [Description("Uses the usable monitor area without taskbar instead of full monitor bounds.")]
        [DefaultValue(true)]
        public bool UseWorkingArea { get; set; } = true;

        [Category("Layout")]
        [DisplayName("Restore window before move")]
        [Description("Restores minimized/maximized windows to normal size before moving.")]
        [DefaultValue(true)]
        public bool RestoreBeforeMove { get; set; } = true;

        [Category("Retry")]
        [DisplayName("Retry interval (ms)")]
        [Description("Wait time between two search passes for late-starting processes.")]
        [DefaultValue(300)]
        public int RetryMilliseconds { get; set; } = 300;

        [Category("Retry")]
        [DisplayName("Retry count")]
        [Description("Maximum number of retries until all debug windows are found.")]
        [DefaultValue(20)]
        public int RetryCount { get; set; } = 20;

        [Category("Rules")]
        [DisplayName("Rules")]
        [Description("Optional process-specific layout rules. Leave empty for automatic grid.")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
        public List<LayoutRule> Rules { get; set; } = new List<LayoutRule>();

        [Category("Information")]
        [DisplayName("Configuration file")]
        [Description("Path to the JSON configuration stored per solution.")]
        [ReadOnly(true)]
        public string ConfigFilePath { get; private set; }

        public override void LoadSettingsFromStorage()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            base.LoadSettingsFromStorage();

            var dte = GetDte();
            ConfigFilePath = LayoutConfigStorage.GetConfigPath(dte);
            Apply(LayoutConfigStorage.Load(dte));
        }

        public override void SaveSettingsToStorage()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var dte = GetDte();
            ConfigFilePath = LayoutConfigStorage.GetConfigPath(dte);

            if (!LayoutConfigStorage.TrySave(dte, ToConfig()))
            {
                VsShellUtilities.ShowMessageBox(
                    this.Site,
                    "Open a solution first so the settings can be saved to .vsdebuglayout.json.",
                    "Debug Window Layout",
                    Microsoft.VisualStudio.Shell.Interop.OLEMSGICON.OLEMSGICON_INFO,
                    Microsoft.VisualStudio.Shell.Interop.OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    Microsoft.VisualStudio.Shell.Interop.OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                return;
            }

            base.SaveSettingsToStorage();
        }

        private static DTE2 GetDte()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return Package.GetGlobalService(typeof(EnvDTE.DTE)) as DTE2;
        }

        private void Apply(LayoutConfig config)
        {
            config = config ?? new LayoutConfig();
            Enabled = config.Enabled;
            AutoArrangeOnDebug = config.AutoArrangeOnDebug;
            TargetMonitor = config.TargetMonitor;
            Margin = config.Margin;
            RetryMilliseconds = config.RetryMilliseconds;
            RetryCount = config.RetryCount;
            UseWorkingArea = config.UseWorkingArea;
            RestoreBeforeMove = config.RestoreBeforeMove;
            Rules = CloneRules(config.Rules);
        }

        private LayoutConfig ToConfig()
        {
            return new LayoutConfig
            {
                Enabled = Enabled,
                AutoArrangeOnDebug = AutoArrangeOnDebug,
                TargetMonitor = TargetMonitor,
                Margin = Margin,
                RetryMilliseconds = RetryMilliseconds,
                RetryCount = RetryCount,
                UseWorkingArea = UseWorkingArea,
                RestoreBeforeMove = RestoreBeforeMove,
                Rules = CloneRules(Rules)
            };
        }

        private static List<LayoutRule> CloneRules(List<LayoutRule> rules)
        {
            var result = new List<LayoutRule>();
            if (rules == null)
                return result;

            foreach (var rule in rules)
            {
                if (rule == null)
                    continue;

                result.Add(new LayoutRule
                {
                    Process = rule.Process,
                    TitleContains = rule.TitleContains,
                    Monitor = rule.Monitor,
                    Zone = rule.Zone,
                    Bounds = rule.Bounds == null
                        ? null
                        : new NormalizedBounds
                        {
                            X = rule.Bounds.X,
                            Y = rule.Bounds.Y,
                            Width = rule.Bounds.Width,
                            Height = rule.Bounds.Height
                        }
                });
            }

            return result;
        }
    }
}
