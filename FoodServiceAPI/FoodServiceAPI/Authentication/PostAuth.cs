﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Encodings.Web;
using System.Security.Claims;
using System.IO;
using Newtonsoft.Json;
using FoodServiceAPI.Database;
using FoodServiceAPI.Models;

namespace FoodServiceAPI.Authentication
{
    /*
        Reads "username" and "password" parameters in POST body containing JSON and checks
        against database. Successful authentication gives User the "uid" and "username" claims.
    */
    
    // FIXME: What should Fail messages contain? Should they influence what's sent to the client?

    public class PostAuthDefaults
    {
        public const string AuthenticationScheme = "PostAuth";
    }

    public class PostAuthOptions : AuthenticationSchemeOptions
    {
        // No options
    }

    public class PostAuthHandler : AuthenticationHandler<PostAuthOptions>
    {
        public class Credentials
        {
            public string username { get; set; }
            public string password { get; set; }
        }

        public PostAuthHandler(IOptionsMonitor<PostAuthOptions> options, ILoggerFactory logger, UrlEncoder encoder, IDataProtectionProvider dataProtection, ISystemClock clock)
            : base(options, logger, encoder, clock)
        { }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            // Request must be POST containing JSON
            if(Request.Method != "POST" || !Request.ContentType.StartsWith("application/json"))
                return AuthenticateResult.NoResult();

            // Parse body JSON for credentials
            Credentials cred;

            using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                string body = await reader.ReadToEndAsync();
                cred = JsonConvert.DeserializeObject(body, typeof(Credentials)) as Credentials;

                // Hack: copy request stream so it's not consumed
                Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
            }

            // Get user info
            FoodContext dbContext = Context.RequestServices.GetService(typeof(FoodContext)) as FoodContext;

            var user = await (
                from u in dbContext.Users
                where u.username == cred.username
                join b in dbContext.Businesses on u.uid equals b.uid into ub
                from b in ub.DefaultIfEmpty()
                join c in dbContext.Clients on u.uid equals c.uid into ubc
                from c in ubc.DefaultIfEmpty()
                select new
                {
                    u.uid,
                    u.username,
                    u.password,
                    bid = (int?)b.bid,
                    cid = (int?)c.cid
                }
            ).FirstOrDefaultAsync();

            if(user == null)
                return AuthenticateResult.Fail("no such user");

            // Check password
            PasswordProtector protector = new PasswordProtector();

            if(!protector.Compare(cred.password, user.password))
                return AuthenticateResult.Fail("incorrect password");

            // Authenticated; create identity
            Claim[] claims = new[]
            {
                new Claim("uid", user.uid.ToString()),
                new Claim("username", user.username)
            };

            ClaimsIdentity identity = new ClaimsIdentity(claims, Scheme.Name);

            if (user.bid != null)
                identity.AddClaim(new Claim("bid", user.bid.ToString()));
            else if (user.cid != null)
                identity.AddClaim(new Claim("cid", user.cid.ToString()));

            ClaimsPrincipal principal = new ClaimsPrincipal(identity);

            return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
        }
    }
}
