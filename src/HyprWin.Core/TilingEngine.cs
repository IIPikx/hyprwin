using HyprWin.Core.Interop;

namespace HyprWin.Core;

/// <summary>
/// Represents a node in a Binary Space Partition tree for window tiling.
/// Each node is either a Split (with two children) or a Leaf (containing a window handle).
/// </summary>
public class BspNode
{
    public enum SplitDirection { Horizontal, Vertical }

    /// <summary>If true, this is a split node with two children. If false, it's a leaf with a window.</summary>
    public bool IsSplit { get; set; }

    /// <summary>Split direction (only valid if IsSplit).</summary>
    public SplitDirection Direction { get; set; }

    /// <summary>Split ratio 0.0-1.0 (only valid if IsSplit). 0.5 = equal split.</summary>
    public double Ratio { get; set; } = 0.5;

    /// <summary>First child (left or top, only valid if IsSplit).</summary>
    public BspNode? First { get; set; }

    /// <summary>Second child (right or bottom, only valid if IsSplit).</summary>
    public BspNode? Second { get; set; }

    /// <summary>Parent reference for upward traversal.</summary>
    public BspNode? Parent { get; set; }

    /// <summary>Window handle (only valid if not IsSplit).</summary>
    public IntPtr WindowHandle { get; set; }

    /// <summary>Computed rectangle for this node (set during layout calculation).</summary>
    public NativeMethods.RECT ComputedRect { get; set; }

    /// <summary>Create a leaf node.</summary>
    public static BspNode Leaf(IntPtr hwnd) => new() { IsSplit = false, WindowHandle = hwnd };

    /// <summary>Create a split node.</summary>
    public static BspNode Split(SplitDirection dir, BspNode first, BspNode second, double ratio = 0.5)
    {
        var node = new BspNode
        {
            IsSplit = true,
            Direction = dir,
            Ratio = ratio,
            First = first,
            Second = second,
        };
        first.Parent = node;
        second.Parent = node;
        return node;
    }

    /// <summary>Find the leaf node containing a given window handle.</summary>
    public BspNode? FindLeaf(IntPtr hwnd)
    {
        if (!IsSplit)
            return WindowHandle == hwnd ? this : null;

        return First?.FindLeaf(hwnd) ?? Second?.FindLeaf(hwnd);
    }

    /// <summary>Get all leaf nodes (windows) in order.</summary>
    public IEnumerable<BspNode> GetLeaves()
    {
        if (!IsSplit)
        {
            yield return this;
            yield break;
        }
        if (First != null)
            foreach (var leaf in First.GetLeaves())
                yield return leaf;
        if (Second != null)
            foreach (var leaf in Second.GetLeaves())
                yield return leaf;
    }

    /// <summary>Count of leaf (window) nodes.</summary>
    public int LeafCount()
    {
        if (!IsSplit) return 1;
        return (First?.LeafCount() ?? 0) + (Second?.LeafCount() ?? 0);
    }
}

/// <summary>
/// BSP (Binary Space Partitioning) tiling engine.
/// Manages BSP trees per workspace and computes tiled window positions with gaps.
/// </summary>
public sealed class TilingEngine
{
    private readonly MonitorManager _monitorManager;
    private readonly AnimationEngine _animationEngine;
    private int _gapsInner;
    private int _gapsOuter;

    public TilingEngine(MonitorManager monitorManager, AnimationEngine animationEngine)
    {
        _monitorManager = monitorManager;
        _animationEngine = animationEngine;
    }

    public void UpdateLayout(int gapsInner, int gapsOuter)
    {
        _gapsInner = gapsInner;
        _gapsOuter = gapsOuter;
    }

