using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using SteamAuth;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;

namespace Steam_Desktop_Authenticator
{
    public partial class ImportAccountForm : Form
    {
        private Manifest mManifest;

        public ImportAccountForm()
        {
            InitializeComponent();
            this.mManifest = Manifest.GetManifest();
        }

        private void btnImport_Click(object sender, EventArgs e)
        {
            // check if data already added is encripted
            #region check if data already added is encripted
            string ContiuneImport = "0";

            string ManifestFile = "maFiles/manifest.json";
            if (File.Exists(ManifestFile))
            {
                string AppManifestContents = File.ReadAllText(ManifestFile);
                AppManifest AppManifestData = JsonConvert.DeserializeObject<AppManifest>(AppManifestContents);
                bool AppManifestData_encrypted = AppManifestData.Encrypted;
                if (AppManifestData_encrypted != true)
                {
                    MessageBox.Show("当前应用必须先设置加密密钥后才能导入。");
                    this.Close();
                }
                else
                {
                    ContiuneImport = "1";
                }
            }
            else
            {
                MessageBox.Show("发生错误，请重启程序！");
            }
            #endregion

            // Continue
            #region Continue
            if (ContiuneImport == "1")
            {
                this.Close();

                // read EncriptionKey from imput box
                string ImportUsingEncriptionKey = txtBox.Text;

                // Open file browser > to select the file
                OpenFileDialog openFileDialog1 = new OpenFileDialog();

                // Set filter options and filter index.
                openFileDialog1.Filter = "maFiles (.maFile)|*.maFile|所有文件 (*.*)|*.*";
                openFileDialog1.FilterIndex = 1;
                openFileDialog1.Multiselect = false;

                // Call the ShowDialog method to show the dialog box.
                DialogResult userClickedOK = openFileDialog1.ShowDialog();

                // Process input if the user clicked OK.
                if (userClickedOK == DialogResult.OK)
                {
                    // Open the selected file to read.
                    System.IO.Stream fileStream = openFileDialog1.OpenFile();
                    string fileContents = null;

                    using (System.IO.StreamReader reader = new System.IO.StreamReader(fileStream))
                    {
                        fileContents = reader.ReadToEnd();
                    }
                    fileStream.Close();

                    try
                    {
                            if (ImportUsingEncriptionKey == "")
                            {
                                SteamGuardAccount maFile = JsonConvert.DeserializeObject<SteamGuardAccount>(fileContents);

                                if (maFile.Session == null || maFile.Session.SteamID == 0 || maFile.Session.IsAccessTokenExpired())
                                {
                                    LoginForm loginForm = new LoginForm(LoginForm.LoginType.Import, maFile);
                                    loginForm.ShowDialog();

                                    if (loginForm.Session == null || loginForm.Session.SteamID == 0)
                                    {
                                        MessageBox.Show("登录失败，请重新导入该账号。", "导入账号", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                        return;
                                    }

                                    maFile.Session = loginForm.Session;
                                }

                                string passKey = mManifest.RequirePassKey();
                                if (passKey == null)
                                    return;
                                if (!mManifest.SaveAccount(maFile, true, passKey))
                                {
                                    MessageBox.Show("无法保存账号（加密失败）。", "导入账号", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                    return;
                                }
                                MessageBox.Show("账号已导入！", "导入账号", MessageBoxButtons.OK);
                            }
                        else
                        {
                            // Import Encripted maFile
                            //-------------------------------------------
                            #region Import Encripted maFile
                            //Read manifest.json encryption_iv encryption_salt
                            string ImportFileName_Found = "0";
                            string Salt_Found = null;
                            string IV_Found = null;
                            string ReadManifestEx = "0";

                            //No directory means no manifest file anyways.
                            ImportManifest newImportManifest = new ImportManifest();
                            newImportManifest.Encrypted = false;
                            newImportManifest.Entries = new List<ImportManifestEntry>();

                            // extract folder path
                            string fullPath = openFileDialog1.FileName;
                            string fileName = openFileDialog1.SafeFileName;
                            string path = fullPath.Replace(fileName, "");

                            // extract fileName
                            string ImportFileName = fullPath.Replace(path, "");

                            string ImportManifestFile = path + "manifest.json";


                            if (File.Exists(ImportManifestFile))
                            {
                                string ImportManifestContents = File.ReadAllText(ImportManifestFile);


                                try
                                {
                                    ImportManifest account = JsonConvert.DeserializeObject<ImportManifest>(ImportManifestContents);
                                    //bool Import_encrypted = account.Encrypted;

                                    List<ImportManifest> newEntries = new List<ImportManifest>();

                                    foreach (var entry in account.Entries)
                                    {
                                        string FileName = entry.Filename;
                                        string encryption_iv = entry.IV;
                                        string encryption_salt = entry.Salt;

                                        if (ImportFileName == FileName)
                                        {
                                            ImportFileName_Found = "1";
                                            IV_Found = entry.IV;
                                            Salt_Found = entry.Salt;
                                        }
                                    }
                                }
                                catch (Exception)
                                {
                                    ReadManifestEx = "1";
                                    MessageBox.Show("manifest.json 内容无效！\n导入失败。");
                                }


                                // DECRIPT & Import
                                //--------------------
                                #region DECRIPT & Import
                                if (ReadManifestEx == "0")
                                {
                                    if (ImportFileName_Found == "1" && Salt_Found != null && IV_Found != null)
                                    {
                                        string decryptedText = FileEncryptor.DecryptData(ImportUsingEncriptionKey, Salt_Found, IV_Found, fileContents);

                                        if (decryptedText == null)
                                        {
                                            MessageBox.Show("解密失败。\n导入失败。");
                                        }
                                        else
                                        {
                                            string fileText = decryptedText;

                                            SteamGuardAccount maFile = JsonConvert.DeserializeObject<SteamGuardAccount>(fileText);
                                            if (maFile.Session == null || maFile.Session.SteamID == 0 || maFile.Session.IsAccessTokenExpired())
                                            {
                                                // Have the user to relogin to steam to get a new session
                                                LoginForm loginForm = new LoginForm(LoginForm.LoginType.Import, maFile);
                                                loginForm.ShowDialog();

                                                if (loginForm.Session == null || loginForm.Session.SteamID == 0)
                                                {
                                                    MessageBox.Show("登录失败，请重新导入该账号。", "导入账号", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                                    return;
                                                }

                                                // Save new session to the maFile
                                                maFile.Session = loginForm.Session;
                                            }

                                            string passKey = mManifest.RequirePassKey();
                                            if (passKey == null)
                                                return;
                                            if (!mManifest.SaveAccount(maFile, true, passKey))
                                            {
                                                MessageBox.Show("无法保存账号（加密失败）。", "导入账号", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                                return;
                                            }
                                            MessageBox.Show("账号已导入！", "导入账号", MessageBoxButtons.OK);
                                        }
                                    }
                                    else
                                    {
                                        if (ImportFileName_Found == "0")
                                        {
                                            MessageBox.Show("在 manifest.json 中找不到该账号。\n导入失败。");
                                        }
                                        else if (Salt_Found == null && IV_Found == null)
                                        {
                                            MessageBox.Show("manifest.json 中没有加密数据。\n该账号可能未加密！\n导入失败。");
                                        }
                                        else
                                        {
                                            if (IV_Found == null)
                                            {
                                                MessageBox.Show("manifest.json 缺少 encryption_iv。\n导入失败。");
                                            }
                                            else if (IV_Found == null)
                                            {
                                                MessageBox.Show("manifest.json 缺少 encryption_salt。\n导入失败。");
                                            }
                                        }
                                    }
                                }
                                #endregion //DECRIPT & Import END


                            }
                            else
                            {
                                MessageBox.Show("找不到 manifest.json！\n导入失败。");
                            }
                            #endregion //Import Encripted maFile END
                        }

                    }
                    catch (Exception)
                    {
                        MessageBox.Show("该文件不是有效的 SteamAuth maFile。\n导入失败。");
                    }
                }
            }
            #endregion // Continue End
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void Import_maFile_Form_FormClosing(object sender, FormClosingEventArgs e)
        {
        }
    }


    public class AppManifest
    {
        [JsonProperty("encrypted")]
        public bool Encrypted { get; set; }
    }


    public class ImportManifest
    {
        [JsonProperty("encrypted")]
        public bool Encrypted { get; set; }

        [JsonProperty("entries")]
        public List<ImportManifestEntry> Entries { get; set; }
    }

    public class ImportManifestEntry
    {
        [JsonProperty("encryption_iv")]
        public string IV { get; set; }

        [JsonProperty("encryption_salt")]
        public string Salt { get; set; }

        [JsonProperty("filename")]
        public string Filename { get; set; }

        [JsonProperty("steamid")]
        public ulong SteamID { get; set; }
    }
}
