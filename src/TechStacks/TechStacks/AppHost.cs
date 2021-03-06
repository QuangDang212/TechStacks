﻿using System;
using System.IO;
using Funq;
using ServiceStack;
using ServiceStack.Auth;
using ServiceStack.Caching;
using ServiceStack.Configuration;
using ServiceStack.Data;
using ServiceStack.Host.Handlers;
using ServiceStack.OrmLite;
using ServiceStack.Razor;
using ServiceStack.Text;
using ServiceStack.Validation;
using TechStacks.ServiceInterface;
using TechStacks.ServiceModel.Types;

namespace TechStacks
{
    public class AppHost : AppHostBase
    {
        /// <summary>
        /// Default constructor.
        /// Base constructor requires a name and assembly to locate web service classes. 
        /// </summary>
        public AppHost()
            : base("TechStacks", typeof(TechnologyServices).Assembly)
        {
            var customSettings = new FileInfo(@"~/appsettings.txt".MapHostAbsolutePath());
            AppSettings = customSettings.Exists
                ? (IAppSettings)new TextFileSettings(customSettings.FullName)
                : new AppSettings();
        }

        /// <summary>
        /// Application specific configuration
        /// This method should initialize any IoC resources utilized by your web service classes.
        /// </summary>
        /// <param name="container"></param>
        public override void Configure(Container container)
        {
            SetConfig(new HostConfig {
                AddRedirectParamsToQueryString = true,
                WebHostUrl = "http://techstacks.io", //for sitemap.xml urls
            });

            JsConfig.DateHandler = DateHandler.ISO8601;

            if (AppSettings.GetString("OrmLite.Provider") == "Postgres")
            {
                container.Register<IDbConnectionFactory>(new OrmLiteConnectionFactory(AppSettings.GetString("OrmLite.ConnectionString"), PostgreSqlDialect.Provider));
            }
            else
            {
                container.Register<IDbConnectionFactory>(new OrmLiteConnectionFactory("~/App_Data/db.sqlite".MapHostAbsolutePath(), SqliteDialect.Provider));
            }

            var dbFactory = container.Resolve<IDbConnectionFactory>();

            this.Plugins.Add(new AuthFeature(() => new CustomUserSession(), new IAuthProvider[]
            {
                new TwitterAuthProvider(AppSettings), 
                new GithubAuthProvider(AppSettings)
            }));

            container.Register(new TwitterUpdates(
                AppSettings.GetString("WebStacks.ConsumerKey"),
                AppSettings.GetString("WebStacks.ConsumerSecret"),
                AppSettings.GetString("WebStacks.AccessToken"),
                AppSettings.GetString("WebStacks.AccessSecret")));

            var authRepo = new OrmLiteAuthRepository<CustomUserAuth, UserAuthDetails>(dbFactory);
            container.Register<IUserAuthRepository>(authRepo);
            authRepo.InitSchema();

            container.RegisterAs<OrmLiteCacheClient, ICacheClient>();
            container.Resolve<ICacheClient>().InitSchema();

            container.Register(c => new ContentCache(new MemoryCacheClient()));

            using (var db = dbFactory.OpenDbConnection())
            {
                db.CreateTableIfNotExists<TechnologyStack>();
                db.CreateTableIfNotExists<Technology>();
                db.CreateTableIfNotExists<TechnologyChoice>();
                db.CreateTableIfNotExists<UserFavoriteTechnologyStack>();
                db.CreateTableIfNotExists<UserFavoriteTechnology>();

                RawHttpHandlers.Add(req => req.PathInfo == "/robots.txt" ? new NotFoundHttpHandler() : null);

                Plugins.Add(new SitemapFeature
                {
                    SitemapIndex = {
                        new Sitemap {
                            AtPath = "/sitemap-techstacks.xml",
                            LastModified = DateTime.UtcNow,
                            UrlSet = db.Select<TechnologyStack>(q => q.OrderByDescending(x => x.LastModified))
                                .Map(x => new SitemapUrl
                                {
                                    Location = new ClientTechnologyStack { Slug = x.Slug }.ToAbsoluteUri(),
                                    LastModified = x.LastModified,
                                    ChangeFrequency = SitemapFrequency.Weekly,
                                }),
                        },
                        new Sitemap {
                            AtPath = "/sitemap-technologies.xml",
                            LastModified = DateTime.UtcNow,
                            UrlSet = db.Select<Technology>(q => q.OrderByDescending(x => x.LastModified))
                                .Map(x => new SitemapUrl
                                {
                                    Location = new ClientTechnology { Slug = x.Slug }.ToAbsoluteUri(),
                                    LastModified = x.LastModified,
                                    ChangeFrequency = SitemapFrequency.Weekly,
                                })
                        },
                        new Sitemap
                        {
                            AtPath = "/sitemap-users.xml",
                            LastModified = DateTime.UtcNow,
                            UrlSet = db.Select<CustomUserAuth>(q => q.OrderByDescending(x => x.ModifiedDate))
                                .Map(x => new SitemapUrl
                                {
                                    Location = new ClientUser { UserName = x.UserName }.ToAbsoluteUri(),
                                    LastModified = x.ModifiedDate,
                                    ChangeFrequency = SitemapFrequency.Weekly,
                                })
                        }
                    }
                });

            }

            Plugins.Add(new RazorFormat());
            Plugins.Add(new AutoQueryFeature { MaxLimit = 200 });
            Plugins.Add(new ValidationFeature());

            container.RegisterValidators(typeof(AppHost).Assembly);
            container.RegisterValidators(typeof(TechnologyServices).Assembly);
        }
    }
}