    /// <summary>
    /// Add a window to a workspace's BSP tree.
    /// New windows split the leaf with the best aspect-ratio match (or largest free space).
    /// </summary>
    public void AddWindow(Workspace workspace, IntPtr hwnd)
    {
        if (workspace.LayoutRoot == null)
        {
            workspace.LayoutRoot = BspNode.Leaf(hwnd);
            return;
        }

        // Find the best leaf to split: prefer the focused window's leaf,
        // fall back to the leaf with the largest ComputedRect area.
        var focusedHwnd = workspace.FocusedWindow?.Handle ?? IntPtr.Zero;
        BspNode? targetLeaf = focusedHwnd != IntPtr.Zero
            ? workspace.LayoutRoot.FindLeaf(focusedHwnd)
            : null;

        if (targetLeaf == null)
        {
            // Pick the leaf with the largest area — avoid LINQ OrderByDescending allocation
            long bestArea = -1;
            foreach (var leaf in workspace.LayoutRoot.GetLeaves())
            {
                long area = (long)leaf.ComputedRect.Width * leaf.ComputedRect.Height;
                if (area > bestArea)
                {
                    bestArea = area;
                    targetLeaf = leaf;
                }
            }
        }

        if (targetLeaf == null)
        {
            workspace.LayoutRoot = BspNode.Leaf(hwnd);
            return;
        }

        // Choose split direction based on the leaf's computed rect aspect ratio.
        // Fall back to depth-based alternation if no ComputedRect is set yet.
        BspNode.SplitDirection direction;
        var cr = targetLeaf.ComputedRect;
        if (cr.Width > 0 && cr.Height > 0)
            direction = cr.Width >= cr.Height
                ? BspNode.SplitDirection.Horizontal
                : BspNode.SplitDirection.Vertical;
        else
        {
            int depth = GetNodeDepth(targetLeaf);
            direction = depth % 2 == 0
                ? BspNode.SplitDirection.Horizontal
                : BspNode.SplitDirection.Vertical;
        }

        var existingLeaf = BspNode.Leaf(targetLeaf.WindowHandle);
        var newLeaf      = BspNode.Leaf(hwnd);
        var splitNode    = BspNode.Split(direction, existingLeaf, newLeaf);

        if (targetLeaf.Parent == null)
        {
            workspace.LayoutRoot = splitNode;
        }
        else
        {
            var parent = targetLeaf.Parent;
            if (parent.First == targetLeaf)
                parent.First = splitNode;
            else
                parent.Second = splitNode;
            splitNode.Parent = parent;
        }
    }

    /// <summary>
    /// Get the depth of a node in the BSP tree (0 = root).
    /// </summary>
    private static int GetNodeDepth(BspNode node)
    {
        int depth = 0;
        var current = node;
        while (current.Parent != null)
        {
            depth++;
            current = current.Parent;
        }
        return depth;
    }

    /// <summary>
    /// Remove a window from a workspace's BSP tree.
    /// The sibling takes over the parent's space.
    /// </summary>
    public void RemoveWindow(Workspace workspace, IntPtr hwnd)
    {
        if (workspace.LayoutRoot == null) return;

        var leaf = workspace.LayoutRoot.FindLeaf(hwnd);
        if (leaf == null) return;

        if (leaf.Parent == null)
        {
            // This was the only window (root leaf)
            workspace.LayoutRoot = null;
            return;
        }

        var parent = leaf.Parent;
        var sibling = parent.First == leaf ? parent.Second : parent.First;

        if (sibling == null) return;

        // Replace parent with sibling
        if (parent.Parent == null)
        {
            // Parent was root
            workspace.LayoutRoot = sibling;
            sibling.Parent = null;
        }
        else
        {
            var grandparent = parent.Parent;
            if (grandparent.First == parent)
                grandparent.First = sibling;
            else
                grandparent.Second = sibling;
            sibling.Parent = grandparent;
        }
    }

