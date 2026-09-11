using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.IO;

namespace Steam_Desktop_Authenticator
{
    public class ExcelAccountRow
    {
        public int RowNumber { get; set; }
        public string Account { get; set; }
        public string Password { get; set; }
        public string Email { get; set; }
        public string EmailPassword { get; set; }
        public string ImapHost { get; set; }
        public string BackupEmail { get; set; }
        public string BackupEmailPassword { get; set; }
        public string Extra { get; set; }
    }

    public class ExcelAccountSheet : IDisposable
    {
        private const int ExtraColumn = 8;

        private string _path;
        private readonly XLWorkbook _workbook;
        private readonly IXLWorksheet _worksheet;

        public string CurrentPath => _path;
        public bool SavedToTemp { get; private set; }

        public ExcelAccountSheet(string path)
        {
            _path = path;
            _workbook = new XLWorkbook(path);
            _worksheet = _workbook.Worksheets.Worksheet(1);
        }

        public List<ExcelAccountRow> ReadAccounts()
        {
            var rows = new List<ExcelAccountRow>();
            int lastRow = _worksheet.LastRowUsed()?.RowNumber() ?? 0;

            for (int r = 3; r <= lastRow; r++)
            {
                string account = Cell(r, 1);
                if (string.IsNullOrWhiteSpace(account))
                    continue;

                rows.Add(new ExcelAccountRow
                {
                    RowNumber = r,
                    Account = account.Trim(),
                    Password = Cell(r, 2),
                    Email = Cell(r, 3),
                    EmailPassword = Cell(r, 4),
                    ImapHost = Cell(r, 5),
                    BackupEmail = Cell(r, 6),
                    BackupEmailPassword = Cell(r, 7),
                    Extra = Cell(r, ExtraColumn)
                });
            }

            return rows;
        }

        public void WriteExtra(int rowNumber, string extra)
        {
            var row = _worksheet.Row(rowNumber);
            double height = row.Height;
            var cell = _worksheet.Cell(rowNumber, ExtraColumn);
            cell.Value = extra ?? "";
            cell.Style.Alignment.WrapText = false;
            row.Height = height;
            SaveWithFallback();
        }

        private void SaveWithFallback()
        {
            try
            {
                _workbook.Save();
                return;
            }
            catch (Exception ex)
            {
                if (SavedToTemp)
                    throw new IOException("写入临时 Excel 失败：" + ex.Message, ex);

                string tempPath = BuildTempPath(_path);
                try
                {
                    _workbook.SaveAs(tempPath);
                }
                catch (Exception tempEx)
                {
                    throw new IOException($"原文件被占用无法写入，临时文件也写入失败。原错误：{ex.Message}；临时文件错误：{tempEx.Message}", tempEx);
                }

                _path = tempPath;
                SavedToTemp = true;
            }
        }

        private static string BuildTempPath(string sourcePath)
        {
            string dir = Path.GetDirectoryName(sourcePath);
            string name = Path.GetFileNameWithoutExtension(sourcePath);
            return Path.Combine(dir, $"{name}_临时_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
        }

        private string Cell(int row, int col)
        {
            return _worksheet.Cell(row, col).GetString()?.Trim() ?? "";
        }

        public void Dispose()
        {
            _workbook?.Dispose();
        }
    }
}
