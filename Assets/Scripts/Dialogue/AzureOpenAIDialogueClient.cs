using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Detective.Dialogue
{
    // Cliente REST para o Azure OpenAI (chat completions com saida estruturada),
    // usado para gerar a fala e as opcoes do NPC dinamico em runtime, e tambem
    // (mesmo deployment, por restricao de cota) na geracao offline do texto-ouro.
    // Suporta tanto modelos NAO-reasoning (ex: gpt-4.1-mini, usa max_tokens/
    // temperature) quanto modelos de RACIOCINIO (familia gpt-5.x, ex:
    // gpt-5.6-luna, usa max_completion_tokens/reasoning_effort, sem temperature)
    // via o flag AzureOpenAIConfig.isReasoningModel. Continua usando Chat
    // Completions nos dois casos (nao migrado para Responses API) - a familia
    // gpt-5.6 ainda suporta esse caminho, desde que sem function tools.
    // Ver docs/arquitetura-npc-dinamico.md secoes 3.1, 4.1, 4.2, 4.4 e 4.8.
    public class AzureOpenAIDialogueClient
    {
        private readonly AzureOpenAIConfig _config;
        private readonly string _apiKey;

        // Disparado apos cada chamada HTTP (sucesso ou falha), com o tempo
        // decorrido em ms. Usado para medir a latencia real e calibrar o
        // delay artificial dos NPCs Yarn (mapa §5). Ver LatencySampleStore.cs.
        public event Action<float, bool> OnRequestCompleted;

        // Conteudo exato devolvido pelo modelo na ultima chamada (o JSON do
        // turno, antes do parse), para o log de sessao. Null se a chamada
        // falhou antes de haver resposta.
        public string LastRawContent { get; private set; }

        public AzureOpenAIDialogueClient(AzureOpenAIConfig config, string apiKey)
        {
            _config = config;
            _apiKey = apiKey;
        }

        // systemPrompt: persona + regras de formato (ver doc 4.2).
        // history: mensagens anteriores da conversa (user = escolha do jogador,
        // assistant = fala anterior do NPC), para manter coerencia contextual.
        // cardNames: nomes das cartas que o NPC pode entregar - viram o "enum"
        // de revelar_pista e carta_alvo, entao o servidor so aceita cartas
        // que o NPC de fato tem (null = nenhuma, usado pelos testes).
        public async Task<DynamicTurnResult> GenerateTurnAsync(string systemPrompt, List<ChatMessage> history,
                                                               IList<string> cardNames = null)
        {
            var messages = new List<ChatMessage> { new ChatMessage("system", systemPrompt) };
            messages.AddRange(history);

            string body = _config.isReasoningModel
                ? JsonUtility.ToJson(new ReasoningChatRequest
                {
                    model = _config.deploymentName,
                    messages = messages.ToArray(),
                    max_completion_tokens = _config.maxTokens,
                    reasoning_effort = _config.reasoningEffort,
                    response_format = BuildResponseFormat(_config.optionCount, cardNames)
                })
                : JsonUtility.ToJson(new ChatRequest
                {
                    model = _config.deploymentName,
                    messages = messages.ToArray(),
                    max_tokens = _config.maxTokens,
                    temperature = _config.temperature,
                    response_format = BuildResponseFormat(_config.optionCount, cardNames)
                });

            LastRawContent = null;
            string content = await PostAsync(_config.ChatCompletionsUrl, body);
            LastRawContent = content;

            var parsed = JsonUtility.FromJson<DynamicTurnResult>(content);

            if (parsed == null || parsed.opcoes == null || parsed.opcoes.Length != _config.optionCount ||
                parsed.papeis_opcoes == null || parsed.papeis_opcoes.Length != _config.optionCount)
            {
                throw new Exception(
                    $"[AzureOpenAIDialogueClient] Resposta fora do contrato esperado " +
                    $"(opcoes esperadas: {_config.optionCount}). Conteudo bruto: {content}");
            }

            return parsed;
        }

        private static ResponseFormat BuildResponseFormat(int optionCount, IList<string> cardNames)
        {
            var cards = new List<string> { "" };
            if (cardNames != null)
            {
                foreach (string name in cardNames)
                    if (!string.IsNullOrEmpty(name) && !cards.Contains(name))
                        cards.Add(name);
            }

            return new ResponseFormat
            {
                json_schema = new JsonSchemaWrapper
                {
                    schema = new TurnSchema
                    {
                        properties = new TurnSchemaProperties
                        {
                            opcoes = new SchemaArrayField { minItems = optionCount, maxItems = optionCount },
                            papeis_opcoes = new SchemaEnumArrayField
                            {
                                items = new SchemaEnumField { @enum = OptionRoles.Todos },
                                minItems = optionCount,
                                maxItems = optionCount
                            },
                            revelar_pista = new SchemaEnumField { @enum = cards.ToArray() },
                            carta_alvo = new SchemaEnumField { @enum = cards.ToArray() }
                        }
                    }
                }
            };
        }

        private async Task<string> PostAsync(string url, string jsonBody)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            using (var www = new UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                www.uploadHandler = new UploadHandlerRaw(bodyRaw);
                www.downloadHandler = new DownloadHandlerBuffer();
                www.SetRequestHeader("Content-Type", "application/json");
                www.SetRequestHeader("api-key", _apiKey); // Azure usa header "api-key", nao "Authorization: Bearer"
                www.timeout = Mathf.CeilToInt(_config.timeoutSeconds);

                var op = www.SendWebRequest();
                while (!op.isDone)
                    await Task.Yield();

                stopwatch.Stop();
                bool success = www.result == UnityWebRequest.Result.Success;
                OnRequestCompleted?.Invoke((float)stopwatch.Elapsed.TotalMilliseconds, success);

                if (!success)
                {
                    throw new Exception(
                        $"[AzureOpenAIDialogueClient] Falha na chamada ({www.responseCode}): " +
                        $"{www.error}. Corpo: {www.downloadHandler?.text}");
                }

                var response = JsonUtility.FromJson<ChatResponse>(www.downloadHandler.text);

                if (response?.choices == null || response.choices.Length == 0)
                {
                    throw new Exception(
                        $"[AzureOpenAIDialogueClient] Resposta sem 'choices'. Corpo: {www.downloadHandler.text}");
                }

                ChatChoice choice = response.choices[0];
                if (string.IsNullOrEmpty(choice.message?.content))
                {
                    // finish_reason "length" = o modelo estourou max_completion_tokens
                    // (nos modelos de raciocinio, pensar consome esse mesmo limite).
                    throw new Exception(
                        $"[AzureOpenAIDialogueClient] Resposta sem conteudo (finish_reason: " +
                        $"{choice.finish_reason}). Se for 'length', aumente maxTokens no AzureOpenAIConfig.");
                }

                return choice.message.content;
            }
        }
    }
}
