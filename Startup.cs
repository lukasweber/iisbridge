using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.IISIntegration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace iisbridge
{
    public class Startup
    {
        private ExecutableHandler exeHandler;
        private IConfiguration config;

        public Startup(IConfiguration config) {
            this.config = config;
        }
        public void ConfigureServices(IServiceCollection services)
        {  
            // Configure Authentication
            services.AddAuthentication(IISDefaults.AuthenticationScheme);
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env, IApplicationLifetime applicationLifetime)
        {
            // Register Shutdown Event
            applicationLifetime.ApplicationStopping.Register(OnShutdown);

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            // Configure & Start web app process handler
            exeHandler = new ExecutableHandler(
                config.GetValue<string>("appSettings:appProcessName"), 
                config.GetValue<string>("appSettings:appProcessArgs"), 
                config.GetValue<int>("appSettings:appProcessPort"),
                ParseEnvConfig(config.GetValue<string>("appSettings:appProcessEnv")));
                
            exeHandler.Start();

            // Configure reverse proxy
            app.UseReverseProxy(new ReverseProxyOptions {
                BaseUrl = $"http://localhost:" + config.GetValue<int>("appSettings:appProcessPort"),
                ExecutableHandler = exeHandler
            });
        }

        public void OnShutdown() {
            exeHandler.Stop();
        }

        private Dictionary<string, string> ParseEnvConfig(string config) 
        {
            if (config != null) {
                string[] envSettings = config.Split(';');
                Dictionary<string, string> dict = envSettings
                    .ToDictionary(setting => setting.Split("=")[0], setting => setting.Split("=")[1]);
                return dict;
            }
            return new Dictionary<string, string>();
        }
    }
}
