using UnityEngine;

namespace Detective.Dialogue
{
    // Componente isolado so pra validar a conexao com o Azure OpenAI, sem
    // depender da UI de jogo nem do DynamicNPCController. Uso:
    // 1. Crie um GameObject vazio na cena, adicione este componente.
    // 2. Arraste o asset AzureOpenAIConfig no campo "Config".
    // 3. Entre em Play Mode (o teste usa UnityWebRequest, que precisa do loop
    //    de frame do Unity rodando - nao funciona parado no modo Edit).
    // 4. Botao direito no cabecalho do componente no Inspector -> "Testar
    //    Conexao Azure OpenAI". Resultado aparece na janela Console.
    public class AzureConnectionTest : MonoBehaviour
    {
        public AzureOpenAIConfig config;

        [ContextMenu("Testar Conexão Azure OpenAI")]
        public async void TestConnection()
        {
            Debug.Log("[AzureConnectionTest] Iniciando teste...");

            if (config == null)
            {
                Debug.LogError("[AzureConnectionTest] Campo 'config' nao atribuido no Inspector.");
                return;
            }

            if (!AzureApiKeyLoader.TryLoad(out string apiKey))
                return; // AzureApiKeyLoader ja loga o erro especifico

            var client = new AzureOpenAIDialogueClient(config, apiKey);

            string systemPrompt =
                "Voce e o Senhor Verde, um convidado educado e um pouco evasivo numa festa " +
                "onde houve um assassinato. Responda em ate 200 caracteres. " +
                "Ofereca exatamente 2 opcoes de resposta para o jogador. " +
                "Este e o ultimo turno: conclua o assunto e defina \"encerrar\": true.";

            var history = new System.Collections.Generic.List<ChatMessage>
            {
                new ChatMessage("user", "Onde você estava na hora do crime?")
            };

            try
            {
                DynamicTurnResult result = await client.GenerateTurnAsync(systemPrompt, history);

                Debug.Log("[AzureConnectionTest] SUCESSO - conexao com Azure OpenAI funcionando.");
                Debug.Log($"[AzureConnectionTest] fala: {result.fala}");
                Debug.Log($"[AzureConnectionTest] opcoes: {string.Join(" | ", result.opcoes)}");
                Debug.Log($"[AzureConnectionTest] encerrar: {result.encerrar}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[AzureConnectionTest] FALHA na chamada: {ex.Message}");
            }
        }
    }
}
