using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace AmazonScraper.Cli.Diagnostics;

public static class PathTreeRenderer
{
    public static string Render(
        AppPathCatalogResult result,
        bool verbose,
        bool ascii)
    {
        var builder = new StringBuilder();
        builder.AppendLine("AMZ CLI paths");
        builder.AppendLine($"OS: {result.Os}");
        builder.AppendLine($"State: {result.StateRoot}");
        builder.AppendLine($"Installation: {result.InstallDir}");
        builder.AppendLine($"Generated: {result.GeneratedAt}");
        builder.AppendLine();

        foreach (var group in result.Groups)
        {
            RenderGroup(builder, group, verbose, ascii);
            builder.AppendLine();
        }

        builder.AppendLine(
            $"Summary: {result.Summary.Files} files, " +
            $"{result.Summary.Directories} directories, " +
            $"{result.Summary.External} external, " +
            $"{result.Summary.Missing} missing, " +
            $"{result.Summary.Stale} stale, " +
            $"{result.Summary.Empty} empty.");
        return builder.ToString();
    }

    public static string ToJson(AppPathCatalogResult result) =>
        JsonSerializer.Serialize(
            result,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });

    private static void RenderGroup(
        StringBuilder builder,
        AppPathGroup group,
        bool verbose,
        bool ascii)
    {
        var title = group.Title;
        if (!string.Equals(group.Status, "ok", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(group.Status))
        {
            title += $" [{group.Status}]";
        }
        builder.AppendLine(title);

        if (group.Entries.Count == 0)
        {
            builder.AppendLine("  (no files)");
            return;
        }

        if (string.Equals(group.Id, "external", StringComparison.Ordinal))
        {
            foreach (var entry in group.Entries
                         .OrderBy(item => item.Path, StringComparer.Ordinal))
            {
                builder.AppendLine(
                    $"  [{entry.Status}] {entry.Path}" +
                    (string.IsNullOrWhiteSpace(entry.Description)
                        ? string.Empty
                        : $"  {entry.Description}"));
            }
            return;
        }

        var root = BuildTree(group);
        var baseLabel = string.IsNullOrWhiteSpace(group.BasePath)
            ? "."
            : group.BasePath;
        builder.AppendLine(baseLabel);
        RenderChildren(builder, root, string.Empty, verbose, ascii);
    }

    private static PathNode BuildTree(AppPathGroup group)
    {
        var root = new PathNode
        {
            Name = group.BasePath,
            FullPath = group.BasePath,
            IsDirectory = true,
        };

        foreach (var entry in group.Entries)
        {
            var relative = RelativeTo(group.BasePath, entry.Path);
            if (relative is null)
            {
                root.Children[entry.Path] = new PathNode
                {
                    Name = entry.Path,
                    FullPath = entry.Path,
                    IsDirectory = entry.Kind == "directory",
                    Entry = entry,
                };
                continue;
            }

            var parts = relative.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                root.Entry = entry;
                root.IsDirectory = entry.Kind == "directory";
                continue;
            }

            var current = root;
            for (var index = 0; index < parts.Length; index++)
            {
                var part = parts[index];
                if (!current.Children.TryGetValue(part, out var node))
                {
                    var fullPath = Path.Combine(
                        current.FullPath,
                        part);
                    node = new PathNode
                    {
                        Name = part,
                        FullPath = fullPath,
                        IsDirectory = index < parts.Length - 1
                            || entry.Kind == "directory",
                    };
                    current.Children[part] = node;
                }

                if (index == parts.Length - 1)
                {
                    node.Entry = entry;
                    node.IsDirectory = entry.Kind == "directory";
                }
                else
                {
                    node.IsDirectory = true;
                }

                current = node;
            }
        }

        return root;
    }

    private static void RenderChildren(
        StringBuilder builder,
        PathNode node,
        string prefix,
        bool verbose,
        bool ascii)
    {
        var children = node.Children.Values
            .OrderByDescending(child => child.IsDirectory)
            .ThenBy(child => child.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        for (var index = 0; index < children.Length; index++)
        {
            var child = children[index];
            var last = index == children.Length - 1;
            var branch = ascii
                ? (last ? "\\-- " : "|-- ")
                : (last ? "└── " : "├── ");
            builder.Append(prefix);
            builder.Append(branch);
            builder.Append(child.Name);
            if (child.IsDirectory)
            {
                builder.Append('/');
            }

            AppendDetails(builder, child, verbose);
            builder.AppendLine();

            var childPrefix = prefix + (last
                ? (ascii ? "    " : "    ")
                : (ascii ? "|   " : "│   "));
            RenderChildren(builder, child, childPrefix, verbose, ascii);
        }
    }

    private static void AppendDetails(
        StringBuilder builder,
        PathNode node,
        bool verbose)
    {
        var entry = node.Entry;
        if (entry is null)
        {
            if (!node.IsDirectory && !File.Exists(node.FullPath)
                && !Directory.Exists(node.FullPath))
            {
                builder.Append(" [missing]");
            }
            return;
        }

        if (entry.Size is { } size)
        {
            builder.Append($"  {FormatSize(size)}");
        }
        if (verbose && entry.ModifiedAt is { } modified)
        {
            builder.Append(
                $"  {modified.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        }
        if (!string.Equals(entry.Status, "ok", StringComparison.Ordinal))
        {
            builder.Append($"  [{entry.Status}]");
        }
    }

    private static string? RelativeTo(string basePath, string path)
    {
        if (string.IsNullOrWhiteSpace(basePath))
        {
            return null;
        }

        var relative = Path.GetRelativePath(basePath, path);
        if (relative is "." or "")
        {
            return string.Empty;
        }
        if (Path.IsPathRooted(relative)
            || relative == ".."
            || relative.StartsWith(
                ".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
        {
            return null;
        }

        return relative;
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{value:0.#} {units[unit]}");
    }

    private sealed class PathNode
    {
        public required string Name { get; init; }

        public required string FullPath { get; init; }

        public bool IsDirectory { get; set; }

        public AppPathEntry? Entry { get; set; }

        public Dictionary<string, PathNode> Children { get; } =
            new(StringComparer.Ordinal);
    }
}
