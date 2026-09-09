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

        [Tooltip("Cartas que este NPC possui, injetadas no contexto do prompt")]
        public List<string> clueCardNames = new List<string>();

        private AzureOpenAIDialogueClient _client;
        private readonly List<ChatMessage> _history = new List<ChatMessage>();
        private int _turnIndex;
        private int _consecutiveFailures;

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
                DynamicTurnResult result = await _client.GenerateTurnAsync(systemPrompt, _history);

                _consecutiveFailures = 0;
                _history.Add(new ChatMessage("assistant", result.fala));
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
                return fallback;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private string BuildSystemPrompt()
        {
            var sb = new StringBuilder();
            sb.AppendLine(personaBackstory);
            sb.AppendLine();
            sb.AppendLine("Cartas que voce possui (pode usar para responder com coerencia, " +
                           "mas nao revele diretamente a menos que apropriado):");
            foreach (string clue in clueCardNames)
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

            return sb.ToString();
        }
    }
}
