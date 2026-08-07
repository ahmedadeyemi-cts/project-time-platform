using System.Globalization;
using System.Text;
using ClosedXML.Excel;

namespace ProjectTime.Api.Modules;

internal sealed record LabEquipmentExportRow(
    string EquipmentNumber, string ManagingTeam, string Name, string Type, string Manufacturer,
    string Model, string SerialNumber, string AssetTag, string Hostname, string Location, string Pod,
    string Rack, string RackUnits, string Status, string Custodian, string Project, string UpdatedAt);

internal sealed record LabIpExportRow(
    string Location, string Pod, string Zone, string Network, string Address, string Gateway,
    string Vlan, string Vrf, string Status, string Equipment, string Interface, string Purpose);

internal sealed record LabConnectionExportRow(
    string Location, string Pod, string From, string FromInterface, string To, string ToInterface,
    string Media, string CableLabel, string Vlan, string Address, string Status, string Notes);

internal sealed record RiskExportRow(
    string RiskNumber, string ProjectCode, string ProjectName, string Customer, string Title,
    string Type, string Category, string Owner, int Probability, int Impact, int InherentExposure,
    string InherentRating, int? ResidualExposure, string ResidualRating, string Strategy, string Status,
    string NextReview, string Trigger, string UpdatedAt);

internal sealed record RiskActionExportRow(
    string RiskNumber, string ProjectCode, string Action, string Owner, string DueDate,
    string Status, bool Overdue, string CompletionEvidence, string UpdatedAt);

internal static class EnterpriseGovernanceExports
{
    private static readonly XLColor Navy = XLColor.FromHtml("#0B2B4B");
    private static readonly XLColor Cyan = XLColor.FromHtml("#00A7D6");
    private static readonly XLColor Pale = XLColor.FromHtml("#EAF5F9");

    internal static byte[] BuildLabExcel(
        IReadOnlyList<LabEquipmentExportRow> equipment,
        IReadOnlyList<LabIpExportRow> addresses,
        IReadOnlyList<LabConnectionExportRow> connections,
        IReadOnlyList<string[]> conflicts,
        IReadOnlyList<string[]> history,
        string scopeLabel,
        string filters)
    {
        using var workbook = NewWorkbook("US Signal Lab Equipment Tracker");
        var summary = AddBrandedSummary(workbook, "Lab Equipment Tracker", scopeLabel, filters);
        WriteMetric(summary, 6, "Equipment", equipment.Count);
        WriteMetric(summary, 7, "IP allocations", addresses.Count);
        WriteMetric(summary, 8, "Connections", connections.Count);
        WriteMetric(summary, 9, "Review items", conflicts.Count);

        var equipmentSheet = workbook.Worksheets.Add("Equipment");
        WriteTable(equipmentSheet,
            ["Equipment ID","Team","Name","Type","Manufacturer","Model","Serial","Asset Tag","Hostname","Location","Pod","Rack","Rack Units","Status","Custodian","Linked Project","Updated UTC"],
            equipment.Select(row => new object?[] { row.EquipmentNumber,row.ManagingTeam,row.Name,row.Type,row.Manufacturer,row.Model,row.SerialNumber,row.AssetTag,row.Hostname,row.Location,row.Pod,row.Rack,row.RackUnits,row.Status,row.Custodian,row.Project,row.UpdatedAt }));

        var ipam = workbook.Worksheets.Add("IP Address Management");
        WriteTable(ipam,
            ["Location","Pod","Zone","Network","IP Address","Gateway","VLAN","VRF","Status","Equipment","Interface","Purpose"],
            addresses.Select(row => new object?[] { row.Location,row.Pod,row.Zone,row.Network,row.Address,row.Gateway,row.Vlan,row.Vrf,row.Status,row.Equipment,row.Interface,row.Purpose }));

        var cable = workbook.Worksheets.Add("Cabling & Connections");
        WriteTable(cable,
            ["Location","Pod","From","From Interface","To","To Interface","Media","Cable Label","VLAN","IP Address","Status","Notes"],
            connections.Select(row => new object?[] { row.Location,row.Pod,row.From,row.FromInterface,row.To,row.ToInterface,row.Media,row.CableLabel,row.Vlan,row.Address,row.Status,row.Notes }));

        var rack = workbook.Worksheets.Add("Rack Occupancy");
        WriteTable(rack,["Location","Pod","Rack","Rack Units","Equipment","Status"],
            equipment.Where(row => !string.IsNullOrWhiteSpace(row.Rack)).Select(row => new object?[] { row.Location,row.Pod,row.Rack,row.RackUnits,row.Name,row.Status }));
        var review = workbook.Worksheets.Add("Conflicts and Review Items");
        WriteTable(review,["Type","Entity","Finding","Status"],conflicts.Select(row => row.Cast<object?>()));
        var audit = workbook.Worksheets.Add("History & Audit");
        WriteTable(audit,["Occurred UTC","Entity","Event","Actor","Evidence"],history.Select(row => row.Cast<object?>()));
        AddEvidenceSheet(workbook,"081",scopeLabel,filters,equipment.Count+addresses.Count+connections.Count);
        return Save(workbook);
    }

