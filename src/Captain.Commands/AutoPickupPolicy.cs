namespace PreLuna.Captain.Tasks
{
    internal enum PickupDecision { Wait, Ask, Take, Decline, Defer }
    internal static class AutoPickupPolicy
    {
        public static PickupDecision Decide(bool visited, bool humanNear, bool available, bool unsafeNow,
            bool ask, bool pending, bool inputAllowed, int answer, bool expired, bool shown = true)
        {
            if (!visited) return PickupDecision.Wait;
            // Answers belong to a displayed question, not to the availability of its executor.
            if (pending && inputAllowed && answer < 0) return PickupDecision.Decline;
            if (pending && expired && !shown) return PickupDecision.Defer;
            bool consent = pending && shown && (expired || inputAllowed && answer > 0);
            if (humanNear || !available) return pending && (expired || consent) ? PickupDecision.Defer : PickupDecision.Wait;
            if (unsafeNow || !ask) return PickupDecision.Take;
            if (!pending) return PickupDecision.Ask;
            return consent ? PickupDecision.Take : PickupDecision.Wait;
        }
    }
}
