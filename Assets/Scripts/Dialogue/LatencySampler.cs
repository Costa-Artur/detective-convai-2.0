using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Detective.Dialogue
{
    // Roda N chamadas reais ao Azure OpenAI so pra medir a latencia (o
    // conteudo da fala e descartado) e acumula as amostras no
    // LatencySampleStore, que mais tarde alimenta o delay artificial dos
    // NPCs Yarn (mapa §5). Rode isto varias vezes, em horarios diferentes,
    // pra ter uma amostra representativa antes do piloto interno.
    //
    // Uso: Play Mode -> botao direito no cabecalho -> "Medir Latência".
    public class LatencySampler : MonoBehaviour
    {
        public AzureOpenAIConfig config;
        public int sampleCount = 10;

        [ContextMenu("Medir Latência (N chamadas)")]
        public async void MeasureLatency()
        {
            if (config == null)
            {
                Debug.LogError("[LatencySampler] Campo 'config' não atribuído.");
                return;
            }

            if (!AzureApiKeyLoader.TryLoad(out string apiKey))
                return;

            var client = new AzureOpenAIDialogueClient(config, apiKey);
            var measured = new List<float>();
            string tag = LatencySampleStore.BuildTag(config);

            client.OnRequestCompleted += (ms, success) =>
            {
                Debug.Log($"[LatencySampler] Chamada: {ms:F0}ms ({(success ? "sucesso" : "falha")})");
                if (success)
                {
                    measured.Add(ms);
                    LatencySampleStore.AppendSample(ms, tag);
                }
            };

            // Prompt minimo, so pra medir o tempo de resposta - conteudo nao importa.
            string systemPrompt =
                "Você é um NPC de teste. Responda em até 100 caracteres, ofereça exatamente " +
                $"{config.optionCount} opções, e defina \"encerrar\": true.";
            var history = new List<ChatMessage> { new ChatMessage("user", "Olá, tudo bem?") };

            Debug.Log($"[LatencySampler] Iniciando {sampleCount} chamadas...");

            for (int i = 0; i < sampleCount; i++)
            {
                try
                {
                    await client.GenerateTurnAsync(systemPrompt, history);
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[LatencySampler] Falha na chamada {i + 1}/{sampleCount}: {ex.Message}");
                }
            }

            if (measured.Count == 0)
            {
                Debug.LogError("[LatencySampler] Nenhuma chamada teve sucesso - nada foi registrado.");
                return;
            }

            measured.Sort();
            float mean = measured.Average();
            float median = measured[measured.Count / 2];
            int totalAccumulated = LatencySampleStore.LoadAllSamples(tag).Count;

            Debug.Log(
                $"[LatencySampler] Concluído: {measured.Count}/{sampleCount} sucesso nesta rodada. " +
                $"Média: {mean:F0}ms · Mediana: {median:F0}ms · Min: {measured.Min():F0}ms · Max: {measured.Max():F0}ms. " +
                $"Total acumulado em Logs/latency_{tag}_ms.txt: {totalAccumulated} amostras.");
        }
    }
}
