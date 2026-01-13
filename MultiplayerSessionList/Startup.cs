using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MultiplayerSessionList.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Runtime;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace MultiplayerSessionList
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
            string[] CorsOriginGames    = Configuration.GetSection("Cors")?.GetSection("Origin")?.GetSection("Games"   )?.Get<string[]>() ?? null;
            string[] CorsOriginSessions = Configuration.GetSection("Cors")?.GetSection("Origin")?.GetSection("Sessions")?.Get<string[]>() ?? null;

            if ((CorsOriginGames?.Length ?? 0) > 0 || (CorsOriginSessions?.Length ?? 0) > 0)
                services.AddCors(options =>
                {
                    if ((CorsOriginGames?.Length ?? 0) > 0)
                        options.AddPolicy("Games",
                            policy =>
                            {
                                policy.WithOrigins(CorsOriginGames);
                            });
                    if ((CorsOriginSessions?.Length ?? 0) > 0)
                        options.AddPolicy("Sessions",
                            policy =>
                            {
                                policy.WithOrigins(CorsOriginSessions);
                            });
                });

            services.AddMemoryCache();

            services.AddSingleton<GogInterface>();
            services.AddSingleton<SteamInterface>();
            services.AddHttpClient();
            services.AddSingleton<CachedAdvancedWebClient>();

            services.AddSingleton<GameListModuleManager>();
            services.AddScoped<ScopedGameListModuleManager>();
            GameListModuleManager.RegisterHandlers(services);

            services.AddControllersWithViews()
                .AddNewtonsoftJson(options =>
                {
                    options.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;

                    // This options stops the JSON being camel cased
                    options.SerializerSettings.ContractResolver = new DefaultContractResolver
                    {
                        NamingStrategy = new DefaultNamingStrategy(),
                    };
                })
                /*.AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
                });*/
                //.AddNdjson();
                //.AddNewtonsoftNdjson();
                ;
            //AddStreamedJson();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            if (!env.IsDevelopment())
                app.UseHttpsRedirection();

            app.UseStaticFiles();

            app.UseRouting();

            app.UseCors();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    //pattern: "{controller=Home}/{action=Index}/{id?}");
                    pattern: "{controller}/{action}/{id?}");
            });
        }
    }
}
