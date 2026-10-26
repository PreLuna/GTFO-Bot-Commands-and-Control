using System;

namespace PreLuna.HeadCamera
{
    public enum FeedOutput { ToneMapped, RawOpaque, OriginalAlpha, TestPattern }

    // Shared across capture instances so repeated T toggles cannot flood disk/GPU readbacks.
    internal sealed class CaptureQuota
    {
        int remaining;
        internal CaptureQuota(int limit) => remaining = Math.Max(0, limit);
        internal bool Take() { if (remaining == 0) return false; remaining--; return true; }
    }
    internal sealed class CaptureSchedule
    {
        readonly CaptureQuota quota;
        double started = double.NaN;
        int attempts;
        bool? controlledView;
        internal void View(bool controlled)
        {
            if (controlledView == controlled) return;
            controlledView = controlled; started = double.NaN; attempts = 0;
        }
        internal CaptureSchedule(CaptureQuota quota) => this.quota = quota;
        internal bool Due(bool enabled, double now, bool urgent = false)
        {
            if (!enabled || attempts >= 2) return false;
            if (double.IsNaN(started)) started = now;
            if (!urgent && now - started < (attempts == 0 ? .5 : 2)) return false;
            attempts++; // A failed diagnostic is also an attempt; never retry it every frame.
            return quota.Take();
        }
    }
    internal sealed class CapturePixelSummary
    {
        internal int Count, Invalid, NonBlack, Transparent, WhitePixels;
        internal float MinRgb = float.PositiveInfinity, MaxRgb = float.NegativeInfinity;
        internal float MinAlpha = float.PositiveInfinity, MaxAlpha = float.NegativeInfinity;
        double sum;
        internal double MeanRgb => Count == 0 ? 0 : sum / (Count * 3d);
        internal void Add(float r, float g, float b, float a)
        {
            if (!float.IsFinite(r) || !float.IsFinite(g) || !float.IsFinite(b) || !float.IsFinite(a)) { Invalid++; return; }
            if (r >= .98f && g >= .98f && b >= .98f) WhitePixels++;
            Count++; sum += (double)r + g + b;
            MinRgb = Math.Min(MinRgb, Math.Min(r, Math.Min(g, b)));
            MaxRgb = Math.Max(MaxRgb, Math.Max(r, Math.Max(g, b)));
            MinAlpha = Math.Min(MinAlpha, a); MaxAlpha = Math.Max(MaxAlpha, a);
            if (Math.Max(r, Math.Max(g, b)) > .001f) NonBlack++;
            if (a < .01f) Transparent++;
        }
        public override string ToString() => FormattableString.Invariant(
            $"finitePixels={Count} invalidPixels={Invalid} RGB=[{MinRgb:G9},{MaxRgb:G9}] meanRGB={MeanRgb:G9} nonBlackPixels={NonBlack} alpha=[{MinAlpha:G9},{MaxAlpha:G9}] transparentPixels={Transparent} nearWhitePixels={WhitePixels}");
        internal static float Preview(float linear, float exposure = 1)
        {
            if (!float.IsFinite(linear)) return 0;
            linear = Math.Clamp(linear * exposure, 0, 1);
            return linear <= .0031308f ? linear * 12.92f : 1.055f * MathF.Pow(linear, 1 / 2.4f) - .055f;
        }
    }
}
