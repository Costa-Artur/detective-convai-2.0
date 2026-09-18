using System;
using UnityEngine;

namespace Detective.Dialogue
{
    // Adaptador que faz o NPC dinamico (modelo de linguagem na Azure) alimentar
    // a interface unificada, no mesmo formato que o adaptador do Yarn.
    //
    // Toda a conversa em si acontece no DynamicNPCController; aqui so ha a
    // traducao de DynamicTurnResult -> DialogueTurn e a ponte entre o mundo
    // async (chamada de rede) e o mundo de callbacks da interface.
    [RequireComponent(typeof(DynamicNPCController))]
    public class DynamicDialogueSource : MonoBehaviour, IDialogueSource
    {
        [Tooltip("Nome exibido do personagem na interface (igual ao dos NPCs roteirizados).")]
        public string speakerName;

        public string SpeakerName => speakerName;

        // O NPC dinamico ja demora naturalmente o tempo da chamada de rede -
        // aplicar atraso artificial aqui o deixaria mais lento que os outros,
        // que e exatamente o oposto do que se quer.
        public bool NeedsArtificialDelay => false;

        private DynamicNPCController _controller;
        private Action<DialogueTurn> _onTurnReady;
        private string[] _currentOptions = Array.Empty<string>();

        private void Awake()
        {
            _controller = GetComponent<DynamicNPCController>();
        }

        public async void Begin(Action<DialogueTurn> onTurnReady)
        {
            _onTurnReady = onTurnReady;

            // async void: uma excecao aqui se perderia silenciosamente e a
            // interface ficaria travada em "digitando..." para sempre.
            try
            {
                DynamicTurnResult result = await _controller.StartConversation();
                EmitTurn(result);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DynamicDialogueSource] Falha ao iniciar conversa: {ex.Message}");
                EmitFailureTurn();
            }
        }

        public async void Choose(int optionIndex)
        {
            if (optionIndex < 0 || optionIndex >= _currentOptions.Length)
            {
                Debug.LogWarning($"[DynamicDialogueSource] Índice de opção inválido: {optionIndex}.");
                return;
            }

            // O modelo recebe de volta o TEXTO da opcao escolhida (nao o
            // indice) - e assim que ele sabe o que o jogador "disse".
            string chosenText = _currentOptions[optionIndex];

            try
            {
                DynamicTurnResult result = await _controller.ChooseOption(chosenText);
                EmitTurn(result);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DynamicDialogueSource] Falha ao avançar conversa: {ex.Message}");
                EmitFailureTurn();
            }
        }

        // Aciona o mesmo painel que os NPCs roteirizados usam pelo <<reveal>>.
        // O LocalInventory ja sabe escolher uma carta do tipo pedido e exibi-la;
        // se o NPC nao tiver carta daquele tipo, ele proprio informa isso.
        private static readonly string[] TiposValidos = { "suspeito", "arma do crime", "local" };

        private Clue RevealClue(string clueType)
        {
            var inventory = GetComponent<LocalInventory>();
            if (inventory == null)
            {
                Debug.LogWarning($"[DynamicDialogueSource] '{gameObject.name}' pediu para revelar " +
                                 $"'{clueType}', mas não tem LocalInventory.");
                LogRevealRequest(clueType, "sem_inventario", null);
                return null;
            }

            // O JSON Schema garante que 'revelar_pista' e uma string, mas nao
            // que o VALOR faz sentido. Sem validar aqui, um valor invalido cai
            // no fallback do LocalInventory, que revela uma carta ALEATORIA de
            // qualquer tipo - entregando ao jogador uma carta que o personagem
            // nunca pretendeu mostrar, e desequilibrando a partida.
            string tipo = clueType.ToLowerInvariant();

            if (System.Array.IndexOf(TiposValidos, tipo) < 0)
            {
                Debug.LogWarning($"[DynamicDialogueSource] {speakerName} pediu revelar_pista=" +
                                 $"'{clueType}', que não é um tipo válido " +
                                 $"({string.Join(", ", TiposValidos)}). Revelação ignorada.");
                LogRevealRequest(clueType, "ignorada_tipo_invalido", inventory);
                return null;
            }

            if (!inventory.GetAllClues().Exists(c => c != null && c.type == tipo))
            {
                Debug.LogWarning($"[DynamicDialogueSource] {speakerName} tentou revelar uma carta do " +
                                 $"tipo '{tipo}', mas não possui nenhuma. Revelação ignorada.");
                LogRevealRequest(clueType, "ignorada_npc_sem_carta_do_tipo", inventory);
                return null;
            }

            Debug.Log($"[DynamicDialogueSource] {speakerName} revela carta do tipo '{tipo}'.");
            LogRevealRequest(clueType, "executada", inventory);
            return inventory.RevealCardOfType(tipo, "dinamico");
        }

        private void LogRevealRequest(string clueType, string resultado, LocalInventory inventory)
        {
            SessionLogger.Log("ia_revelacao_pedida",
                ("npc", SessionLogger.NomeNpc(this)),
                ("revelar_pista", clueType),
                ("resultado", resultado),
                ("cartas_do_npc", SessionLogger.Cartas(inventory != null ? inventory.GetAllClues() : null)));
        }

        // Ultimo recurso: o DynamicNPCController ja tem o cache local de
        // contingencia (FallbackDialoguePool), entao chegar aqui significa que
        // ate ele falhou. Encerra a conversa de forma limpa em vez de deixar a
        // interface presa em "digitando...".
        private void EmitFailureTurn()
        {
            _onTurnReady?.Invoke(new DialogueTurn
            {
                speakerName = speakerName,
                line = "Prefiro não falar mais sobre isso agora.",
                options = Array.Empty<string>(),
                isEnd = true
            });
        }

        public void End()
        {
            _onTurnReady = null;
            _currentOptions = Array.Empty<string>();
        }

        private void EmitTurn(DynamicTurnResult result)
        {
            _currentOptions = result.opcoes ?? Array.Empty<string>();

            // Equivalente ao <<reveal>> dos NPCs roteirizados: abre o painel
            // "Pista Revelada" com uma carta do tipo pedido. Sem isto o NPC
            // dinamico so falaria SOBRE as cartas, sem nunca entrega-las -
            // seria inutil na investigacao, e isso o denunciaria.
            Clue revealed = null;
            if (!string.IsNullOrWhiteSpace(result.revelar_pista))
                revealed = RevealClue(result.revelar_pista.Trim());

            var inventory = GetComponent<LocalInventory>();
            SessionLogger.VerificarCartasNaFala(SessionLogger.NomeNpc(this), result.fala,
                result.revelar_pista, inventory != null ? inventory.GetAllClues() : null, revealed);

            _onTurnReady?.Invoke(new DialogueTurn
            {
                speakerName = speakerName,
                line = result.fala,
                options = result.encerrar ? Array.Empty<string>() : _currentOptions,
                isEnd = result.encerrar
            });
        }
    }
}
