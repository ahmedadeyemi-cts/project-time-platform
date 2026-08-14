#!/usr/bin/env python3
from __future__ import annotations

import argparse
from pathlib import Path

PDF_PAGE_METHOD = r'''    private static string BuildPdfPage(
        ProjectFlowHiveArtifactRequest request,
        ProjectFlowHiveScheduleResult schedule,
        IReadOnlyList<ArtifactTaskRow> tasks,
        int pageNumber,
        int pageCount)
    {
        var content = new StringBuilder();
        content.Append("q 86 0 0 57 36 520 cm /Im1 Do Q\n");
        PdfText(content, 135, 568, 18, "US Signal Project FlowHive", true, "0.04 0.17 0.29");
        PdfText(content, 135, 548, 10, ArtifactLabel, true, "0.04 0.37 0.66");
        PdfText(content, 36, 512, 13, request.ArtifactTitle ?? request.Plan?.PlanName ?? "Governed project plan", true, "0.04 0.17 0.29");
        PdfText(content, 36, 494, 8, $"Project: {Join(request.Plan?.ProjectCode, request.Plan?.ProjectName)}", false, "0.18 0.25 0.34");
        PdfText(content, 36, 480, 8, $"Customer: {request.Plan?.CustomerName ?? "Not specified"}", false, "0.18 0.25 0.34");
        PdfText(content, 560, 494, 8, $"Schedule: {FormatDate(schedule.ProjectStartDate)} - {FormatDate(schedule.ProjectFinishDate)}", false, "0.18 0.25 0.34");
        PdfText(content, 560, 480, 8, $"Tasks: {schedule.Tasks.Count} | Critical tasks: {schedule.CriticalTaskCount} | Page rows: {tasks.Count}", false, "0.18 0.25 0.34");

        PdfText(content, 36, 456, 9, "EXECUTIVE SUMMARY", true, "0.04 0.37 0.66");
        var executiveSummaryLines = WrapPdfText(ExecutiveSummary(request.Plan), 145, 3);
        var executiveSummaryY = 441d;
        foreach (var line in executiveSummaryLines)
        {
            PdfText(content, 36, executiveSummaryY, 7.2, line, false, "0.18 0.25 0.34");
            executiveSummaryY -= 13;
        }

        content.Append("0.04 0.17 0.29 rg 36 378 936 24 re f\n");
        var headings = new[]
        {
            ("WBS", 42), ("TASK NAME", 78), ("START DATE", 220), ("END DATE", 278),
            ("DURATION IN DAYS", 336), ("PROGRESS", 410), ("PREDECESSOR", 466), ("TYPE", 536),
            ("COMMENTS", 570), ("NOTES", 690), ("ASSIGNED IDENTITY", 815)
        };
        foreach (var (label, x) in headings) PdfText(content, x, 387, 5.8, label, true, "1 1 1");

        var y = 359;
        for (var index = 0; index < tasks.Count; index++)
        {
            var task = tasks[index];
            if (index % 2 == 0) content.Append($"0.94 0.98 1 rg 36 {y - 5} 936 20 re f\n");
            PdfText(content, 42, y, 6.2, Truncate(task.Wbs, 7), true, "0.06 0.16 0.27");
            PdfText(content, 78, y, 6.2, Truncate(task.TaskName, 29), false, "0.06 0.16 0.27");
            PdfText(content, 220, y, 6.2, FormatDate(task.StartDate), false, "0.06 0.16 0.27");
            PdfText(content, 278, y, 6.2, FormatDate(task.EndDate), false, "0.06 0.16 0.27");
            PdfText(content, 336, y, 6.2, task.DurationInDays.ToString(CultureInfo.InvariantCulture), false, "0.06 0.16 0.27");
            PdfText(content, 410, y, 6.2, $"{Math.Round(task.Progress, 0, MidpointRounding.AwayFromZero)}%", false, "0.06 0.16 0.27");
            PdfText(content, 466, y, 6.2, Truncate(task.Predecessor, 12), false, "0.06 0.16 0.27");
            PdfText(content, 536, y, 6.2, Truncate(task.DependencyType, 4), false, "0.06 0.16 0.27");
            PdfText(content, 570, y, 6.2, Truncate(task.Comments, 22), false, "0.06 0.16 0.27");
            PdfText(content, 690, y, 6.2, Truncate(task.Notes, 22), false, "0.06 0.16 0.27");
            PdfText(content, 815, y, 6.2, Truncate(task.AssignedIdentity, 26), false, "0.06 0.16 0.27");
            y -= 22;
        }

        content.Append("0.68 0.77 0.84 RG 36 53 m 972 53 l S\n");
        PdfText(content, 36, 35, 7, "US Signal internal governed Project FlowHive artifact", false, "0.34 0.42 0.50");
        PdfText(content, 915, 35, 7, $"Page {pageNumber} of {pageCount}", false, "0.34 0.42 0.50");
        return content.ToString();
    }
'''

