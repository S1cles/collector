using System.Globalization;
using System.Text;

namespace SpectraCollector.Core;

/// <summary>Reads and writes the sample time table (CSV).</summary>
public static class SampleTable
{
    private const string Header = "Sample;Exposition;Begin;End;Material";
    private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";
    private static readonly char[] Delimiters = [';', '\t', ','];

    /// <summary>Parses the table; throws <see cref="SampleTimesException"/> with a <c>Line n: </c> prefix on bad input.</summary>
    public static IReadOnlyList<Sample> Parse(TextReader reader)
    {
        var lines = ReadLines(reader);
        var headerIndex = lines.FindIndex(l => !IsBlank(l.Text, null));
        if (headerIndex < 0)
        {
            throw Fail(1, "The file is empty.");
        }

        var headerText = lines[headerIndex].Text.TrimStart('﻿');
        var delimiter = Delimiters.FirstOrDefault(headerText.Contains, ';');
        var header = SplitFields(headerText, delimiter, 1);
        var sampleColumn = ColumnOf(header, "Sample");
        var beginColumn = ColumnOf(header, "Begin");
        var endColumn = ColumnOf(header, "End");

        var samples = new List<Sample>();
        foreach (var (number, text) in lines.Skip(headerIndex + 1))
        {
            if (IsBlank(text, delimiter))
            {
                continue;
            }

            var fields = SplitFields(text, delimiter, number);
            var name = FieldAt(fields, sampleColumn);
            if (name.Length == 0)
            {
                throw Fail(number, "Sample name is empty.");
            }

            var begin = TimeAt(fields, beginColumn, "Begin", "2026-11-06 13:30:00", number);
            var end = TimeAt(fields, endColumn, "End", "2026-11-06 13:40:00", number);
            if (end < begin)
            {
                throw Fail(number, "End is earlier than Begin.");
            }

            samples.Add(new Sample(name, begin, end));
        }

        if (samples.Count == 0)
        {
            throw Fail(1, "The file has no sample rows.");
        }

        return samples;
    }

    /// <summary>Writes the table in the format <see cref="Parse"/> reads, CRLF-terminated.</summary>
    public static string ToCsv(IReadOnlyList<Sample> samples)
    {
        var csv = new StringBuilder(Header).Append("\r\n");
        foreach (var sample in samples)
        {
            csv.Append(Quote(sample.Name)).Append(";;")
                .Append(sample.Begin.ToString(TimeFormat, CultureInfo.InvariantCulture)).Append(';')
                .Append(sample.End.ToString(TimeFormat, CultureInfo.InvariantCulture)).Append(";\r\n");
        }

        return csv.ToString();
    }

    private sealed record Line(int Number, string Text);

    private static List<Line> ReadLines(TextReader reader)
    {
        var lines = new List<Line>();
        for (var text = reader.ReadLine(); text is not null; text = reader.ReadLine())
        {
            lines.Add(new Line(lines.Count + 1, text));
        }

        return lines;
    }

    private static bool IsBlank(string text, char? delimiter) =>
        text.All(c => char.IsWhiteSpace(c) || c == delimiter);

    private static int ColumnOf(List<string> header, string name)
    {
        var index = header.FindIndex(h => h.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index : throw Fail(1, $"Column '{name}' is missing.");
    }

    private static string FieldAt(List<string> fields, int column) =>
        column < fields.Count ? fields[column] : string.Empty;

    private static DateTime TimeAt(List<string> fields, int column, string label, string example, int number) =>
        SampleTime.TryParse(FieldAt(fields, column), out var value)
            ? value
            : throw Fail(number, $"{label} is not a valid time. Use {example}.");

    private static List<string> SplitFields(string text, char delimiter, int number)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c != '"')
                {
                    field.Append(c);
                }
                else if (i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else
                {
                    quoted = false;
                }
            }
            else if (c == '"' && field.ToString().Trim().Length == 0)
            {
                field.Clear();
                quoted = true;
            }
            else if (c == delimiter)
            {
                fields.Add(field.ToString().Trim());
                field.Clear();
            }
            else
            {
                field.Append(c);
            }
        }

        if (quoted)
        {
            throw Fail(number, "A quoted field is not closed.");
        }

        fields.Add(field.ToString().Trim());
        return fields;
    }

    private static string Quote(string name) =>
        name.IndexOfAny([';', '"', '\r', '\n']) < 0 ? name : $"\"{name.Replace("\"", "\"\"")}\"";

    private static SampleTimesException Fail(int line, string message) =>
        new($"Line {line}: {message}");
}
