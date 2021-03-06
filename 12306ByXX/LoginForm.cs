﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using HtmlAgilityPack;
using Newtonsoft.Json;
using _12306ByXX.Common;
using _12306ByXX.Properties;
using HtmlDocument = HtmlAgilityPack.HtmlDocument;


namespace _12306ByXX
{
    public partial class LoginForm : Form
    {
        public LoginForm()
        {
            InitializeComponent();
        }

        private const string DefaultAgent =
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_13_1) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/63.0.3239.84 Safari/537.36";

        private CookieContainer _cookie = null;

        private readonly string basePath = AppDomain.CurrentDomain.BaseDirectory;

        public CaptchaCheck Check { get; set; }
        public string LinkAddress { get; set; }
        private void LoginForm_Load(object sender, EventArgs e)
        {
            string fileName = basePath + "\\data.dat";
            if (File.Exists(fileName))
            {
                using (FileStream fs = new FileStream(fileName, FileMode.Open))
                {
                    BinaryFormatter formatter = new BinaryFormatter();
                    Dictionary<string, string> userInfo = (Dictionary<string, string>) formatter.Deserialize(fs);
                    tb_userName.Text = userInfo.FirstOrDefault().Key;
                    tb_passWord.Text = userInfo.FirstOrDefault().Value;
                    cb_remember.Checked = true;
                }
            }
            _cookie = new CookieContainer();
            HttpHelper.Get(DefaultAgent, "https://kyfw.12306.cn/otn/login/init", _cookie);
            Check = captchaCheck;
            captchaCheck.Agent = DefaultAgent;
            captchaCheck.Login += Login;
            captchaCheck.Cookie = _cookie;
            if (rb_check2.Checked)
            {
                LinkAddress = "1";
                captchaCheck.LinkAddress = LinkAddress;
            }
        }


        /// <summary>
        /// 登录校验
        /// </summary>
        private void Login()
        {

            SavePwd();
            string userName = tb_userName.Text;
            string passWord = tb_passWord.Text;

            string randCode = captchaCheck.RandCode;
            bool loginRet = false;
            string message;
            loginRet = LinkAddress == "1"
                ? Login(userName, passWord, out message)
                : Login(userName, passWord, randCode, out message);

            if (loginRet)
            {
                LogHelper.Info("登录成功！");
                const string mainurl = "https://kyfw.12306.cn/otn/index/initMy12306";
                Stream mainStream = HttpHelper.Get(DefaultAgent, mainurl, _cookie);
                StreamReader mainStreamReader = new StreamReader(mainStream, Encoding.UTF8);
                string mainContent = mainStreamReader.ReadToEnd();
                HtmlDocument doc = new HtmlDocument();
                doc.LoadHtml(mainContent);
                HtmlNode node = doc.DocumentNode.SelectSingleNode("//div[@id='my12306page']");
                HtmlNode noticeNode = node.SelectSingleNode("//h3");
                string notice = noticeNode.InnerText.Replace("\n", "");
                HtmlNode nameNode = doc.DocumentNode.SelectSingleNode("//a[@id='login_user']");
                string name = nameNode.InnerText.Replace("\n", "");
                List<Passenger> lsDic = GetPassengers();
                this.Hide();
                MainForm mainForm = new MainForm {ParenForm = this, AllPassengers = lsDic, Cookie = _cookie};
                mainForm.SetGridBoxText(name + notice);
                mainForm.ShowDialog();
            }
            else
            {
                MessageBox.Show(message);
                captchaCheck.LoadCaptchaImg();
            }
        }

        private bool Login(string userName, string passWord, string randCode,out string msg)
        {
            msg = "";
            const string loginUrl = "https://kyfw.12306.cn/otn/login/loginAysnSuggest";
            string postData = string.Format("loginUserDTO.user_name={0}&userDTO.password={1}&randCode={2}", userName,
                passWord, randCode);
            HttpJsonEntity<Dictionary<string, string>> retEntity =
                HttpHelper.Post(DefaultAgent, loginUrl, postData, _cookie);
            if (retEntity.status.ToUpper().Equals("TRUE") && retEntity.httpstatus.Equals(200))
            {
                return true;
            }
            msg = retEntity.messages[0];
            return false;
        }

