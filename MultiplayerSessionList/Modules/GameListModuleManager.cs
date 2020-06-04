using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MultiplayerSessionList.Modules
{
    public class GameListModuleManager
    {
        public Dictionary<string, IGameListModule> GameListPlugins { get; set; }

        public GameListModuleManager(IConfiguration Configuration, IServiceProvider ServiceProvider)
        {
            GameListPlugins = new Dictionary<string, IGameListModule>();

            foreach (Type item in typeof(IGameListModule).GetTypeInfo().Assembly.GetTypes())
            {
                //if (!item.IsClass) continue;
                if (item.GetInterfaces().Contains(typeof(IGameListModule)))
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

                            IGameListModule plugin = (IGameListModule)Activator.CreateInstance(item, paramList);
                            GameListPlugins.Add(plugin.GameID, plugin);

                            break;
                        }
                        catch { }
                    }
                }
            }
        }
    }
}