    /// <summary>
    /// Calculate the layout rectangles for all windows in a workspace,
    /// then apply them via SetWindowPos (with optional animation).
    /// </summary>
    public void TileWorkspace(Workspace workspace, bool animate = true)
    {
        if (workspace.LayoutRoot == null) return;

        var mon = _monitorManager.GetByIndex(workspace.MonitorIndex);
        if (mon == null) return;

        // ── Validate tree consistency ────────────────────────────────────────
        // If any leaf references a window that should no longer be tiled
        // (gone, minimized, fullscreen, floating), sync the tree incrementally
        // (Hyprland dwindle: preserve split ratios/directions).
        // Build a lookup dictionary once instead of O(N*M) FirstOrDefault per leaf.
        Dictionary<IntPtr, ManagedWindow>? windowLookup = null;
        bool needSync = false;
        foreach (var leaf in workspace.LayoutRoot.GetLeaves())
        {
            windowLookup ??= BuildWindowLookup(workspace);
            windowLookup.TryGetValue(leaf.WindowHandle, out var w);
            if (w == null || w.IsFloating || w.IsFullscreen
                || !NativeMethods.IsWindow(w.Handle)
                || w.IsMinimized
                || NativeMethods.IsIconic(w.Handle))
            {
                needSync = true;
                break;
            }
        }
        if (needSync)
        {
            SyncTree(workspace);
            if (workspace.LayoutRoot == null) return; // nothing to tile
        }

        var workArea    = mon.EffectiveWorkArea;
        var monBounds   = mon.Bounds;   // physical monitor bounds — used for clamping

        // Apply outer gaps
        var tilingArea = new NativeMethods.RECT(
            workArea.Left   + _gapsOuter,
            workArea.Top    + _gapsOuter,
            workArea.Right  - _gapsOuter,
            workArea.Bottom - _gapsOuter);

        // Calculate layout
        CalculateLayout(workspace.LayoutRoot, tilingArea);

        // Build lookup once for O(1) hit per leaf
        windowLookup ??= BuildWindowLookup(workspace);

        // Collect windows to position, then apply as a batch via DeferWindowPos
        // to reduce DWM recomposition passes (significant perf win with many windows).
        var leaves = workspace.LayoutRoot.GetLeaves().ToList();
        var pendingMoves = new List<(IntPtr hwnd, NativeMethods.RECT target, NativeMethods.RECT adjusted)>();

        foreach (var leaf in leaves)
        {
            windowLookup.TryGetValue(leaf.WindowHandle, out var window);
            if (window == null || window.IsFloating || window.IsFullscreen) continue;
            if (window.IsMinimized) continue;

            var targetRect = leaf.ComputedRect;

            if (NativeMethods.IsZoomed(window.Handle))
                NativeMethods.ShowWindow(window.Handle, NativeMethods.SW_RESTORE);

            var adjustedRect = ClampToMonitor(
                AdjustForDwmBorders(window.Handle, targetRect), monBounds);

            if (animate && _animationEngine.IsEnabled)
            {
                NativeMethods.GetWindowRect(window.Handle, out var currentRect);
                _animationEngine.AnimateMove(window.Handle, currentRect, adjustedRect);
            }
            else
            {
                pendingMoves.Add((window.Handle, targetRect, adjustedRect));
            }

            window.Bounds = targetRect;
        }

        // Batch all non-animated moves into a single DeferWindowPos call
        if (pendingMoves.Count > 0)
        {
            var hdwp = NativeMethods.BeginDeferWindowPos(pendingMoves.Count);
            if (hdwp != IntPtr.Zero)
            {
                foreach (var (hwnd, _, adj) in pendingMoves)
                {
                    hdwp = NativeMethods.DeferWindowPos(hdwp, hwnd, IntPtr.Zero,
                        adj.Left, adj.Top, adj.Width, adj.Height,
                        NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE
                        | NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_NOCOPYBITS
                        | NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_NOSENDCHANGING);
                    if (hdwp == IntPtr.Zero) break;
                }
                if (hdwp != IntPtr.Zero)
                    NativeMethods.EndDeferWindowPos(hdwp);
                else
                {
                    foreach (var (hwnd, _, adj) in pendingMoves)
                        ApplyWindowPosition(hwnd, adj);
                }
            }
            else
            {
                foreach (var (hwnd, _, adj) in pendingMoves)
                    ApplyWindowPosition(hwnd, adj);
            }
        }
    }

