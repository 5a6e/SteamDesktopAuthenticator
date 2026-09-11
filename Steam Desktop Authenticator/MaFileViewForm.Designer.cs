using System;
using System.Drawing;
using System.Windows.Forms;

namespace Steam_Desktop_Authenticator
{
    partial class MaFileViewForm
    {
        private System.ComponentModel.IContainer components = null;
        private TextBox txtJson;
        private Button btnCopy;
        private Button btnClose;
        private Label lblCopied;

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
            this.txtJson = new TextBox();
            this.btnCopy = new Button();
            this.btnClose = new Button();
            this.lblCopied = new Label();
            this.SuspendLayout();
            //
            // txtJson
            //
            this.txtJson.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            this.txtJson.Font = new Font("Consolas", 9.75F);
            this.txtJson.Location = new Point(12, 12);
            this.txtJson.Multiline = true;
            this.txtJson.Name = "txtJson";
            this.txtJson.ReadOnly = true;
            this.txtJson.ScrollBars = ScrollBars.Both;
            this.txtJson.Size = new Size(560, 360);
            this.txtJson.TabIndex = 0;
            this.txtJson.WordWrap = false;
            //
            // btnCopy
            //
            this.btnCopy.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            this.btnCopy.Location = new Point(376, 384);
            this.btnCopy.Name = "btnCopy";
            this.btnCopy.Size = new Size(100, 28);
            this.btnCopy.TabIndex = 1;
            this.btnCopy.Text = "复制全部";
            this.btnCopy.UseVisualStyleBackColor = true;
            this.btnCopy.Click += new EventHandler(this.btnCopy_Click);
            //
            // btnClose
            //
            this.btnClose.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            this.btnClose.Location = new Point(482, 384);
            this.btnClose.Name = "btnClose";
            this.btnClose.Size = new Size(90, 28);
            this.btnClose.TabIndex = 2;
            this.btnClose.Text = "关闭";
            this.btnClose.UseVisualStyleBackColor = true;
            this.btnClose.Click += new EventHandler(this.btnClose_Click);
            //
            // lblCopied
            //
            this.lblCopied.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            this.lblCopied.AutoSize = true;
            this.lblCopied.Location = new Point(12, 390);
            this.lblCopied.Name = "lblCopied";
            this.lblCopied.Size = new Size(0, 15);
            //
            // MaFileViewForm
            //
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(584, 421);
            this.Controls.Add(this.lblCopied);
            this.Controls.Add(this.btnClose);
            this.Controls.Add(this.btnCopy);
            this.Controls.Add(this.txtJson);
            this.Font = new Font("Segoe UI", 9F);
            this.MinimizeBox = false;
            this.MinimumSize = new Size(480, 320);
            this.Name = "MaFileViewForm";
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.Text = "查看 maFile";
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}
