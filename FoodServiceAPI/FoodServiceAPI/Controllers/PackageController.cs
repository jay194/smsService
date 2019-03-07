﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using FoodServiceAPI.Database;
using FoodServiceAPI.Models;
using FoodServiceAPI.Jobs;

namespace FoodServiceAPI.Controllers
{
    [Produces("application/json")]
    [Route("api/package")]
    public class PackageController : Controller
    {
        private readonly FoodContext dbContext;
        private readonly DbContextOptions dbOptions;

        public class PackageRetrievalOptions
        {
            public bool only_eligible { get; set; }
        }

        public class PackageIdentifier
        {
            public int pid { get; set; }
        }

        public class PackageClaimOptions : PackageIdentifier
        {
            public bool? claim { get; set; }
        }

        public class PackageDesign
        {
            public string name { get; set; }
            public string description { get; set; }
            public string quantity { get; set; }
            public decimal price { get; set; }
            public DateTime? expires { get; set; }
        }

        public class PackageResult
        {
            public class PackageInfo
            {
                public int pid { get; set; }
                public int owner_bid { get; set; }
                public int? claimer_cid { get; set; }
                public string name { get; set; }
                public string description { get; set; }
                public string quantity { get; set; }
                public decimal price { get; set; }
                public DateTime created { get; set; }
                public DateTime? expires { get; set; }
                public DateTime? claimed { get; set; }
                public DateTime? received { get; set; }
                public string business_name { get; set; }
                public string business_address { get; set; }

                public PackageInfo(Package package, string businessName, string businessAddress)
                {
                    pid = package.pid;
                    owner_bid = package.owner_bid;
                    claimer_cid = package.claimer_cid;
                    name = package.name;
                    description = package.description;
                    quantity = package.quantity;
                    price = package.price;
                    created = package.created;
                    expires = package.expires;
                    claimed = package.claimed;
                    received = package.received;
                    business_name = businessName;
                    business_address = businessAddress;
                }
            }

            public List<PackageInfo> packages { get; set; }
        }

        public PackageController(FoodContext dbContext, DbContextOptions dbOptions)
        {
            this.dbContext = dbContext;
            this.dbOptions = dbOptions;
        }

        [Route("getpackages")]
        [HttpPost]
        [Authorize("Session")]
        public async Task<IActionResult> GetPackages([FromBody]PackageRetrievalOptions options)
        {
            PackageResult result = new PackageResult();

            if(User.FindFirst("bid") != null)
            {
                int bid = Convert.ToInt32(User.FindFirstValue("bid"));
                Business business = await dbContext.Businesses.Include(b => b.User).FirstAsync(b => b.bid == bid);

                IQueryable<PackageResult.PackageInfo> query =
                    from p in dbContext.Packages
                    where p.owner_bid == bid
                    select new PackageResult.PackageInfo(p, business.name, business.User.address);

                if(options.only_eligible)
                    query = query.Where(p => p.received == null);

                result.packages = await query.ToListAsync();
            }
            else if(User.FindFirst("cid") != null)
            {
                int cid = Convert.ToInt32(User.FindFirstValue("cid"));

                IQueryable<PackageResult.PackageInfo> query =
                    from p in dbContext.Packages
                    join n in dbContext.Notices on p.pid equals n.pid into pn
                    from n in pn.DefaultIfEmpty()
                    where (n.cid == cid && p.claimer_cid == null) ||
                        (p.claimer_cid == cid && (!options.only_eligible || p.received == null))
                    join b in dbContext.Businesses on p.owner_bid equals b.bid
                    join u in dbContext.Users on b.uid equals u.uid
                    select new PackageResult.PackageInfo(p, b.name, u.address);

                result.packages = await query.ToListAsync();
            }
            else
                return BadRequest(new ResultBody("Function does not support this user type."));

            return Ok(result);
        }

        [Route("createpackage")]
        [HttpPost]
        [Authorize("Session")]
        [Authorize("Business")]
        public async Task<IActionResult> CreatePackage([FromBody]PackageDesign design)
        {
            int bid = Convert.ToInt32(User.FindFirstValue("bid"));

            Package package = new Package
            {
                owner_bid = bid,
                name = design.name,
                description = design.description,
                quantity = design.quantity,
                price = design.price,
                created = DateTime.UtcNow,
                expires = design.expires
            };

            await dbContext.Packages.AddAsync(package);
            await dbContext.SaveChangesAsync();
            StartPackageNotification(package);

            return Ok(new ResultBody("Package created."));
        }

