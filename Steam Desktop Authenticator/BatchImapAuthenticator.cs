using SteamKit2.Authentication;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Steam_Desktop_Authenticator
{
    internal class BatchImapAuthenticator : IAuthenticator
    {
        private readonly ExcelAccountRow _row;
        private readonly IProgress<string> _progress;
        private readonly CancellationToken _ct;
        private SteamImapClient _imap;
        private bool _ownsImap;

        public SteamImapClient Imap => _imap;

        public BatchImapAuthenticator(ExcelAccountRow row, IProgress<string> progress, CancellationToken ct)
        {
            _row = row;
            _progress = progress;
            _ct = ct;
        }

        public void AttachImap(SteamImapClient imap)
        {
            _imap = imap;
            _ownsImap = false;
        }

        public async Task EnsureImapAsync()
        {
            if (_imap != null)
                return;
            _progress?.Report("正在连接邮箱...");
            _imap = await SteamImapClient.ConnectAsync(_row, _ct).ConfigureAwait(false);
            _ownsImap = true;
        }

        public Task<bool> AcceptDeviceConfirmationAsync()
        {
            return Task.FromResult(false);
        }

        public Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect)
        {
            throw new InvalidOperationException("账号已绑定令牌，批量导入无法覆盖现有验证器");
        }

        public async Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect)
        {
            _progress?.Report(previousCodeWasIncorrect
                ? "登录邮箱验证码不正确，正在重新收取件箱..."
                : "登录需要邮箱验证码，正在收取件箱...");

            await EnsureImapAsync();
            DateTimeOffset after = previousCodeWasIncorrect
                ? DateTimeOffset.UtcNow
                : DateTimeOffset.UtcNow.AddSeconds(-15);
            if (previousCodeWasIncorrect)
                await _imap.IgnoreLatestMessagesAsync(_ct).ConfigureAwait(false);

            string code = await _imap.WaitForLoginGuardCodeAsync(after, _ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(code))
                throw new InvalidOperationException("未在邮箱中找到 Steam 登录验证码");
            _progress?.Report("已取得登录邮箱验证码 " + code);
            return code;
        }

        public void DisposeImapIfOwned()
        {
            if (_ownsImap)
            {
                _imap?.Dispose();
                _imap = null;
                _ownsImap = false;
            }
        }
    }
}
