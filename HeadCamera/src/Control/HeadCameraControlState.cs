using System;
using System.Collections.Generic;

namespace PreLuna.HeadCamera.Control
{
    internal sealed class HeadCameraControlState
    {
        internal const string TargetId = "PreLuna.HeadCamera";
        internal const string ControlVersion = "0.3.16";

        internal static readonly IReadOnlyList<HeadCameraControlSetting> Definitions = new[]
        {
            new HeadCameraControlSetting("enabled", "启用", HeadCameraControlKind.Boolean, HeadCameraControlValue.Boolean(true), "开关", "启用"),
            new HeadCameraControlSetting("width", "渲染宽度", HeadCameraControlKind.Number, HeadCameraControlValue.Number(1280), "画面", "渲染宽度", 160, 1920),
            new HeadCameraControlSetting("fps", "刷新帧率", HeadCameraControlKind.Number, HeadCameraControlValue.Number(30), "画面", "刷新帧率", 5, 60),
            new HeadCameraControlSetting("fov", "视野角度", HeadCameraControlKind.Number, HeadCameraControlValue.Number(80), "画面", "视野角度", 45, 110),
            new HeadCameraControlSetting("viewDistance", "最大绘制距离", HeadCameraControlKind.Number, HeadCameraControlValue.Number(80), "画面", "最大绘制距离", 15, 120),
            new HeadCameraControlSetting("windowWidth", "小窗宽度", HeadCameraControlKind.Number, HeadCameraControlValue.Number(480), "小窗", "显示宽度", 200, 960)
        };

        readonly string path;
        readonly Func<string, HeadCameraControlValue> readSaved;
        readonly Action<string, HeadCameraControlValue> writeSaved;
        readonly Func<string, string> serialize;
        readonly Func<bool> getAutoSave;
        readonly Action<bool> setAutoSave;
        readonly Dictionary<string, HeadCameraControlValue> saved = new(StringComparer.Ordinal);
        readonly Dictionary<string, HeadCameraControlValue> effective = new(StringComparer.Ordinal);
        string revision = "1";
        string fileHash;

        internal HeadCameraControlState(
            string path,
            Func<string, HeadCameraControlValue> readSaved,
            Action<string, HeadCameraControlValue> writeSaved,
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
            fileHash = HeadCameraControlFileStore.Capture(path);

            foreach (var definition in Definitions)
            {
                var value = this.readSaved(definition.Key) ?? definition.DefaultValue;
                saved[definition.Key] = value;
                effective[definition.Key] = value;
            }
        }

        internal string Revision => revision;

        internal HeadCameraControlSnapshot ReadSnapshot()
        {
            RefreshSaved();
            return Snapshot();
        }

        internal HeadCameraControlResult Validate(HeadCameraControlChange change)
        {
            var check = Check(change);
            if (check != null) return check;
            return new HeadCameraControlResult(HeadCameraControlOutcome.NoChanges, "没有改动", null, Snapshot());
        }

        internal HeadCameraControlResult SetPersistent(HeadCameraControlChange change)
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
                    var writes = new List<HeadCameraControlWrite>();
                    foreach (var pair in change.Values)
                    {
                        var definition = FindDefinition(pair.Key);
                        var text = serialize(pair.Key);
                        if (definition == null || text == null)
                            throw new InvalidOperationException("缺少配置映射：" + pair.Key);
                        writes.Add(new HeadCameraControlWrite(definition.Section, definition.ConfigKey, text));
                    }

                    fileHash = HeadCameraControlFileStore.Save(path, fileHash, writes);
                }
                catch (HeadCameraControlConflictException error)
                {
                    RestoreValues(previous);
                    RefreshSaved(false);
                    return new HeadCameraControlResult(HeadCameraControlOutcome.StateConflict, "配置文件已被外部修改：" + error.Message, null, Snapshot());
                }
                catch (Exception error)
                {
                    RestoreValues(previous);
                    RefreshSaved(false);
                    return new HeadCameraControlResult(HeadCameraControlOutcome.SaveFailed, "保存失败：" + error.Message, null, Snapshot());
                }

                foreach (var pair in change.Values)
                {
                    saved[pair.Key] = pair.Value;
                    effective[pair.Key] = pair.Value;
                }

                BumpRevision();
                return new HeadCameraControlResult(HeadCameraControlOutcome.Success, "已保存并生效", null, Snapshot());
            }
            finally
            {
                if (restoreAutoSave) setAutoSave(previousAutoSave.Value);
            }
        }

        HeadCameraControlResult Check(HeadCameraControlChange change, bool requireChanges = false)
        {
            if (change == null)
                return new HeadCameraControlResult(HeadCameraControlOutcome.ValidationRejected, "缺少变更集", new[] { new HeadCameraControlIssue(string.Empty, "null change") }, Snapshot());
            if (!string.Equals(change.ExpectedRevision, revision, StringComparison.Ordinal))
                return new HeadCameraControlResult(HeadCameraControlOutcome.StateConflict, "目标状态已变化，请刷新后重试", null, Snapshot());
            if (requireChanges && change.Values.Count == 0)
                return new HeadCameraControlResult(HeadCameraControlOutcome.NoChanges, "没有改动", null, Snapshot());

            var issues = new List<HeadCameraControlIssue>();
            foreach (var pair in change.Values)
            {
                var definition = FindDefinition(pair.Key);
                if (definition == null)
                {
                    issues.Add(new HeadCameraControlIssue(pair.Key, "未注册的设置"));
                    continue;
                }

                var issue = definition.Validate(pair.Value);
                if (issue != null) issues.Add(issue);
            }

            if (issues.Count == 0) return null;
            return new HeadCameraControlResult(HeadCameraControlOutcome.ValidationRejected, "变更未通过校验", issues, Snapshot());
        }

        Dictionary<string, HeadCameraControlValue> SnapshotValues(IEnumerable<string> keys)
        {
            var values = new Dictionary<string, HeadCameraControlValue>(StringComparer.Ordinal);
            foreach (var key in keys)
                if (saved.TryGetValue(key, out var value)) values[key] = value;
            return values;
        }

        void RestoreValues(Dictionary<string, HeadCameraControlValue> values)
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
                    var current = HeadCameraControlFileStore.Capture(path);
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

        HeadCameraControlSnapshot Snapshot()
            => new HeadCameraControlSnapshot(
                TargetId,
                "PreLuna 头戴摄像头",
                ControlVersion,
                HeadCameraControlStatus.Available,
                revision,
                HeadCameraControlCapabilities.Validate | HeadCameraControlCapabilities.SavePersistent,
                Definitions,
                saved,
                effective);

        internal static HeadCameraControlSnapshot Unavailable(HeadCameraControlStatus status)
            => new HeadCameraControlSnapshot(
                TargetId,
                "PreLuna 头戴摄像头",
                string.Empty,
                status,
                string.Empty,
                HeadCameraControlCapabilities.None,
                Array.Empty<HeadCameraControlSetting>(),
                new Dictionary<string, HeadCameraControlValue>(),
                new Dictionary<string, HeadCameraControlValue>());

        static HeadCameraControlSetting FindDefinition(string key)
        {
            foreach (var definition in Definitions)
                if (string.Equals(definition.Key, key, StringComparison.Ordinal)) return definition;
            return null;
        }
    }
}
