using System;
using System.Windows.Forms;
using SteamAuth;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Drawing;
using System.Linq;

namespace Steam_Desktop_Authenticator
{
    public partial class MainForm : Form
    {
        private SteamGuardAccount currentAccount = null;
        private SteamGuardAccount[] allAccounts;
        private List<string> updatedSessions = new List<string>();
        private Manifest manifest;
        private static SemaphoreSlim confirmationsSemaphore = new SemaphoreSlim(1, 1);

        private long steamTime = 0;
        private long currentSteamChunk = 0;
        private string passKey = null;
        private bool startSilent = false;

        // Forms
        private TradePopupForm popupFrm = new TradePopupForm();

        public MainForm()
        {
            InitializeComponent();
        }

        public void SetEncryptionKey(string key)
        {
            passKey = key;
        }

        public void StartSilent(bool silent)
        {
            startSilent = silent;
        }

        // Form event handlers

        private void MainForm_Shown(object sender, EventArgs e)
        {
            this.labelVersion.Text = String.Format("v{0}", Application.ProductVersion);
            try
            {
                this.manifest = Manifest.GetManifest();
            }
            catch (ManifestParseException)
            {
                MessageBox.Show("无法读取设置，请重启程序。", "Steam 桌面令牌", MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.Close();
            }

            // Make sure we don't show that welcome dialog again
            this.manifest.FirstRun = false;
            this.manifest.Save();

            // Tick first time manually to sync time
            timerSteamGuard_Tick(new object(), EventArgs.Empty);

            if (manifest.Encrypted)
            {
                if (passKey == null)
                {
                    passKey = manifest.PromptForPassKey();
                    if (passKey == null)
                    {
                        Application.Exit();
                        return;
                    }
                }
            }
            else
            {
                passKey = manifest.PromptSetupPassKey("必须设置加密密钥后才能使用。");
                if (passKey == null)
                {
                    Application.Exit();
                    return;
                }
            }

            btnManageEncryption.Text = "管理加密";

            btnManageEncryption.Enabled = manifest.Entries.Count > 0;

            loadSettings();
            loadAccountsList();

            if (startSilent)
            {
                this.WindowState = FormWindowState.Minimized;
            }
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            trayIcon.Icon = this.Icon;
        }

        private void MainForm_Resize(object sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized)
            {
                this.Hide();
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            Application.Exit();
        }


        // UI Button handlers

        private void btnSteamLogin_Click(object sender, EventArgs e)
        {
            string previousAccount = currentAccount?.AccountName;
            bool tradesWasEnabled = timerTradesPopup.Enabled;
            timerSteamGuard.Enabled = false;
            timerTradesPopup.Enabled = false;
            btnSteamLogin.Enabled = false;

            var ui = this;
            var thread = new Thread(() =>
            {
                LoginForm loginForm = null;
                try
                {
                    loginForm = new LoginForm(LoginForm.LoginType.Initial, null, passKey);
                    Application.Run(loginForm);
                }
                finally
                {
                    string boundName = loginForm?.BoundAccount?.AccountName;
                    try
                    {
                        ui.BeginInvoke(new Action(() =>
                        {
                            if (ui.IsDisposed)
                                return;
                            btnSteamLogin.Enabled = true;
                            timerSteamGuard.Enabled = true;
                            timerTradesPopup.Enabled = tradesWasEnabled;
                            loadAccountsList(boundName ?? previousAccount);
                        }));
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Name = "SteamLoginUI";
            thread.Start();
        }

        private void btnViewMaFile_Click(object sender, EventArgs e)
        {
            if (currentAccount == null)
            {
                MessageBox.Show(this, "请先选择一个账号。", "查看 maFile", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var form = new MaFileViewForm(currentAccount))
            {
                form.ShowDialog(this);
            }
        }

        private void btnManageEncryption_Click(object sender, EventArgs e)
        {
            InputForm currentPassKeyForm = new InputForm("请输入当前加密密钥", true);
            currentPassKeyForm.ShowDialog();

            if (currentPassKeyForm.Canceled)
            {
                return;
            }

            string curPassKey = currentPassKeyForm.txtBox.Text;
            if (!manifest.VerifyPasskey(curPassKey))
            {
                MessageBox.Show("加密密钥不正确。");
                return;
            }

            InputForm changePassKeyForm = new InputForm("请输入新的加密密钥。", true);
            changePassKeyForm.ShowDialog();

            if (changePassKeyForm.Canceled || string.IsNullOrEmpty(changePassKeyForm.txtBox.Text))
            {
                return;
            }

            InputForm changePassKeyForm2 = new InputForm("请再次输入新密钥以确认。", true);
            changePassKeyForm2.ShowDialog();

            if (changePassKeyForm2.Canceled || string.IsNullOrEmpty(changePassKeyForm2.txtBox.Text))
            {
                return;
            }

            string newPassKey = changePassKeyForm.txtBox.Text;
            string confirmPassKey = changePassKeyForm2.txtBox.Text;

            if (newPassKey != confirmPassKey)
            {
                MessageBox.Show("两次输入的加密密钥不一致。");
                return;
            }

            if (!manifest.ChangeEncryptionKey(curPassKey, newPassKey))
            {
                MessageBox.Show("无法更改加密密钥。");
            }
            else
            {
                passKey = newPassKey;
                MessageBox.Show("加密密钥已更改。");
                this.loadAccountsList();
            }
        }

        private void btnCopy_Click(object sender, EventArgs e)
        {
            CopyLoginToken();
        }


        // Tool strip menu handlers

        private void menuQuit_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void menuRemoveAccountFromManifest_Click(object sender, EventArgs e)
        {
            if (manifest.Encrypted)
            {
                MessageBox.Show("加密状态下无法从清单移除账号。", "从清单移除", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else
            {
                DialogResult res = MessageBox.Show("将从清单中移除所选账号。\n可用于把 maFile 移到另一台电脑。\n这不会删除 maFile。", "从清单移除", MessageBoxButtons.OKCancel);
                if (res == DialogResult.OK)
                {
                    manifest.RemoveAccount(currentAccount, false);
                    MessageBox.Show("已从清单移除账号。\n现在可以把 maFile 复制到其他电脑，再用「文件」菜单导入。", "从清单移除");
                    loadAccountsList();
                }
            }
        }

        private void menuLoginAgain_Click(object sender, EventArgs e)
        {
            this.PromptRefreshLogin(currentAccount);
        }

        private void menuTradeConfirmations_Click(object sender, EventArgs e)
        {
            ShowTradeConfirmations();
        }

        private void menuImportAccount_Click(object sender, EventArgs e)
        {
            ImportAccountForm currentImport_maFile_Form = new ImportAccountForm();
            currentImport_maFile_Form.ShowDialog();
            loadAccountsList();
        }

        private void btnBatchBind_Click(object sender, EventArgs e)
        {
            btnSteamLogin.Enabled = false;
            menuImportAccount.Enabled = false;
            btnBatchBind.Enabled = false;
            try
            {
                using (var form = new BatchImportExcelForm(passKey))
                {
                    form.ShowDialog(this);
                }
            }
            finally
            {
                btnSteamLogin.Enabled = true;
                menuImportAccount.Enabled = true;
                btnBatchBind.Enabled = true;
            }
            loadAccountsList();
        }

        private void menuSettings_Click(object sender, EventArgs e)
        {
            new SettingsForm().ShowDialog();
            manifest = Manifest.GetManifest(true);
            loadSettings();
        }

        private async void menuDeactivateAuthenticator_Click(object sender, EventArgs e)
        {
            if (currentAccount == null) return;

            // Check for a valid refresh token first
            if (currentAccount.Session.IsRefreshTokenExpired())
            {
                MessageBox.Show("会话已过期。请使用「当前账号」菜单中的「重新登录」。", "解绑令牌", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Check for a valid access token, refresh it if needed
            if (currentAccount.Session.IsAccessTokenExpired())
            {
                try
                {
                    await currentAccount.Session.RefreshAccessToken();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "解绑令牌错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            DialogResult res = MessageBox.Show("是否彻底移除 Steam 令牌？\n是 - 完全关闭 Steam Guard。\n否 - 改回邮箱验证。", "解绑令牌：" + currentAccount.AccountName, MessageBoxButtons.YesNoCancel);
            int scheme = 0;
            if (res == DialogResult.Yes)
            {
                scheme = 2;
            }
            else if (res == DialogResult.No)
            {
                scheme = 1;
            }
            else if (res == DialogResult.Cancel)
            {
                scheme = 0;
            }

            if (scheme != 0)
            {
                string confCode = currentAccount.GenerateSteamGuardCode();
                InputForm confirmationDialog = new InputForm(String.Format("正在从 {0} 移除 Steam 令牌。请输入此确认码：{1}", currentAccount.AccountName, confCode));
                confirmationDialog.ShowDialog();

                if (confirmationDialog.Canceled)
                {
                    return;
                }

                string enteredCode = confirmationDialog.txtBox.Text.ToUpper();
                if (enteredCode != confCode)
                {
                    MessageBox.Show("确认码不一致，未移除 Steam 令牌。");
                    return;
                }

                bool success = await currentAccount.DeactivateAuthenticator(scheme);
                if (success)
                {
                    MessageBox.Show(String.Format("Steam 令牌已{0}。点确定后将删除 maFile。如需备份请现在操作。", (scheme == 2 ? "完全移除" : "改回邮箱验证")));
                    this.manifest.RemoveAccount(currentAccount);
                    this.loadAccountsList();
                }
                else
                {
                    MessageBox.Show("解绑 Steam 令牌失败。");
                }
            }
            else
            {
                MessageBox.Show("未移除 Steam 令牌，未做任何更改。");
            }
        }

        // Tray menu handlers
        private void trayIcon_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            trayRestore_Click(sender, EventArgs.Empty);
        }

        private void trayRestore_Click(object sender, EventArgs e)
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
        }

        private void trayQuit_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void trayTradeConfirmations_Click(object sender, EventArgs e)
        {
            ShowTradeConfirmations();
        }

        private void ShowTradeConfirmations()
        {
            if (currentAccount == null) return;

            ConfirmationFormWeb confirms = new ConfirmationFormWeb(currentAccount);
            confirms.Show();
        }

        private void trayCopySteamGuard_Click(object sender, EventArgs e)
        {
            if (txtLoginToken.Text != "")
            {
                Clipboard.SetText(txtLoginToken.Text);
            }
        }

        private void trayAccountMenuItem_Click(object sender, EventArgs e)
        {
            if (sender is ToolStripMenuItem item)
            {
                int idx = listAccounts.Items.IndexOf(item.Text);
                if (idx >= 0)
                    listAccounts.SelectedIndex = idx;
            }
        }

        private void RefreshTrayAccountMenu()
        {
            trayAccountList.DropDownItems.Clear();
            foreach (string name in listAccounts.Items)
            {
                var item = new ToolStripMenuItem(name);
                item.Click += trayAccountMenuItem_Click;
                if (currentAccount != null && currentAccount.AccountName == name)
                    item.Checked = true;
                trayAccountList.DropDownItems.Add(item);
            }
            trayAccountList.Enabled = trayAccountList.DropDownItems.Count > 0;
        }


        // Misc UI handlers
        private void listAccounts_SelectedValueChanged(object sender, EventArgs e)
        {
            for (int i = 0; i < allAccounts.Length; i++)
            {
                // Check if index is out of bounds first
                if (i < 0 || listAccounts.SelectedIndex < 0)
                    continue;

                SteamGuardAccount account = allAccounts[i];
                if (account.AccountName == (string)listAccounts.Items[listAccounts.SelectedIndex])
                {
                    currentAccount = account;
                    loadAccountInfo();
                    RefreshTrayAccountMenu();
                    break;
                }
            }
        }

        private void txtAccSearch_TextChanged(object sender, EventArgs e)
        {
            List<string> names = new List<string>(getAllNames());
            names = names.FindAll(new Predicate<string>(IsFilter));

            listAccounts.Items.Clear();
            listAccounts.Items.AddRange(names.ToArray());
            RefreshTrayAccountMenu();
        }


        // Timers

        private async void timerSteamGuard_Tick(object sender, EventArgs e)
        {
            lblStatus.Text = "正在与 Steam 同步时间...";
            steamTime = await TimeAligner.GetSteamTimeAsync();
            lblStatus.Text = "";

            currentSteamChunk = steamTime / 30L;
            int secondsUntilChange = (int)(steamTime - (currentSteamChunk * 30L));

            loadAccountInfo();
            if (currentAccount != null)
            {
                pbTimeout.Value = 30 - secondsUntilChange;
            }
        }

        private async void timerTradesPopup_Tick(object sender, EventArgs e)
        {
            if (currentAccount == null || popupFrm.Visible) return;
            if (!confirmationsSemaphore.Wait(0))
            {
                return; //Only one thread may access this critical section at once. Mutex is a bad choice here because it'll cause a pileup of threads.
            }

            List<Confirmation> confs = new List<Confirmation>();
            Dictionary<SteamGuardAccount, List<Confirmation>> autoAcceptConfirmations = new Dictionary<SteamGuardAccount, List<Confirmation>>();

            SteamGuardAccount[] accs =
                manifest.CheckAllAccounts ? allAccounts : new SteamGuardAccount[] { currentAccount };

            try
            {
                lblStatus.Text = "正在检查确认...";

                foreach (var acc in accs)
                {
                    // Check for a valid refresh token first
                    if (acc.Session.IsRefreshTokenExpired())
                    {
                        MessageBox.Show("账号 " + acc.AccountName + " 的会话已过期，将提示你重新登录。", "交易确认", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        PromptRefreshLogin(acc);
                        break;
                    }

                    // Check for a valid access token, refresh it if needed
                    if (acc.Session.IsAccessTokenExpired())
                    {
                        try
                        {
                            lblStatus.Text = "正在刷新会话...";
                            await acc.Session.RefreshAccessToken();
                            lblStatus.Text = "正在检查确认...";
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(ex.Message, "Steam 登录错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            break;
                        }
                    }

                    try
                    {
                        Confirmation[] tmp = await acc.FetchConfirmationsAsync();
                        foreach (var conf in tmp)
                        {
                            if ((conf.ConfType == Confirmation.EMobileConfirmationType.MarketListing && manifest.AutoConfirmMarketTransactions) ||
                                (conf.ConfType == Confirmation.EMobileConfirmationType.Trade && manifest.AutoConfirmTrades))
                            {
                                if (!autoAcceptConfirmations.ContainsKey(acc))
                                    autoAcceptConfirmations[acc] = new List<Confirmation>();
                                autoAcceptConfirmations[acc].Add(conf);
                            }
                            else
                                confs.Add(conf);
                        }
                    }
                    catch (Exception)
                    {

                    }
                }

                lblStatus.Text = "";

                if (confs.Count > 0)
                {
                    popupFrm.Confirmations = confs.ToArray();
                    popupFrm.Popup();
                }
                if (autoAcceptConfirmations.Count > 0)
                {
                    foreach (var acc in autoAcceptConfirmations.Keys)
                    {
                        var confirmations = autoAcceptConfirmations[acc].ToArray();
                        await acc.AcceptMultipleConfirmations(confirmations);
                    }
                }
            }
            catch (SteamGuardAccount.WGTokenInvalidException)
            {
                lblStatus.Text = "";
            }

            confirmationsSemaphore.Release();
        }

        // Other methods

        private void CopyLoginToken()
        {
            string text = txtLoginToken.Text;
            if (String.IsNullOrEmpty(text))
                return;
            Clipboard.SetText(text);
        }

        /// <summary>
        /// Display a login form to the user to refresh their OAuth Token
        /// </summary>
        /// <param name="account">The account to refresh</param>
        private void PromptRefreshLogin(SteamGuardAccount account)
        {
            var loginForm = new LoginForm(LoginForm.LoginType.Refresh, account, passKey);
            loginForm.ShowDialog();
        }

        /// <summary>
        /// Load UI with the current account info, this is run every second
        /// </summary>
        private void loadAccountInfo()
        {
            if (currentAccount != null && steamTime != 0)
            {
                popupFrm.Account = currentAccount;
                txtLoginToken.Text = currentAccount.GenerateSteamGuardCodeForTime(steamTime);
                groupAccount.Text = "账号: " + currentAccount.AccountName;
            }
        }

        /// <summary>
        /// Decrypts files and populates list UI with accounts
        /// </summary>
        private void loadAccountsList(string selectAccountName = null)
        {
            currentAccount = null;

            listAccounts.Items.Clear();
            listAccounts.SelectedIndex = -1;

            allAccounts = manifest.GetAllAccounts(passKey);

            if (allAccounts.Length > 0)
            {
                for (int i = 0; i < allAccounts.Length; i++)
                {
                    SteamGuardAccount account = allAccounts[i];
                    listAccounts.Items.Add(account.AccountName);
                }

                listAccounts.Sorted = true;

                int selectIndex = 0;
                if (!string.IsNullOrEmpty(selectAccountName))
                {
                    int found = listAccounts.Items.IndexOf(selectAccountName);
                    if (found >= 0)
                        selectIndex = found;
                }
                listAccounts.SelectedIndex = selectIndex;
            }

            RefreshTrayAccountMenu();
            bool hasAccounts = allAccounts.Length > 0;
            menuTradeConfirmations.Enabled = menuDeactivateAuthenticator.Enabled = btnViewMaFile.Enabled = hasAccounts;
            btnManageEncryption.Enabled = hasAccounts;
        }

        private void listAccounts_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control)
            {
                if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
                {
                    int to = listAccounts.SelectedIndex - (e.KeyCode == Keys.Up ? 1 : -1);
                    manifest.MoveEntry(listAccounts.SelectedIndex, to);
                    loadAccountsList();
                }
                return;
            }

            if (!IsKeyAChar(e.KeyCode) && !IsKeyADigit(e.KeyCode))
            {
                return;
            }

            txtAccSearch.Focus();
            txtAccSearch.Text = e.KeyCode.ToString();
            txtAccSearch.SelectionStart = 1;
        }

        private static bool IsKeyAChar(Keys key)
        {
            return key >= Keys.A && key <= Keys.Z;
        }

        private static bool IsKeyADigit(Keys key)
        {
            return (key >= Keys.D0 && key <= Keys.D9) || (key >= Keys.NumPad0 && key <= Keys.NumPad9);
        }

        private bool IsFilter(string f)
        {
            if (txtAccSearch.Text.StartsWith("~"))
            {
                try
                {
                    return Regex.IsMatch(f, txtAccSearch.Text);
                }
                catch (Exception)
                {
                    return true;
                }

            }
            else
            {
                return f.Contains(txtAccSearch.Text.ToLower());
            }
        }

        private string[] getAllNames()
        {
            string[] itemArray = new string[allAccounts.Length];
            for (int i = 0; i < itemArray.Length; i++)
            {
                itemArray[i] = allAccounts[i].AccountName;
            }
            return itemArray;
        }

        private void loadSettings()
        {
            timerTradesPopup.Enabled = manifest.PeriodicChecking;
            timerTradesPopup.Interval = manifest.PeriodicCheckingInterval * 1000;
        }

        private void MainForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.C && e.Modifiers == Keys.Control)
            {
                CopyLoginToken();
            }
        }

        private void panelButtons_SizeChanged(object sender, EventArgs e)
        {
            int totButtons = panelButtons.Controls.OfType<Button>().Count();

            Point curPos = new Point(0, 0);
            foreach (Button but in panelButtons.Controls.OfType<Button>())
            {
                but.Width = panelButtons.Width / totButtons;
                but.Location = curPos;
                curPos = new Point(curPos.X + but.Width, 0);
            }
        }
    }
}
