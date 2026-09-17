using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using MimeKit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Steam_Desktop_Authenticator
{
    public class SteamImapClient : IDisposable
    {
        private static readonly Regex LoginLabelCodeRegex = new Regex(
            @"(?:登录代码|登录验证码|Steam\s*令牌验证码|login\s*code)[:：\s]*[\r\n]+\s*([A-Za-z0-9]{5})\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex AuthCodeBlockRegex = new Regex(
            @"<!--\s*Auth Code\s*-->(.*?)<!--\s*END Auth Code\s*-->",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex AuthCodeCellRegex = new Regex(
            @"<td[^>]*(?:title-48|c-blue1|font-size:\s*48px)[^>]*>\s*([A-Za-z0-9]{4,8})\s*</td>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private const int FetchLatest = 5;
        private readonly ImapClient _client = new ImapClient();
        private readonly HashSet<string> _seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static string InferHost(string imapHost, string email)
        {
            if (!string.IsNullOrWhiteSpace(imapHost))
                return imapHost.Trim();

            string domain = (email ?? "").Contains("@") ? email.Split('@').Last().ToLowerInvariant() : "";
            return domain switch
            {
                "qq.com" or "vip.qq.com" or "foxmail.com" => "imap.qq.com",
                "163.com" => "imap.163.com",
                "126.com" => "imap.126.com",
                "yeah.net" => "imap.yeah.net",
                "gmail.com" => "imap.gmail.com",
                "outlook.com" or "hotmail.com" or "live.com" => "outlook.office365.com",
                "sina.com" or "sina.cn" => "imap.sina.com",
                _ => domain.Length > 0 ? "imap." + domain : imapHost
            };
        }

        public static async Task<SteamImapClient> ConnectAsync(ExcelAccountRow row, CancellationToken ct)
        {
            Exception lastError = null;

            var attempts = new List<(string email, string password, string imapHost)>();
            if (!string.IsNullOrWhiteSpace(row.Email) && !string.IsNullOrWhiteSpace(row.EmailPassword))
                attempts.Add((row.Email, row.EmailPassword, row.ImapHost));
            if (!string.IsNullOrWhiteSpace(row.BackupEmail) && !string.IsNullOrWhiteSpace(row.BackupEmailPassword))
                attempts.Add((row.BackupEmail, row.BackupEmailPassword, row.BackupImapHost));

            if (attempts.Count == 0)
                throw new InvalidOperationException("未填写邮箱或邮箱密码");

            foreach (var attempt in attempts)
            {
                string hostSpec = InferHost(attempt.imapHost, attempt.email);
                if (string.IsNullOrWhiteSpace(hostSpec))
                {
                    lastError = new InvalidOperationException("未填写邮箱地址（IMAP 主机）");
                    continue;
                }

                var (host, specifiedPort) = ParseHostPort(hostSpec);
                foreach (var endpoint in GetEndpoints(specifiedPort))
                {
                    var client = new SteamImapClient();
                    try
                    {
                        client._client.CheckCertificateRevocation = false;
                        client._client.ServerCertificateValidationCallback = (sender, certificate, chain, errors) => true;
                        await client._client.ConnectAsync(host, endpoint.Port, endpoint.Options, ct);
                        await client._client.AuthenticateAsync(attempt.email, attempt.password, ct);
                        return client;
                    }
                    catch (OperationCanceledException)
                    {
                        client.Dispose();
                        throw;
                    }
                    catch (Exception ex)
                    {
                        client.Dispose();
                        lastError = ex;
                    }
                }
            }

            throw new InvalidOperationException("IMAP 连接失败: " + (lastError?.Message ?? "未知错误"));
        }

        private static (string Host, int? Port) ParseHostPort(string raw)
        {
            raw = (raw ?? "").Trim();
            int scheme = raw.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0)
                raw = raw[(scheme + 3)..];

            int slash = raw.IndexOf('/');
            if (slash >= 0)
                raw = raw[..slash];

            if (raw.StartsWith("["))
            {
                int end = raw.IndexOf(']');
                if (end > 0)
                {
                    string ipv6 = raw[1..end];
                    string rest = raw[(end + 1)..];
                    if (rest.StartsWith(":") && int.TryParse(rest[1..], out int ipv6Port))
                        return (ipv6, ipv6Port);
                    return (ipv6, null);
                }
            }

            int colon = raw.LastIndexOf(':');
            if (colon > 0 && int.TryParse(raw[(colon + 1)..], out int port) && port > 0 && port <= 65535)
                return (raw[..colon], port);

            return (raw, null);
        }

        private static IEnumerable<(int Port, SecureSocketOptions Options)> GetEndpoints(int? specifiedPort)
        {
            if (specifiedPort.HasValue)
            {
                int port = specifiedPort.Value;
                yield return (port, SecureSocketOptions.Auto);
                yield return (port, SecureSocketOptions.SslOnConnect);
                yield return (port, SecureSocketOptions.StartTls);
                yield return (port, SecureSocketOptions.StartTlsWhenAvailable);
                yield return (port, SecureSocketOptions.None);
                yield break;
            }

            yield return (993, SecureSocketOptions.SslOnConnect);
            yield return (143, SecureSocketOptions.StartTls);
            yield return (143, SecureSocketOptions.StartTlsWhenAvailable);
        }

        public async Task IgnoreLatestMessagesAsync(CancellationToken ct)
        {
            var inbox = _client.Inbox;
            if (!inbox.IsOpen)
                await inbox.OpenAsync(FolderAccess.ReadOnly, ct);

            if (inbox.Count == 0)
                return;

            int take = Math.Min(FetchLatest, inbox.Count);
            int start = inbox.Count - take;
            IList<IMessageSummary> summaries = await inbox.FetchAsync(start, -1, MessageSummaryItems.UniqueId, ct);
            foreach (var summary in summaries)
                _seenIds.Add(summary.UniqueId.ToString());
        }

        public async Task<string> WaitForAddAuthenticatorCodeAsync(DateTimeOffset afterUtc, CancellationToken ct)
        {
            return await WaitForCodeAsync(afterUtc, IsAddAuthenticatorMail, ExtractBindCode, ct);
        }

        public async Task<string> WaitForLoginGuardCodeAsync(DateTimeOffset afterUtc, CancellationToken ct)
        {
            return await WaitForCodeAsync(afterUtc, IsLoginGuardMail, ExtractLoginCode, ct);
        }

        private async Task<string> WaitForCodeAsync(
            DateTimeOffset afterUtc,
            Func<string, string, string, bool> matcher,
            Func<string, string, string> extractCode,
            CancellationToken ct)
        {
            var inbox = _client.Inbox;
            if (!inbox.IsOpen)
                await inbox.OpenAsync(FolderAccess.ReadOnly, ct);

            DateTime deadline = DateTime.UtcNow.AddSeconds(90);
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    await inbox.CheckAsync(ct);
                }
                catch
                {
                    await inbox.OpenAsync(FolderAccess.ReadOnly, ct);
                }

                if (inbox.Count == 0)
                {
                    await Task.Delay(3000, ct);
                    continue;
                }

                int take = Math.Min(FetchLatest, inbox.Count);
                int start = inbox.Count - take;
                IList<IMessageSummary> summaries = await inbox.FetchAsync(
                    start, -1,
                    MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.InternalDate,
                    ct);

                foreach (var summary in summaries.Reverse())
                {
                    ct.ThrowIfCancellationRequested();
                    string id = summary.UniqueId.ToString();
                    if (_seenIds.Contains(id))
                        continue;

                    if (IsOlderThanTrigger(summary, afterUtc))
                    {
                        _seenIds.Add(id);
                        continue;
                    }

                    var message = await inbox.GetMessageAsync(summary.UniqueId, ct);
                    if (IsOlderThanTrigger(message, afterUtc))
                    {
                        _seenIds.Add(id);
                        continue;
                    }
                    string subject = message.Subject ?? summary.Envelope?.Subject ?? "";
                    string from = FormatFrom(message);
                    ExtractBodies(message, out string text, out string html);
                    string stripped = StripTags(html);
                    string body = string.IsNullOrWhiteSpace(text) ? stripped : text;
                    string haystack = subject + "\n" + body + "\n" + html;
                    if (!matcher(subject, from, haystack))
                    {
                        _seenIds.Add(id);
                        continue;
                    }

                    string code = extractCode(html, body);
                    _seenIds.Add(id);
                    if (!string.IsNullOrEmpty(code))
                        return code;
                }

                await Task.Delay(3000, ct);
            }

            return null;
        }

        private static bool IsOlderThanTrigger(IMessageSummary summary, DateTimeOffset afterUtc)
        {
            if (summary.InternalDate is DateTimeOffset internalDate && internalDate < afterUtc)
                return true;
            if (summary.Date is DateTimeOffset envelopeDate && envelopeDate != default && envelopeDate < afterUtc)
                return true;
            return false;
        }

        private static bool IsOlderThanTrigger(MimeMessage message, DateTimeOffset afterUtc)
        {
            if (message.Date != default && message.Date < afterUtc)
                return true;
            return false;
        }

        private static bool IsAddAuthenticatorMail(string subject, string from, string body)
        {
            string text = (subject + " " + body).ToLowerInvariant();
            return text.Contains("添加验证器请求")
                || text.Contains("添加验证器")
                || text.Contains("add authenticator")
                || text.Contains("authenticator request");
        }

        private static bool IsLoginGuardMail(string subject, string from, string body)
        {
            if (IsAddAuthenticatorMail(subject, from, body))
                return false;

            string text = (subject + " " + from + " " + body).ToLowerInvariant();
            return text.Contains("steam登录验证")
                || text.Contains("steam 登录验证")
                || text.Contains("steam 令牌")
                || text.Contains("steam令牌")
                || text.Contains("steam guard")
                || text.Contains("access from new")
                || text.Contains("从新计算机")
                || text.Contains("从新的计算机")
                || text.Contains("从新设备")
                || text.Contains("登录验证")
                || from.Contains("steampowered.com");
        }

        private static string FormatFrom(MimeMessage message)
        {
            if (message.From == null || message.From.Count == 0)
                return "";
            return string.Join(" ", message.From.Mailboxes.Select(m => (m.Name + " " + m.Address).Trim()));
        }

        private static string ExtractBindCode(string html, string text)
        {
            if (!string.IsNullOrEmpty(html))
            {
                var block = AuthCodeBlockRegex.Match(html);
                string scope = block.Success ? block.Groups[1].Value : html;
                var cell = AuthCodeCellRegex.Match(scope);
                if (cell.Success)
                    return cell.Groups[1].Value.Trim().ToUpperInvariant();
            }

            return null;
        }

        private static string ExtractLoginCode(string html, string text)
        {
            string source = (text ?? "") + "\n" + StripTags(html ?? "");
            var labeled = LoginLabelCodeRegex.Match(source);
            if (labeled.Success)
                return labeled.Groups[1].Value.Trim().ToUpperInvariant();

            var cell = AuthCodeCellRegex.Match(html ?? "");
            if (cell.Success)
                return cell.Groups[1].Value.Trim().ToUpperInvariant();

            return null;
        }

        private static void ExtractBodies(MimeMessage message, out string text, out string html)
        {
            text = "";
            html = "";
            try { html = message.HtmlBody ?? ""; } catch { }
            try { text = message.TextBody ?? ""; } catch { }
            if (string.IsNullOrEmpty(html) || string.IsNullOrEmpty(text))
                CollectTextParts(message.Body, ref text, ref html);
        }

        private static void CollectTextParts(MimeEntity entity, ref string text, ref string html)
        {
            if (entity == null)
                return;

            if (entity is Multipart multipart)
            {
                foreach (var part in multipart)
                    CollectTextParts(part, ref text, ref html);
                return;
            }

            if (entity is not TextPart textPart)
                return;

            string body;
            try
            {
                body = textPart.Text ?? "";
            }
            catch
            {
                return;
            }

            if (textPart.IsPlain && string.IsNullOrEmpty(text))
                text = body;
            else if (textPart.IsHtml && string.IsNullOrEmpty(html))
                html = body;
        }

        private static string StripTags(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            var b = new System.Text.StringBuilder(s.Length);
            bool inTag = false;
            foreach (char r in s)
            {
                if (r == '<')
                    inTag = true;
                else if (r == '>')
                    inTag = false;
                else if (!inTag)
                    b.Append(r);
            }
            return b.ToString();
        }

        public void Dispose()
        {
            try
            {
                if (_client.IsConnected)
                    _client.Disconnect(true);
            }
            catch { }
            _client.Dispose();
        }
    }
}