        private bool Login(string userName, string passWord,out string msg)
        {
            msg = "";
            string url = "https://kyfw.12306.cn/passport/web/login";
            string postData = string.Format("username={0}&password={1}&appid={2}", userName,
                passWord, "otn");
            Thread.Sleep(1000);
            string content = HttpHelper.StringPost(DefaultAgent, url, postData, _cookie); ;
            Dictionary<string, string> retDic = JsonConvert.DeserializeObject<Dictionary<string, string>>(content);
            if (retDic.ContainsKey("result_code") && retDic["result_code"].Equals("0"))
            {
                postData = "appid=otn";
                url = "https://kyfw.12306.cn/passport/web/auth/uamtk";
                Thread.Sleep(1000);
                content = HttpHelper.StringPost(DefaultAgent, url, postData, _cookie); ;
                retDic = JsonConvert.DeserializeObject<Dictionary<string, string>>(content);
                if (retDic.ContainsKey("result_code") && retDic["result_code"].Equals("0"))
                {
                    string newapptk = retDic["newapptk"];
                    url = "https://kyfw.12306.cn/otn/uamauthclient";
                    postData = "tk=" + newapptk;
                    Thread.Sleep(1000);
                    content = HttpHelper.StringPost(DefaultAgent, url, postData, _cookie); ;
                    retDic = JsonConvert.DeserializeObject<Dictionary<string, string>>(content);
                    if (retDic.ContainsKey("result_code") && retDic["result_code"].Equals("0"))
                    {
                        return true;
                    }
                }
            }
            msg = retDic["result_message"];
            return false;
        }
        /// <summary>
        /// 获取乘客信息
        /// </summary>
        /// <returns></returns>
        private List<Passenger> GetPassengers()
        {
            const string passengersUrl = "https://kyfw.12306.cn/otn/passengers/init";
            const string postData = "_json_att=";
            string htmlContent = HttpHelper.StringPost(DefaultAgent, passengersUrl, postData, _cookie);
            const string regexStr = @"<script xml:space=\""preserve\"">([\s\S]+?)<\/script>";
            string result = Regex.Match(htmlContent, regexStr).Value;

            const string regexPassenger = @"\{'([\s\S]+?)\}";
            Regex reg = new Regex(regexPassenger, RegexOptions.IgnoreCase);
            MatchCollection matchs = reg.Matches(result);
            List<Passenger> lsPassenger = (from Match item in matchs
                where item.Success
                select JsonConvert.DeserializeObject<Passenger>(item.Value)).ToList();

            return lsPassenger;
        }

        private void SavePwd()
        {
            string userName = tb_userName.Text;
            string passWord = tb_passWord.Text;
            if (string.IsNullOrEmpty(userName.Trim()) || string.IsNullOrEmpty(passWord))
            {
                MessageBox.Show(Resources.CanNotBeEmpty);
                return;
            }
            string fileName = basePath + "\\data.dat";
            if (cb_remember.Checked)
            {
                Dictionary<string, string> dicUser = new Dictionary<string, string> {{userName, passWord}};

                //序列化写入文件
                using (FileStream fs = new FileStream(fileName, FileMode.Create))
                {
                    BinaryFormatter formatter = new BinaryFormatter();
                    formatter.Serialize(fs, dicUser);
                }
            }
            else
            {
                File.Delete(fileName);
            }
        }

        private void rb_LinkAdress_CheckedChanged(object sender, EventArgs e)
        {
            LinkAddress = rb_check1.Checked ? "0" : "1";
            captchaCheck.LinkAddress = LinkAddress;
            captchaCheck.LoadCaptchaImg();
        }
    }
}
