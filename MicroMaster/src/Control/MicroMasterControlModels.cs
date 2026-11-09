using System;
using System.Collections.Generic;

namespace PreLuna.MicroMaster.Control
{
    public enum MicroMasterControlStatus
    {
        Available,
        Uninitialized,
        Missing,
        Incompatible
    }

    [Flags]
    public enum MicroMasterControlCapabilities
    {
        None = 0,
        Validate = 1,
        SavePersistent = 2
    }

    public enum MicroMasterControlOutcome
    {
        Success,
        NoChanges,
        ValidationRejected,
        StateConflict,
        Unavailable,
        SaveFailed
    }

    public enum MicroMasterControlKind
    {
        Boolean,
        Number,
        Integer
    }

    public sealed class MicroMasterControlValue
    {
        MicroMasterControlValue(MicroMasterControlKind kind, object raw)
        {
            Kind = kind;
            Raw = raw;
        }

        public MicroMasterControlKind Kind { get; }
        public object Raw { get; }

        public static MicroMasterControlValue Boolean(bool value) => new MicroMasterControlValue(MicroMasterControlKind.Boolean, value);
        public static MicroMasterControlValue Number(double value) => new MicroMasterControlValue(MicroMasterControlKind.Number, value);
        public static MicroMasterControlValue Integer(int value) => new MicroMasterControlValue(MicroMasterControlKind.Integer, value);

        public bool TryGetBoolean(out bool value)
        {
            if (Kind == MicroMasterControlKind.Boolean && Raw is bool typed)
            {
                value = typed;
                return true;
            }

            value = false;
            return false;
        }

        public bool TryGetNumber(out double value)
        {
            if (Kind == MicroMasterControlKind.Number && Raw is double typed)
            {
                value = typed;
                return true;
            }

            value = 0;
            return false;
        }

        public bool TryGetInteger(out int value)
        {
            if (Kind == MicroMasterControlKind.Integer && Raw is int typed)
            {
                value = typed;
                return true;
            }

            value = 0;
            return false;
        }

        public bool ValueEquals(MicroMasterControlValue other)
        {
            if (other == null || Kind != other.Kind || Raw == null || other.Raw == null) return false;
            if (Kind == MicroMasterControlKind.Number)
                return Raw is double left && other.Raw is double right && Math.Abs(left - right) < 1e-9;
            return Raw.Equals(other.Raw);
        }

        public override string ToString() => Raw?.ToString() ?? string.Empty;
    }

    public sealed class MicroMasterControlSetting
    {
        public MicroMasterControlSetting(
            string key,
            string displayName,
            MicroMasterControlKind kind,
            MicroMasterControlValue defaultValue,
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
        public MicroMasterControlKind Kind { get; }
        public MicroMasterControlValue DefaultValue { get; }
        public string Section { get; }
        public string ConfigKey { get; }
        public double? Minimum { get; }
        public double? Maximum { get; }

        public MicroMasterControlIssue Validate(MicroMasterControlValue value)
        {
            if (value == null) return new MicroMasterControlIssue(Key, "缺少值");
            if (value.Kind != Kind) return new MicroMasterControlIssue(Key, "值类型不匹配");

            if (Kind == MicroMasterControlKind.Boolean)
                return value.TryGetBoolean(out _) ? null : new MicroMasterControlIssue(Key, "不是合法布尔值");

            if (Kind == MicroMasterControlKind.Integer)
            {
                if (!value.TryGetInteger(out var integer)) return new MicroMasterControlIssue(Key, "不是合法整数");
                return CheckRange(integer);
            }

            if (!value.TryGetNumber(out var number) || double.IsNaN(number) || double.IsInfinity(number))
                return new MicroMasterControlIssue(Key, "不是合法数值");
            return CheckRange(number);
        }

        MicroMasterControlIssue CheckRange(double value)
        {
            if (Minimum.HasValue && value < Minimum.Value) return new MicroMasterControlIssue(Key, "小于最小值 " + Minimum.Value);
            if (Maximum.HasValue && value > Maximum.Value) return new MicroMasterControlIssue(Key, "大于最大值 " + Maximum.Value);
            return null;
        }
    }

    public sealed class MicroMasterControlIssue
    {
        public MicroMasterControlIssue(string key, string message)
        {
            Key = key ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public string Key { get; }
        public string Message { get; }
    }

    public sealed class MicroMasterControlSnapshot
    {
        public MicroMasterControlSnapshot(
            string targetId,
            string displayName,
            string version,
            MicroMasterControlStatus status,
            string revision,
            MicroMasterControlCapabilities capabilities,
            IReadOnlyList<MicroMasterControlSetting> settings,
            IDictionary<string, MicroMasterControlValue> saved,
            IDictionary<string, MicroMasterControlValue> effective)
        {
            TargetId = targetId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Version = version ?? string.Empty;
            Status = status;
            Revision = revision ?? string.Empty;
            Capabilities = capabilities;
            Settings = settings ?? Array.Empty<MicroMasterControlSetting>();
            Saved = Copy(saved);
            Effective = Copy(effective);
        }

        public string TargetId { get; }
        public string DisplayName { get; }
        public string Version { get; }
        public MicroMasterControlStatus Status { get; }
        public string Revision { get; }
        public MicroMasterControlCapabilities Capabilities { get; }
        public IReadOnlyList<MicroMasterControlSetting> Settings { get; }
        public IReadOnlyDictionary<string, MicroMasterControlValue> Saved { get; }
        public IReadOnlyDictionary<string, MicroMasterControlValue> Effective { get; }

        static IReadOnlyDictionary<string, MicroMasterControlValue> Copy(IDictionary<string, MicroMasterControlValue> source)
        {
            var copy = new Dictionary<string, MicroMasterControlValue>(StringComparer.Ordinal);
            if (source != null)
                foreach (var pair in source) copy[pair.Key] = pair.Value;
            return copy;
        }
    }

    public sealed class MicroMasterControlChange
    {
        public MicroMasterControlChange(string expectedRevision, IDictionary<string, MicroMasterControlValue> values)
        {
            ExpectedRevision = expectedRevision ?? string.Empty;
            var copy = new Dictionary<string, MicroMasterControlValue>(StringComparer.Ordinal);
            if (values != null)
                foreach (var pair in values) copy[pair.Key] = pair.Value;
            Values = copy;
        }

        public string ExpectedRevision { get; }
        public IReadOnlyDictionary<string, MicroMasterControlValue> Values { get; }
    }

    public sealed class MicroMasterControlResult
    {
        public MicroMasterControlResult(
            MicroMasterControlOutcome outcome,
            string message,
            IReadOnlyList<MicroMasterControlIssue> issues = null,
            MicroMasterControlSnapshot snapshot = null)
        {
            Outcome = outcome;
            Message = message ?? string.Empty;
            Issues = issues ?? Array.Empty<MicroMasterControlIssue>();
            Snapshot = snapshot;
        }

        public MicroMasterControlOutcome Outcome { get; }
        public string Message { get; }
        public IReadOnlyList<MicroMasterControlIssue> Issues { get; }
        public MicroMasterControlSnapshot Snapshot { get; }
    }
}