        [Route("deletepackage")]
        [HttpPost]
        [Authorize("Session")]
        [Authorize("Business")]
        public async Task<IActionResult> DeletePackage([FromBody]PackageIdentifier identifier)
        {
            // FIXME: Should businesses be able to delete claimed or received packages?
            int bid = Convert.ToInt32(User.FindFirstValue("bid"));
            Package package = await dbContext.Packages.FirstOrDefaultAsync(p => p.pid == identifier.pid && p.owner_bid == bid);

            if (package == null)
                return BadRequest(new ResultBody("No such package owned by this business"));

            dbContext.Packages.Remove(package);
            await dbContext.SaveChangesAsync();

            return Ok(new ResultBody("Package has been deleted."));
        }

        [Route("claim")]
        [HttpPost]
        [Authorize("Session")]
        public async Task<IActionResult> ClaimPackage([FromBody]PackageClaimOptions options)
        {
            int? claimerCID = null;
            Package package;

            if (User.FindFirst("bid") != null)
            {
                // Get package if owned by this business, claimed, and not yet received
                int bid = Convert.ToInt32(User.FindFirstValue("bid"));
                package = await dbContext.Packages.FindAsync(options.pid);

                if(package == null || package.owner_bid != bid || package.claimer_cid == null || package.received != null)
                    return BadRequest(new ResultBody("No such package owned by this business and possible to unclaim"));
            }
            else if(User.FindFirst("cid") != null)
            {
                int cid = Convert.ToInt32(User.FindFirstValue("cid"));

                if (options.claim == true)
                {
                    // Get package if client has a notice for it
                    package = await (
                        from p in dbContext.Packages
                        where p.pid == options.pid
                        where dbContext.Notices.Any(n => n.cid == cid && n.pid == options.pid)
                        select p
                    ).FirstOrDefaultAsync();

                    claimerCID = cid;
                }
                else
                {
                    // Get package if client is current claimer
                    package = await dbContext.Packages.FirstOrDefaultAsync(p => p.pid == options.pid && p.claimer_cid == cid);
                }

                if(package == null)
                    return BadRequest(new ResultBody("No such package available to this client for this operation"));
            }
            else
                return BadRequest(new ResultBody("Function does not support this user type"));

            package.claimer_cid = claimerCID;

            if (claimerCID == null)
            {
                package.claimed = null;
                await dbContext.SaveChangesAsync();

                // Begin recreating notices
                /* FIXME: It would be nice to retrieve or reactivate old notices from before the
                package was claimed */
                StartPackageNotification(package);
            }
            else
            {
                package.claimed = DateTime.UtcNow;
                dbContext.Notices.RemoveRange(dbContext.Notices.Where(n => n.pid == package.pid)); // Delete all notices
                await dbContext.SaveChangesAsync();
            }

            return Ok(new ResultBody(claimerCID == null ? "Unclaimed package" : "Claimed package"));
        }

        [Route("markreceived")]
        [HttpPost]
        [Authorize("Session")]
        [Authorize("Business")]
        public async Task<IActionResult> MarkReceived([FromBody]PackageIdentifier identifier)
        {
            int bid = Convert.ToInt32(User.FindFirstValue("bid"));
            Package package = await dbContext.Packages.FindAsync(identifier.pid);

            if (package == null || package.owner_bid != bid || package.claimer_cid == null || package.received != null)
                return BadRequest(new ResultBody("No such claimed and unreceived package owned by this business"));

            package.received = DateTime.UtcNow;
            await dbContext.SaveChangesAsync();
            return Ok(new ResultBody("Marked package as received"));
        }

        public void StartPackageNotification(Package package)
        {
            // FIXME: Should be variables based on number of viable clients and expiration date
            const int INTERVAL = 10;
            const int NUM_PER_NOTIFY = 1;

            FluentScheduler.JobManager.AddJob(
                new PackageNotifyJob(dbOptions, package.pid, INTERVAL, NUM_PER_NOTIFY),
                s => s.ToRunNow()
            );
        }
    }
}