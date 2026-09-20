using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SteamAuth;
using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.Internal;

namespace Steam_Desktop_Authenticator
{
    public partial class LoginForm : Form
    {
        public SteamGuardAccount account;
        public LoginType LoginReason;
        public SessionData Session;
        public SteamGuardAccount BoundAccount { get; private set; }

        private readonly string existingPassKey;
        private CancellationTokenSource loginCts;
        private SteamClient steamClient;
        private SteamGuardAccount pendingLinkAccount;
        private bool linkFinalized;

        public LoginForm(LoginType loginReason = LoginType.Initial, SteamGuardAccount account = null, string passKey = null)
        {
            InitializeComponent();
            this.LoginReason = loginReason;
            this.account = account;
            this.existingPassKey = passKey;
            this.FormClosing += LoginForm_FormClosing;

            try
            {
                if (this.LoginReason != LoginType.Initial)
                {
                    txtUsername.Text = account.AccountName;
                    txtUsername.Enabled = false;
                }

                if (this.LoginReason == LoginType.Refresh)
                {
                    labelLoginExplanation.Text = "登录凭据已过期。要正常处理交易和市场确认，请重新登录。";
                }
                else if (this.LoginReason == LoginType.Import)
                {
                    labelLoginExplanation.Text = "请登录 Steam 账号以导入。";
                }
            }
            catch (Exception)
            {
                MessageBox.Show("找不到该账号，请关闭后重新打开程序。", "登录失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.Close();
            }
        }

        public void SetUsername(string username)
        {
            txtUsername.Text = username;
        }

        public string FilterPhoneNumber(string phoneNumber)
        {
            return phoneNumber.Replace("-", "").Replace("(", "").Replace(")", "");
        }

        public bool PhoneNumberOkay(string phoneNumber)
        {
            if (phoneNumber == null || phoneNumber.Length == 0) return false;
            if (phoneNumber[0] != '+') return false;
            return true;
        }

        private void ResetLoginButton()
        {
            btnSteamLogin.Enabled = true;
            btnSteamLogin.Text = "登录";
        }

        private void DiscardPendingLink()
        {
            if (linkFinalized || pendingLinkAccount == null)
                return;

            SteamGuardAccount acc = pendingLinkAccount;
            pendingLinkAccount = null;
            try
            {
                Manifest.GetManifest().RemoveAccount(acc);
            }
            catch
            {
            }
        }

        private void LoginForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            loginCts?.Cancel();
            DiscardPendingLink();
            SteamClient client = steamClient;
            steamClient = null;
            if (client == null)
                return;

            // Never Disconnect on the UI thread — SteamKit can callback into WinForms and deadlock.
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { client.Disconnect(); } catch { }
            });
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            loginCts?.Cancel();
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }

        private void btnSteamLogin_Click(object sender, EventArgs e)
        {
            btnSteamLogin.Enabled = false;
            btnSteamLogin.Text = "登录中...";
            btnCancel.Enabled = true;
            btnCancel.Focus();

            string username = txtUsername.Text;
            string password = txtPassword.Text;

            loginCts?.Cancel();
            loginCts = new CancellationTokenSource();
            CancellationToken ct = loginCts.Token;

            _ = Task.Factory.StartNew(async () =>
            {
                SynchronizationContext.SetSynchronizationContext(new ThreadPoolSyncContext());
                try
                {
                    SessionData sessionData = await LoginSteamAsync(username, password, ct).ConfigureAwait(false);
                    PostToUi(() => ContinueAfterSteamLogin(sessionData, ct));
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    PostToUi(() =>
                    {
                        if (IsDisposed)
                            return;
                        MessageBox.Show(this, ex.Message, "Steam 登录错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        ResetLoginButton();
                    });
                }
            }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
        }

        private void PostToUi(Action action)
        {
            if (IsDisposed || !IsHandleCreated)
                return;
            try
            {
                BeginInvoke(action);
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private async void ContinueAfterSteamLogin(SessionData sessionData, CancellationToken ct)
        {
            if (IsDisposed || ct.IsCancellationRequested)
                return;

            this.Session = sessionData;

            // If we're only logging in for an account import, stop here
            if (LoginReason == LoginType.Import)
            {
                this.Close();
                return;
            }

            // If we're only logging in for a session refresh then save it and exit
            if (LoginReason == LoginType.Refresh)
            {
                Manifest man = Manifest.GetManifest();
                account.FullyEnrolled = true;
                account.Session = sessionData;
                HandleManifest(man, true);
                this.Close();
                return;
            }

            // Begin linking mobile authenticator
            AuthenticatorLinker linker = new AuthenticatorLinker(sessionData);

            AuthenticatorLinker.LinkResult linkResponse = AuthenticatorLinker.LinkResult.GeneralFailure;
            while (linkResponse != AuthenticatorLinker.LinkResult.AwaitingFinalization)
            {
                try
                {
                    linkResponse = await Task.Run(() => linker.AddAuthenticator(), ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    if (IsDisposed)
                        return;
                    MessageBox.Show("绑定令牌时出错：" + ex.Message, "Steam 登录", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    ResetLoginButton();
                    return;
                }

                switch (linkResponse)
                {
                    case AuthenticatorLinker.LinkResult.MustProvidePhoneNumber:

                        // Show the phone input form
                        PhoneInputForm phoneInputForm = new PhoneInputForm(account);
                        phoneInputForm.ShowDialog();
                        if (phoneInputForm.Canceled)
                        {
                            this.Close();
                            return;
                        }

                        linker.PhoneNumber = phoneInputForm.PhoneNumber;
                        linker.PhoneCountryCode = phoneInputForm.CountryCode;
                        break;

                    case AuthenticatorLinker.LinkResult.AuthenticatorPresent:
                        MessageBox.Show("该账号已绑定令牌。必须先解绑现有令牌，才能绑定本程序。", "Steam 登录", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        this.Close();
                        return;

                    case AuthenticatorLinker.LinkResult.FailureAddingPhone:
                        MessageBox.Show("添加手机号失败。请重试或换一个号码。", "Steam 登录", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        linker.PhoneNumber = null;
                        break;

                    case AuthenticatorLinker.LinkResult.MustRemovePhoneNumber:
                        linker.PhoneNumber = null;
                        break;

                    case AuthenticatorLinker.LinkResult.MustConfirmEmail:
                        MessageBox.Show("请先查看邮箱，点击 Steam 发送的确认链接后再继续。", "Steam 登录", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        break;

                    case AuthenticatorLinker.LinkResult.GeneralFailure:
                        MessageBox.Show("绑定令牌失败。", "Steam 登录错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        this.Close();
                        return;
                }
            } // End while loop checking for AwaitingFinalization

            Manifest manifest = Manifest.GetManifest();
            string passKey = GetPassKey(manifest);
            if (passKey == null)
            {
                this.Close();
                return;
            }

            // Keep the secret on disk in case of crash, but Steam is not bound until Finalize succeeds.
            pendingLinkAccount = linker.LinkedAccount;
            if (!manifest.SaveAccount(linker.LinkedAccount, true, passKey))
            {
                DiscardPendingLink();
                MessageBox.Show("无法保存令牌文件，尚未完成绑定。");
                this.Close();
                return;
            }

            AuthenticatorLinker.FinalizeResult finalizeResponse = AuthenticatorLinker.FinalizeResult.GeneralFailure;
            try
            {
                while (finalizeResponse != AuthenticatorLinker.FinalizeResult.Success)
                {
                    InputForm smsCodeForm = new InputForm("请输入绑定验证码（短信或「添加验证器」邮件里的代码，不是登录验证码）。");
                    smsCodeForm.ShowDialog(this);
                    if (smsCodeForm.Canceled)
                    {
                        DiscardPendingLink();
                        this.Close();
                        return;
                    }

                    string smsCode = smsCodeForm.txtBox.Text;
                    finalizeResponse = await Task.Run(() => linker.FinalizeAddAuthenticator(smsCode), ct);

                    switch (finalizeResponse)
                    {
                        case AuthenticatorLinker.FinalizeResult.BadSMSCode:
                            continue;

                        case AuthenticatorLinker.FinalizeResult.UnableToGenerateCorrectCodes:
                            DiscardPendingLink();
                            MessageBox.Show("无法生成正确的验证码来完成绑定。令牌尚未在 Steam 上生效。");
                            this.Close();
                            return;

                        case AuthenticatorLinker.FinalizeResult.GeneralFailure:
                            DiscardPendingLink();
                            MessageBox.Show("无法完成令牌绑定。令牌尚未在 Steam 上生效。");
                            this.Close();
                            return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                DiscardPendingLink();
                return;
            }
            catch (Exception ex)
            {
                DiscardPendingLink();
                MessageBox.Show("完成绑定时出错：" + ex.Message, "Steam 登录", MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.Close();
                return;
            }

            linkFinalized = true;
            pendingLinkAccount = null;
            BoundAccount = linker.LinkedAccount;
            manifest.SaveAccount(linker.LinkedAccount, true, passKey);
            MessageBox.Show("手机令牌绑定成功。");
            this.Close();
        }

        private async Task<SessionData> LoginSteamAsync(string username, string password, CancellationToken ct)
        {
            steamClient = new SteamClient();
            using var pumpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Task pumpTask = PumpCallbacksAsync(steamClient, pumpCts.Token);
            try
            {
                steamClient.Connect();

                DateTime connectDeadline = DateTime.UtcNow.AddSeconds(10);
                while (!steamClient.IsConnected)
                {
                    ct.ThrowIfCancellationRequested();
                    if (DateTime.UtcNow > connectDeadline)
                        throw new TimeoutException("Steam 连接超时，请检查网络后重试。");
                    await Task.Delay(200, ct).ConfigureAwait(false);
                }

                CredentialsAuthSession authSession = await AwaitWithTimeout(
                    steamClient.Authentication.BeginAuthSessionViaCredentialsAsync(new AuthSessionDetails
                    {
                        Username = username,
                        Password = password,
                        IsPersistentSession = false,
                        PlatformType = EAuthTokenPlatformType.k_EAuthTokenPlatformType_MobileApp,
                        ClientOSType = EOSType.Android9,
                        DeviceFriendlyName = SteamDeviceName.Generate(),
                        Authenticator = new UserFormAuthenticator(this.account, this, ct),
                    }),
                    TimeSpan.FromMinutes(2),
                    ct,
                    "Steam 登录超时").ConfigureAwait(false);

                AuthPollResult pollResponse = await AwaitWithTimeout(
                    authSession.PollingWaitForResultAsync(),
                    TimeSpan.FromMinutes(5),
                    ct,
                    "Steam 登录等待超时").ConfigureAwait(false);

                return new SessionData()
                {
                    SteamID = authSession.SteamID.ConvertToUInt64(),
                    AccessToken = pollResponse.AccessToken,
                    RefreshToken = pollResponse.RefreshToken,
                };
            }
            finally
            {
                pumpCts.Cancel();
                try { await pumpTask.ConfigureAwait(false); } catch { }
            }
        }

        private static async Task PumpCallbacksAsync(SteamClient client, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                    await client.WaitForCallbackAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private static async Task<T> AwaitWithTimeout<T>(Task<T> task, TimeSpan timeout, CancellationToken ct, string timeoutMessage)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);
            Task completed = await Task.WhenAny(task, Task.Delay(Timeout.Infinite, timeoutCts.Token)).ConfigureAwait(false);
            if (completed != task)
            {
                ct.ThrowIfCancellationRequested();
                throw new TimeoutException(timeoutMessage);
            }

            return await task.ConfigureAwait(false);
        }

        private string GetPassKey(Manifest man)
        {
            if (!string.IsNullOrEmpty(existingPassKey))
                return existingPassKey;
            return man.RequirePassKey();
        }

        private void HandleManifest(Manifest man, bool IsRefreshing = false)
        {
            string passKey = GetPassKey(man);
            if (passKey == null)
            {
                this.Close();
                return;
            }

            man.SaveAccount(account, true, passKey);
            if (IsRefreshing)
            {
                MessageBox.Show("会话已刷新。", "Steam 登录", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show("手机令牌绑定成功。", "Steam 登录", MessageBoxButtons.OK);
            }
            this.Close();
        }

        private void LoginForm_Load(object sender, EventArgs e)
        {
            if (account != null && account.AccountName != null)
            {
                txtUsername.Text = account.AccountName;
            }

            CenterToScreen();
        }

        public enum LoginType
        {
            Initial,
            Refresh,
            Import
        }

        /// <summary>
        /// Runs SteamKit callbacks on the calling thread/thread-pool instead of WinForms UI.
        /// </summary>
        private sealed class ThreadPoolSyncContext : SynchronizationContext
        {
            public override void Post(SendOrPostCallback d, object state)
            {
                ThreadPool.QueueUserWorkItem(_ => d(state));
            }

            public override void Send(SendOrPostCallback d, object state)
            {
                d(state);
            }

            public override SynchronizationContext CreateCopy()
            {
                return this;
            }
        }
    }
}
