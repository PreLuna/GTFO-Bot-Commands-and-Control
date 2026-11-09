using System;
using System.Collections.Generic;

namespace PreLuna.MicroMaster.Control
{
    internal sealed class MicroMasterControlState
    {
        internal const string TargetId = "PreLuna.MicroMaster";
        internal const string ControlVersion = "0.5.18";

        internal static readonly IReadOnlyList<MicroMasterControlSetting> Definitions = new[]
        {
            new MicroMasterControlSetting("enabled", "启用", MicroMasterControlKind.Boolean, MicroMasterControlValue.Boolean(true), "开关", "启用"),
            new MicroMasterControlSetting("doubleClickWindow", "双击间隔", MicroMasterControlKind.Number, MicroMasterControlValue.Number(.3), "按键", "双击间隔", .18, .6),
            new MicroMasterControlSetting("walkHaste", "步行速度档位", MicroMasterControlKind.Number, MicroMasterControlValue.Number(.25), "微调", "步行速度档位", .05, .5),
            new MicroMasterControlSetting("mouseSensitivity", "鼠标灵敏度", MicroMasterControlKind.Number, MicroMasterControlValue.Number(1), "观察", "鼠标灵敏度", .05, 10),
            new MicroMasterControlSetting("invertMouse", "反转纵轴", MicroMasterControlKind.Boolean, MicroMasterControlValue.Boolean(false), "观察", "反转纵轴"),
            new MicroMasterControlSetting("nativeLight", "原生手电常亮", MicroMasterControlKind.Boolean, MicroMasterControlValue.Boolean(true), "接管手电", "原生手电常亮")
        };

        readonly string path;
        readonly Func<string, MicroMasterControlValue> readSaved;
        readonly Action<string, MicroMasterControlValue> writeSaved;
        readonly Func<string, string> serialize;
        readonly Func<bool> getAutoSave;
        readonly Action<bool> setAutoSave;
        readonly Dictionary<string, MicroMasterControlValue> saved = new(StringComparer.Ordinal);
        readonly Dictionary<string, MicroMasterControlValue> effective = new(StringComparer.Ordinal);
        string revision = "1";
        string fileHash;

        internal MicroMasterControlState(
            string path,
            Func<string, MicroMasterControlValue> readSaved,
            Action<string, MicroMasterControlValue> writeSaved,
            Func<string, string> serialize,
            Func<bool> getAutoSave = null,
            Action<bool> setAutoSave = null)
        {
            this.path = path ?? throw new ArgumentNullException(nameof(path));
            this.readSaved = readSaved ?? throw new ArgumentNullException(nameof(readSaved));
            this.writeSaved = writeSaved ?? throw new ArgumentNullException(nameof(writeSaved));
            this.serialize = serialize ?? throw new ArgumentNullException(nameof(serialize));
            this.getAutoSave = getAutoSave;
            this.setAutoSave = setAutoSave;
            fileHash = MicroMasterControlFileStore.Capture(path);

            foreach (var definition in Definitions)
            {
                var value = this.readSaved(definition.Key) ?? definition.DefaultValue;
                saved[definition.Key] = value;
                effective[definition.Key] = value;
            }
        }

        internal string Revision => revision;

        internal MicroMasterControlSnapshot ReadSnapshot()
        {
            RefreshSaved();
            return Snapshot();
        }

        internal MicroMasterControlResult Validate(MicroMasterControlChange change)
        {
            var check = Check(change);
            if (check != null) return check;
            return new MicroMasterControlResult(MicroMasterControlOutcome.NoChanges, "没有改动", null, Snapshot());
        }

        internal MicroMasterControlResult SetPersistent(MicroMasterControlChange change)
        {
            var check = Check(change, requireChanges: true);
            if (check != null) return check;

            var previous = SnapshotValues(change.Values.Keys);
            var previousAutoSave = getAutoSave?.Invoke();
            var restoreAutoSave = setAutoSave != null && previousAutoSave.HasValue;
            if (restoreAutoSave) setAutoSave(false);

            try
            {
                try
                {
                    foreach (var pair in change.Values) writeSaved(pair.Key, pair.Value);
                    var writes = new List<MicroMasterControlWrite>();
                    foreach (var pair in change.Values)
                    {
                        var definition = FindDefinition(pair.Key);
                        var text = serialize(pair.Key);
                        if (definition == null || text == null)
                            throw new InvalidOperationException("缺少配置映射：" + pair.Key);
                        writes.Add(new MicroMasterControlWrite(definition.Section, definition.ConfigKey, text));
                    }

                    fileHash = MicroMasterControlFileStore.Save(path, fileHash, writes);
                }
                catch (MicroMasterControlConflictException error)
                {
                    RestoreValues(previous);
                    RefreshSaved(false);
                    return new MicroMasterControlResult(MicroMasterControlOutcome.StateConflict, "配置文件已被外部修改：" + error.Message, null, Snapshot());
                }
                catch (Exception error)
                {
                    RestoreValues(previous);
                    RefreshSaved(false);
                    return new MicroMasterControlResult(MicroMasterControlOutcome.SaveFailed, "保存失败：" + error.Message, null, Snapshot());
                }

                foreach (var pair in change.Values)
                {
                    saved[pair.Key] = pair.Value;
                    effective[pair.Key] = pair.Value;
                }

                BumpRevision();
                return new MicroMasterControlResult(MicroMasterControlOutcome.Success, "已保存并生效", null, Snapshot());
            }
            finally
            {
                if (restoreAutoSave) setAutoSave(previousAutoSave.Value);
            }
        }

