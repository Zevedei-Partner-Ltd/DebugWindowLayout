using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;

namespace DebugWindowLayout
{
    [DataContract]
    public sealed class LayoutConfig
    {
        [DataMember(Name = "enabled")]
        public bool Enabled { get; set; } = false;

        [DataMember(Name = "autoArrangeOnDebug")]
        public bool AutoArrangeOnDebug { get; set; } = true;

        // Uses Windows display numbers (\\.\DISPLAY1, \\.\DISPLAY2, ...), not zero-based indexes.
        [DataMember(Name = "targetMonitor")]
        public int TargetMonitor { get; set; } = 1;

        [DataMember(Name = "margin")]
        public int Margin { get; set; } = 8;

        [DataMember(Name = "retryMilliseconds")]
        public int RetryMilliseconds { get; set; } = 300;

        [DataMember(Name = "retryCount")]
        public int RetryCount { get; set; } = 20;

        [DataMember(Name = "useWorkingArea")]
        public bool UseWorkingArea { get; set; } = true;

        [DataMember(Name = "restoreBeforeMove")]
        public bool RestoreBeforeMove { get; set; } = true;

        [DataMember(Name = "rules")]
        public List<LayoutRule> Rules { get; set; } = new List<LayoutRule>();


        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            Enabled = true;
            AutoArrangeOnDebug = true;
            TargetMonitor = 1;
            Margin = 8;
            RetryMilliseconds = 300;
            RetryCount = 20;
            UseWorkingArea = true;
            RestoreBeforeMove = true;
            Rules = new List<LayoutRule>();
        }

        public static LayoutConfig LoadOrDefault(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return new LayoutConfig();

            using (var stream = File.OpenRead(path))
            {
                var serializer = new DataContractJsonSerializer(typeof(LayoutConfig));
                var config = serializer.ReadObject(stream) as LayoutConfig ?? new LayoutConfig();
                config.Normalize();
                return config;
            }
        }

        public void Save(string path)
        {
            Normalize();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var stream = File.Create(path))
            {
                var serializer = new DataContractJsonSerializer(
                    typeof(LayoutConfig),
                    new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, ownsStream: false, indent: true))
                {
                    serializer.WriteObject(writer, this);
                    writer.Flush();
                }
            }
        }

        private void Normalize()
        {
            if (TargetMonitor <= 0) TargetMonitor = 1;
            if (Margin < 0) Margin = 0;
            if (RetryMilliseconds < 50) RetryMilliseconds = 50;
            if (RetryCount < 1) RetryCount = 1;
            if (RetryCount > 100) RetryCount = 100;
            if (Rules == null) Rules = new List<LayoutRule>();
        }
    }

    public enum LayoutZone
    {
        Full,
        Left,
        Right,
        Top,
        Bottom,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    [DataContract]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public sealed class LayoutRule
    {
        [DataMember(Name = "process", EmitDefaultValue = false)]
        [DisplayName("Process")]
        [Description("Name of the debug process, for example Gateway or Worker.")]
        public string Process { get; set; }

        // Useful for console windows, because the visible HWND may belong to conhost/OpenConsole.
        [DataMember(Name = "titleContains", EmitDefaultValue = false)]
        [DisplayName("Window title contains")]
        [Description("Optional text that must be part of the window title. Especially useful for console windows.")]
        public string TitleContains { get; set; }

        [DataMember(Name = "monitor", EmitDefaultValue = false)]
        [DisplayName("Monitor")]
        [Description("Optional target monitor for this rule only. Empty = global target monitor.")]
        public int? Monitor { get; set; }

        [DataMember(Name = "zone", EmitDefaultValue = false)]
        private string ZoneValue
        {
            get => Zone == LayoutZone.Full ? null : Zone.ToString();
            set => Zone = ParseZone(value);
        }

        [IgnoreDataMember]
        [DisplayName("Zone")]
        [Description("Predefined zone: Full, Left, Right, Top, Bottom, TopLeft, TopRight, BottomLeft, BottomRight.")]
        public LayoutZone Zone { get; set; } = LayoutZone.Full;

        [DataMember(Name = "bounds", EmitDefaultValue = false)]
        [DisplayName("Relative Bounds")]
        [Description("Free relative position and size (x/y/width/height). Overrides the zone.")]
        public NormalizedBounds Bounds { get; set; }

        public override string ToString()
        {
            if (!string.IsNullOrWhiteSpace(Process))
                return Process + " → " + Zone;
            if (!string.IsNullOrWhiteSpace(TitleContains))
                return "Title: " + TitleContains;
            return "New Rule";
        }

        private static LayoutZone ParseZone(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return LayoutZone.Full;

            LayoutZone zone;
            return Enum.TryParse(value.Trim(), true, out zone)
                ? zone
                : LayoutZone.Full;
        }
    }

    [DataContract]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public sealed class NormalizedBounds
    {
        [DataMember(Name = "x")]
        [DisplayName("X")]
        [Description("Left relative position from 0 to 1.")]
        public double X { get; set; }

        [DataMember(Name = "y")]
        [DisplayName("Y")]
        [Description("Top relative position from 0 to 1.")]
        public double Y { get; set; }

        [DataMember(Name = "width")]
        [DisplayName("Width")]
        [Description("Relative width from 0 to 1.")]
        public double Width { get; set; }

        [DataMember(Name = "height")]
        [DisplayName("Height")]
        [Description("Relative height from 0 to 1.")]
        public double Height { get; set; }

        public override string ToString()
        {
            return string.Format("X={0:0.##}, Y={1:0.##}, W={2:0.##}, H={3:0.##}", X, Y, Width, Height);
        }

        public NormalizedBounds Clamp()
        {
            var x = Math.Max(0, Math.Min(1, X));
            var y = Math.Max(0, Math.Min(1, Y));
            var width = Math.Max(0.05, Math.Min(1 - x, Width));
            var height = Math.Max(0.05, Math.Min(1 - y, Height));
            return new NormalizedBounds { X = x, Y = y, Width = width, Height = height };
        }
    }
}
