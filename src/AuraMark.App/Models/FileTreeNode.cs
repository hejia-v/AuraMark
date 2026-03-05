using System.Collections.ObjectModel;

namespace AuraMark.App.Models;

public sealed class FileTreeNode
{
    public string Name { get; init; } = string.Empty;

    public string FullPath { get; init; } = string.Empty;

    public bool IsDirectory { get; init; }

    public ObservableCollection<FileTreeNode> Children { get; } = [];
}
