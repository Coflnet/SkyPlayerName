using System;
using System.IO;
using System.Reflection;
using Coflnet.Core.Security;
using Coflnet.Sky.PlayerName.Infrastructure;
using Microsoft.AspNetCore.Http;
using Coflnet.Sky.PlayerName.Services;
using Coflnet.Sky.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi;
using Prometheus;

namespace Coflnet.Sky.PlayerName
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "SkyPlayerName", Version = "v1" });
                // Set the comments path for the Swagger JSON and UI.
                var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
                c.IncludeXmlComments(xmlPath);
            });

            var serverVersion = new MariaDbServerVersion(new Version(Configuration["MARIADB_VERSION"]));
            var connectionString = TransportSecurity.DatabaseConnectionString(Configuration);
            if (Configuration.GetSection("DB:TLS").Exists())
            {
                services.AddKeyedSingleton<PemTlsOptions>("DB", (_, _) =>
                    PemTlsOptions.FromConfiguration(Configuration.GetSection("DB:TLS")));
                services.AddSingleton(p => new DatabaseTlsInterceptor(p.GetRequiredKeyedService<PemTlsOptions>("DB")));
            }
            if (Configuration.GetSection("HTTPS:TLS").Exists())
                services.AddKeyedSingleton<PemTlsOptions>("HTTPS", (_, _) =>
                    PemTlsOptions.FromConfiguration(Configuration.GetSection("HTTPS:TLS"), serverCertificate: true));
            services.AddDbContext<HypixelContext>((provider, options) =>
            {
                options.UseMySql(connectionString, serverVersion);
                var interceptor = provider.GetService<DatabaseTlsInterceptor>();
                if (interceptor != null) options.AddInterceptors(interceptor);
            });
            services.AddHostedService<DatabaseReadiness>();
            services.AddResponseCaching();
            services.AddMemoryCache();
            services.AddJaeger(Configuration);
            services.AddTransient<PlayerNameService>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "SkyPlayerName v1");
                c.RoutePrefix = "api";
            });

            app.UseRouting();

            app.UseResponseCaching();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/health/ready", async context =>
                {
                    try
                    {
                        var database = context.RequestServices.GetRequiredService<HypixelContext>();
                        await DatabaseReadiness.CheckAsync(database, context.RequestAborted);
                        context.Response.StatusCode = StatusCodes.Status200OK;
                    }
                    catch (Exception)
                    {
                        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    }
                });
                endpoints.MapMetrics();
                endpoints.MapControllers();
            });
        }
    }
}
