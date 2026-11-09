using System;
using System.Collections.Generic;

namespace PreLuna.Captain.Control
{
    public enum CaptainControlStatus
    {
        Available,
        Uninitialized,
        Missing,
        Incompatible
    }

    [Flags]
    public enum CaptainControlCapabilities
    {
        None = 0,
        Validate = 1,
        SavePersistent = 2
    }

    public enum CaptainControlOutcome
    {
        Success,
        NoChanges,
        ValidationRejected,
        StateConflict,
        Unavailable,
        SaveFailed
    }

    public enum CaptainControlKind
    {
        Boolean,
        Number,
        Integer
    }

    public sealed class CaptainControlValue
    {
        CaptainControlValue(CaptainControlKind kind, object raw)
        {
            Kind = kind;
            Raw = raw;
        }

        public CaptainControlKind Kind { get; }
        public object Raw { get; }

        public static CaptainControlValue Boolean(bool value) => new CaptainControlValue(CaptainControlKind.Boolean, value);
        public static CaptainControlValue Number(double value) => new CaptainControlValue(CaptainControlKind.Number, value);
        public static CaptainControlValue Integer(int value) => new CaptainControlValue(CaptainControlKind.Integer, value);

        public bool TryGetBoolean(out bool value)
        {
            if (Kind == CaptainControlKind.Boolean && Raw is bool typed)
            {
                value = typed;
                return true;
            }

            value = false;
            return false;
        }

        public bool TryGetNumber(out double value)
        {
            if (Kind == CaptainControlKind.Number && Raw is double typed)
            {
                value = typed;
                return true;
            }

            value = 0;
            return false;
        }

        public bool TryGetInteger(out int value)
        {
            if (Kind == CaptainControlKind.Integer && Raw is int typed)
            {
                value = typed;
                return true;
            }

            value = 0;
            return false;
        }

        public bool ValueEquals(CaptainControlValue other)
        {
            if (other == null || Kind != other.Kind || Raw == null || other.Raw == null) return false;
            if (Kind == CaptainControlKind.Number)
                return Raw is double left && other.Raw is double right && Math.Abs(left - right) < 1e-9;
            return Raw.Equals(other.Raw);
        }

        public override string ToString() => Raw?.ToString() ?? string.Empty;
    }

    public sealed class CaptainControlSetting
    {
        public CaptainControlSetting(
            string key,
            string displayName,
            CaptainControlKind kind,
            CaptainControlValue defaultValue,
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
        public CaptainControlKind Kind { get; }
        public CaptainControlValue DefaultValue { get; }
        public string Section { get; }
        public string ConfigKey { get; }
        public double? Minimum { get; }
        public double? Maximum { get; }

        public CaptainControlIssue Validate(CaptainControlValue value)
        {
            if (value == null) return new CaptainControlIssue(Key, "缺少值");
            if (value.Kind != Kind) return new CaptainControlIssue(Key, "值类型不匹配");

            if (Kind == CaptainControlKind.Boolean)
                return value.TryGetBoolean(out _) ? null : new CaptainControlIssue(Key, "不是合法布尔值");

            if (Kind == CaptainControlKind.Integer)
            {
                if (!value.TryGetInteger(out var integer)) return new CaptainControlIssue(Key, "不是合法整数");
                return CheckRange(integer);
            }

            if (!value.TryGetNumber(out var number) || double.IsNaN(number) || double.IsInfinity(number))
                return new CaptainControlIssue(Key, "不是合法数值");
            return CheckRange(number);
        }

        CaptainControlIssue CheckRange(double value)
        {
            if (Minimum.HasValue && value < Minimum.Value) return new CaptainControlIssue(Key, "小于最小值 " + Minimum.Value);
            if (Maximum.HasValue && value > Maximum.Value) return new CaptainControlIssue(Key, "大于最大值 " + Maximum.Value);
            return null;
        }
    }

    public sealed class CaptainControlIssue
    {
        public CaptainControlIssue(string key, string message)
        {
            Key = key ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public string Key { get; }
        public string Message { get; }
    }

    public sealed class CaptainControlSnapshot
    {
        public CaptainControlSnapshot(
            string targetId,
            string displayName,
            string version,
            CaptainControlStatus status,
            string revision,
            CaptainControlCapabilities capabilities,
            IReadOnlyList<CaptainControlSetting> settings,
            IDictionary<string, CaptainControlValue> saved,
            IDictionary<string, CaptainControlValue> effective)
        {
            TargetId = targetId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Version = version ?? string.Empty;
            Status = status;
            Revision = revision ?? string.Empty;
            Capabilities = capabilities;
            Settings = settings ?? Array.Empty<CaptainControlSetting>();
            Saved = Copy(saved);
            Effective = Copy(effective);
        }

        public string TargetId { get; }
        public string DisplayName { get; }
        public string Version { get; }
        public CaptainControlStatus Status { get; }
        public string Revision { get; }
        public CaptainControlCapabilities Capabilities { get; }
        public IReadOnlyList<CaptainControlSetting> Settings { get; }
        public IReadOnlyDictionary<string, CaptainControlValue> Saved { get; }
        public IReadOnlyDictionary<string, CaptainControlValue> Effective { get; }

        static IReadOnlyDictionary<string, CaptainControlValue> Copy(IDictionary<string, CaptainControlValue> source)
        {
            var copy = new Dictionary<string, CaptainControlValue>(StringComparer.Ordinal);
            if (source != null)
                foreach (var pair in source) copy[pair.Key] = pair.Value;
            return copy;
        }
    }

    public sealed class CaptainControlChange
    {
        public CaptainControlChange(string expectedRevision, IDictionary<string, CaptainControlValue> values)
        {
            ExpectedRevision = expectedRevision ?? string.Empty;
            var copy = new Dictionary<string, CaptainControlValue>(StringComparer.Ordinal);
            if (values != null)
                foreach (var pair in values) copy[pair.Key] = pair.Value;
            Values = copy;
        }

        public string ExpectedRevision { get; }
        public IReadOnlyDictionary<string, CaptainControlValue> Values { get; }
    }

    public sealed class CaptainControlResult
    {
        public CaptainControlResult(
            CaptainControlOutcome outcome,
            string message,
            IReadOnlyList<CaptainControlIssue> issues = null,
            CaptainControlSnapshot snapshot = null)
        {
            Outcome = outcome;
            Message = message ?? string.Empty;
            Issues = issues ?? Array.Empty<CaptainControlIssue>();
            Snapshot = snapshot;
        }

        public CaptainControlOutcome Outcome { get; }
        public string Message { get; }
        public IReadOnlyList<CaptainControlIssue> Issues { get; }
        public CaptainControlSnapshot Snapshot { get; }
    }
}
