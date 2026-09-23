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
        // aplicar atraso artificial aqui o deixaria mais lento que os outros.
        // A excecao e o turno repetido do cache (conversa revisitada): ele nao
        // passa pela rede e apareceria instantaneamente, entao recebe o mesmo
        // atraso calibrado dos roteirizados.
        public bool NeedsArtificialDelay => _lastTurnFromCache;
        private bool _lastTurnFromCache;

        private DynamicNPCController _controller;
        private Action<DialogueTurn> _onTurnReady;
        private string[] _currentOptions = Array.Empty<string>();
        private string[] _currentRoles = Array.Empty<string>();

        // Identifica a conversa atual. Se o jogador troca de NPC enquanto a
        // chamada de rede esta em andamento, a resposta que chega depois e de
        // uma conversa abandonada e precisa ser descartada - senao ela aparece
        // na tela por cima da conversa nova.
        private int _generation;

        private void Awake()
        {
            _controller = GetComponent<DynamicNPCController>();
        }

        public async void Begin(Action<DialogueTurn> onTurnReady)
        {
            _onTurnReady = onTurnReady;
            int generation = ++_generation;

            // async void: uma excecao aqui se perderia silenciosamente e a
            // interface ficaria travada em "digitando..." para sempre.
            try
            {
                DynamicTurnResult result = await _controller.StartConversation();
                if (Outdated(generation)) return;
                EmitTurn(result);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DynamicDialogueSource] Falha ao iniciar conversa: {ex.Message}");
                if (!Outdated(generation))
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
            // O papel da opcao (pedir a carta, sair...) define o roteiro do
            // turno seguinte - ver DynamicNPCController.
            string chosenText = _currentOptions[optionIndex];
            string chosenRole = optionIndex < _currentRoles.Length ? _currentRoles[optionIndex] : null;

            int generation = _generation;

            try
            {
                DynamicTurnResult result = await _controller.ChooseOption(chosenText, chosenRole);
                if (Outdated(generation)) return;
                EmitTurn(result);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DynamicDialogueSource] Falha ao avançar conversa: {ex.Message}");
                if (!Outdated(generation))
                    EmitFailureTurn();
            }
        }

        // Aciona o mesmo painel que os NPCs roteirizados usam pelo <<reveal>>,
        // com a carta ESPECIFICA escolhida pelo modelo (pelo nome). O schema
        // ja restringe o valor as cartas do NPC; a checagem aqui cobre as
        // cartas extras de teste, que nao existem no inventario.
        private Clue RevealClue(string cardName)
        {
            var inventory = GetComponent<LocalInventory>();
            if (inventory == null)
            {
                Debug.LogWarning($"[DynamicDialogueSource] '{gameObject.name}' pediu para revelar " +
                                 $"'{cardName}', mas não tem LocalInventory.");
                LogRevealRequest(cardName, "sem_inventario", null);
                return null;
            }

            Clue revealed = inventory.RevealSpecificCard(cardName, "dinamico");
            if (revealed == null)
            {
                Debug.LogWarning($"[DynamicDialogueSource] {speakerName} pediu revelar '{cardName}', " +
                                 "que não está no inventário. Revelação ignorada.");
                LogRevealRequest(cardName, "ignorada_carta_fora_do_inventario", inventory);
                return null;
            }

            LogRevealRequest(cardName, "executada", inventory);
            return revealed;
        }

        private void LogRevealRequest(string cardName, string resultado, LocalInventory inventory)
        {
            SessionLogger.Log("ia_revelacao_pedida",
                ("npc", SessionLogger.NomeNpc(this)),
                ("revelar_pista", cardName),
                ("resultado", resultado),
                ("cartas_do_npc", SessionLogger.Cartas(inventory != null ? inventory.GetAllClues() : null)));
        }

        // Ultimo recurso: o DynamicNPCController ja tem o cache local de
        // contingencia (FallbackDialoguePool), entao chegar aqui significa que
        // ate ele falhou. Encerra a conversa de forma limpa em vez de deixar a
        // interface presa em "digitando...".
        private void EmitFailureTurn()
        {
            _lastTurnFromCache = false;
            _onTurnReady?.Invoke(new DialogueTurn
            {
                speakerName = speakerName,
                line = "Prefiro não falar mais sobre isso agora.",
                options = Array.Empty<string>(),
                isEnd = true
            });
        }

        private bool Outdated(int generation)
        {
            if (generation == _generation)
                return false;

            SessionLogger.Log("ia_turno_descartado",
                ("npc", SessionLogger.NomeNpc(this)),
                ("motivo", "a conversa foi trocada enquanto a resposta vinha"));
            return true;
        }

        public void End()
        {
            _generation++;
            _onTurnReady = null;
            _currentOptions = Array.Empty<string>();
            _currentRoles = Array.Empty<string>();
        }

        private void EmitTurn(DynamicTurnResult result)
        {
            _lastTurnFromCache = _controller != null && _controller.LastTurnFromCache;
            _currentOptions = result.opcoes ?? Array.Empty<string>();
            _currentRoles = result.papeis_opcoes ?? Array.Empty<string>();

            // Equivalente ao <<reveal>> dos NPCs roteirizados: abre o painel
            // "Pista Revelada" com uma carta do tipo pedido. Sem isto o NPC
            // dinamico so falaria SOBRE as cartas, sem nunca entrega-las -
            // seria inutil na investigacao, e isso o denunciaria.
            Clue revealed = null;
            if (!string.IsNullOrWhiteSpace(result.revelar_pista))
                revealed = RevealClue(result.revelar_pista.Trim());

            // Cartas citadas na persona (os comodos do alibi, por exemplo) nao
            // contam como citacao de carta.
            var inventory = GetComponent<LocalInventory>();
            SessionLogger.VerificarCartasNaFala(SessionLogger.NomeNpc(this), result.fala,
                result.revelar_pista, inventory != null ? inventory.GetAllClues() : null, revealed,
                _controller != null ? _controller.personaBackstory : null);

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
