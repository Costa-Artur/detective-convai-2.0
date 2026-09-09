using System.IO;
using UnityEngine;

namespace Detective.Dialogue
{
    // Le a API key do Azure OpenAI de um arquivo LOCAL, fora do git
    // (Assets/StreamingAssets/azure_openai_key.txt, listado no .gitignore).
    // Nunca hardcode a chave em codigo ou em um ScriptableObject serializado.
    public static class AzureApiKeyLoader
    {
        private const string FileName = "azure_openai_key.txt";

        public static bool TryLoad(out string apiKey)
        {
            string path = Path.Combine(Application.streamingAssetsPath, FileName);

            if (!File.Exists(path))
            {
                apiKey = null;
                Debug.LogError(
                    $"[AzureApiKeyLoader] Arquivo de chave nao encontrado em '{path}'. " +
                    "Crie o arquivo com sua chave do Azure OpenAI (uma linha, sem espacos). " +
                    "Esse arquivo esta no .gitignore e nao deve ser commitado.");
                return false;
            }

            apiKey = File.ReadAllText(path).Trim();

            if (string.IsNullOrEmpty(apiKey))
            {
                Debug.LogError($"[AzureApiKeyLoader] Arquivo '{FileName}' esta vazio.");
                return false;
            }

            return true;
        }
    }
}
