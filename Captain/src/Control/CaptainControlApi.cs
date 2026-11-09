using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using PreLuna.Captain;

namespace PreLuna.Captain.Control
{
    public static class CaptainControlApi
    {
        public const string TargetId = CaptainControlState.TargetId;
        public const string ControlVersion = CaptainControlState.ControlVersion;

        static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public static CaptainControlSnapshot GetSnapshot()
        {
            var control = Control;
            if (control != null) return control.ReadSnapshot();
            return CaptainControlState.Unavailable(
                Plugin.Instance == null ? CaptainControlStatus.Missing : CaptainControlStatus.Uninitialized);
        }

        public static CaptainControlResult Validate(CaptainControlChange change)
            => Control?.Validate(change) ?? Unavailable("第三人称镜头控制状态不可用");

        public static CaptainControlResult SetPersistent(CaptainControlChange change)
            => Control?.SetPersistent(change) ?? Unavailable("第三人称镜头控制状态不可用");

        public static string GetSnapshotJson()
            => JsonSerializer.Serialize(ToDto(GetSnapshot()), JsonOptions);

        public static string ValidateJson(string changeJson)
            => JsonSerializer.Serialize(ToDto(ValidateRead(changeJson)), JsonOptions);

        public static string SetPersistentJson(string changeJson)
            => JsonSerializer.Serialize(ToDto(SetPersistentRead(changeJson)), JsonOptions);

        static CaptainControlState Control => Plugin.Instance?.EnsureControlState();

        static CaptainControlResult Unavailable(string message)
            => new CaptainControlResult(
                CaptainControlOutcome.Unavailable,
                message,
                null,
                CaptainControlState.Unavailable(
                    Plugin.Instance == null ? CaptainControlStatus.Missing : CaptainControlStatus.Uninitialized));

        static CaptainControlResult ValidateRead(string changeJson)
        {
            if (!TryReadChange(changeJson, out var change, out var error)) return InvalidChange(error);
            return Validate(change);
        }

        static CaptainControlResult SetPersistentRead(string changeJson)
        {
            if (!TryReadChange(changeJson, out var change, out var error)) return InvalidChange(error);
            return SetPersistent(change);
        }

        static CaptainControlResult InvalidChange(string message)
            => new CaptainControlResult(
                CaptainControlOutcome.ValidationRejected,
                message,
                new[] { new CaptainControlIssue(string.Empty, message) },
                GetSnapshot());

        static bool TryReadChange(string changeJson, out CaptainControlChange change, out string error)
        {
            change = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(changeJson))
            {
                error = "变更集为空";
                return false;
            }

            try
            {
                var dto = JsonSerializer.Deserialize<ChangeDto>(changeJson, JsonOptions);
                if (dto == null)
                {
                    error = "变更集无法解析";
                    return false;
                }

                var values = new Dictionary<string, CaptainControlValue>(StringComparer.Ordinal);
                if (dto.Values != null)
                {
                    foreach (var pair in dto.Values)
                    {
                        var value = ToValue(pair.Value);
                        if (value == null)
                        {
                            error = "变更值类型无效：" + pair.Key;
                            return false;
                        }
                        values[pair.Key] = value;
                    }
                }

                change = new CaptainControlChange(dto.ExpectedRevision, values);
                return true;
            }
            catch (JsonException exception)
            {
                error = "变更集 JSON 无效：" + exception.Message;
                return false;
            }
        }

        static CaptainControlValue ToValue(ValueDto dto)
        {
            if (dto == null) return null;
            switch (dto.Kind)
            {
                case (int)CaptainControlKind.Boolean:
                    return dto.Boolean.HasValue ? CaptainControlValue.Boolean(dto.Boolean.Value) : null;
                case (int)CaptainControlKind.Number:
                    return dto.Number.HasValue ? CaptainControlValue.Number(dto.Number.Value) : null;
                case (int)CaptainControlKind.Integer:
                    return dto.Integer.HasValue ? CaptainControlValue.Integer(dto.Integer.Value) : null;
                default:
                    return null;
            }
        }

        static SnapshotDto ToDto(CaptainControlSnapshot snapshot)
        {
            if (snapshot == null) return null;
            var dto = new SnapshotDto
            {
                TargetId = snapshot.TargetId,
                DisplayName = snapshot.DisplayName,
                Version = snapshot.Version,
                Status = (int)snapshot.Status,
                Revision = snapshot.Revision,
                Capabilities = (int)snapshot.Capabilities,
                Settings = new List<SettingDto>(),
                Saved = ToDtoValues(snapshot.Saved),
                Effective = ToDtoValues(snapshot.Effective)
            };
            foreach (var setting in snapshot.Settings)
            {
                dto.Settings.Add(new SettingDto
                {
                    Key = setting.Key,
                    DisplayName = setting.DisplayName,
                    Kind = (int)setting.Kind,
                    DefaultValue = ToDto(setting.DefaultValue),
                    Minimum = setting.Minimum,
                    Maximum = setting.Maximum
                });
            }

            return dto;
        }

        static ResultDto ToDto(CaptainControlResult result)
        {
            if (result == null) return null;
            var dto = new ResultDto
            {
                Outcome = (int)result.Outcome,
                Message = result.Message,
                Issues = new List<IssueDto>(),
                Snapshot = ToDto(result.Snapshot)
            };
            foreach (var issue in result.Issues)
                dto.Issues.Add(new IssueDto { Key = issue.Key, Message = issue.Message });
            return dto;
        }

        static ValueDto ToDto(CaptainControlValue value)
        {
            if (value == null) return null;
            var dto = new ValueDto { Kind = (int)value.Kind };
            if (value.TryGetBoolean(out var boolean)) dto.Boolean = boolean;
            else if (value.TryGetNumber(out var number)) dto.Number = number;
            else if (value.TryGetInteger(out var integer)) dto.Integer = integer;
            return dto;
        }

        static Dictionary<string, ValueDto> ToDtoValues(IReadOnlyDictionary<string, CaptainControlValue> values)
        {
            var dto = new Dictionary<string, ValueDto>(StringComparer.Ordinal);
            if (values != null)
                foreach (var pair in values) dto[pair.Key] = ToDto(pair.Value);
            return dto;
        }

        sealed class SnapshotDto
        {
            public string TargetId { get; set; }
            public string DisplayName { get; set; }
            public string Version { get; set; }
            public int Status { get; set; }
            public string Revision { get; set; }
            public int Capabilities { get; set; }
            public List<SettingDto> Settings { get; set; }
            public Dictionary<string, ValueDto> Saved { get; set; }
            public Dictionary<string, ValueDto> Effective { get; set; }
        }

        sealed class SettingDto
        {
            public string Key { get; set; }
            public string DisplayName { get; set; }
            public int Kind { get; set; }
            public ValueDto DefaultValue { get; set; }
            public double? Minimum { get; set; }
            public double? Maximum { get; set; }
        }

        sealed class ValueDto
        {
            public int Kind { get; set; }
            public bool? Boolean { get; set; }
            public double? Number { get; set; }
            public int? Integer { get; set; }
        }

        sealed class ChangeDto
        {
            public string ExpectedRevision { get; set; }
            public Dictionary<string, ValueDto> Values { get; set; }
        }

        sealed class ResultDto
        {
            public int Outcome { get; set; }
            public string Message { get; set; }
            public List<IssueDto> Issues { get; set; }
            public SnapshotDto Snapshot { get; set; }
        }

        sealed class IssueDto
        {
            public string Key { get; set; }
            public string Message { get; set; }
        }
    }
}
