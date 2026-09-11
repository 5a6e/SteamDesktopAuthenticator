using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace Steam_Desktop_Authenticator
{
    partial class BatchImportExcelForm
    {
        private IContainer components = null;
        private TextBox txtPath;
        private Button btnBrowse;
        private Button btnStart;
        private Button btnCancelRun;
        private ProgressBar progressBar;
        private Label lblStatus;
        private Label lblSummary;
        private ListBox listLog;
        private Label lblFile;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.txtPath = new TextBox();
            this.btnBrowse = new Button();
            this.btnStart = new Button();
            this.btnCancelRun = new Button();
            this.progressBar = new ProgressBar();
            this.lblStatus = new Label();
            this.lblSummary = new Label();
            this.listLog = new ListBox();
            this.lblFile = new Label();
            this.SuspendLayout();
            //
            // lblFile
            //
            this.lblFile.AutoSize = true;
            this.lblFile.Location = new Point(12, 15);
            this.lblFile.Name = "lblFile";
            this.lblFile.Size = new Size(79, 15);
            this.lblFile.Text = "Excel 文件";
            //
            // txtPath
            //
            this.txtPath.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            this.txtPath.Location = new Point(97, 12);
            this.txtPath.Name = "txtPath";
            this.txtPath.ReadOnly = true;
            this.txtPath.Size = new Size(430, 23);
            this.txtPath.TabIndex = 0;
            //
            // btnBrowse
            //
            this.btnBrowse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            this.btnBrowse.Location = new Point(533, 11);
            this.btnBrowse.Name = "btnBrowse";
            this.btnBrowse.Size = new Size(75, 25);
            this.btnBrowse.TabIndex = 1;
            this.btnBrowse.Text = "浏览...";
            this.btnBrowse.UseVisualStyleBackColor = true;
            this.btnBrowse.Click += new EventHandler(this.btnBrowse_Click);
            //
            // btnStart
            //
            this.btnStart.Location = new Point(12, 47);
            this.btnStart.Name = "btnStart";
            this.btnStart.Size = new Size(100, 28);
            this.btnStart.TabIndex = 2;
            this.btnStart.Text = "开始";
            this.btnStart.UseVisualStyleBackColor = true;
            this.btnStart.Click += new EventHandler(this.btnStart_Click);
            //
            // btnCancelRun
            //
            this.btnCancelRun.Enabled = false;
            this.btnCancelRun.Location = new Point(118, 47);
            this.btnCancelRun.Name = "btnCancelRun";
            this.btnCancelRun.Size = new Size(100, 28);
            this.btnCancelRun.TabIndex = 3;
            this.btnCancelRun.Text = "取消";
            this.btnCancelRun.UseVisualStyleBackColor = true;
            this.btnCancelRun.Click += new EventHandler(this.btnCancelRun_Click);
            //
            // lblSummary
            //
            this.lblSummary.AutoSize = true;
            this.lblSummary.Location = new Point(230, 53);
            this.lblSummary.Name = "lblSummary";
            this.lblSummary.Size = new Size(0, 15);
            //
            // progressBar
            //
            this.progressBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            this.progressBar.Location = new Point(12, 84);
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new Size(596, 18);
            this.progressBar.TabIndex = 4;
            //
            // lblStatus
            //
            this.lblStatus.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            this.lblStatus.Location = new Point(12, 108);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new Size(596, 20);
            this.lblStatus.Text = "选择 Excel 后点击开始。成功写入原表额外信息列；原文件被占用则写入临时文件。";
            //
            // listLog
            //
            this.listLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            this.listLog.FormattingEnabled = true;
            this.listLog.HorizontalScrollbar = true;
            this.listLog.ItemHeight = 15;
            this.listLog.Location = new Point(12, 132);
            this.listLog.Name = "listLog";
            this.listLog.Size = new Size(596, 274);
            this.listLog.TabIndex = 5;
            //
            // BatchImportExcelForm
            //
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(620, 421);
            this.Controls.Add(this.listLog);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.progressBar);
            this.Controls.Add(this.lblSummary);
            this.Controls.Add(this.btnCancelRun);
            this.Controls.Add(this.btnStart);
            this.Controls.Add(this.btnBrowse);
            this.Controls.Add(this.txtPath);
            this.Controls.Add(this.lblFile);
            this.Font = new Font("Segoe UI", 9F);
            this.MinimizeBox = false;
            this.MinimumSize = new Size(520, 360);
            this.Name = "BatchImportExcelForm";
            this.StartPosition = FormStartPosition.CenterParent;
            this.Text = "批量绑定令牌";
            this.FormClosing += new FormClosingEventHandler(this.BatchImportExcelForm_FormClosing);
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}
