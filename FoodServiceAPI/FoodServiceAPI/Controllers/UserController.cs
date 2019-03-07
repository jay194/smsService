﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FoodServiceAPI.Authentication;
using FoodServiceAPI.Database;
using FoodServiceAPI.Models;

namespace FoodServiceAPI.Controllers
{
    [Produces("application/json")]
    [Route("api/user")]
    public class UserController : Controller
    {
        private readonly FoodContext dbContext;

        public class UserRegistration
        {
            public string username { get; set; }
            public string password { get; set; }
            public string email { get; set; }
            public string address { get; set; }
            public string zip { get; set; }
            public string user_type { get; set; }

            // Client
            public string first_name { get; set; }
            public string last_name { get; set; }
            public string cell_phone { get; set; }
            public bool paying { get; set; }

            // Business
            public string name { get; set; }
            public string work_phone { get; set; }
            public string instructions { get; set; }
        }

        public class UserInfo
        {
            public string username { get; set; }
            public string email { get; set; }
            public string address { get; set; }
            public string zip { get; set; }
            public string user_type { get; set; }

            public UserInfo(UserData user)
            {
                username = user.username;
                email = user.email;
                address = user.address;
                zip = user.zip;
                user_type = null;
            }
        }

        public class ClientInfo : UserInfo
        {
            public int cid { get; set; }
	        public string first_name { get; set; }
	        public string last_name { get; set; }
	        public string cell_phone { get; set; }

            public ClientInfo(Client client) : base(client.User)
            {
                user_type = "client";
                cid = client.cid;
                first_name = client.first_name;
                last_name = client.last_name;
                cell_phone = client.cell_phone;
            }
        }

        public class BusinessInfo : UserInfo
        {
            public string name { get; set; }
            public string work_phone { get; set; }
            public string instructions { get; set; }

            public BusinessInfo(Business business) : base(business.User)
            {
                user_type = "business";
                name = business.name;
                work_phone = business.work_phone;
                instructions = business.instructions;
            }
        }

        public class UserTypeInfo
        {
            public string user_type { get; set; }
        }

        public class LoginReturn
        {
            public string session_token { get; set; }
        }

        public class PasswordSetter
        {
            public string new_password { get; set; }
        }

        public class InfoUpdate
        {
            public string new_username { get; set; }
            public string email { get; set; }
            public string address { get; set; }
            public string zip { get; set; }

            //Client
            public string first_name { get; set; }
            public string last_name { get; set; }
            public string cell_phone { get; set; }

            //Business
            public string name { get; set; }
            public string work_phone { get; set; }
            public string instructions { get; set; }
        }

        public UserController(FoodContext dbContext)
        {
            this.dbContext = dbContext;
        }

        [Route("register")]
        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> Register([FromBody]UserRegistration reg)
        {
            // FIXME: validate
            UserData user = new UserData
            {
                username = reg.username,
                password = new PasswordProtector().Protect(reg.password),
                email = reg.email,
                address = reg.address,
                zip = reg.zip
            };

            await dbContext.Users.AddAsync(user);

            if (reg.user_type.ToLower() == "client")
            {
                Client client = new Client
                {
                    uid = user.uid,
                    first_name = reg.first_name,
                    last_name = reg.last_name,
                    cell_phone = reg.cell_phone,
                    paying = reg.paying
                };

                await dbContext.Clients.AddAsync(client);
            }
            else if (reg.user_type.ToLower() == "business")
            {
                Business business = new Business
                {
                    uid = user.uid,
                    name = reg.name,
                    work_phone = reg.work_phone,
                    instructions = reg.instructions
                };

                await dbContext.Businesses.AddAsync(business);
            }
            else
            {
                return BadRequest(new ResultBody("Invalid user type.")); 
            }

            await dbContext.SaveChangesAsync();

            return Ok(new ResultBody("Successfully created new user")); 
        }

        [Route("login")]
        [HttpPost]
        [Authorize("UserPass")]
        public async Task<IActionResult> Login()
        {
            // Create session
            SessionData session = new SessionData
            {
                uid = Convert.ToInt32(User.FindFirst("uid").Value),
                created = DateTime.UtcNow.ToString(),
                expires = DateTime.UtcNow.AddMinutes(20)
            };

            await dbContext.Sessions.AddAsync(session);
            await dbContext.SaveChangesAsync();

            // Create token
            Claim[] claims = new[]
            {
                new Claim("sid", session.sid.ToString()),
                new Claim("created", session.created)
            };

            SymmetricSecurityKey key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("secretsecretsupersecret"));
            SigningCredentials credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            JwtSecurityToken token = new JwtSecurityToken(
                issuer: "http://localhost:50577/",
                audience: "http://localhost:50577/",
                claims: claims,
                expires: session.expires,
                signingCredentials: credentials
            );

