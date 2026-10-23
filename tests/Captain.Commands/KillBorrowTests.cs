using System;
using PreLuna.Captain.Tasks;
static class KillBorrowTests
{
    static int count;
    static void Check(bool ok, string why) { count++; if (!ok) throw new Exception(why); }
    sealed class Back : IBorrowReturnSession
    {
        public bool Valid { get; set; } = true;
        public bool Retained { get; set; } = true;
        public bool Safe { get; set; } = true;
        public bool Arrived { get; set; }
        public bool Moving { get; set; }
        public bool Permit = true, Renewed = true, CanMove = true, ThrowRelease;
        public int Acquires, Moves, Releases, Finishes;
        public bool Restored;
        public bool Acquire() { Acquires++; return Permit; }
        public bool Renew() => Renewed;
        public void Release() { Releases++; if (ThrowRelease) throw new Exception("native release"); }
        public bool Move() { Moves++; return CanMove; }
        public void Finish(bool restore, string reason) { Finishes++; Restored = restore; }
    }
    sealed class ItemSession : IItemTaskSession
    {
        public bool Valid => true;
        public bool CanExecute => true;
        public float InteractionDuration => .1f;
        public int Suspends, Applies, Acquires, Amount = 3;
        public bool At = true;
        public bool Acquire() { Acquires++; return true; }
        public bool Renew(ItemTaskStage s) => true;
        public bool AtDestination(bool r) => At;
        public bool Move(bool r) => true;
        public void StopOwnMovement() { }
        public void Suspend() => Suspends++;
        public string Validate() => null;
        public bool Apply(out string error) { error = null; Applies++; Amount += 4; return true; }
        public bool Verify() => true;
        public void Release(ItemTaskStage s) { }
        public void Notify(ItemTaskStage s, string detail) { }
    }
    static void Main()
    {
        var b = new Back(); var r = new BorrowReturnRunner(b, 0);
        r.Tick(0); r.Tick(.9f); Check(b.Acquires == 0, "return waits for stable safety");
        r.Tick(1); Check(b.Acquires == 1 && b.Moves == 1, "return owns one lease before movement");
        b.Arrived = true; r.Tick(2); Check(r.Finished && b.Releases == 1 && b.Restored && b.Finishes == 1, "arrival releases before restore");
        r.Tick(3); r.Abort(true, "duplicate"); Check(b.Finishes == 1 && b.Releases == 1, "completion is idempotent");
        b = new Back { Safe = false }; r = new(b, 0); r.Tick(0); r.Tick(60);
        Check(b.Acquires == 0 && b.Moves == 0 && !r.Finished, "combat and rescue wait without blocking control");
        b.Safe = true; r.Tick(61); r.Tick(61.9f); Check(b.Acquires == 0, "safety window restarts after danger");
        r.Tick(62); b.Safe = false; r.Tick(63); Check(b.Releases == 1 && !r.Finished, "danger during return releases immediately");
        b.Safe = true; b.Arrived = true; r.Tick(64); r.Tick(65);
        Check(b.Acquires == 2 && b.Releases == 2 && b.Restored, "safe recovery reacquires without losing continuation");
        b = new Back { Safe = false }; r = new(b, 0); r.Tick(120);
        Check(r.Finished && b.Restored && b.Moves == 0 && b.Acquires == 0, "bounded danger timeout never forces teleport");
        b = new Back { Valid = false }; r = new(b, 0); r.Tick(0);
        Check(r.Finished && !b.Restored && b.Acquires == 0, "new floor or bot identity discards stale callbacks");
        b = new Back { Retained = false }; r = new(b, 0); r.Tick(0);
        Check(r.Finished && b.Restored && b.Moves == 0 && b.Acquires == 0, "changed player instruction cancels old return before teleport");
        b = new Back { CanMove = false }; r = new(b, 0); r.Tick(0); r.Tick(1);
        Check(b.Moves == 1 && b.Releases == 1 && b.Restored, "blocked route falls back to native task validation");
        b = new Back { Renewed = false }; r = new(b, 0); r.Tick(0); r.Tick(1);
        Check(b.Moves == 0 && b.Releases == 1 && b.Restored, "lost lease never moves");
        b = new Back { Permit = false }; r = new(b, 0); r.Tick(0); r.Tick(1); r.Tick(120);
        Check(b.Moves == 0 && b.Releases == 0 && b.Restored, "failed acquisition does not release another task lease");
        b = new Back(); r = new(b, 0); r.Tick(0); r.Tick(1); b.Moving = true;
        for (int t = 2; t < 90; t++) r.Tick(t);
        Check(b.Moves == 1 && !r.Finished, "accepted native walking is not restarted every two seconds");
        b.Arrived = true; r.Tick(90); Check(b.Restored && b.Releases == 1, "walking return can finish beyond the retry window");
        b = new Back(); r = new(b, 0); r.Tick(0); r.Tick(1); r.Tick(3); r.Tick(5); r.Tick(7);
        Check(r.Finished && b.Moves == 3 && b.Restored, "terminated travel retries have a finite limit");
        b = new Back { ThrowRelease = true }; r = new(b, 0); r.Tick(0); r.Tick(1);
        try { r.Abort(true, "cancel"); } catch { }
        Check(r.Finished && b.Finishes == 1 && b.Restored, "release exception cannot prevent saved task restoration");
        int restored = 0; var hold = new BorrowHold(() => true, () => restored++);
        hold.Restore(); hold.Restore(); Check(restored == 1, "callback only executes once");
        hold = new(() => true, () => restored++); hold.Discard(); hold.Restore(); Check(restored == 1, "discarded callback cannot be revived");
        var s = new ItemSession(); var task = new ItemTaskRunner(ItemTaskKind.Pickup, s, 0, false);
        task.Tick(0); Check(task.Stage == ItemTaskStage.Interacting, "real item runner reached native atomic interaction");
        task.Pause(true); int revision = task.PauseRevision;
        hold = new(() => task.Finished || task.ManuallySuspended,
            () => { if (!task.Finished && task.PauseRevision == revision) task.Pause(false); }, () => task.PauseRevision == revision);
        task.Tick(.3f); Check(task.Stage == ItemTaskStage.Verifying && !hold.Yielded && s.Applies == 1, "borrow cannot cut an already submitted inventory transaction");
        task.Tick(.4f); Check(task.Finished && hold.Yielded && s.Amount == 7 && s.Applies == 1, "atomic completion preserves 4+3=7 without duplicate application");
        hold.Restore(); task.Tick(.5f); Check(s.Applies == 1, "completed original pickup is not restarted by restoration");
        s = new() { At = false }; task = new(ItemTaskKind.Pickup, s, 0, false); task.Tick(0); task.Pause(true); task.Tick(.1f);
        revision = task.PauseRevision;
        hold = new(() => task.ManuallySuspended, () => { if (task.PauseRevision == revision) task.Pause(false); }, () => task.PauseRevision == revision);
        Check(hold.Yielded && s.Suspends == 1 && s.Applies == 0, "non-atomic pickup yields lease and keeps target");
        hold.Restore(); s.At = true; task.Tick(2); task.Tick(2.3f); task.Tick(2.4f);
        Check(task.Finished && s.Amount == 7 && s.Applies == 1 && s.Acquires == 2, "resumed real item runner reacquires and applies exactly once");
        s = new(); task = new(ItemTaskKind.Pickup, s, 0); task.Pause(true); revision = task.PauseRevision;
        hold = new(() => true, () => { if (task.PauseRevision == revision) task.Pause(false); }, () => task.PauseRevision == revision);
        task.Pause(true); Check(!hold.Retained, "repeated explicit pause invalidates old restoration revision");
        hold.Restore(); Check(task.ManualPaused, "player's later pause is not undone by old callback");
        Console.WriteLine("PASS " + count + " borrow-return and real item transaction assertions; Unity movement not executed.");
    }
}
