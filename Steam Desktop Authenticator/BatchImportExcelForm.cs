using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Steam_Desktop_Authenticator
{
    public partial class BatchImportExcelForm : Form
    {
        private readonly string _passKey;
        private CancellationTokenSource _cts;
        private bool _running;

        public BatchImportExcelForm(string passKey)
        {
            _passKey = passKey;
            InitializeComponent();
        }

        private void btnBrowse_Click(object sender, EventArgs e)
        {
            using var dialog = new OpenFileDialog
            {
                Filter = "Excel 文件 (*.xlsx)|*.xlsx",
                Title = "选择账号 Excel"
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                txtPath.Text = dialog.FileName;
        }

        private async void btnStart_Click(object sender, EventArgs e)
        {
            if (_running)
                return;

            string path = txtPath.Text;
            if (string.IsNullOrWhiteSpace(path))
            {
                MessageBox.Show(this, "请先选择 Excel 文件。", "批量导入 Excel", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!File.Exists(path))
            {
                MessageBox.Show(this, "找不到所选的 Excel 文件。", "批量导入 Excel", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Manifest manifest = Manifest.GetManifest();
            if (!manifest.Encrypted || string.IsNullOrEmpty(_passKey))
            {
                MessageBox.Show(this, "必须先设置并解锁加密密钥后再批量绑定。", "批量导入 Excel", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _running = true;
            _cts = new CancellationTokenSource();
            btnStart.Enabled = false;
            btnBrowse.Enabled = false;
            btnCancelRun.Enabled = true;
            listLog.Items.Clear();
            lblSummary.Text = "";
            progressBar.Value = 0;

            int success = 0;
            int fail = 0;

            try
            {
                using var sheet = new ExcelAccountSheet(path);
                var rows = sheet.ReadAccounts();
                if (rows.Count == 0)
                {
                    AppendLog("未找到账号行（数据需从第 3 行开始）。");
                    return;
                }

                AppendLog("结果将写入原 Excel 额外信息列。");

                progressBar.Maximum = rows.Count;
                var binder = new AuthenticatorBindService();

                for (int i = 0; i < rows.Count; i++)
                {
                    if (_cts.IsCancellationRequested)
                    {
                        AppendLog("已取消，后续账号未处理。");
                        break;
                    }

                    var row = rows[i];
                    progressBar.Value = i;
                    lblStatus.Text = $"正在处理 {i + 1}/{rows.Count}：{row.Account}";
                    AppendLog($"[{row.Account}] 开始");

                    var progress = new Progress<string>(msg =>
                    {
                        lblStatus.Text = $"[{row.Account}] {msg}";
                        AppendLog($"[{row.Account}] {msg}");
                    });

                    BindResult result;
                    try
                    {
                        result = await binder.BindAsync(row, _passKey, progress, _cts.Token).ConfigureAwait(true);
                    }
                    catch (OperationCanceledException)
                    {
                        result = new BindResult { Success = false, Error = "已取消" };
                    }

                    try
                    {
                        if (result.Success)
                        {
                            success++;
                            bool wasTemp = sheet.SavedToTemp;
                            sheet.WriteExtra(row.RowNumber, result.MaFileJson);
                            if (!wasTemp && sheet.SavedToTemp)
                                AppendLog("原文件被占用，已改写到临时文件：" + sheet.CurrentPath);
                            AppendLog($"[{row.Account}] 成功，已写入额外信息列");
                        }
                        else
                        {
                            fail++;
                            string reason = result.Error ?? "未知错误";
                            AppendLog($"[{row.Account}] 失败：{reason}");
                        }
                    }
                    catch (Exception ex)
                    {
                        fail++;
                        AppendLog($"[{row.Account}] 写入 Excel 失败：{ex.Message}");
                    }

                    progressBar.Value = i + 1;
                    if (i < rows.Count - 1 && !_cts.IsCancellationRequested)
                        await Task.Delay(2000, CancellationToken.None);
                }

                lblSummary.Text = $"成功 {success} / 失败 {fail}";
                if (sheet.SavedToTemp)
                {
                    lblStatus.Text = "处理结束。原文件被占用，结果已写入临时文件。";
                    AppendLog("临时文件：" + sheet.CurrentPath);
                }
                else
                {
                    lblStatus.Text = "处理结束。成功账号已写入原 Excel 额外信息列。";
                }
            }
            catch (Exception ex)
            {
                AppendLog("批处理中止：" + ex.Message);
                MessageBox.Show(this, ex.Message, "批量导入 Excel", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _running = false;
                btnStart.Enabled = true;
                btnBrowse.Enabled = true;
                btnCancelRun.Enabled = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void btnCancelRun_Click(object sender, EventArgs e)
        {
            _cts?.Cancel();
            btnCancelRun.Enabled = false;
            AppendLog("正在取消...");
        }

        private void BatchImportExcelForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_running)
            {
                e.Cancel = true;
                _cts?.Cancel();
            }
        }

        private void AppendLog(string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(AppendLog), text);
                return;
            }

            listLog.Items.Add($"{DateTime.Now:HH:mm:ss} {text}");
            listLog.TopIndex = listLog.Items.Count - 1;
        }
    }
}