PDF_TEXT_HELPERS = r'''    private static string EscapePdf(string? value) => NormalizePdfText(value)
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("(", "\\(", StringComparison.Ordinal)
        .Replace(")", "\\)", StringComparison.Ordinal);

    private static string NormalizePdfText(string? value)
    {
        var input = value ?? string.Empty;
        var output = new StringBuilder(input.Length);
        var previousWasSpace = false;
        foreach (var character in input)
        {
            string replacement = character switch
            {
                '\u2013' or '\u2014' => " - ",
                '\u2018' or '\u2019' => "'",
                '\u201c' or '\u201d' => "\"",
                '\u2026' => "...",
                '\r' or '\n' or '\t' => " ",
                >= ' ' and <= '~' => character.ToString(),
                _ => " "
            };

            foreach (var normalized in replacement)
            {
                if (char.IsWhiteSpace(normalized))
                {
                    if (previousWasSpace) continue;
                    output.Append(' ');
                    previousWasSpace = true;
                }
                else
                {
                    output.Append(normalized);
                    previousWasSpace = false;
                }
            }
        }
        return output.ToString().Trim();
    }

    private static string Truncate(string? value, int length)
    {
        var clean = NormalizePdfText(value);
        if (clean.Length <= length) return clean;
        if (length <= 3) return clean[..Math.Max(0, length)];
        return $"{clean[..(length - 3)].TrimEnd()}...";
    }

    private static IReadOnlyList<string> WrapPdfText(string? value, int maxCharacters, int maxLines)
    {
        var clean = NormalizePdfText(value);
        if (string.IsNullOrWhiteSpace(clean)) return ["No executive summary was provided."];

        var lines = new List<string>();
        var current = new StringBuilder();
        var wasTruncated = false;
        foreach (var word in clean.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var required = current.Length == 0 ? word.Length : current.Length + 1 + word.Length;
            if (required > maxCharacters && current.Length > 0)
            {
                lines.Add(current.ToString());
                current.Clear();
                if (lines.Count == maxLines)
                {
                    wasTruncated = true;
                    break;
                }
            }
            if (current.Length > 0) current.Append(' ');
            current.Append(word);
        }

        if (!wasTruncated && current.Length > 0 && lines.Count < maxLines)
            lines.Add(current.ToString());
        if (wasTruncated && lines.Count > 0)
            lines[^1] = Truncate($"{lines[^1]}...", maxCharacters);
        return lines;
    }

'''


def replace_once(source: str, old: str, new: str, label: str) -> str:
    count = source.count(old)
    if count != 1:
        raise SystemExit(f'{label}: expected exactly one source anchor, found {count}')
    return source.replace(old, new, 1)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument('--input', required=True)
    parser.add_argument('--output', required=True)
    args = parser.parse_args()

    source = Path(args.input).read_text(encoding='utf-8')
    source = replace_once(
        source,
        '    private const string DraftLabel = "PROJECT MANAGEMENT WORKING PLAN — REVIEW REQUIRED";',
        '    private const string ArtifactLabel = "PROJECT MANAGEMENT PLAN";',
        'artifact label')
    draft_references = source.count('DraftLabel')
    if draft_references != 3:
        raise SystemExit(f'artifact label references: expected three, found {draft_references}')
    source = source.replace('DraftLabel', 'ArtifactLabel')
    source = replace_once(
        source,
        'summary.Cell("C2").Style.Font.FontColor = XLColor.FromHtml("#B42318");',
        'summary.Cell("C2").Style.Font.FontColor = XLColor.FromHtml("#0B5E9B");',
        'production status color')
    source = replace_once(source, '"Unversioned draft"', '"Working revision"', 'revision label')
    source = replace_once(source, '        const int rowsPerPage = 16;', '        const int rowsPerPage = 12;', 'PDF page size')
    source = replace_once(
        source,
        '''        summary.Cell("A12").Value = "Logo checksum";
        summary.Cell("B12").Value = ProjectFlowHiveBrandAssets.LogoSha256;''',
        '''        summary.Cell("A12").Value = "Governance";
        summary.Cell("B12").Value = "Internal governed artifact";''',
        'summary governance')

    page_start = source.find('    private static string BuildPdfPage(')
    page_end = source.find('    private static byte[] BuildPdfDocument', page_start)
    if page_start < 0 or page_end < 0 or page_end <= page_start:
        raise SystemExit('PDF page method anchors were not found')
    source = source[:page_start] + PDF_PAGE_METHOD + '\n' + source[page_end:]

    helper_start = source.find('    private static string EscapePdf(')
    helper_end = source.find('    private static string ExecutiveSummary', helper_start)
    if helper_start < 0 or helper_end < 0 or helper_end <= helper_start:
        raise SystemExit('PDF text helper anchors were not found')
    source = source[:helper_start] + PDF_TEXT_HELPERS + source[helper_end:]

    required = (
        'PROJECT MANAGEMENT PLAN',
        'EXECUTIVE SUMMARY',
        'Internal governed artifact',
        'NormalizePdfText',
        'WrapPdfText',
        'const int rowsPerPage = 12;',
    )
    forbidden = ('REVIEW REQUIRED', 'Logo SHA-256 {ProjectFlowHiveBrandAssets.LogoSha256}')
    missing = [marker for marker in required if marker not in source]
    retained = [marker for marker in forbidden if marker in source]
    if missing or retained:
        raise SystemExit(f'production artifact contract failed; missing={missing}, retained={retained}')

    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(source, encoding='utf-8')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