    internal static byte[] BuildRiskExcel(
        IReadOnlyList<RiskExportRow> risks,
        IReadOnlyList<RiskActionExportRow> actions,
        IReadOnlyList<string[]> history,
        string scopeLabel,
        string filters)
    {
        using var workbook = NewWorkbook("US Signal Enterprise Project Risk Register");
        var summary = AddBrandedSummary(workbook,"Enterprise Project Risk Register",scopeLabel,filters,"Executive Summary");
        WriteMetric(summary,6,"Open risks",risks.Count(row => row.Status is not ("closed" or "retired")));
        WriteMetric(summary,7,"High / critical",risks.Count(row => row.InherentRating is "high" or "critical" && row.Status is not ("closed" or "retired")));
        WriteMetric(summary,8,"Overdue reviews",risks.Count(row => DateOnly.TryParse(row.NextReview,out var date) && date<DateOnly.FromDateTime(DateTime.UtcNow) && row.Status is not ("closed" or "retired")));
        WriteMetric(summary,9,"Overdue actions",actions.Count(row => row.Overdue));

        var open = workbook.Worksheets.Add("Open Risks");
        WriteRiskTable(open,risks.Where(row => row.Status is not ("closed" or "retired")));
        var closed = workbook.Worksheets.Add("Closed & Retired Risks");
        WriteRiskTable(closed,risks.Where(row => row.Status is "closed" or "retired"));

        var heatmap = workbook.Worksheets.Add("Heatmap Data");
        WriteTable(heatmap,["Risk","Project","Probability","Impact","Inherent Exposure","Inherent Rating","Residual Exposure","Residual Rating"],
            risks.Select(row => new object?[] { row.RiskNumber,row.ProjectCode,row.Probability,row.Impact,row.InherentExposure,row.InherentRating,row.ResidualExposure,row.ResidualRating }));
        ApplyHeatmapColors(heatmap,risks.Count+1,5);

        var actionSheet = workbook.Worksheets.Add("Risk Actions");
        WriteTable(actionSheet,["Risk","Project","Action","Owner","Due Date","Status","Overdue","Completion Evidence","Updated UTC"],
            actions.Select(row => new object?[] { row.RiskNumber,row.ProjectCode,row.Action,row.Owner,row.DueDate,row.Status,row.Overdue?"Yes":"No",row.CompletionEvidence,row.UpdatedAt }));
        var overdue = workbook.Worksheets.Add("Overdue Reviews");
        WriteRiskTable(overdue,risks.Where(row => DateOnly.TryParse(row.NextReview,out var date) && date<DateOnly.FromDateTime(DateTime.UtcNow) && row.Status is not ("closed" or "retired")));
        var portfolio = workbook.Worksheets.Add("Portfolio Summary");
        WriteTable(portfolio,["Project","Open","High / Critical","Overdue Review","Overdue Action"],
            risks.GroupBy(row => $"{row.ProjectCode} · {row.ProjectName}").Select(group => new object?[]
            {
                group.Key,
                group.Count(row => row.Status is not ("closed" or "retired")),
                group.Count(row => row.InherentRating is "high" or "critical" && row.Status is not ("closed" or "retired")),
                group.Count(row => DateOnly.TryParse(row.NextReview,out var date) && date<DateOnly.FromDateTime(DateTime.UtcNow) && row.Status is not ("closed" or "retired")),
                actions.Count(action => group.Any(risk => risk.RiskNumber==action.RiskNumber) && action.Overdue)
            }));
        var audit = workbook.Worksheets.Add("History & Audit");
        WriteTable(audit,["Occurred UTC","Risk","Event","Actor","Evidence"],history.Select(row => row.Cast<object?>()));
        AddEvidenceSheet(workbook,"082",scopeLabel,filters,risks.Count+actions.Count);
        return Save(workbook);
    }

