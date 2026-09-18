using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Detective.Dialogue
{
    // Controla a conversa com o NPC dinamico: mantem historico e turnIndex,
    // chama o Azure OpenAI a cada turno e aplica o cap de profundidade (maxTurns).
    // Ver docs/arquitetura-npc-dinamico.md secao 3.1.
    //
    // Este script ainda precisa ser ligado na cena (prefab do NPC dinamico +
    // UI de multipla escolha) - isso e trabalho de Editor, nao de codigo.
    public class DynamicNPCController : MonoBehaviour
    {
        [Header("Configuracao")]
        public AzureOpenAIConfig config;

        [Tooltip("Cache local usado se a chamada ao Azure OpenAI falhar (risco 4.7.3 do TCC: " +
                 "'Problemas tecnicos durante a integracao com motores de IA').")]
        public FallbackDialoguePool fallbackPool;

        [Header("Persona do personagem (base fatual - ver doc secao 2.2)")]
        [TextArea(5, 15)]
        public string personaBackstory;

        [Tooltip("Cartas extras injetadas no prompt, escritas à mão. As cartas REAIS da partida " +
                 "são lidas automaticamente do LocalInventory deste mesmo NPC - esta lista é " +
                 "somada àquelas, e serve principalmente para testes (ex: a pista-isca do " +
                 "DynamicNPCContextTest).")]
        public List<string> clueCardNames = new List<string>();

        private AzureOpenAIDialogueClient _client;
        private readonly List<ChatMessage> _history = new List<ChatMessage>();
        private int _turnIndex;
        private int _consecutiveFailures;
        private float _lastLatencyMs = -1f;

        public bool IsBusy { get; private set; }
        public int TurnIndex => _turnIndex;

        private void Awake()
        {
            if (config == null)
            {
                Debug.LogError($"[DynamicNPCController] Campo 'config' não atribuído em '{gameObject.name}'. " +
                                "Arraste o asset AzureOpenAIConfig no Inspector.");
                enabled = false;
                return;
            }

            if (!AzureApiKeyLoader.TryLoad(out string apiKey))
            {
                enabled = false; // sem chave, este NPC nao funciona - evita null ref em runtime
                return;
            }

            _client = new AzureOpenAIDialogueClient(config, apiKey);
            _client.OnRequestCompleted += (ms, _) => _lastLatencyMs = ms;
        }

        // Inicia a conversa (equivalente ao "Inicio" de um no .yarn).
        public async Task<DynamicTurnResult> StartConversation()
        {
            _history.Clear();
            _turnIndex = 0;
            return await RequestNextTurn();
        }

        // Avanca a conversa a partir da opcao que o jogador escolheu.
        public async Task<DynamicTurnResult> ChooseOption(string chosenOptionText)
        {
            _history.Add(new ChatMessage("user", chosenOptionText));
            _turnIndex++;
            return await RequestNextTurn();
        }

        private async Task<DynamicTurnResult> RequestNextTurn()
        {
            IsBusy = true;
            try
            {
                if (config == null)
                    throw new System.Exception("'config' não atribuído no Inspector.");
                if (_client == null)
                    throw new System.Exception(
                        "cliente não inicializado (Awake falhou - provavelmente chave de API ausente ou config nula).");

                string systemPrompt = BuildSystemPrompt();
                _lastLatencyMs = -1f;
                LogRequest(systemPrompt);
                DynamicTurnResult result = await _client.GenerateTurnAsync(systemPrompt, _history);

                _consecutiveFailures = 0;
                _history.Add(new ChatMessage("assistant", result.fala));
                LogResponse(result);
                return result;
            }
            catch (System.Exception ex)
            {
                // Contingencia do TCC (4.7.3): falha de integracao com o motor de IA
                // nao deve travar a sessao de teste - cai pro cache local.
                Debug.LogWarning($"[DynamicNPCController] Falha ao gerar turno, usando fallback. {ex.Message}");

                _consecutiveFailures++;
                bool forceClose = _consecutiveFailures >= 2; // 2a falha seguida: encerra educadamente

                if (fallbackPool == null)
                {
                    Debug.LogError("[DynamicNPCController] fallbackPool nao atribuido - sem cache local para contingencia.");
                    throw;
                }

                DynamicTurnResult fallback = fallbackPool.BuildFallbackTurn(forceClose);
                _history.Add(new ChatMessage("assistant", fallback.fala));
                SessionLogger.Log("ia_falha_fallback",
                    ("npc", SessionLogger.NomeNpc(this)),
                    ("turno", _turnIndex + 1),
                    ("erro", ex.Message),
                    ("resposta_bruta", _client?.LastRawContent),
                    ("latencia_ms", _lastLatencyMs),
                    ("fala_fallback", fallback.fala),
                    ("encerramento_forcado", forceClose));
                return fallback;
            }
            finally
            {
                IsBusy = false;
            }
        }

        // Log de sessao (RNF09): prompt completo, historico e as cartas que o
        // modelo recebeu neste turno. Ver SessionLogger.
        private void LogRequest(string systemPrompt)
        {
            var historico = new List<string>();
            foreach (ChatMessage m in _history)
                historico.Add($"{m.role}: {m.content}");

            var inventory = GetComponent<LocalInventory>();
            SessionLogger.Log("ia_requisicao",
                ("npc", SessionLogger.NomeNpc(this)),
                ("turno", _turnIndex + 1),
                ("cartas_do_npc", SessionLogger.Cartas(inventory != null ? inventory.GetAllClues() : null)),
                ("cartas_no_prompt", CollectClueCards().ToArray()),
                ("historico", historico.ToArray()),
                ("prompt_sistema", systemPrompt));
        }

        private void LogResponse(DynamicTurnResult result)
        {
            SessionLogger.Log("ia_resposta",
                ("npc", SessionLogger.NomeNpc(this)),
                ("turno", _turnIndex + 1),
                ("latencia_ms", _lastLatencyMs),
                ("fala", result.fala),
                ("opcoes", result.opcoes),
                ("encerrar", result.encerrar),
                ("revelar_pista", result.revelar_pista ?? ""),
                ("resposta_bruta", _client.LastRawContent));
        }

        // Junta as cartas REAIS sorteadas para este NPC na partida (lidas do
        // LocalInventory no mesmo GameObject) com as cartas escritas a mao no
        // Inspector (usadas em teste).
        //
        // Isto substitui o antigo InventoryNotifier, que "avisava" as cartas ao
        // NPC por uma mensagem de chat da Convai - necessario porque a Convai
        // guardava o estado da conversa no servidor. Aqui as cartas entram no
        // prompt a cada requisicao, entao nao ha nada a avisar de antemao.
        private List<string> CollectClueCards()
        {
            var cards = new List<string>(clueCardNames);

            var inventory = GetComponent<LocalInventory>();
            if (inventory != null)
            {
                foreach (Clue clue in inventory.GetAllClues())
                {
                    if (clue != null && !string.IsNullOrEmpty(clue.evidenceName))
                        cards.Add(clue.evidenceName);
                }
            }

            return cards;
        }

        private string BuildSystemPrompt()
        {
            var sb = new StringBuilder();

            // Enquadramento do papel. Sem isto o modelo assume o papel de
            // NARRADOR do jogo: descreve a cena em terceira pessoa e propoe
            // ACOES do jogador ("Observar a Senhorita Vermelho") em vez de
            // FALAS do detetive. Isso nao pode depender de o autor lembrar de
            // escrever essas regras em cada persona.
            sb.AppendLine("Voce interpreta UM personagem sendo interrogado por um detetive em um " +
                           "jogo de misterio. Regras invioláveis do seu papel:");
            sb.AppendLine("- Fale SEMPRE em primeira pessoa, como o personagem. Voce nao e narrador.");
            sb.AppendLine("- Nunca descreva a cena, o ambiente, nem as acoes de outras pessoas em " +
                           "terceira pessoa. Voce so diz o que o SEU personagem fala em voz alta.");
            sb.AppendLine("- As opcoes que voce devolve sao FALAS DO DETETIVE dirigidas a voce " +
                           "(perguntas ou afirmacoes que ele diz), nunca acoes do jogador e nunca " +
                           "falas suas.");
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(config.sharedSceneContext))
            {
                sb.AppendLine(config.sharedSceneContext);
                sb.AppendLine();
            }

            // Nome do personagem: sem isto o modelo nao sabe quem esta
            // interpretando quando a persona ainda nao foi escrita.
            var identity = GetComponent<DynamicDialogueSource>();
            string characterName = identity != null && !string.IsNullOrWhiteSpace(identity.speakerName)
                ? identity.speakerName
                : gameObject.name;
            sb.AppendLine($"Voce e: {characterName}.");
            sb.AppendLine();

            if (string.IsNullOrWhiteSpace(personaBackstory))
            {
                Debug.LogWarning($"[DynamicNPCController] '{gameObject.name}' está sem " +
                                 "'personaBackstory' - o modelo vai inventar uma personalidade " +
                                 "genérica, diferente da dos NPCs roteirizados.");
            }
            else
            {
                sb.AppendLine("Seu personagem:");
                sb.AppendLine(personaBackstory);
                sb.AppendLine();
            }

            sb.AppendLine("Cartas que voce possui (pode usar para responder com coerencia, " +
                           "mas nao revele diretamente a menos que apropriado):");
            foreach (string clue in CollectClueCards())
                sb.AppendLine($"- {clue}");
            sb.AppendLine();
            sb.AppendLine($"Regras de formato OBRIGATORIAS:");
            sb.AppendLine($"- Responda em ate {config.maxChars} caracteres.");
            sb.AppendLine($"- Ofereca exatamente {config.optionCount} opcoes de resposta para o jogador.");
            sb.AppendLine($"- Turno atual: {_turnIndex + 1} de no maximo {config.maxTurns}.");
            sb.AppendLine(_turnIndex + 1 >= config.maxTurns
                ? "- Este e o ultimo turno: conclua o assunto e defina \"encerrar\": true."
                : "- Defina \"encerrar\": false, a menos que o assunto se esgote naturalmente.");
            sb.AppendLine("- Nunca saia do personagem, nunca mencione que voce e uma IA.");
            sb.AppendLine("- Revele uma pista completa APENAS se o jogador perguntar diretamente sobre " +
                           "ela pela segunda vez, ou insistir explicitamente. Na primeira mencao, de so " +
                           "um indicio parcial ou desvie - siga o traco de personalidade definido acima.");
            sb.AppendLine("- Varie a estrutura das suas falas entre turnos. Nao repita o mesmo formato " +
                           "de frase (ex: \"quer que eu fale de X ou Y?\") em turnos consecutivos.");
            sb.AppendLine("- Campo \"revelar_pista\": use \"\" (vazio) na maioria dos turnos. Preencha " +
                           "com \"suspeito\", \"arma do crime\" ou \"local\" APENAS quando voce decidir " +
                           "finalmente entregar ao detetive uma carta daquele tipo que voce possui - " +
                           "ou seja, depois que ele insistiu e voce cedeu. Ao preencher, sua fala deve " +
                           "acompanhar a entrega (voce esta mostrando algo a ele). Nao preencha com um " +
                           "tipo de carta que voce nao possui na lista acima.");

            return sb.ToString();
        }
    }
}
