using System;
using System.Collections.Generic;

namespace PreLuna.HeadCamera.Control
{
    public enum HeadCameraControlStatus
    {
        Available,
        Uninitialized,
        Missing,
        Incompatible
    }

    [Flags]
    public enum HeadCameraControlCapabilities
    {
        None = 0,
        Validate = 1,
        SavePersistent = 2
    }

    public enum HeadCameraControlOutcome
    {
        Success,
        NoChanges,
        ValidationRejected,
        StateConflict,
        Unavailable,
        SaveFailed
    }

    public enum HeadCameraControlKind
    {
        Boolean,
        Number,
        Integer
    }

    public sealed class HeadCameraControlValue
    {
        HeadCameraControlValue(HeadCameraControlKind kind, object raw)
        {
            Kind = kind;
            Raw = raw;
        }

        public HeadCameraControlKind Kind { get; }
        public object Raw { get; }

        public static HeadCameraControlValue Boolean(bool value) => new HeadCameraControlValue(HeadCameraControlKind.Boolean, value);
        public static HeadCameraControlValue Number(double value) => new HeadCameraControlValue(HeadCameraControlKind.Number, value);
        public static HeadCameraControlValue Integer(int value) => new HeadCameraControlValue(HeadCameraControlKind.Integer, value);

        public bool TryGetBoolean(out bool value)
        {
            if (Kind == HeadCameraControlKind.Boolean && Raw is bool typed)
            {
                value = typed;
                return true;
            }

            value = false;
            return false;
        }

        public bool TryGetNumber(out double value)
        {
            if (Kind == HeadCameraControlKind.Number && Raw is double typed)
            {
                value = typed;
                return true;
            }

            value = 0;
            return false;
        }

        public bool TryGetInteger(out int value)
        {
            if (Kind == HeadCameraControlKind.Integer && Raw is int typed)
            {
                value = typed;
                return true;
            }

            value = 0;
            return false;
        }

        public bool ValueEquals(HeadCameraControlValue other)
        {
            if (other == null || Kind != other.Kind || Raw == null || other.Raw == null) return false;
            if (Kind == HeadCameraControlKind.Number)
                return Raw is double left && other.Raw is double right && Math.Abs(left - right) < 1e-9;
            return Raw.Equals(other.Raw);
        }

        public override string ToString() => Raw?.ToString() ?? string.Empty;
    }

    public sealed class HeadCameraControlSetting
    {
        public HeadCameraControlSetting(
            string key,
            string displayName,
            HeadCameraControlKind kind,
            HeadCameraControlValue defaultValue,
            string section,
            string configKey,
            double? minimum = null,
            double? maximum = null)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            DisplayName = displayName ?? key;
            Kind = kind;
            DefaultValue = defaultValue;
            Section = section ?? string.Empty;
            ConfigKey = configKey ?? string.Empty;
            Minimum = minimum;
            Maximum = maximum;
        }

        public string Key { get; }
        public string DisplayName { get; }
        public HeadCameraControlKind Kind { get; }
        public HeadCameraControlValue DefaultValue { get; }
        public string Section { get; }
        public string ConfigKey { get; }
        public double? Minimum { get; }
        public double? Maximum { get; }

        public HeadCameraControlIssue Validate(HeadCameraControlValue value)
        {
            if (value == null) return new HeadCameraControlIssue(Key, "缺少值");
            if (value.Kind != Kind) return new HeadCameraControlIssue(Key, "值类型不匹配");

            if (Kind == HeadCameraControlKind.Boolean)
                return value.TryGetBoolean(out _) ? null : new HeadCameraControlIssue(Key, "不是合法布尔值");

            if (Kind == HeadCameraControlKind.Integer)
            {
                if (!value.TryGetInteger(out var integer)) return new HeadCameraControlIssue(Key, "不是合法整数");
                return CheckRange(integer);
            }

            if (!value.TryGetNumber(out var number) || double.IsNaN(number) || double.IsInfinity(number))
                return new HeadCameraControlIssue(Key, "不是合法数值");
            return CheckRange(number);
        }

        HeadCameraControlIssue CheckRange(double value)
        {
            if (Minimum.HasValue && value < Minimum.Value) return new HeadCameraControlIssue(Key, "小于最小值 " + Minimum.Value);
            if (Maximum.HasValue && value > Maximum.Value) return new HeadCameraControlIssue(Key, "大于最大值 " + Maximum.Value);
            return null;
        }
    }

    public sealed class HeadCameraControlIssue
    {
        public HeadCameraControlIssue(string key, string message)
        {
            Key = key ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public string Key { get; }
        public string Message { get; }
    }

    public sealed class HeadCameraControlSnapshot
    {
        public HeadCameraControlSnapshot(
            string targetId,
            string displayName,
            string version,
            HeadCameraControlStatus status,
            string revision,
            HeadCameraControlCapabilities capabilities,
            IReadOnlyList<HeadCameraControlSetting> settings,
            IDictionary<string, HeadCameraControlValue> saved,
            IDictionary<string, HeadCameraControlValue> effective)
        {
            TargetId = targetId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Version = version ?? string.Empty;
            Status = status;
            Revision = revision ?? string.Empty;
            Capabilities = capabilities;
            Settings = settings ?? Array.Empty<HeadCameraControlSetting>();
            Saved = Copy(saved);
            Effective = Copy(effective);
        }

        public string TargetId { get; }
        public string DisplayName { get; }
        public string Version { get; }
        public HeadCameraControlStatus Status { get; }
        public string Revision { get; }
        public HeadCameraControlCapabilities Capabilities { get; }
        public IReadOnlyList<HeadCameraControlSetting> Settings { get; }
        public IReadOnlyDictionary<string, HeadCameraControlValue> Saved { get; }
        public IReadOnlyDictionary<string, HeadCameraControlValue> Effective { get; }

        static IReadOnlyDictionary<string, HeadCameraControlValue> Copy(IDictionary<string, HeadCameraControlValue> source)
        {
            var copy = new Dictionary<string, HeadCameraControlValue>(StringComparer.Ordinal);
            if (source != null)
                foreach (var pair in source) copy[pair.Key] = pair.Value;
            return copy;
        }
    }

    public sealed class HeadCameraControlChange
    {
        public HeadCameraControlChange(string expectedRevision, IDictionary<string, HeadCameraControlValue> values)
        {
            ExpectedRevision = expectedRevision ?? string.Empty;
            var copy = new Dictionary<string, HeadCameraControlValue>(StringComparer.Ordinal);
            if (values != null)
                foreach (var pair in values) copy[pair.Key] = pair.Value;
            Values = copy;
        }

        public string ExpectedRevision { get; }
        public IReadOnlyDictionary<string, HeadCameraControlValue> Values { get; }
    }

    public sealed class HeadCameraControlResult
    {
        public HeadCameraControlResult(
            HeadCameraControlOutcome outcome,
            string message,
            IReadOnlyList<HeadCameraControlIssue> issues = null,
            HeadCameraControlSnapshot snapshot = null)
        {
            Outcome = outcome;
            Message = message ?? string.Empty;
            Issues = issues ?? Array.Empty<HeadCameraControlIssue>();
            Snapshot = snapshot;
        }

        public HeadCameraControlOutcome Outcome { get; }
        public string Message { get; }
        public IReadOnlyList<HeadCameraControlIssue> Issues { get; }
        public HeadCameraControlSnapshot Snapshot { get; }
    }
}
