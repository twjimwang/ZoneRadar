﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using ZoneRadar.Models.ViewModels;
using ZoneRadar.Repositories;
using ZoneRadar.Models;
using System.Web.Security;
using Newtonsoft.Json;
using ZoneRadar.Utilities;
using Microsoft.AspNet.Identity;
using System.Security.Policy;
using System.Net.Mail;
using System.Net;
using System.Text;
using System.Web.Mvc;
using System.Web.Routing;
using System.IO;

namespace ZoneRadar.Services
{
    public class MemberService
    {
        private readonly ZONERadarRepository _repository;
        public MemberService()
        {
            _repository = new ZONERadarRepository();
        }        

        /// <summary>
        /// 將未驗證的註冊資訊先存進資料庫
        /// </summary>
        /// <param name="registerVM"></param>
        /// <returns>回傳會員資訊及註冊是否成功</returns>
        public RegisterResult RegisterMember(RegisterZONERadarViewModel registerVM)
        {
            var registerResult = new RegisterResult
            {
                User = null,
                IsSuccessful = false
            };

            registerVM.Name = HttpUtility.HtmlEncode(registerVM.Name);
            registerVM.Email = HttpUtility.HtmlEncode(registerVM.Email);
            registerVM.Password = HttpUtility.HtmlEncode(registerVM.Password).MD5Hash();

            var isSameEmail = _repository.GetAll<Member>().Any(x => x.Email.ToUpper() == registerVM.Email.ToUpper());

            if (isSameEmail || registerVM == null)
            {
                return registerResult;
            }
            else
            {
                try
                {
                    var member = new Member
                    {
                        Email = registerVM.Email,
                        Password = registerVM.Password,
                        Name = registerVM.Name,
                        ReceiveEDM = false,
                        SignUpDateTime = new DateTime(1753,1,1), //未驗證時時間為西元1753年
                        LastLogin = DateTime.Now
                    };
                    _repository.Create<Member>(member);
                    _repository.SaveChanges();
                    registerResult.User = member;
                    registerResult.IsSuccessful = true;
                    return registerResult;
                }
                catch (Exception ex)
                {
                    return registerResult;
                }
            }
        }

        /// <summary>
        /// 寄送驗證信
        /// </summary>
        /// <param name="request"></param>
        /// <param name="urlHelper"></param>
        public void SentEmail(HttpServerUtilityBase server, HttpRequestBase request, UrlHelper urlHelper, string userEmail)
        {
            var afterTenMinutes = DateTime.Now.AddMinutes(10).ToString();
            var route = new RouteValueDictionary { { "email", userEmail }, { "expired", afterTenMinutes } };
            var confirmLink = urlHelper.Action("ConfirmEmail", "MemberCenter", route, request.Url.Scheme, request.Url.Host);

            string ZONERadarAccount = "swkzta3@gmail.com";
            string ZONERadarPassword = "Minato,Naruto";

            SmtpClient client = new SmtpClient("smtp.gmail.com", 587);
            client.Credentials = new NetworkCredential(ZONERadarAccount, ZONERadarPassword);
            client.EnableSsl = true;

            MailMessage mail = new MailMessage(ZONERadarAccount, ZONERadarAccount);
            mail.Subject = "ZONERadar會員確認信";
            mail.SubjectEncoding = Encoding.UTF8;
            mail.IsBodyHtml = true;
            string confirmEmailContent = File.ReadAllText(Path.Combine(server.MapPath("~/Views/MemberCenter/ConfirmEmailContent.html")));
            mail.Body = confirmEmailContent.Replace("confirmLink", confirmLink);
            mail.BodyEncoding = Encoding.UTF8;

            try
            {
                client.Send(mail);
            }
            catch (Exception ex)
            {
                throw ex;
            }
            finally
            {
                mail.Dispose();
                client.Dispose();
            }
        }


