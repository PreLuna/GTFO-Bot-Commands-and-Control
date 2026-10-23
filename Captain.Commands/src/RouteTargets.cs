namespace PreLuna.Captain.Tasks
{
    internal static class RouteTargets
    {
        // Capture exact indexed identities; resolving never performs a scene search,
        // discovers objects, interacts with a target, or reserves a bot task.
        internal static RouteTarget Capture(CaptainMenuLeaf leaf, KnownSupplySnapshot supplies, KnownTerminalSnapshot terminals,
            KnownContainerSnapshot containers, KnownInteractionSnapshot interactions)
        {
            if (leaf.SupplyId != 0 && supplies?.Targets.TryGetValue(leaf.SupplyId, out var item) == true)
                return new(leaf.Title, () => KnownSupplyIndex.Current(leaf.SupplyId, item) && ItemRegion.TryResolve(item, out var node)
                    ? new(node, ItemInteractionGeometry.Point(item)) : null);
            if (leaf.TerminalId != 0 && terminals?.Targets.TryGetValue(leaf.TerminalId, out var terminal) == true)
                return new(leaf.Title, () => KnownTerminalIndex.Current(leaf.TerminalId, terminal) && KnownTerminalIndex.Resolve(terminal.Value, out var node)
                    ? new(node, terminal.Value.transform.position) : null);
            if (leaf.ContainerId != 0 && containers?.Targets.TryGetValue(leaf.ContainerId, out var container) == true)
                return new(leaf.Title, () => KnownContainerIndex.Current(leaf.ContainerId, container) && KnownContainerIndex.Resolve(container.Value, out var node)
                    ? new(node, container.Value.transform.position) : null);
            if (leaf.InteractionId != 0 && interactions?.Targets.TryGetValue(leaf.InteractionId, out var interaction) == true)
                return new(leaf.Title, () => KnownInteractionIndex.Current(leaf.InteractionId, interaction) && KnownInteractionIndex.Resolve(interaction.Candidate, out var node)
                    ? new(node, interaction.Candidate.Portal?.Position ?? interaction.Candidate.Value.transform.position, interaction.Candidate.Portal) : null);
            return null;
        }
    }
}
