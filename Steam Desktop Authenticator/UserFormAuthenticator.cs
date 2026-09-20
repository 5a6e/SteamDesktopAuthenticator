using SteamAuth;
using SteamKit2.Authentication;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Steam_Desktop_Authenticator
{
    internal class UserFormAuthenticator : IAuthenticator
    {
        private SteamGuardAccount account;
        private readonly Control ui;
        private readonly CancellationToken ct;
        private int deviceCodesGenerated = 0;

        public UserFormAuthenticator(SteamGuardAccount account, Control ui = null, CancellationToken ct = default)
        {
            this.account = account;
            this.ui = ui;
            this.ct = ct;
        }

        public Task<bool> AcceptDeviceConfirmationAsync()
        {
            return Task.FromResult(false);
        }

        public async Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect)
        {
            if (previousCodeWasIncorrect)
            {
                if (deviceCodesGenerated > 2)
                    RunOnUi(() => MessageBox.Show(Owner(), "使用这些两步验证码登录似乎有问题。请确认本程序仍是该账号的令牌。"));

                await Task.Delay(30000, ct).ConfigureAwait(false);
            }

            if (account == null)
            {
                RunOnUi(() => MessageBox.Show(Owner(), "该账号已绑定令牌。必须先解绑现有令牌，才能绑定本程序。", "Steam 登录", MessageBoxButtons.OK, MessageBoxIcon.Error));
                return null;
            }

            string deviceCode = await account.GenerateSteamGuardCodeAsync().ConfigureAwait(false);
            deviceCodesGenerated++;
            return deviceCode;
        }

        public Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect)
        {
            string message = "请输入发到邮箱的验证码：";
            if (previousCodeWasIncorrect)
            {
                message = "验证码不正确，请重新输入发到邮箱的验证码：";
            }

            return Task.FromResult(RunOnUi(() =>
            {
                InputForm emailForm = new InputForm(message);
                Form owner = ui as Form;
                if (owner != null && owner.IsHandleCreated)
                    emailForm.ShowDialog(owner);
                else
                    emailForm.ShowDialog();
                return emailForm.txtBox.Text;
            }));
        }

        private IWin32Window Owner()
        {
            return ui is IWin32Window w && !ui.IsDisposed ? w : null;
        }

        private bool CanUseUi()
        {
            return ui != null && ui.IsHandleCreated && !ui.IsDisposed && !ui.Disposing;
        }

        private void RunOnUi(Action action)
        {
            RunOnUi<object>(() =>
            {
                action();
                return null;
            });
        }

        private T RunOnUi<T>(Func<T> func)
        {
            if (!CanUseUi() || ct.IsCancellationRequested)
                return default;

            if (!ui.InvokeRequired)
                return func();

            T result = default;
            Exception error = null;
            using var done = new ManualResetEventSlim(false);
            try
            {
                ui.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (CanUseUi() && !ct.IsCancellationRequested)
                            result = func();
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                    finally
                    {
                        done.Set();
                    }
                }));
            }
            catch
            {
                return default;
            }

            try
            {
                done.Wait(ct);
            }
            catch (OperationCanceledException)
            {
                return default;
            }

            if (error != null)
                throw error;
            return result;
        }
    }
}
