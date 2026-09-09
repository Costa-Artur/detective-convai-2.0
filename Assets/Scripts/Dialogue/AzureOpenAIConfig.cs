using UnityEngine;

namespace Detective.Dialogue
{
    // Parâmetros de conexão e geração para o NPC dinâmico via Azure OpenAI.
    // A API key NUNCA fica aqui (isto é serializado no .asset e vai pro git) -
    // ela é lida em runtime de Assets/StreamingAssets/azure_openai_key.txt,
    // que está no .gitignore. Ver docs/arquitetura-npc-dinamico.md secao 4/6.
    [CreateAssetMenu(fileName = "AzureOpenAIConfig", menuName = "Detective/Azure OpenAI Config")]
    public class AzureOpenAIConfig : ScriptableObject
    {
        [Header("Conexão (Azure AI Foundry / Azure OpenAI)")]
        [Tooltip("Ex: https://SEU-RECURSO.openai.azure.com - o dominio classico do RECURSO, " +
                 "NAO o endpoint de projeto do Foundry (.../api/projects/...), que e uma API diferente.")]
        public string endpoint = "https://detective-convai-ai-better.openai.azure.com/";

        [Tooltip("Nome do deployment do modelo, usado tanto em runtime (NPC dinamico) quanto na " +
                 "geracao offline do texto-ouro (§2.2). Ver 'isReasoningModel' abaixo - o nome sozinho " +
                 "nao muda o comportamento, precisa marcar o flag junto. " +
                 "Ver docs/arquitetura-npc-dinamico.md secao 4.4/4.8.")]
        public string deploymentName = "gpt-5.6-luna";

        [Tooltip("NAO usado na URL (o caminho /openai/v1/ da Azure e sem query param de versao - " +
                 "manda erro 400 se enviar 'api-version'). Mantido so como registro textual pra " +
                 "reprodutibilidade (RNF09): qual API path esta em uso.")]
        public string apiVersion = "v1 (sem query param)";

        [Header("Modelo de raciocínio (família gpt-5.x)")]
        [Tooltip("Marque se 'deploymentName' e um modelo de RACIOCINIO (gpt-5.x, ex: gpt-5.6-luna). " +
                 "Muda o corpo do request: max_completion_tokens em vez de max_tokens, sem " +
                 "'temperature', com 'reasoning_effort'. Desmarque para modelos nao-reasoning " +
                 "(ex: gpt-4.1-mini) - af usa max_tokens/temperature normalmente.")]
        public bool isReasoningModel = true;

        [Tooltip("Esforco de raciocinio (so usado se isReasoningModel=true). Valores aceitos variam " +
                 "por modelo - gpt-5.6-luna NAO aceita 'minimal', so: none, low, medium, high, xhigh. " +
                 "'low' reduz latencia - importante pro NPC dinamico ao vivo, onde o jogador esta " +
                 "esperando a resposta (ver §5 do mapa, simetria de latencia). 'none' pode desligar " +
                 "o raciocinio por completo, dependendo do modelo - testar se a qualidade cai.")]
        public string reasoningEffort = "low";

        [Header("Geração (calibrar na Fase 0 do mapa)")]
        public int optionCount = 2;
        public int maxTurns = 5;
        public int maxChars = 220;
        [Tooltip("Usado como max_tokens (modelo nao-reasoning) ou max_completion_tokens (reasoning).")]
        public int maxTokens = 300;
        [Range(0f, 2f)] public float temperature = 0.8f;
        public float timeoutSeconds = 20f;

        // API v1 - sem query param de versao (a Azure rejeita com 400 se enviar
        // "api-version" nesse caminho) e sem o nome do deployment na URL (vai no
        // campo "model" do corpo do request). Ver docs secao 4.8.
        public string ChatCompletionsUrl =>
            $"{endpoint.TrimEnd('/')}/openai/v1/chat/completions";
    }
}