    internal static byte[] BuildLabPdf(
        IReadOnlyList<LabEquipmentExportRow> equipment,
        IReadOnlyList<LabIpExportRow> addresses,
        string scopeLabel,
        string filters) => BuildPdf(
            "US Signal Lab Equipment Tracker",
            $"Scope: {scopeLabel} · Filters: {filters}",
            [
                $"Equipment: {equipment.Count}",
                $"IP allocations: {addresses.Count}",
                $"Active equipment: {equipment.Count(row => row.Status=="active")}",
                $"IP conflicts: {addresses.Count(row => row.Status=="conflict")}"
            ],
            ["ID","EQUIPMENT","TEAM","LOCATION / POD","RACK","STATUS"],
            equipment.Select(row => new[] { row.EquipmentNumber,row.Name,row.ManagingTeam,$"{row.Location} / {row.Pod}",$"{row.Rack} {row.RackUnits}",row.Status }).ToArray());

    internal static byte[] BuildRiskPdf(
        IReadOnlyList<RiskExportRow> risks,
        IReadOnlyList<RiskActionExportRow> actions,
        string scopeLabel,
        string filters) => BuildPdf(
            "US Signal Enterprise Project Risk Register",
            $"Scope: {scopeLabel} · Filters: {filters}",
            [
                $"Open risks: {risks.Count(row => row.Status is not ("closed" or "retired"))}",
                $"High / critical: {risks.Count(row => row.InherentRating is "high" or "critical" && row.Status is not ("closed" or "retired"))}",
                $"Overdue reviews: {risks.Count(row => DateOnly.TryParse(row.NextReview,out var date) && date<DateOnly.FromDateTime(DateTime.UtcNow) && row.Status is not ("closed" or "retired"))}",
                $"Overdue actions: {actions.Count(row => row.Overdue)}"
            ],
            ["RISK","PROJECT","TITLE","OWNER","P×I","RATING"],
            risks.OrderByDescending(row => row.InherentExposure).Select(row => new[] { row.RiskNumber,row.ProjectCode,row.Title,row.Owner,$"{row.Probability}×{row.Impact}",row.InherentRating }).ToArray());

    internal static string SafeText(string? value)
    {
        var text = (value ?? string.Empty).Replace('\0',' ').Trim();
        if (text.Length > 0 && "=+-@".Contains(text[0])) return "'" + text;
        return text;
    }

    private static XLWorkbook NewWorkbook(string title)
    {
        var workbook = new XLWorkbook();
        workbook.Properties.Title=title;
        workbook.Properties.Company="US Signal";
        workbook.Properties.Author="US Signal Pulse";
        return workbook;
    }

    private static IXLWorksheet AddBrandedSummary(XLWorkbook workbook,string title,string scope,string filters,string sheetName="Summary")
    {
        var sheet=workbook.Worksheets.Add(sheetName);
        using var logoStream=new MemoryStream(ProjectFlowHiveBrandAssets.LogoJpeg,writable:false);
        var picture=sheet.AddPicture(logoStream,"USSignalLogo");
        picture.MoveTo(sheet.Cell("A1"));picture.Width=100;picture.Height=67;
        sheet.Cell("C1").Value=title;sheet.Cell("C1").Style.Font.Bold=true;sheet.Cell("C1").Style.Font.FontSize=18;sheet.Cell("C1").Style.Font.FontColor=Navy;
        sheet.Cell("C2").Value="Role-scoped operational evidence";sheet.Cell("C2").Style.Font.FontColor=Cyan;sheet.Cell("C2").Style.Font.Bold=true;
        sheet.Cell("A4").Value="Scope";sheet.Cell("B4").Value=SafeText(scope);
        sheet.Cell("A5").Value="Filters";sheet.Cell("B5").Value=SafeText(filters);
        sheet.Range("A4:A12").Style.Font.Bold=true;sheet.Columns("A:F").AdjustToContents();sheet.SheetView.FreezeRows(3);
        return sheet;
    }

    private static void WriteMetric(IXLWorksheet sheet,int row,string label,int value)
    { sheet.Cell(row,1).Value=label;sheet.Cell(row,2).Value=value; }

    private static void WriteRiskTable(IXLWorksheet sheet,IEnumerable<RiskExportRow> rows) => WriteTable(sheet,
        ["Risk","Project","Project Name","Customer","Title","Type","Category","Owner","Probability","Impact","Inherent Exposure","Inherent Rating","Residual Exposure","Residual Rating","Strategy","Status","Next Review","Trigger","Updated UTC"],
        rows.Select(row => new object?[] { row.RiskNumber,row.ProjectCode,row.ProjectName,row.Customer,row.Title,row.Type,row.Category,row.Owner,row.Probability,row.Impact,row.InherentExposure,row.InherentRating,row.ResidualExposure,row.ResidualRating,row.Strategy,row.Status,row.NextReview,row.Trigger,row.UpdatedAt }));