            LoginReturn ret = new LoginReturn
            {
                session_token = new JwtSecurityTokenHandler().WriteToken(token)
            };

            return Ok(ret);
        }

        [Route("logout")]
        [HttpPost]
        [Authorize("Session")]
        public async Task<IActionResult> Logout()
        {
            SessionData session = await dbContext.Sessions.FindAsync(Convert.ToInt32(User.FindFirstValue("sid")));
            dbContext.Sessions.Remove(session);
            await dbContext.SaveChangesAsync();

            return Ok(new ResultBody("Log out was a success."));
        }

        [Route("getinfo")]
        [HttpPost]
        [Authorize("Session")]
        public async Task<IActionResult> GetInfo()
        {
            int uid = Convert.ToInt32(User.FindFirstValue("uid"));
            UserData user = await dbContext.Users.Include(u => u.Client).Include(u => u.Business).FirstOrDefaultAsync(u => u.uid == uid);

            
            if (user.Client != null)
            {
               return Ok(new ClientInfo(user.Client));
            }
            else if (user.Business != null)
            {
                return Ok(new BusinessInfo(user.Business));
            }
            else
            {
                return BadRequest(new ResultBody("Function does not support this type of user."));
            }
        }

        [Route("getusertype")]
        [HttpPost]
        [Authorize("Session")]
        public IActionResult GetUserType()
        {
            UserTypeInfo type = new UserTypeInfo();

            if(User.FindFirstValue("cid") != null)
                type.user_type = "client";
            else if(User.FindFirstValue("bid") != null)
                type.user_type = "business";
            else
                return BadRequest(new ResultBody("Function does not support this type of user."));

            return Ok(type);
        }

        [Route("logoutall")]
        [HttpPost]
        [Authorize("UserPass")]
        public async Task<IActionResult> LogoutAllSessions()
        {
            int uid = Convert.ToInt32(User.FindFirstValue("uid"));
            SessionData[] sessions = await dbContext.Sessions.Where(s => s.uid == uid).ToArrayAsync();
            dbContext.Sessions.RemoveRange(sessions);
            await dbContext.SaveChangesAsync();

            return Ok(new ResultBody("Successfully logged out of all sessions."));
        }

        [Route("setinfo")]
        [HttpPost]
        [Authorize("UserPass")]
        public async Task<IActionResult> SetInfo([FromBody] InfoUpdate updated)
        {
            int uid = Convert.ToInt32(User.FindFirstValue("uid"));
            UserData user = await dbContext.Users.Include(u => u.Client).Include(u => u.Business).FirstOrDefaultAsync(u => u.uid == uid);
            string stringuid = uid.ToString();

            user.username = updated.new_username;
            user.email = updated.email;
            user.address = updated.address;
            user.zip = updated.zip;

            //FIXME: Validate
            if (user.Client != null)
            {
                user.Client.first_name = updated.first_name;
                user.Client.last_name = updated.last_name;
                user.Client.cell_phone = updated.cell_phone;
                await dbContext.SaveChangesAsync();

                return Ok(new ResultBody("Client info updated."));
            }
            else if (user.Business != null)
            {
                user.Business.name = updated.name;
                user.Business.work_phone = updated.work_phone;
                user.Business.instructions = updated.instructions;
                await dbContext.SaveChangesAsync();

                return Ok(new ResultBody("Business info updated."));
            }
            else
            {
                return BadRequest(new ResultBody("User type was invalid."));
            }
        }

        [Route("setpassword")]
        [HttpPost]
        [Authorize("UserPass")]
        public async Task<IActionResult> SetPassword([FromBody]PasswordSetter new_pass)
        {
            // FIXME: Error handle
            int uid = Convert.ToInt32(User.FindFirstValue("uid"));
            UserData userData = await dbContext.Users.FirstAsync(u => u.uid == uid);

            userData.password = new PasswordProtector().Protect(new_pass.new_password);
            dbContext.Users.Update(userData);
            await dbContext.SaveChangesAsync();

            return Ok(new ResultBody("Password has been reset."));
        }

        [Route("delete")]
        [HttpPost]
        [Authorize("UserPass")]
        [Authorize("Client")]
        public async Task<IActionResult> DeleteUser()
        {
            // FIXME: Error handle
            int uid = Convert.ToInt32(User.FindFirstValue("uid"));
            UserData userData = await dbContext.Users.FirstAsync(u => u.uid == uid);
            Client user = await dbContext.Clients.FirstAsync(u => u.uid == uid);

            dbContext.Clients.Remove(user);
            dbContext.Users.Remove(userData);
            await dbContext.SaveChangesAsync();

            return Ok(new ResultBody("User deleted successfully."));
        }
    }
}