using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.ObjectPool;
using MultiplayerSessionList.Modules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MultiplayerSessionList.Services
{
    public class GameListModuleManager
    {
        //private IServiceProvider serviceProvider;
        public Dictionary<string, IGameListModuleOld> GameListPluginsOld { get; set; }
        //public Dictionary<string, IGameListModule> GameListPlugins { get; set; }
        private Dictionary<string, (Type Type, GameListModuleAttribute Data)> GameListPlugins { get; set; }

        public GameListModuleManager(IConfiguration Configuration, IServiceProvider ServiceProvider)
        {
            //this.serviceProvider = ServiceProvider;

            {
                //GameListPlugins = new Dictionary<string, IGameListModule>();
                GameListPlugins = new Dictionary<string, (Type Type, GameListModuleAttribute Data)>();

                //foreach (Type item in typeof(IGameListModule).GetTypeInfo().Assembly.GetTypes())
                //{
                //    //if (!item.IsClass) continue;
                //    if (item.GetInterfaces().Contains(typeof(IGameListModule)))
                //    {
                //        ConstructorInfo[] cons = item.GetConstructors();
                //        foreach (ConstructorInfo con in cons)
                //        {
                //            try
                //            {
                //                ParameterInfo[] @params = con.GetParameters();
                //                object[] paramList = new object[@params.Length];
                //                for (int i = 0; i < @params.Length; i++)
                //                {
                //                    paramList[i] = ServiceProvider.GetService(@params[i].ParameterType);
                //                }
                //                
                //                IGameListModule plugin = (IGameListModule)Activator.CreateInstance(item, paramList);
                //                GameListPlugins.Add(plugin.GameID, plugin);
                //        
                //                break;
                //            }
                //            catch { }
                //        }
                //    }
                //}

                foreach (Type type in typeof(GameListModuleAttribute).GetTypeInfo().Assembly.GetTypes())
                {
                    var attributes = type.GetCustomAttributes(typeof(GameListModuleAttribute), false);
                    if (attributes.Length > 0)
                    {
                        foreach (GameListModuleAttribute attribute in attributes)
                        {
                            GameListPlugins.Add(attribute.GameID, (type, attribute));
                        }
                    }
                }
            }

            {
                GameListPluginsOld = new Dictionary<string, IGameListModuleOld>();

                foreach (Type item in typeof(IGameListModuleOld).GetTypeInfo().Assembly.GetTypes())
                {
                    //if (!item.IsClass) continue;
                    if (item.GetInterfaces().Contains(typeof(IGameListModuleOld)))
                    {
                        ConstructorInfo[] cons = item.GetConstructors();
                        foreach (ConstructorInfo con in cons)
                        {
                            try
                            {
                                ParameterInfo[] @params = con.GetParameters();
                                object[] paramList = new object[@params.Length];
                                for (int i = 0; i < @params.Length; i++)
                                {
                                    paramList[i] = ServiceProvider.GetService(@params[i].ParameterType);
                                }

                                IGameListModuleOld plugin = (IGameListModuleOld)Activator.CreateInstance(item, paramList);
                                GameListPluginsOld.Add(plugin.GameID, plugin);

                                break;
                            }
                            catch { }
                        }
                    }
                }
            }
        }

        public List<(string GameID, string Title)> GetPluginList(bool ShowPrivate)
        {
            List<(string GameID, string Title)> result = new List<(string GameID, string Title)>();
            foreach (var item in GameListPlugins)
            {
                if (!ShowPrivate && !item.Value.Data.IsPublic)
                    continue;
                result.Add((item.Value.Data.GameID, item.Value.Data.Title));
            }
            return result;
        }

        public bool HasPlugin(string game)
        {
            return GameListPlugins.ContainsKey(game);
        }
        public bool IsPublic(string game)
        {
            try
            {
                if (!GameListPlugins.ContainsKey(game))
                    return false;

                return GameListPlugins[game].Data.IsPublic;
            }
            catch { }

            return false;
        }
        public Type GetPluginType(string game)
        {
            try
            {
                if (!GameListPlugins.ContainsKey(game))
                    return null;

                return GameListPlugins[game].Type;
            }
            catch { }

            return null;
        }
        //public IGameListModule GetPlugin(string game)
        //{
        //    try
        //    {
        //        if (!GameListPlugins.ContainsKey(game))
        //            return null;
        //        
        //        return serviceProvider.GetService(GameListPlugins[game].Type) as IGameListModule;
        //    }
        //    catch { }
        //
        //    return null;
        //}

        public static void RegisterHandlers(IServiceCollection services)
        {
            foreach (Type item in typeof(IGameListModule).GetTypeInfo().Assembly.GetTypes())
            {
                //if (!item.IsClass) continue;
                if (item.GetInterfaces().Contains(typeof(IGameListModule)))
                {
                    services.AddScoped(item);
                }
            }
        }
    }

    public class ScopedGameListModuleManager
    {
        private GameListModuleManager manager;
        private IServiceProvider serviceProvider;
        public ScopedGameListModuleManager(GameListModuleManager manager, IServiceProvider ServiceProvider)
        {
            this.manager = manager;
            this.serviceProvider = ServiceProvider;
        }
        public IGameListModule GetPlugin(string game)
        {
            try
            {
                if (!manager.HasPlugin(game))
                    return null;
                
                return serviceProvider.GetService(manager.GetPluginType(game)) as IGameListModule;
            }
            catch { }

            return null;
        }
    }
}