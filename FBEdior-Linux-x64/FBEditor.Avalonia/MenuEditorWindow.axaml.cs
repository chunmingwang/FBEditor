using System.Collections.Generic;
using Avalonia.Controls;
using FBEditor.Core;

namespace FBEditor.Avalonia;

/// <summary>
/// Edits a W9FormDesign's menu structure (W9MenuItemInfo tree). Mutates the design's
/// MenuItems list in place; the form generator emits the menu bar from it.
/// </summary>
public partial class MenuEditorWindow : Window
{
    private readonly W9FormDesign _design;
    private readonly Dictionary<W9MenuItemInfo, List<W9MenuItemInfo>> _parentList = new();
    private W9MenuItemInfo? _selected;
    private bool _suppress;
    private int _seq;

    public MenuEditorWindow(W9FormDesign design)
    {
        InitializeComponent();
        _design = design;

        // Seed the enum counter past anything already present.
        _seq = CountItems(_design.MenuItems);

        BtnAddMenu.Click += (_, _) =>
        {
            _design.MenuItems.Add(new W9MenuItemInfo { IsTopLevel = true, Text = "Menu", EnumName = Gen("miMenu") });
            RebuildTree();
        };
        BtnAddItem.Click += (_, _) =>
        {
            if (_selected == null) return;
            _selected.Children.Add(new W9MenuItemInfo { Text = "Item", EnumName = Gen("miItem") });
            RebuildTree();
        };
        BtnAddSep.Click += (_, _) =>
        {
            if (_selected == null) return;
            _selected.Children.Add(new W9MenuItemInfo { IsSeparator = true, EnumName = Gen("miSep") });
            RebuildTree();
        };
        BtnRemove.Click += (_, _) =>
        {
            if (_selected != null && _parentList.TryGetValue(_selected, out var list))
            {
                list.Remove(_selected);
                _selected = null;
                RebuildTree();
                LoadFields();
            }
        };
        BtnDone.Click += (_, _) => Close();

        MenuTree.SelectionChanged += (_, _) =>
        {
            _selected = (MenuTree.SelectedItem as TreeViewItem)?.Tag as W9MenuItemInfo;
            LoadFields();
        };
        ItemText.TextChanged += (_, _) => { if (!_suppress && _selected != null) { _selected.Text = ItemText.Text ?? ""; RefreshSelectedHeader(); } };
        ItemEnum.TextChanged += (_, _) => { if (!_suppress && _selected != null) _selected.EnumName = ItemEnum.Text ?? ""; };

        RebuildTree();
        LoadFields();
    }

    private void RebuildTree()
    {
        MenuTree.Items.Clear();
        _parentList.Clear();
        foreach (var top in _design.MenuItems)
            MenuTree.Items.Add(BuildNode(top, _design.MenuItems));
    }

    private TreeViewItem BuildNode(W9MenuItemInfo info, List<W9MenuItemInfo> containingList)
    {
        _parentList[info] = containingList;
        var node = new TreeViewItem { Header = HeaderFor(info), Tag = info, IsExpanded = true };
        foreach (var child in info.Children)
            node.Items.Add(BuildNode(child, info.Children));
        return node;
    }

    private static string HeaderFor(W9MenuItemInfo info) =>
        info.IsSeparator ? "──────────" : $"{info.Text}   ({info.EnumName})";

    private void RefreshSelectedHeader()
    {
        if (_selected != null && MenuTree.SelectedItem is TreeViewItem ti)
            ti.Header = HeaderFor(_selected);
    }

    private void LoadFields()
    {
        _suppress = true;
        if (_selected == null || _selected.IsSeparator)
        {
            ItemText.Text = "";
            ItemEnum.Text = _selected?.EnumName ?? "";
            ItemText.IsEnabled = _selected != null && !_selected.IsSeparator;
        }
        else
        {
            ItemText.IsEnabled = true;
            ItemText.Text = _selected.Text;
            ItemEnum.Text = _selected.EnumName;
        }
        _suppress = false;
    }

    private int CountItems(List<W9MenuItemInfo> items)
    {
        int n = 0;
        foreach (var i in items) n += 1 + CountItems(i.Children);
        return n;
    }

    private string Gen(string prefix) => prefix + (++_seq);
}
