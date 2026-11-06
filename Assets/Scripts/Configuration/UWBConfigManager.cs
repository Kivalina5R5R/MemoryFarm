using UnityEngine;

namespace Fortal.Configuration
{
    public class UWBConfigManager : BaseConfigManager<UWBConfigManager, UWBConfigData>
    {
        protected override string DefaultFileName => "UWBConfig";

        public static UWBConfigData GetConfig()
        {
            if (Instance == null)
            {
                Debug.LogError("UWBConfigManager instance not found!");
                return null;
            }
            return Instance.Config;
        }
    }
}