    private static void WriteTable(IXLWorksheet sheet,IReadOnlyList<string> headers,IEnumerable<IEnumerable<object?>> source)
    {
        for(var column=0;column<headers.Count;column++) sheet.Cell(1,column+1).Value=headers[column];
        var rowIndex=2;
        foreach(var row in source)
        {
            var column=1;
            foreach(var value in row)
            {
                if(value is string text) sheet.Cell(rowIndex,column).Value=SafeText(text);
                else if(value is null) sheet.Cell(rowIndex,column).Value=string.Empty;
                else sheet.Cell(rowIndex,column).Value=XLCellValue.FromObject(value);
                column++;
            }
            rowIndex++;
        }
        var range=sheet.Range(1,1,Math.Max(1,rowIndex-1),headers.Count);
        range.CreateTable();
        sheet.Row(1).Style.Fill.BackgroundColor=Navy;sheet.Row(1).Style.Font.FontColor=XLColor.White;sheet.Row(1).Style.Font.Bold=true;
        sheet.SheetView.FreezeRows(1);sheet.Columns().AdjustToContents(4,48);sheet.Rows().Style.Alignment.WrapText=true;
        sheet.PageSetup.PageOrientation=XLPageOrientation.Landscape;sheet.PageSetup.FitToPages(1,0);
    }

    private static void ApplyHeatmapColors(IXLWorksheet sheet,int lastRow,int exposureColumn)
    {
        for(var row=2;row<=lastRow;row++)
        {
            var exposure=sheet.Cell(row,exposureColumn).GetValue<int>();
            sheet.Cell(row,exposureColumn).Style.Fill.BackgroundColor=exposure switch
            { >=17 => XLColor.FromHtml("#FECACA"), >=10 => XLColor.FromHtml("#FED7AA"), >=5 => XLColor.FromHtml("#FEF3C7"), _ => XLColor.FromHtml("#DCFCE7") };
        }
    }

    private static void AddEvidenceSheet(XLWorkbook workbook,string module,string scope,string filters,int rows)
    {
        var sheet=workbook.Worksheets.Add("Export Evidence");
        var evidence=new[]
        {
            new object?[]{"Module",module},new object?[]{"Generated UTC",DateTimeOffset.UtcNow.ToString("O",CultureInfo.InvariantCulture)},
            new object?[]{"Effective scope",SafeText(scope)},new object?[]{"Filters",SafeText(filters)},new object?[]{"Exported rows",rows},
            new object?[]{"Formula neutralization","Enabled"},new object?[]{"US Signal logo SHA-256",ProjectFlowHiveBrandAssets.LogoSha256},
            new object?[]{"Confidentiality","US Signal Confidential — role-scoped operational evidence"}
        };
        WriteTable(sheet,["Control","Value"],evidence);
    }

    private static byte[] Save(XLWorkbook workbook)
    { using var output=new MemoryStream();workbook.SaveAs(output);return output.ToArray(); }

    private static byte[] BuildPdf(string title,string subtitle,IReadOnlyList<string> metrics,IReadOnlyList<string> headers,IReadOnlyList<string[]> rows)
    {
        const int rowsPerPage=20;
        var chunks=rows.Chunk(rowsPerPage).Select(chunk=>chunk.ToArray()).ToList();
        if(chunks.Count==0) chunks.Add([]);
        var pageContents=chunks.Select((chunk,index)=>BuildPdfPage(title,subtitle,metrics,headers,chunk,index+1,chunks.Count)).ToArray();
        return AssemblePdf(pageContents,ProjectFlowHiveBrandAssets.LogoJpeg);
    }

