using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Steam_Desktop_Authenticator
{
    public partial class WelcomeForm : Form
    {
        private Manifest man;

        public WelcomeForm()
        {
            InitializeComponent();
            man = Manifest.GetManifest();
        }

        private void btnJustStart_Click(object sender, EventArgs e)
        {
            // Mark as not first run anymore
            man.FirstRun = false;
            man.Save();

            showMainForm();
        }

        private void btnImportConfig_Click(object sender, EventArgs e)
        {
            // Let the user select the config dir
            FolderBrowserDialog folderBrowser = new FolderBrowserDialog();
            folderBrowser.Description = "请选择旧版 Steam 桌面令牌的安装目录";
            DialogResult userClickedOK = folderBrowser.ShowDialog();

            if (userClickedOK == DialogResult.OK)
            {
                string path = folderBrowser.SelectedPath;
                string pathToCopy = null;

                if (Directory.Exists(path + "/maFiles"))
                {
                    // User selected the root install dir
                    pathToCopy = path + "/maFiles";
                }
                else if (File.Exists(path + "/manifest.json"))
                {
                    // User selected the maFiles dir
                    pathToCopy = path;
                }
                else
                {
                    // Could not find either.
                    MessageBox.Show("此文件夹不包含 manifest.json 或 maFiles。\n请选择之前安装 Steam 桌面令牌的目录。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // Copy the contents of the config dir to the new config dir
                string currentPath = Manifest.GetExecutableDir();

                // Create config dir if we don't have it
                if (!Directory.Exists(currentPath + "/maFiles"))
                {
                    Directory.CreateDirectory(currentPath + "/maFiles");
                }

                // Copy all files from the old dir to the new one
                foreach (string newPath in Directory.GetFiles(pathToCopy, "*.*", SearchOption.AllDirectories))
                {
                    File.Copy(newPath, newPath.Replace(pathToCopy, currentPath + "/maFiles"), true);
                }

                // Set first run in manifest
                try
                {
                    man = Manifest.GetManifest(true);
                    man.FirstRun = false;
                    man.Save();
                }
                catch (ManifestParseException)
                {
                    // Manifest file was corrupted, generate a new one.
                    try
                    {
                        MessageBox.Show("设置文件已损坏，已重置为默认值。", "Steam 桌面令牌", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                        man = Manifest.GenerateNewManifest(true);
                    }
                    catch (MaFileEncryptedException)
                    {
                        // An maFile was encrypted, we're fucked.
                        MessageBox.Show("由于使用了加密，无法恢复账号。\n请通过移除验证器来恢复 Steam 账号。\n点确定查看说明。", "Steam 桌面令牌", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        System.Diagnostics.Process.Start(@"https://github.com/Jessecar96/SteamDesktopAuthenticator/wiki/Help!-I'm-locked-out-of-my-account");
                        this.Close();
                        return;
                    }
                }

                // All done!
                MessageBox.Show("账号和设置已全部导入！点确定继续。", "导入账号", MessageBoxButtons.OK, MessageBoxIcon.Information);
                showMainForm();
            }

        }

        private void showMainForm()
        {
            this.Hide();
            new MainForm().Show();
        }
    }
}
