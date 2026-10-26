using System;
using System.Collections.Generic;
using System.Linq;

namespace Gacha.Presentation
{
    public enum MobileAccessibilityRole { Text, Heading, Button }

    public sealed class MobileAccessibilityLayer
    {
        public string Id { get; set; }
        public string ParentId { get; set; }
        public bool IsModal { get; set; }
        public bool IsVisible { get; set; }
        public int Priority { get; set; }
        public long Order { get; set; }
    }

    public sealed class MobileAccessibilityNodeData
    {
        public string Id { get; set; }
        public string ScopeId { get; set; }
        public string Label { get; set; }
        public string Value { get; set; }
        public string Hint { get; set; }
        public MobileAccessibilityRole Role { get; set; }
        public bool CanActivate { get; set; }
        public bool IsSelected { get; set; }
        public bool IsLoading { get; set; }
    }

    // Pure projection and focus history. It neither owns platform focus nor performs business actions.
    public sealed class MobileAccessibilityModel
    {
        private readonly Dictionary<string, string> focusByScope = new Dictionary<string, string>(StringComparer.Ordinal);
        public string ActiveScreenId { get; private set; }
        public string ActiveScopeId { get; private set; }
        public string RequestedFocusId { get; private set; }
        public IReadOnlyList<MobileAccessibilityNodeData> Nodes { get; private set; } = Array.Empty<MobileAccessibilityNodeData>();

        public bool Update(IEnumerable<MobileAccessibilityLayer> layers, IEnumerable<MobileAccessibilityNodeData> nodes)
        {
            MobileAccessibilityLayer[] all = layers.ToArray();
            if (all.Select(layer => layer.Id).Distinct(StringComparer.Ordinal).Count() != all.Length)
                throw new ArgumentException("Accessibility scope IDs must be unique.", nameof(layers));
            var screen = all.Where(layer => !layer.IsModal && layer.IsVisible)
                .OrderByDescending(layer => layer.Priority).ThenByDescending(layer => layer.Order).FirstOrDefault();
            MobileAccessibilityLayer top = screen;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (top != null && visited.Add(top.Id))
            {
                var child = all.Where(layer => layer.IsModal && layer.IsVisible && layer.ParentId == top.Id)
                    .OrderByDescending(layer => layer.Order).FirstOrDefault();
                if (child == null)
                    break;
                top = child;
            }
            string nextScope = top?.Id;
            bool changed = nextScope != ActiveScopeId || screen?.Id != ActiveScreenId;
            ActiveScopeId = nextScope;
            ActiveScreenId = screen?.Id;
            Nodes = nodes.Where(node => node.ScopeId == nextScope && !string.IsNullOrWhiteSpace(node.Label)).ToArray();
            if (Nodes.Select(node => node.Id).Distinct(StringComparer.Ordinal).Count() != Nodes.Count)
                throw new ArgumentException("Accessibility node IDs must be unique inside their scope.", nameof(nodes));
            RequestedFocusId = null;
            if (changed)
            {
                if (nextScope != null && focusByScope.TryGetValue(nextScope, out string previous) &&
                    Nodes.Any(node => node.Id == previous))
                    RequestedFocusId = previous;
                else
                    RequestedFocusId = Nodes.FirstOrDefault()?.Id;
            }
            foreach (string removed in focusByScope.Keys.Where(key => all.All(layer => layer.Id != key)).ToArray())
                focusByScope.Remove(removed);
            return changed;
        }

        public void RememberFocus(string nodeId)
        {
            if (ActiveScopeId != null && Nodes.Any(node => node.Id == nodeId))
                focusByScope[ActiveScopeId] = nodeId;
        }

        public bool Contains(string nodeId) => Nodes.Any(node => node.Id == nodeId);
    }
}
