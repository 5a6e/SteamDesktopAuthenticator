using Newtonsoft.Json;
using SteamAuth;
using System;
using System.Windows.Forms;

namespace Steam_Desktop_Authenticator
{
    public partial class MaFileViewForm : Form
    {
        public MaFileViewForm(SteamGuardAccount account)
        {
            InitializeComponent();
            string name = string.IsNullOrEmpty(account?.AccountName) ? "maFile" : account.AccountName;
            Text = "查看 maFile - " + name;
            txtJson.Text = JsonConvert.SerializeObject(account, Formatting.Indented);
            txtJson.SelectionStart = 0;
            txtJson.SelectionLength = 0;
        }

        private void btnCopy_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(txtJson.Text))
                return;

            Clipboard.SetText(txtJson.Text);
            lblCopied.Text = "已复制到剪贴板";
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}
