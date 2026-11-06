using UnityEngine;
using System.IO;

namespace Fortal.Configuration
{
    public abstract class BaseConfigManager<TManager, TConfig> : MonoBehaviour
        where TManager : BaseConfigManager<TManager, TConfig>
        where TConfig : class, new()
    {
        public static TManager Instance { get; private set; }
        public string fileName;
        public bool debugMode = false;

        public TConfig Config;
        private string filePath;

        protected abstract string DefaultFileName { get; }

        protected virtual void Awake()
        {
            if (Instance != null)
            {
                Destroy(this);
                return;
            }

            Instance = (TManager)this;
            // Keep configuration scene-local: this object can also host gameplay components.
            // A scene reload must recreate those components and their scene references.

            if (string.IsNullOrEmpty(fileName))
                fileName = DefaultFileName;

            filePath = Path.Combine(Application.persistentDataPath, $"{fileName}.json");
            LoadConfig();
        }

        protected virtual void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void SaveConfig()
        {
            string json = JsonUtility.ToJson(Config, true);
            File.WriteAllText(filePath, json);
            if (debugMode)
                Debug.Log($"{fileName} Saved to: {filePath}");
        }

        public void LoadConfig()
        {
            if (debugMode)
                Debug.Log($"Load {fileName} from: {filePath}");

            // Keep the project StreamingAssets JSON as the calibration source of truth,
            // matching the paintingGround UWB setup. This prevents an old persistent copy
            // from silently restoring a stale coordinate mapping.
            string streamingPath = Path.Combine(Application.streamingAssetsPath, $"{fileName}.json");
            if (File.Exists(streamingPath))
            {
                string streamingJson = File.ReadAllText(streamingPath);
                TConfig streamingConfig = JsonUtility.FromJson<TConfig>(streamingJson);
                if (streamingConfig != null)
                {
                    Config = streamingConfig;
                    if (debugMode)
                        Debug.Log($"Loaded {fileName} from project StreamingAssets: {streamingPath}");
                    return;
                }
            }

            if (File.Exists(filePath))
            {
                string json = File.ReadAllText(filePath);
                Config = JsonUtility.FromJson<TConfig>(json);
            }
            else
            {
                if (debugMode)
                    Debug.Log($"No {fileName} file exists, creating default...");

                Config = new TConfig();
                SaveConfig();
            }
        }
    }
}