        MicroMasterControlResult Check(MicroMasterControlChange change, bool requireChanges = false)
        {
            if (change == null)
                return new MicroMasterControlResult(MicroMasterControlOutcome.ValidationRejected, "缺少变更集", new[] { new MicroMasterControlIssue(string.Empty, "null change") }, Snapshot());
            if (!string.Equals(change.ExpectedRevision, revision, StringComparison.Ordinal))
                return new MicroMasterControlResult(MicroMasterControlOutcome.StateConflict, "目标状态已变化，请刷新后重试", null, Snapshot());
            if (requireChanges && change.Values.Count == 0)
                return new MicroMasterControlResult(MicroMasterControlOutcome.NoChanges, "没有改动", null, Snapshot());

            var issues = new List<MicroMasterControlIssue>();
            foreach (var pair in change.Values)
            {
                var definition = FindDefinition(pair.Key);
                if (definition == null)
                {
                    issues.Add(new MicroMasterControlIssue(pair.Key, "未注册的设置"));
                    continue;
                }

                var issue = definition.Validate(pair.Value);
                if (issue != null) issues.Add(issue);
            }

            if (issues.Count == 0) return null;
            return new MicroMasterControlResult(MicroMasterControlOutcome.ValidationRejected, "变更未通过校验", issues, Snapshot());
        }

        Dictionary<string, MicroMasterControlValue> SnapshotValues(IEnumerable<string> keys)
        {
            var values = new Dictionary<string, MicroMasterControlValue>(StringComparer.Ordinal);
            foreach (var key in keys)
                if (saved.TryGetValue(key, out var value)) values[key] = value;
            return values;
        }

        void RestoreValues(Dictionary<string, MicroMasterControlValue> values)
        {
            foreach (var pair in values)
            {
                try { writeSaved(pair.Key, pair.Value); }
                catch { }
            }
        }

        void RefreshSaved(bool adoptFile = true)
        {
            var changed = false;
            foreach (var definition in Definitions)
            {
                var value = readSaved(definition.Key) ?? definition.DefaultValue;
                if (!saved.TryGetValue(definition.Key, out var previous) || !previous.ValueEquals(value))
                {
                    saved[definition.Key] = value;
                    effective[definition.Key] = value;
                    changed = true;
                }
            }

            if (changed)
            {
                // Hotkeys (F9/F10 etc.) update ConfigEntry values and let BepInEx save the file.
                // Adopt that internal file state so a subsequent ModControl write is not a false conflict.
                if (adoptFile)
                {
                    var current = MicroMasterControlFileStore.Capture(path);
                    if (!string.IsNullOrEmpty(current)) fileHash = current;
                }

                BumpRevision();
            }
        }

        void BumpRevision()
        {
            var number = 1;
            if (int.TryParse(revision, out var parsed)) number = parsed + 1;
            revision = number.ToString();
        }

        MicroMasterControlSnapshot Snapshot()
            => new MicroMasterControlSnapshot(
                TargetId,
                "PreLuna 微操大师",
                ControlVersion,
                MicroMasterControlStatus.Available,
                revision,
                MicroMasterControlCapabilities.Validate | MicroMasterControlCapabilities.SavePersistent,
                Definitions,
                saved,
                effective);

        internal static MicroMasterControlSnapshot Unavailable(MicroMasterControlStatus status)
            => new MicroMasterControlSnapshot(
                TargetId,
                "PreLuna 微操大师",
                string.Empty,
                status,
                string.Empty,
                MicroMasterControlCapabilities.None,
                Array.Empty<MicroMasterControlSetting>(),
                new Dictionary<string, MicroMasterControlValue>(),
                new Dictionary<string, MicroMasterControlValue>());

        static MicroMasterControlSetting FindDefinition(string key)
        {
            foreach (var definition in Definitions)
                if (string.Equals(definition.Key, key, StringComparison.Ordinal)) return definition;
            return null;
        }
    }
}
