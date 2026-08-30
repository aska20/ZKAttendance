using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ZKAttendance.Domain.Entities;
using ZKAttendance.Application.Dtos;

using ZKAttendance.Application.Abstractions;

namespace ZKAttendance.Infrastructure.Services.Report
{
    public class PdfReportService
    {
        public byte[] GeneratePdfReport(List<AttendanceViewModel> data, DateTime? fromDate, DateTime? toDate)
        {
            var groupedByEmployee = data
                .GroupBy(d => d.BiometricUserId)
                .Select(g => new
                {
                    BiometricUserId = g.Key,
                    EmployeeName = g.First().EmployeeName,
                    BranchName = g.First().BranchName,
                    Records = g.OrderBy(x => x.Date).ToList(),
                    TotalHours = g.Sum(x => x.WorkingHours),
                    PresentDays = g.Count(x => x.Status == "Full Day"),
                    PartialDays = g.Count(x => x.Status == "Check-in Only"),
                    AbsentDays = g.Count(x => x.Status == "Absent")
                })
                .ToList();

            var document = Document.Create(container =>
            {
                foreach (var employee in groupedByEmployee)
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(15);
                        page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial"));

                        page.Header().Column(column =>
                        {
                            column.Item().Background(Colors.Blue.Darken2).Padding(8)
                                .AlignCenter().Text("Attendance Report")
                                .FontSize(14).Bold().FontColor(Colors.White);

                            column.Item().PaddingTop(3).AlignCenter()
                                .Text($"From {fromDate?.ToString("dd/MM/yyyy") ?? "Start"} To {toDate?.ToString("dd/MM/yyyy") ?? "End"}")
                                .FontSize(10);
                        });

                        page.Content().PaddingTop(10).Column(column =>
                        {
                            column.Item().Text($"Employee: {employee.EmployeeName}  |  Biometric ID: {employee.BiometricUserId}")
                                .FontSize(11).Bold();

                            column.Item().PaddingTop(10).Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.ConstantColumn(30);
                                    columns.RelativeColumn(2);
                                    columns.RelativeColumn(2);
                                    columns.RelativeColumn(2);
                                    columns.RelativeColumn(1);
                                    columns.RelativeColumn(2);
                                });

                                table.Header(header =>
                                {
                                    header.Cell().Text("#").Bold();
                                    header.Cell().Text("Date").Bold();
                                    header.Cell().Text("In").Bold();
                                    header.Cell().Text("Out").Bold();
                                    header.Cell().Text("hours").Bold();
                                    header.Cell().Text("Status").Bold();
                                });

                                int index = 1;
                                foreach (var record in employee.Records)
                                {
                                    table.Cell().Text(index.ToString());
                                    table.Cell().Text(record.Date.ToString("dd/MM/yyyy"));
                                    table.Cell().Text(record.CheckInTime?.ToString("HH:mm") ?? "-");
                                    table.Cell().Text(record.CheckOutTime?.ToString("HH:mm") ?? "-");
                                    table.Cell().Text(record.WorkingHours > 0
                                        ? $"{record.WorkingHours:F1}" : "-");
                                    table.Cell().Text(record.Status);
                                    index++;
                                }
                            });

                            column.Item().PaddingTop(15).Text($"Total working hours: {employee.TotalHours:F2} hours")
                                .Bold().FontSize(11);
                        });
                    });
                }
            });

            return document.GeneratePdf();
        }
    }
}
