using Newtonsoft.Json;
using SteamAuth;
using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.Internal;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Steam_Desktop_Authenticator
{
    public class BindResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public string MaFileJson { get; set; }
    }

    public class AuthenticatorBindService
    {
        public Task<BindResult> BindAsync(ExcelAccountRow row, string passKey, IProgress<string> progress, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(row.Account) || string.IsNullOrWhiteSpace(row.Password))
                return Task.FromResult(Fail("账号或密码为空"));

            progress?.Report("登录 Steam...");

            return Task.Run(() => BindOnThreadPoolAsync(row, passKey, progress, ct), ct);
        }

        private static async Task<BindResult> BindOnThreadPoolAsync(ExcelAccountRow row, string passKey, IProgress<string> progress, CancellationToken ct)
        {
            var authenticator = new BatchImapAuthenticator(row, progress, ct);
            SteamClient steamClient = null;

            try
            {
                ct.ThrowIfCancellationRequested();

                steamClient = SteamClientFactory.Create();
                steamClient.Connect();

                DateTime connectDeadline = DateTime.UtcNow.AddSeconds(AppConfig.GetSteamConnectTimeoutSeconds());
                while (!steamClient.IsConnected)
                {
                    if (DateTime.UtcNow > connectDeadline)
                        return Fail("Steam 连接超时");
                    await Task.Delay(500, ct).ConfigureAwait(false);
                }

                CredentialsAuthSession authSession;
                try
                {
                    authSession = await AwaitWithTimeout(
                        steamClient.Authentication.BeginAuthSessionViaCredentialsAsync(new AuthSessionDetails
                        {
                            Username = row.Account,
                            Password = row.Password,
                            IsPersistentSession = false,
                            PlatformType = EAuthTokenPlatformType.k_EAuthTokenPlatformType_MobileApp,
                            ClientOSType = EOSType.Android9,
                            DeviceFriendlyName = SteamDeviceName.Generate(),
                            Authenticator = authenticator,
                        }),
                        TimeSpan.FromMinutes(2),
                        ct,
                        "Steam 登录超时").ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    return Fail("Steam 登录失败 - " + ex.Message);
                }

                AuthPollResult pollResponse;
                try
                {
                    pollResponse = await AwaitWithTimeout(
                        authSession.PollingWaitForResultAsync(),
                        TimeSpan.FromMinutes(3),
                        ct,
                        "Steam 登录轮询超时").ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    return Fail("Steam 登录失败 - " + ex.Message);
                }

                var sessionData = new SessionData()
                {
                    SteamID = authSession.SteamID.ConvertToUInt64(),
                    AccessToken = pollResponse.AccessToken,
                    RefreshToken = pollResponse.RefreshToken,
                };

                try
                {
                    await authenticator.EnsureImapAsync().ConfigureAwait(false);
                    await authenticator.Imap.IgnoreLatestMessagesAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    return Fail("IMAP 连接失败 - " + ex.Message);
                }

                progress?.Report("已发起添加验证器");
                DateTimeOffset triggeredAt = DateTimeOffset.UtcNow.AddSeconds(-15);
                var linker = new AuthenticatorLinker(sessionData);
                AuthenticatorLinker.LinkResult linkResponse;
                try
                {
                    linkResponse = await AwaitWithTimeout(
                        linker.AddAuthenticator(),
                        TimeSpan.FromMinutes(1),
                        ct,
                        "发起添加验证器超时").ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    return Fail("发起添加验证器失败 - " + ex.Message);
                }

                switch (linkResponse)
                {
                    case AuthenticatorLinker.LinkResult.AwaitingFinalization:
                        break;
                    case AuthenticatorLinker.LinkResult.AuthenticatorPresent:
                        return Fail("账号已绑定令牌");
                    case AuthenticatorLinker.LinkResult.MustProvidePhoneNumber:
                        return Fail("账号需要手机号，无法仅通过邮箱完成绑定");
                    case AuthenticatorLinker.LinkResult.MustRemovePhoneNumber:
                        return Fail("账号手机号状态异常，无法自动绑定");
                    case AuthenticatorLinker.LinkResult.MustConfirmEmail:
                        return Fail("需要点击邮件确认链接，无法自动完成");
                    default:
                        return Fail("发起添加验证器失败 - " + linkResponse);
                }

                if (linker.LinkedAccount == null)
                    return Fail("发起添加验证器失败 - 未返回令牌数据");

                progress?.Report("正在收取件箱，查找「添加验证器请求」邮件...");
                string code;
                try
                {
                    code = await authenticator.Imap.WaitForAddAuthenticatorCodeAsync(triggeredAt, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return Fail("读取绑定验证码失败 - " + ex.Message);
                }

                if (string.IsNullOrEmpty(code))
                    return Fail("未找到「添加验证器请求」邮件或无法解析验证码");

                progress?.Report("已取得绑定验证码 " + code + "，正在完成绑定...");
                AuthenticatorLinker.FinalizeResult finalizeResponse;
                try
                {
                    finalizeResponse = await AwaitWithTimeout(
                        linker.FinalizeAddAuthenticator(code),
                        TimeSpan.FromMinutes(1),
                        ct,
                        "完成绑定超时").ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    return Fail("完成绑定失败 - " + ex.Message + "，验证码=" + code);
                }

                if (finalizeResponse != AuthenticatorLinker.FinalizeResult.Success)
                    return Fail("完成绑定失败 - " + finalizeResponse + "，验证码=" + code);

                Manifest manifest = Manifest.GetManifest();
                if (string.IsNullOrEmpty(passKey) || !manifest.Encrypted)
                    return Fail("必须先设置加密密钥");

                if (!manifest.SaveAccount(linker.LinkedAccount, true, passKey))
                {
                    manifest.RemoveAccount(linker.LinkedAccount);
                    return Fail("无法保存 maFile");
                }

                string json = JsonConvert.SerializeObject(linker.LinkedAccount);
                progress?.Report("成功，已写入 maFile");
                return new BindResult { Success = true, MaFileJson = json };
            }
            catch (OperationCanceledException)
            {
                return Fail("已取消");
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
            finally
            {
                authenticator.DisposeImapIfOwned();
                try { steamClient?.Disconnect(); } catch { }
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

        private static BindResult Fail(string error)
        {
            return new BindResult { Success = false, Error = error };
        }
    }
}
