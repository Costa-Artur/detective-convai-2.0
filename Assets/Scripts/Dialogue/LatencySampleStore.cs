using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Detective.Dialogue
{
    // Guarda e reamostra latencias reais medidas do Azure OpenAI, para
    // calibrar o delay artificial aplicado aos NPCs Yarn (mapa §5 - simetria
    // de latencia entre todos os NPCs).
    //
    // Arquivos locais em Logs/ (pasta ja no .gitignore) - nunca versionados.
    // SEPARADOS POR CONFIGURACAO (modelo + reasoning_effort): latencia e
    // inseparavel desses dois parametros, entao misturar medicoes de
    // configuracoes diferentes no mesmo arquivo invalidaria a calibragem.
    // Ex: Logs/latency_gpt-5.6-luna_medium_ms.txt
    public static class LatencySampleStore
    {
        private static string LogsDirectory =>
            Path.Combine(Application.dataPath, "..", "Logs");

        // Identificador da configuracao usada na medicao - vira parte do nome
        // do arquivo, garantindo que amostras de configs diferentes nunca se
        // misturem.
        public static string BuildTag(AzureOpenAIConfig config)
        {
            if (config == null)
                return "desconhecido";

            string effort = config.isReasoningModel ? config.reasoningEffort : "sem-reasoning";
            string raw = $"{config.deploymentName}_{effort}";

            // Remove qualquer caractere invalido para nome de arquivo.
            return Regex.Replace(raw, @"[^\w\.\-]", "_");
        }

        private static string FilePathFor(string tag) =>
            Path.Combine(LogsDirectory, $"latency_{tag}_ms.txt");

        public static void AppendSample(float latencyMs, string tag)
        {
            if (!Directory.Exists(LogsDirectory))
                Directory.CreateDirectory(LogsDirectory);

            File.AppendAllText(FilePathFor(tag), latencyMs.ToString("F0") + "\n");
        }

        public static List<float> LoadAllSamples(string tag)
        {
            var samples = new List<float>();
            string path = FilePathFor(tag);

            if (!File.Exists(path))
                return samples;

            foreach (string line in File.ReadAllLines(path))
            {
                if (float.TryParse(line, out float ms))
                    samples.Add(ms);
            }
            return samples;
        }

        // Amostragem empirica (bootstrap): sorteia um valor JA MEDIDO de
        // verdade com ESTA configuracao, em vez de assumir a forma de uma
        // distribuicao (normal, uniforme etc). Retorna null se ainda nao ha
        // amostras suficientes - quem chama decide o fallback.
        public static float? GetRandomDelaySeconds(string tag, int minimumSamples = 5)
        {
            List<float> samples = LoadAllSamples(tag);
            if (samples.Count < minimumSamples)
                return null;

            float ms = samples[Random.Range(0, samples.Count)];
            return ms / 1000f;
        }
    }
}