    /// <summary>
    /// Recursively calculate layout rectangles for the BSP tree.
    /// </summary>
    private void CalculateLayout(BspNode node, NativeMethods.RECT area)
    {
        node.ComputedRect = area;

        if (!node.IsSplit) return;
        if (node.First == null || node.Second == null) return;

        int halfGap = _gapsInner / 2;

        if (node.Direction == BspNode.SplitDirection.Horizontal)
        {
            // Split left/right
            int splitX = area.Left + (int)(area.Width * node.Ratio);
            var firstRect = new NativeMethods.RECT(area.Left, area.Top, splitX - halfGap, area.Bottom);
            var secondRect = new NativeMethods.RECT(splitX + halfGap, area.Top, area.Right, area.Bottom);
            CalculateLayout(node.First, firstRect);
            CalculateLayout(node.Second, secondRect);
        }
        else
        {
            // Split top/bottom
            int splitY = area.Top + (int)(area.Height * node.Ratio);
            var firstRect = new NativeMethods.RECT(area.Left, area.Top, area.Right, splitY - halfGap);
            var secondRect = new NativeMethods.RECT(area.Left, splitY + halfGap, area.Right, area.Bottom);
            CalculateLayout(node.First, firstRect);
            CalculateLayout(node.Second, secondRect);
        }
    }

