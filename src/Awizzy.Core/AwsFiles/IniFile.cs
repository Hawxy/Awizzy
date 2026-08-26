using System.Text;

namespace Awizzy.Core.AwsFiles;

/// <summary>Round-tripping INI document: sections and lines this class does not touch are
/// written back byte-for-byte, including comments, blank lines, ordering, and newline style.</summary>
public class IniFile
{
    private readonly List<Section> _sections = [];
    private readonly List<string> _preamble = [];
    private readonly string _newline;
    private readonly bool _trailingNewline;

    private class Section(string headerLine, string name)
    {
        public string HeaderLine { get; } = headerLine;
        public string Name { get; } = name;
        public List<string> Lines { get; } = [];
    }

    private IniFile(string newline, bool trailingNewline)
    {
        _newline = newline;
        _trailingNewline = trailingNewline;
    }

    public static IniFile Empty() => new(Environment.NewLine, trailingNewline: true);

    public static IniFile Parse(string content)
    {
        var newline = content.Contains("\r\n") ? "\r\n" : "\n";
        var trailingNewline = content.Length == 0 || content.EndsWith('\n');
        var ini = new IniFile(newline, trailingNewline);

        Section? current = null;
        foreach (var line in SplitLines(content))
        {
            if (TryParseSectionHeader(line, out var name))
            {
                current = new Section(line, name);
                ini._sections.Add(current);
            }
            else if (current is null)
            {
                ini._preamble.Add(line);
            }
            else
            {
                current.Lines.Add(line);
            }
        }

        return ini;
    }

    public IReadOnlyList<string> SectionNames => _sections.Select(s => s.Name).ToList();

    public bool HasSection(string name) => Find(name) is not null;

    /// <summary>True when the section carries the given marker comment on its first line.</summary>
    public bool SectionHasMarker(string name, string marker) =>
        Find(name) is { } section
        && section.Lines.FirstOrDefault(l => l.Trim().Length > 0) is { } first
        && first.Trim() == marker;

    /// <summary>Replaces the section's content with the marker comment followed by the given keys.
    /// Creates the section at the end of the file if it does not exist.</summary>
    public void SetSection(string name, IEnumerable<KeyValuePair<string, string>> values, string marker)
    {
        var section = Find(name);
        if (section is null)
        {
            section = new Section($"[{name}]", name);
            _sections.Add(section);
        }

        section.Lines.Clear();
        section.Lines.Add(marker);
        foreach (var (key, value) in values)
            section.Lines.Add($"{key} = {value}");
        section.Lines.Add(string.Empty);
    }

    public void RemoveSection(string name)
    {
        var section = Find(name);
        if (section is not null)
            _sections.Remove(section);
    }

    public override string ToString()
    {
        var lines = _preamble
            .Concat(_sections.SelectMany(s => s.Lines.Prepend(s.HeaderLine)))
            .ToList();

        // Collapse a run of blank lines at the end down to the original trailing-newline style.
        while (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);

        if (lines.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendJoin(_newline, lines);
        if (_trailingNewline)
            sb.Append(_newline);
        return sb.ToString();
    }

    private Section? Find(string name) =>
        _sections.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal));

    private static bool TryParseSectionHeader(string line, out string name)
    {
        name = string.Empty;
        var trimmed = line.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '[' || trimmed[^1] != ']')
            return false;
        name = trimmed[1..^1].Trim();
        return name.Length > 0;
    }

    private static IEnumerable<string> SplitLines(string content)
    {
        if (content.Length == 0)
            yield break;

        // Strip a UTF-8 BOM if the file carries one; it is not re-emitted on write.
        if (content[0] == '﻿')
            content = content[1..];

        var lines = content.Split('\n');
        // A trailing newline produces one empty final element; drop it (re-added by ToString).
        var count = content.EndsWith('\n') ? lines.Length - 1 : lines.Length;
        for (var i = 0; i < count; i++)
            yield return lines[i].TrimEnd('\r');
    }
}
