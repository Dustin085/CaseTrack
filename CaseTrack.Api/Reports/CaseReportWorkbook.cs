using CaseTrack.Application.Reports;
using CaseTrack.Domain.Cases;
using ClosedXML.Excel;

namespace CaseTrack.Api.Reports;

// 把報表資料轉成 Excel（.xlsx）。跟 JSON 序列化一樣是「輸出格式」，放在 Api 層；
// Application 只回傳資料，之後要加 ODS 或 PDF，只要在這一層多一種輸出。
// 檔案在記憶體產生（筆數上限由 CaseReportHandler 控制）
public static class CaseReportWorkbook
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static readonly TimeSpan TaiwanOffset = TimeSpan.FromHours(8);
    private const string DateTimeFormat = "yyyy/mm/dd hh:mm";

    // 報表上的狀態一律用中文。用 switch 列出每一種：新增狀態沒補上會丟例外，不會默默印出英文
    public static string StatusLabel(CaseStatus status) => status switch
    {
        CaseStatus.Submitted => "已送件",
        CaseStatus.UnderReview => "審核中",
        CaseStatus.AwaitingSupplement => "補件中",
        CaseStatus.PendingApproval => "待核定",
        CaseStatus.Closed => "已結案",
        CaseStatus.Rejected => "已退件",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "報表沒有這個狀態的名稱"),
    };

    // 統計表：每個單位一列、每個狀態一欄，最後一欄和最後一列是合計。
    // 沒有案件的狀態也要有欄位（顯示 0）：每個月的報表欄位固定，使用者才能直接比較或貼到別的表
    public static byte[] Statistics(IReadOnlyList<DepartmentStatusCount> counts, DateOnly from, DateOnly to, DateTimeOffset generatedAt)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("案件統計");
        var statuses = Enum.GetValues<CaseStatus>();

        var row = WriteTitle(sheet, "陳情案件統計表", from, to, generatedAt, statuses.Length + 2);

        sheet.Cell(row, 1).Value = "承辦單位";
        for (var i = 0; i < statuses.Length; i++)
        {
            sheet.Cell(row, i + 2).Value = StatusLabel(statuses[i]);
        }
        sheet.Cell(row, statuses.Length + 2).Value = "合計";
        StyleHeader(sheet.Range(row, 1, row, statuses.Length + 2));
        var headerRow = row;

        // 未分派的案件也要算進來，放在最後
        var departments = counts
            .GroupBy(c => c.DepartmentId)
            .OrderBy(g => g.Key is null)
            .ThenBy(g => g.First().DepartmentName);
        foreach (var department in departments)
        {
            row++;
            sheet.Cell(row, 1).Value = department.First().DepartmentName ?? "未分派";
            for (var i = 0; i < statuses.Length; i++)
            {
                sheet.Cell(row, i + 2).Value = department.Where(c => c.Status == statuses[i]).Sum(c => c.Count);
            }
            sheet.Cell(row, statuses.Length + 2).Value = department.Sum(c => c.Count);
        }

        row++;
        sheet.Cell(row, 1).Value = "合計";
        for (var i = 0; i < statuses.Length; i++)
        {
            sheet.Cell(row, i + 2).Value = counts.Where(c => c.Status == statuses[i]).Sum(c => c.Count);
        }
        sheet.Cell(row, statuses.Length + 2).Value = counts.Sum(c => c.Count);
        sheet.Row(row).Style.Font.Bold = true;
        sheet.Range(headerRow, 1, row, statuses.Length + 2).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        sheet.Range(headerRow, 1, row, statuses.Length + 2).Style.Border.InsideBorder = XLBorderStyleValues.Thin;

        sheet.Columns().AdjustToContents();
        return Save(workbook);
    }

    // 案件清單：不含陳情人（見 CaseListRow 的說明）
    public static byte[] CaseList(IReadOnlyList<CaseListRow> rows, DateOnly from, DateOnly to, DateTimeOffset generatedAt)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("案件清單");
        string[] headers = ["案件編號", "主旨", "狀態", "承辦單位", "送件時間"];

        var row = WriteTitle(sheet, "陳情案件清單", from, to, generatedAt, headers.Length);
        for (var i = 0; i < headers.Length; i++)
        {
            sheet.Cell(row, i + 1).Value = headers[i];
        }
        StyleHeader(sheet.Range(row, 1, row, headers.Length));
        var headerRow = row;

        foreach (var item in rows)
        {
            row++;
            // 文字一律用 Value 寫入：ClosedXML 會存成文字儲存格。主旨是民眾輸入的，
            // 內容是 "=HYPERLINK(...)" 也只會顯示成文字，不會變成公式（CSV／Excel 公式注入）
            sheet.Cell(row, 1).Value = item.CaseNumber;
            sheet.Cell(row, 2).Value = item.Subject;
            sheet.Cell(row, 3).Value = StatusLabel(item.Status);
            sheet.Cell(row, 4).Value = item.DepartmentName ?? "未分派";
            // Excel 的日期沒有時區：先換成台灣時間再寫入，不然會差 8 小時
            sheet.Cell(row, 5).Value = item.SubmittedAt.ToOffset(TaiwanOffset).DateTime;
            sheet.Cell(row, 5).Style.DateFormat.Format = DateTimeFormat;
        }

        // 篩選按鈕與凍結表頭：清單通常很長，使用者會在 Excel 裡再篩選
        sheet.Range(headerRow, 1, row, headers.Length).SetAutoFilter();
        sheet.SheetView.FreezeRows(headerRow);
        sheet.Columns().AdjustToContents();
        sheet.Column(2).Width = Math.Min(sheet.Column(2).Width, 60);
        return Save(workbook);
    }

    // 標題、期間、產製時間：報表印出來或轉寄之後，還看得出是什麼時候、哪個範圍的資料
    private static int WriteTitle(IXLWorksheet sheet, string title, DateOnly from, DateOnly to,
        DateTimeOffset generatedAt, int width)
    {
        sheet.Cell(1, 1).Value = title;
        sheet.Range(1, 1, 1, width).Merge().Style.Font.SetBold().Font.SetFontSize(14);
        sheet.Cell(2, 1).Value = $"送件期間：{from:yyyy/MM/dd}～{to:yyyy/MM/dd}";
        sheet.Cell(3, 1).Value = $"產製時間：{generatedAt.ToOffset(TaiwanOffset):yyyy/MM/dd HH:mm}";
        return 5;
    }

    private static void StyleHeader(IXLRange range)
    {
        range.Style.Font.Bold = true;
        range.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EEF7");
    }

    private static byte[] Save(XLWorkbook workbook)
    {
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