    /// <summary>
    /// Apply a window position directly (no animation).
    /// Uses SWP_ASYNCWINDOWPOS (Komorebi pattern) to avoid blocking when
    /// the target window's thread is not responding.
    /// Uses SWP_FRAMECHANGED so the frame is recalculated after style changes.
    /// </summary>
    public static void ApplyWindowPosition(IntPtr hwnd, NativeMethods.RECT rect)
    {
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero,
            rect.Left, rect.Top, rect.Width, rect.Height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE
            | NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_ASYNCWINDOWPOS
            | NativeMethods.SWP_NOCOPYBITS | NativeMethods.SWP_FRAMECHANGED
            | NativeMethods.SWP_NOSENDCHANGING);
    }

    /// <summary>
    /// Clamp a rect so it does not exceed the physical monitor bounds.
    /// This prevents DWM-border-adjusted windows from bleeding onto adjacent monitors.
    /// A 1px inset from the hard boundary is allowed to avoid hairline artefacts.
    /// </summary>
    private static NativeMethods.RECT ClampToMonitor(
        NativeMethods.RECT rect, NativeMethods.RECT monBounds)
    {
        return new NativeMethods.RECT(
            Math.Max(rect.Left,   monBounds.Left   - 8),
            Math.Max(rect.Top,    monBounds.Top    - 8),
            Math.Min(rect.Right,  monBounds.Right  + 8),
            Math.Min(rect.Bottom, monBounds.Bottom + 8));
    }

    /// <summary>
    /// Adjust target rect to compensate for DWM invisible borders (~7px on each side).
    /// </summary>
    private static NativeMethods.RECT AdjustForDwmBorders(IntPtr hwnd, NativeMethods.RECT target)
    {
        try
        {
            if (!NativeMethods.GetWindowRect(hwnd, out var windowRect)) return target;
            var extendedRect = NativeMethods.GetExtendedFrameBounds(hwnd);

            // If DWM returns same rect, no adjustment needed
            if (extendedRect.Width == 0 && extendedRect.Height == 0)
                return target;

            int borderLeft = extendedRect.Left - windowRect.Left;
            int borderTop = extendedRect.Top - windowRect.Top;
            int borderRight = windowRect.Right - extendedRect.Right;
            int borderBottom = windowRect.Bottom - extendedRect.Bottom;

            return new NativeMethods.RECT(
                target.Left - borderLeft,
                target.Top - borderTop,
                target.Right + borderRight,
                target.Bottom + borderBottom);
        }
        catch
        {
            return target;
        }
    }

    /// <summary>
    /// Swap two windows in the BSP tree (for move operations).
    /// </summary>
    public void SwapWindows(Workspace workspace, IntPtr hwnd1, IntPtr hwnd2)
    {
        if (workspace.LayoutRoot == null) return;

        var leaf1 = workspace.LayoutRoot.FindLeaf(hwnd1);
        var leaf2 = workspace.LayoutRoot.FindLeaf(hwnd2);

        if (leaf1 != null && leaf2 != null)
        {
            // Simply swap the window handles
            (leaf1.WindowHandle, leaf2.WindowHandle) = (leaf2.WindowHandle, leaf1.WindowHandle);
        }
    }

    /// <summary>
    /// Set the split direction of the focused window's immediate parent BSP node.
    /// Only the single split node that contains the focused window is changed —
    /// all other splits in the workspace remain untouched.
    /// </summary>
    public bool RotateSplitToDirection(Workspace workspace, IntPtr hwnd, BspNode.SplitDirection direction)
    {
        if (workspace.LayoutRoot == null) return false;

        var leaf = workspace.LayoutRoot.FindLeaf(hwnd);
        if (leaf?.Parent == null) return false; // root leaf (only window) — nothing to rotate

        leaf.Parent.Direction = direction;
        return true;
    }

    /// <summary>
    /// Mirror (flip) the BSP tree along the horizontal axis.
    /// Swaps First/Second children of every Vertical split node,
    /// effectively reversing the left-right order of all windows.
    /// </summary>
    public void MirrorHorizontal(Workspace workspace)
    {
        if (workspace.LayoutRoot == null) return;
        MirrorNode(workspace.LayoutRoot, BspNode.SplitDirection.Vertical);
    }

    /// <summary>
    /// Mirror (flip) the BSP tree along the vertical axis.
    /// Swaps First/Second children of every Horizontal split node,
    /// effectively reversing the top-bottom order of all windows.
    /// </summary>
    public void MirrorVertical(Workspace workspace)
    {
        if (workspace.LayoutRoot == null) return;
        MirrorNode(workspace.LayoutRoot, BspNode.SplitDirection.Horizontal);
    }

    /// <summary>
    /// Recursively swap First/Second children for all split nodes matching the given direction.
    /// </summary>
    private static void MirrorNode(BspNode node, BspNode.SplitDirection targetDirection)
    {
        if (!node.IsSplit) return;

        // Recurse into children first
        if (node.First != null) MirrorNode(node.First, targetDirection);
        if (node.Second != null) MirrorNode(node.Second, targetDirection);

        // Swap children if this split matches the target direction
        if (node.Direction == targetDirection)
        {
            (node.First, node.Second) = (node.Second, node.First);
            node.Ratio = 1.0 - node.Ratio; // Invert ratio to preserve sizes
        }
    }

    /// <summary>
    /// Resize the focused window in the given direction.
    /// Finds the nearest ancestor BSP split on the key's axis and moves it in the pressed direction:
    ///   RIGHT/DOWN → ratio += step (split moves right/down)
    ///   LEFT/UP    → ratio -= step (split moves left/up)
    /// Because the same split is used regardless of which side of it the window is on,
    /// this supports both growing AND shrinking:
    ///   FIRST child  + RIGHT → grow   |  FIRST child  + LEFT → shrink
    ///   SECOND child + LEFT  → grow   |  SECOND child + RIGHT → shrink
    /// </summary>
    public bool ResizeInDirection(Workspace workspace, IntPtr hwnd, int dx, int dy, double step = 0.035)
    {
        if (workspace.LayoutRoot == null) return false;

        var leaf = workspace.LayoutRoot.FindLeaf(hwnd);
        if (leaf == null) return false;

        bool isHorizontal = dx != 0;
        // delta always moves the split in the key direction: RIGHT/DOWN = +, LEFT/UP = -
        double delta = (dx != 0 ? dx : dy) * step;

        // Walk up the tree to find the nearest ancestor split on the key axis,
        // then move that split by delta — nearest inner split is preferred so that
        // resize affects the most immediately adjacent boundary.
        var current = leaf;
        while (current.Parent != null)
        {
            var parent = current.Parent;
            if (parent.First == null || parent.Second == null) { current = parent; continue; }

            bool axisMatches = isHorizontal
                ? parent.Direction == BspNode.SplitDirection.Horizontal
                : parent.Direction == BspNode.SplitDirection.Vertical;

            if (axisMatches)
            {
                double newRatio = Math.Clamp(parent.Ratio + delta, 0.05, 0.95);
                if (Math.Abs(newRatio - parent.Ratio) < 0.001) return false; // already at limit
                parent.Ratio = newRatio;
                return true;
            }

            current = parent;
        }

        return false; // no split on this axis found
    }

    /// <summary>
    /// Build a handle→window dictionary for O(1) lookups during tiling.
    /// </summary>
    private static Dictionary<IntPtr, ManagedWindow> BuildWindowLookup(Workspace workspace)
    {
        var dict = new Dictionary<IntPtr, ManagedWindow>(workspace.Windows.Count);
        foreach (var w in workspace.Windows)
            dict[w.Handle] = w;
        return dict;
    }

    /// <summary>
    /// Sync the BSP tree incrementally with the workspace's current window list.
    /// This is the Hyprland "dwindle" approach: the tree evolves organically.
    /// - New windows split the focused leaf (or largest available).
    /// - Removed windows' siblings absorb the parent space.
    /// - Split directions and ratios are PRESERVED across changes.
    /// Unlike RebuildTree, this never creates a fresh balanced tree — it mutates
    /// the existing one. Use this for all normal operations (window add/remove/
    /// minimize/restore). RebuildTree remains available for initial startup from
    /// scratch when there is no existing tree state to preserve.
    /// </summary>
    public void SyncTree(Workspace workspace)
    {
        // Promote IsMinimized via live IsIconic()  (same rule as RebuildTree:
        // ONLY promote to true, never reset to false — handled by MinimizeEnd hook).
        foreach (var w in workspace.Windows)
            if (NativeMethods.IsIconic(w.Handle))
                w.IsMinimized = true;

        // Collect valid handles without LINQ allocations
        var validHandles = new List<IntPtr>(workspace.Windows.Count);
        foreach (var w in workspace.Windows)
        {
            if (!w.IsFloating && !w.IsFullscreen && !w.IsMinimized
                && NativeMethods.IsWindow(w.Handle))
                validHandles.Add(w.Handle);
        }

        if (validHandles.Count == 0)
        {
            workspace.LayoutRoot = null;
            return;
        }

        var area = GetMonitorArea(workspace.MonitorIndex);
        var tilingArea = new NativeMethods.RECT(
            area.Left + _gapsOuter, area.Top + _gapsOuter,
            area.Right - _gapsOuter, area.Bottom - _gapsOuter);

        // ── Case 1: No tree yet — build incrementally (dwindle spiral) ──
        if (workspace.LayoutRoot == null)
        {
            workspace.LayoutRoot = BspNode.Leaf(validHandles[0]);
            CalculateLayout(workspace.LayoutRoot, tilingArea);

            for (int i = 1; i < validHandles.Count; i++)
            {
                AddWindow(workspace, validHandles[i]);
                CalculateLayout(workspace.LayoutRoot, tilingArea);
            }
            return;
        }

        // ── Case 2: Existing tree — remove stale, add new ──
        var validSet = validHandles.ToHashSet();
        bool changed = false;

        // Remove leaves that are no longer valid
        foreach (var leaf in workspace.LayoutRoot.GetLeaves().ToList())
        {
            if (!validSet.Contains(leaf.WindowHandle))
            {
                RemoveWindow(workspace, leaf.WindowHandle);
                changed = true;
                if (workspace.LayoutRoot == null) break;
            }
        }

        // If tree was completely emptied, rebuild incrementally
        if (workspace.LayoutRoot == null)
        {
            workspace.LayoutRoot = BspNode.Leaf(validHandles[0]);
            CalculateLayout(workspace.LayoutRoot, tilingArea);
            for (int i = 1; i < validHandles.Count; i++)
            {
                AddWindow(workspace, validHandles[i]);
                CalculateLayout(workspace.LayoutRoot, tilingArea);
            }
            return;
        }

        // Add windows that aren't in the tree yet
        var inTree = new HashSet<IntPtr>();
        foreach (var leaf in workspace.LayoutRoot.GetLeaves())
            inTree.Add(leaf.WindowHandle);

        foreach (var h in validHandles)
        {
            if (!inTree.Contains(h))
            {
                // Pre-compute rects so AddWindow can determine split direction from aspect ratio
                if (changed || inTree.Count == 0)
                    CalculateLayout(workspace.LayoutRoot, tilingArea);
                AddWindow(workspace, h);
                changed = true;
                changed = true;
            }
        }
    }

    /// <summary>
    /// Build a fresh BSP tree for a workspace from its window list.
    /// Uses a balanced recursive split so that N windows produce a near-grid layout
    /// rather than a degenerate right-leaning chain.
    /// NOTE: This is only for initial startup. For normal operation, use SyncTree
    /// which preserves the existing tree structure (Hyprland dwindle behavior).
    /// </summary>
    public void RebuildTree(Workspace workspace)
    {
        // Promote IsMinimized to true via live IsIconic() — but NEVER reset it to false here.
        // EVENT_SYSTEM_MINIMIZESTART fires BEFORE the window becomes iconic (the animation is
        // still in progress), so IsIconic() returns false at that point. We already set
        // w.IsMinimized = true in the MinimizeStart hook callback; clearing it here with the
        // live result would race against the animation and include the minimizing window in
        // the tree, leaving a blank spot once the animation finishes.
        // IsMinimized is only reset to false by the MinimizeEnd hook callback.
        foreach (var w in workspace.Windows)
            if (NativeMethods.IsIconic(w.Handle))
                w.IsMinimized = true;

        var handles = new List<IntPtr>(workspace.Windows.Count);
        foreach (var w in workspace.Windows)
        {
            if (!w.IsFloating && !w.IsFullscreen && !w.IsMinimized
                && NativeMethods.IsWindow(w.Handle))
                handles.Add(w.Handle);
        }

        workspace.LayoutRoot = handles.Count == 0
            ? null
            : BuildBalancedSubtree(handles, GetMonitorArea(workspace.MonitorIndex));
    }

    private NativeMethods.RECT GetMonitorArea(int monitorIndex)
    {
        var mon = _monitorManager.GetByIndex(monitorIndex);
        return mon?.EffectiveWorkArea ?? new NativeMethods.RECT(0, 0, 1920, 1080);
    }

    /// <summary>
    /// Recursively build a balanced BSP subtree for a list of window handles,
    /// choosing split direction based on the segment's aspect ratio.
    /// </summary>
    private static BspNode BuildBalancedSubtree(
        List<IntPtr> handles, NativeMethods.RECT area)
    {
        if (handles.Count == 1)
            return BspNode.Leaf(handles[0]);

        // Choose split direction that keeps cells closest to square
        var dir = area.Width >= area.Height
            ? BspNode.SplitDirection.Horizontal
            : BspNode.SplitDirection.Vertical;

        int mid = handles.Count / 2;
        var firstHandles  = handles.GetRange(0, mid);
        var secondHandles = handles.GetRange(mid, handles.Count - mid);

        NativeMethods.RECT firstArea, secondArea;
        if (dir == BspNode.SplitDirection.Horizontal)
        {
            int splitX = area.Left + area.Width / 2;
            firstArea  = new NativeMethods.RECT(area.Left, area.Top, splitX, area.Bottom);
            secondArea = new NativeMethods.RECT(splitX,    area.Top, area.Right, area.Bottom);
        }
        else
        {
            int splitY = area.Top + area.Height / 2;
            firstArea  = new NativeMethods.RECT(area.Left, area.Top,    area.Right, splitY);
            secondArea = new NativeMethods.RECT(area.Left, splitY, area.Right, area.Bottom);
        }

        var firstNode  = BuildBalancedSubtree(firstHandles,  firstArea);
        var secondNode = BuildBalancedSubtree(secondHandles, secondArea);
        return BspNode.Split(dir, firstNode, secondNode);
    }}