    private static string BuildPdfPage(string title,string subtitle,IReadOnlyList<string> metrics,IReadOnlyList<string> headers,IReadOnlyList<string[]> rows,int page,int pages)
    {
        var content=new StringBuilder();
        content.Append("q 100 0 0 67 30 514 cm /Im1 Do Q\n");
        PdfText(content,145,568,17,title,true,"0.04 0.17 0.29");
        PdfText(content,145,548,8,Truncate(subtitle,118),false,"0.25 0.34 0.43");
        var metricX=30;foreach(var metric in metrics){PdfText(content,metricX,500,9,metric,true,"0.04 0.42 0.57");metricX+=185;}
        content.Append("0.04 0.17 0.29 rg 30 458 732 25 re f\n");
        var widths=new[]{70,85,260,105,70,100};var x=36;
        for(var i=0;i<headers.Count&&i<widths.Length;i++){PdfText(content,x,467,7,headers[i],true,"1 1 1");x+=widths[i];}
        var y=440;
        for(var rowIndex=0;rowIndex<rows.Count;rowIndex++)
        {
            if(rowIndex%2==0) content.Append($"0.94 0.98 1 rg 30 {y-6} 732 20 re f\n");
            x=36;var row=rows[rowIndex];
            for(var i=0;i<row.Length&&i<widths.Length;i++){var max=Math.Max(8,(int)(widths[i]/5.4));PdfText(content,x,y,7,Truncate(SafeText(row[i]),max),i is 0 or 5,"0.06 0.16 0.27");x+=widths[i];}
            y-=20;
        }
        content.Append("0.68 0.77 0.84 RG 30 38 m 762 38 l S\n");
        PdfText(content,30,22,7,"US Signal Confidential · Role-scoped at execution time",false,"0.34 0.42 0.50");
        PdfText(content,650,22,7,$"Page {page} of {pages}",false,"0.34 0.42 0.50");
        return content.ToString();
    }

    private static byte[] AssemblePdf(IReadOnlyList<string> pages,byte[] logo)
    {
        var pageIds=pages.Select((_,index)=>7+index*2).ToArray();var contentIds=pages.Select((_,index)=>6+index*2).ToArray();
        var objects=new SortedDictionary<int,byte[]>();
        objects[1]=Ascii("<< /Type /Catalog /Pages 2 0 R >>");
        objects[2]=Ascii($"<< /Type /Pages /Kids [{string.Join(' ',pageIds.Select(id=>$"{id} 0 R"))}] /Count {pageIds.Length} >>");
        objects[3]=Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");objects[4]=Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>");
        objects[5]=StreamObject($"/Type /XObject /Subtype /Image /Width 222 /Height 148 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {logo.Length}",logo);
        for(var index=0;index<pages.Count;index++)
        {
            var bytes=Ascii(pages[index]);objects[contentIds[index]]=StreamObject($"/Length {bytes.Length}",bytes);
            objects[pageIds[index]]=Ascii($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 792 612] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> /XObject << /Im1 5 0 R >> >> /Contents {contentIds[index]} 0 R >>");
        }
        using var output=new MemoryStream();WriteAscii(output,"%PDF-1.7\n%USSignal\n");var offsets=new Dictionary<int,long>();
        foreach(var pair in objects){offsets[pair.Key]=output.Position;WriteAscii(output,$"{pair.Key} 0 obj\n");output.Write(pair.Value);WriteAscii(output,"\nendobj\n");}
        var xref=output.Position;var maxId=objects.Keys.Max();WriteAscii(output,$"xref\n0 {maxId+1}\n0000000000 65535 f \n");
        for(var id=1;id<=maxId;id++) WriteAscii(output,$"{offsets[id]:D10} 00000 n \n");
        WriteAscii(output,$"trailer\n<< /Size {maxId+1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF");return output.ToArray();
    }

    private static void PdfText(StringBuilder builder,double x,double y,double size,string text,bool bold,string color)
    { builder.Append($"BT /{(bold?"F2":"F1")} {size.ToString("0.##",CultureInfo.InvariantCulture)} Tf {color} rg {x.ToString("0.##",CultureInfo.InvariantCulture)} {y.ToString("0.##",CultureInfo.InvariantCulture)} Td ({EscapePdf(text)}) Tj ET\n"); }
    private static string EscapePdf(string text)=>text.Replace("\\","\\\\").Replace("(","\\(").Replace(")","\\)").Replace("\r"," ").Replace("\n"," ");
    private static string Truncate(string text,int max)=>text.Length<=max?text:text[..Math.Max(1,max-1)]+"…";
    private static byte[] Ascii(string value)=>Encoding.ASCII.GetBytes(value.Replace('…','.'));
    private static byte[] StreamObject(string dictionary,byte[] content){using var output=new MemoryStream();WriteAscii(output,$"<< {dictionary} >>\nstream\n");output.Write(content);WriteAscii(output,"\nendstream");return output.ToArray();}
    private static void WriteAscii(Stream stream,string value)=>stream.Write(Encoding.ASCII.GetBytes(value));
}
