using System;
using System.Collections.Generic;

namespace PreLuna.Captain.Control
{
    internal sealed class CaptainControlState
    {
        internal const string TargetId = "PreLuna.Captain";
        internal const string ControlVersion = "0.5.4";

        internal static readonly IReadOnlyList<CaptainControlSetting> Definitions = new[]
        {
            new CaptainControlSetting("chineseMenus", "通讯菜单", CaptainControlKind.Boolean, CaptainControlValue.Boolean(true), "中文显示", "通讯菜单"),
            new CaptainControlSetting("chineseItems", "携带物品", CaptainControlKind.Boolean, CaptainControlValue.Boolean(true), "中文显示", "携带物品")
        };

        readonly string path;
        readonly Func<string, CaptainControlValue> readSaved;
        readonly Action<string, CaptainControlValue> writeSaved;
        readonly Func<string, string> serialize;
        readonly Func<bool> getAutoSave;
        readonly Action<bool> setAutoSave;
        readonly Dictionary<string, CaptainControlValue> saved = new(StringComparer.Ordinal);
        readonly Dictionary<string, CaptainControlValue> effective = new(StringComparer.Ordinal);
        string revision = "1";
        string fileHash;

        internal CaptainControlState(
            string path,
            Func<string, CaptainControlValue> readSaved,
            Action<string, CaptainControlValue> writeSaved,
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
            fileHash = CaptainControlFileStore.Capture(path);

            foreach (var definition in Definitions)
            {
                var value = this.readSaved(definition.Key) ?? definition.DefaultValue;
                saved[definition.Key] = value;
                effective[definition.Key] = value;
            }
        }

        internal string Revision => revision;

        internal CaptainControlSnapshot ReadSnapshot()
        {
            RefreshSaved();
            return Snapshot();
        }

        internal CaptainControlResult Validate(CaptainControlChange change)
        {
            var check = Check(change);
            if (check != null) return check;
            return new CaptainControlResult(CaptainControlOutcome.NoChanges, "没有改动", null, Snapshot());
        }

        internal CaptainControlResult SetPersistent(CaptainControlChange change)
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
                    var writes = new List<CaptainControlWrite>();
                    foreach (var pair in change.Values)
                    {
                        var definition = FindDefinition(pair.Key);
                        var text = serialize(pair.Key);
                        if (definition == null || text == null)
                            throw new InvalidOperationException("缺少配置映射：" + pair.Key);
                        writes.Add(new CaptainControlWrite(definition.Section, definition.ConfigKey, text));
                    }

                    fileHash = CaptainControlFileStore.Save(path, fileHash, writes);
                }
                catch (CaptainControlConflictException error)
                {
                    RestoreValues(previous);
                    RefreshSaved(false);
                    return new CaptainControlResult(CaptainControlOutcome.StateConflict, "配置文件已被外部修改：" + error.Message, null, Snapshot());
                }
                catch (Exception error)
                {
                    RestoreValues(previous);
                    RefreshSaved(false);
                    return new CaptainControlResult(CaptainControlOutcome.SaveFailed, "保存失败：" + error.Message, null, Snapshot());
                }

                foreach (var pair in change.Values)
                {
                    saved[pair.Key] = pair.Value;
                    effective[pair.Key] = pair.Value;
                }

                BumpRevision();
                return new CaptainControlResult(CaptainControlOutcome.Success, "已保存并生效", null, Snapshot());
            }
            finally
            {
                if (restoreAutoSave) setAutoSave(previousAutoSave.Value);
            }
        }

        CaptainControlResult Check(CaptainControlChange change, bool requireChanges = false)
        {
            if (change == null)
                return new CaptainControlResult(CaptainControlOutcome.ValidationRejected, "缺少变更集", new[] { new CaptainControlIssue(string.Empty, "null change") }, Snapshot());
            if (!string.Equals(change.ExpectedRevision, revision, StringComparison.Ordinal))
                return new CaptainControlResult(CaptainControlOutcome.StateConflict, "目标状态已变化，请刷新后重试", null, Snapshot());
            if (requireChanges && change.Values.Count == 0)
                return new CaptainControlResult(CaptainControlOutcome.NoChanges, "没有改动", null, Snapshot());

            var issues = new List<CaptainControlIssue>();
            foreach (var pair in change.Values)
            {
                var definition = FindDefinition(pair.Key);
                if (definition == null)
                {
                    issues.Add(new CaptainControlIssue(pair.Key, "未注册的设置"));
                    continue;
                }

                var issue = definition.Validate(pair.Value);
                if (issue != null) issues.Add(issue);
            }

            if (issues.Count == 0) return null;
            return new CaptainControlResult(CaptainControlOutcome.ValidationRejected, "变更未通过校验", issues, Snapshot());
        }

        Dictionary<string, CaptainControlValue> SnapshotValues(IEnumerable<string> keys)
        {
            var values = new Dictionary<string, CaptainControlValue>(StringComparer.Ordinal);
            foreach (var key in keys)
                if (saved.TryGetValue(key, out var value)) values[key] = value;
            return values;
        }

        void RestoreValues(Dictionary<string, CaptainControlValue> values)
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
                    var current = CaptainControlFileStore.Capture(path);
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

        CaptainControlSnapshot Snapshot()
            => new CaptainControlSnapshot(
                TargetId,
                "PreLuna 队长中文显示",
                ControlVersion,
                CaptainControlStatus.Available,
                revision,
                CaptainControlCapabilities.Validate | CaptainControlCapabilities.SavePersistent,
                Definitions,
                saved,
                effective);

        internal static CaptainControlSnapshot Unavailable(CaptainControlStatus status)
            => new CaptainControlSnapshot(
                TargetId,
                "PreLuna 队长中文显示",
                string.Empty,
                status,
                string.Empty,
                CaptainControlCapabilities.None,
                Array.Empty<CaptainControlSetting>(),
                new Dictionary<string, CaptainControlValue>(),
                new Dictionary<string, CaptainControlValue>());

        static CaptainControlSetting FindDefinition(string key)
        {
            foreach (var definition in Definitions)
                if (string.Equals(definition.Key, key, StringComparison.Ordinal)) return definition;
            return null;
        }
    }
}