        /// <summary>
        /// 點擊驗證連結後做確認，是否有此會員的註冊紀錄
        /// </summary>
        /// <param name="email"></param>
        public RegisterResult ConfirmRegister(string email)
        {
            var registerResult = new RegisterResult
            {
                IsSuccessful = false
            };
            
            var hasThisUser = _repository.GetAll<Member>().Any(x => x.Email.ToUpper() == email.ToUpper());
            if (hasThisUser)
            {
                var user = _repository.GetAll<Member>().First(x => x.Email.ToUpper() == email.ToUpper());
                //將會員的註冊時間改成現在時間，代表驗證成功
                user.SignUpDateTime = DateTime.Now;
                _repository.Update<Member>(user);
                _repository.SaveChanges();
                registerResult.User = user;
                registerResult.IsSuccessful = true;
                registerResult.RegisterMessage = $"{user.Name}，歡迎！";
            }
            return registerResult;
        }

        /// <summary>
        /// 比對是否有此會員
        /// </summary>
        /// <param name="loginVM"></param>
        /// <returns></returns>
        public Member UserLogin(LoginZONERadarViewModel loginVM)
        {          
            //使用HtmlEncode將帳密做HTML編碼, 去除有害的字元
            loginVM.Email = HttpUtility.HtmlEncode(loginVM.Email);
            loginVM.Password = HttpUtility.HtmlEncode(loginVM.Password).MD5Hash();

            //EF比對資料庫帳密
            //以Email及Password查詢比對Member資料表記錄，且註冊時間不得為預設1753年
            var members = _repository.GetAll<Member>().ToList();
            var user = members.SingleOrDefault(x => x.Email.ToUpper() == loginVM.Email.ToUpper() && x.Password == loginVM.Password && x.SignUpDateTime.Year != 1753);

            //修改上次登入時間
            if(user != null)
            {
                user.LastLogin = DateTime.Now;
                _repository.Update(user);
                _repository.SaveChanges();
            }

            return user;
        }

        /// <summary>
        /// 建造加密表單驗證票證
        /// </summary>
        /// <param name="user"></param>
        /// <returns></returns>
        public string CreateEncryptedTicket(Member user)
        {
            var userInfo = new UserInfo
            {
                MemberId = user.MemberID,
                MemberPhoto = user.Photo == null ? "https://img.88icon.com/download/jpg/20200815/cacc4178c4846c91dc1bfa1540152f93_512_512.jpg!88con" : user.Photo
            };
            var jsonUserInfo = JsonConvert.SerializeObject(userInfo);
            //建立FormsAuthenticationTicket
            var ticket = new FormsAuthenticationTicket(
            version: 1,
            name: user.MemberID.ToString(), //可以放使用者Id
            issueDate: DateTime.UtcNow,//現在UTC時間
            expiration: DateTime.UtcNow.AddMinutes(30),//Cookie有效時間=現在時間往後+30分鐘
            isPersistent: true,// 是否要記住我 true or false
            userData: jsonUserInfo, //可以放使用者角色名稱
            cookiePath: FormsAuthentication.FormsCookiePath);

            //加密Ticket
            var encryptedTicket = FormsAuthentication.Encrypt(ticket);

            return encryptedTicket;
        }

        /// <summary>
        /// 建造Cookie
        /// </summary>
        /// <param name="encryptedTicket"></param>
        /// <param name="responseBase"></param>
        public void CreateCookie(string encryptedTicket, HttpResponseBase responseBase)
        {
            //初始化Cookie的名稱和值(將加密的表單驗證票證放進Cookie裡)
            var cookie = new HttpCookie(FormsAuthentication.FormsCookieName, encryptedTicket);
            responseBase.Cookies.Add(cookie);
        }

        /// <summary>
        /// 取得使用者原先欲造訪的路由
        /// </summary>
        /// <param name="userName"></param>
        /// <returns></returns>
        public string GetOriginalUrl(string userName)
        {
            var url = FormsAuthentication.GetRedirectUrl(userName, true);
            return url;
        }
    }
}