using System;
using System.Collections.Generic;
using CellMenu;
using UnityEngine;
namespace PreLuna.Captain.Boosters
{
    internal static class BotBoosterLayout
    {
        sealed class Layout { public Vector3 Original, Applied, Header, HeaderApplied, Clothes; public Vector2 Size; public bool Written; public IntPtr Parent; public float NextCheck; }
        static readonly Dictionary<IntPtr, Layout> layouts = new();
        public static void Apply(CM_PlayerLobbyBar bar)
        {
            var align = bar?.m_boosterImplantAlign; var clothes = bar?.m_clothesButton; var holder = bar?.m_boosterImplantSlotHolder;
            if (align?.parent == null || clothes == null || holder == null || !clothes.gameObject.activeInHierarchy) return;
            if (!layouts.TryGetValue(bar.Pointer, out var record) || record.Parent != align.parent.Pointer)
            {
                if (layouts.Count > 32) layouts.Clear();
                record = new Layout { Parent = align.parent.Pointer, Original = align.localPosition, Header = bar.m_boosterImplantsHeaderText?.transform.position ?? Vector3.zero };
                layouts[bar.Pointer] = record;
            }
            if (record.Written && (align.localPosition - record.Applied).sqrMagnitude > .0001f)
            { record.Original = align.localPosition; record.Header = bar.m_boosterImplantsHeaderText?.transform.position ?? Vector3.zero; record.Written = false; }
            if (Time.unscaledTime < record.NextCheck && (record.Clothes - clothes.transform.position).sqrMagnitude < .0001f
                && record.Size == holder.m_size) return;
            record.NextCheck = Time.unscaledTime + 2; record.Clothes = clothes.transform.position; record.Size = holder.m_size;
            float minY = float.PositiveInfinity, maxY = float.NegativeInfinity, minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            foreach (var renderer in clothes.GetComponentsInChildren<Renderer>())
            {
                if (renderer == null || !renderer.gameObject.activeInHierarchy) continue;
                var bounds = renderer.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    var p = align.parent.InverseTransformPoint(new Vector3((corner & 1) == 0 ? bounds.min.x : bounds.max.x,
                        (corner & 2) == 0 ? bounds.min.y : bounds.max.y, (corner & 4) == 0 ? bounds.min.z : bounds.max.z));
                    minY = Math.Min(minY, p.y); maxY = Math.Max(maxY, p.y); minX = Math.Min(minX, p.x); maxX = Math.Max(maxX, p.x);
                }
            }
            if (!float.IsFinite(minY) || holder.m_size.y <= 0) return;
            var desired = record.Original;
            float width = Math.Abs(holder.m_size.x);
            if (desired.x + width >= minX && desired.x - width <= maxX)
                desired.y = BoosterViewPolicy.ResolveY(desired.y, Math.Abs(holder.m_size.y), minY, maxY,
                    Math.Max(.01f, Math.Abs(bar.m_boosterHeaderSpacing) + Math.Abs(bar.m_headerSpacing)));
            if ((align.localPosition - desired).sqrMagnitude < .0001f) return;
            align.localPosition = desired; record.Applied = desired; record.Written = true;
            if (bar.m_boosterImplantsHeaderText != null)
            {
                record.HeaderApplied = record.Header + align.parent.TransformVector(desired - record.Original);
                bar.m_boosterImplantsHeaderText.transform.position = record.HeaderApplied;
            }
        }
        public static void Restore(CM_PlayerLobbyBar bar)
        {
            if (bar == null || !layouts.TryGetValue(bar.Pointer, out var record)) return;
            var align = bar.m_boosterImplantAlign;
            if (record.Written && align != null && (align.localPosition - record.Applied).sqrMagnitude < .0001f) align.localPosition = record.Original;
            var header = bar.m_boosterImplantsHeaderText;
            if (record.Written && header != null && (header.transform.position - record.HeaderApplied).sqrMagnitude < .0001f) header.transform.position = record.Header;
            layouts.Remove(bar.Pointer);
        }
        public static void Reset()
        {
            foreach (var bar in CM_PlayerLobbyBar.instances) { try { Restore(bar); } catch { /* Menu teardown may already have destroyed native controls. */ } }
            layouts.Clear();
        }
    }
}
