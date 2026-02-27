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
    /// New windows split the focused window's space (alternating H/V based on depth).
    /// </summary>
    public void AddWindow(Workspace workspace, IntPtr hwnd)
    {
        if (workspace.LayoutRoot == null)
        {
            // First window — make it the root leaf
            workspace.LayoutRoot = BspNode.Leaf(hwnd);
            return;
        }

        // Find where to insert: at the focused window, or the last leaf
        var focusedHwnd = workspace.FocusedWindow?.Handle ?? IntPtr.Zero;
        var targetLeaf = focusedHwnd != IntPtr.Zero
            ? workspace.LayoutRoot.FindLeaf(focusedHwnd)
            : null;
        targetLeaf ??= workspace.LayoutRoot.GetLeaves().LastOrDefault();

        if (targetLeaf == null)
        {
            workspace.LayoutRoot = BspNode.Leaf(hwnd);
            return;
        }

        // Determine split direction based on depth in tree (alternating H/V)
        // This ensures proper alternation regardless of ComputedRect state
        int depth = GetNodeDepth(targetLeaf);
        var direction = (depth % 2 == 0)
            ? BspNode.SplitDirection.Horizontal  // Even depth → side by side
            : BspNode.SplitDirection.Vertical;   // Odd depth → stacked

        // Create the new split
        var existingLeaf = BspNode.Leaf(targetLeaf.WindowHandle);
        var newLeaf = BspNode.Leaf(hwnd);
        var splitNode = BspNode.Split(direction, existingLeaf, newLeaf);

        // Replace the target leaf in the tree
        if (targetLeaf.Parent == null)
        {
            // Target was root
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

        var workArea = mon.EffectiveWorkArea;

        // Apply outer gaps
        var tilingArea = new NativeMethods.RECT(
            workArea.Left + _gapsOuter,
            workArea.Top + _gapsOuter,
            workArea.Right - _gapsOuter,
            workArea.Bottom - _gapsOuter);

        // Calculate layout
        CalculateLayout(workspace.LayoutRoot, tilingArea);

        // Apply positions
        foreach (var leaf in workspace.LayoutRoot.GetLeaves())
        {
            var window = workspace.Windows.FirstOrDefault(w => w.Handle == leaf.WindowHandle);
            if (window == null || window.IsFloating || window.IsFullscreen || window.IsMinimized)
                continue;

            var targetRect = leaf.ComputedRect;

            // Restore (un-maximize / un-minimize) BEFORE repositioning.
            // If SW_RESTORE is called after SetWindowPos, Windows overrides our
            // position with the window's own "restore" coordinates.
            if (NativeMethods.IsZoomed(window.Handle) || NativeMethods.IsIconic(window.Handle))
            {
                NativeMethods.ShowWindow(window.Handle, NativeMethods.SW_RESTORE);
            }

            // Account for DWM invisible borders (extend window beyond target to compensate)
            var adjustedRect = AdjustForDwmBorders(window.Handle, targetRect);

            if (animate && _animationEngine.IsEnabled)
            {
                NativeMethods.GetWindowRect(window.Handle, out var currentRect);
                _animationEngine.AnimateMove(window.Handle, currentRect, adjustedRect);
            }
            else
            {
                ApplyWindowPosition(window.Handle, adjustedRect);
            }

            window.Bounds = targetRect;
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
    /// </summary>
    public static void ApplyWindowPosition(IntPtr hwnd, NativeMethods.RECT rect)
    {
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero,
            rect.Left, rect.Top, rect.Width, rect.Height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
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
    /// Resize the focused window in the given direction — Hyprland semantics:
    ///   dx=+1 (RIGHT) → grow window rightward  → find the nearest Horizontal split where
    ///                    the window is the FIRST (left) child and move the split line right.
    ///   dx=-1 (LEFT)  → grow window leftward   → find the nearest Horizontal split where
    ///                    the window is the SECOND (right) child and move the split line left.
    ///   dy=+1 (DOWN)  → grow window downward   → Vertical split, window is FIRST (top) child.
    ///   dy=-1 (UP)    → grow window upward      → Vertical split, window is SECOND (bottom) child.
    ///
    /// The tree is walked upward so that inner splits are preferred over outer ones.
    /// If no adjustable edge is found in the direction (e.g., window is at the screen edge),
    /// the method returns false and nothing changes.
    /// </summary>
    public bool ResizeInDirection(Workspace workspace, IntPtr hwnd, int dx, int dy, double step = 0.035)
    {
        if (workspace.LayoutRoot == null) return false;

        var leaf = workspace.LayoutRoot.FindLeaf(hwnd);
        if (leaf == null) return false;

        bool isHorizontal  = dx != 0;
        double dirSign     = dx != 0 ? dx : dy; // +1 or -1

        var current = leaf;
        while (current.Parent != null)
        {
            var parent = current.Parent;

            if (parent.First == null || parent.Second == null) { current = parent; continue; }

            bool splitMatchesAxis = isHorizontal
                ? parent.Direction == BspNode.SplitDirection.Horizontal
                : parent.Direction == BspNode.SplitDirection.Vertical;

            if (splitMatchesAxis)
            {
                // Bidirectional semantics:
                //   delta = dirSign * step moves the split line in the pressed direction.
                //   • FIRST child  + RIGHT → ratio grows   (window grows right)
                //   • FIRST child  + LEFT  → ratio shrinks (window shrinks from right) ← was broken
                //   • SECOND child + LEFT  → ratio shrinks (window grows left)
                //   • SECOND child + RIGHT → ratio grows   (window shrinks from left)
                double delta    = dirSign * step;
                double newRatio = Math.Clamp(parent.Ratio + delta, 0.10, 0.90);
                if (Math.Abs(newRatio - parent.Ratio) < 0.001) return false;

                parent.Ratio = newRatio;
                return true;
            }

            current = parent;
        }

        return false; // No adjustable split found on this axis
    }

    /// <summary>
    /// Build a fresh BSP tree for a workspace from its window list.
    /// </summary>
    public void RebuildTree(Workspace workspace)
    {
        workspace.LayoutRoot = null;
        foreach (var w in workspace.Windows.Where(w => !w.IsFloating && !w.IsFullscreen && !w.IsMinimized))
        {
            AddWindow(workspace, w.Handle);
        }
    }
}
