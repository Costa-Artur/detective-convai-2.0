using System;
using System.Collections.Generic;
using UnityEngine;
using Yarn.Unity;

namespace Detective.Dialogue
{
    // Adaptador que faz um NPC roteirizado (Yarn Spinner) alimentar a interface
    // unificada, em vez da interface propria do Yarn.
    //
    // Como funciona: este componente e registrado como uma "view" do
    // DialogueRunner (campo dialogueViews). O Yarn empurra falas via RunLine()
    // e opcoes via RunOptions(); aqui elas sao capturadas, convertidas em
    // DialogueTurn e entregues a UnifiedDialogueUI - que nao sabe nem se
    // importa de onde vieram.
    //
    // Detalhe do fluxo do Yarn: uma fala e as opcoes chegam em chamadas
    // SEPARADAS e em sequencia (primeiro RunLine, depois RunOptions). Por isso
    // a fala fica em buffer (_pendingLine) ate as opcoes chegarem, e so entao
    // o turno completo e emitido - espelhando o formato do NPC dinamico, que
    // devolve fala e opcoes juntas.
    public class YarnDialogueSource : DialogueViewBase, IDialogueSource
    {
        [Tooltip("O DialogueRunner do Yarn Spinner na cena.")]
        public DialogueRunner dialogueRunner;

        [Tooltip("Nome do nó .yarn onde a conversa deste personagem começa (ex: SenhorVerdeInicio).")]
        public string startNodeName;

        [Tooltip("Nome exibido do personagem na interface.")]
        public string speakerName;

        public string SpeakerName => speakerName;

        // Falas do Yarn sao instantaneas (ja estao no disco), entao precisam do
        // atraso artificial para nao denunciarem o NPC dinamico por eliminacao.
        // Ver docs/arquitetura-npc-dinamico.md §5.
        public bool NeedsArtificialDelay => true;

        private Action<DialogueTurn> _onTurnReady;
        private Action<int> _onOptionSelected;
        private string _pendingLine;

        // Última fala já exibida - reaproveitada no turno final quando o nó
        // termina sem fala nova (ver DialogueComplete).
        private string _lastLine;

        public void Begin(Action<DialogueTurn> onTurnReady)
        {
            _pendingLine = null;
            _lastLine = null;
            _onOptionSelected = null;
            _started = false;

            if (dialogueRunner == null)
            {
                Debug.LogError("[YarnDialogueSource] 'dialogueRunner' não atribuído.");
                return;
            }

            // Para a conversa anterior ANTES de registrar o callback: o Stop()
            // dispara DialogueComplete, que emitiria um turno final vazio
            // nesta conversa nova.
            if (dialogueRunner.IsDialogueRunning)
                dialogueRunner.Stop();

            _onTurnReady = onTurnReady;

            if (!dialogueRunner.NodeExists(startNodeName))
            {
                Debug.LogError($"[YarnDialogueSource] Nó '{startNodeName}' não existe no projeto Yarn.");
                return;
            }

            dialogueRunner.StartDialogue(startNodeName);
        }

        public void Choose(int optionIndex)
        {
            // O Yarn identifica opcoes por um ID proprio, nao pelo indice de
            // exibicao - a traducao entre os dois e feita em RunOptions().
            if (_onOptionSelected == null)
            {
                Debug.LogWarning("[YarnDialogueSource] Escolha recebida sem opções pendentes.");
                return;
            }

            if (optionIndex < 0 || optionIndex >= _optionIds.Count)
            {
                Debug.LogWarning($"[YarnDialogueSource] Índice de opção inválido: {optionIndex}.");
                return;
            }

            Action<int> callback = _onOptionSelected;
            int yarnOptionId = _optionIds[optionIndex];

            _onOptionSelected = null;
            _optionIds.Clear();

            callback(yarnOptionId);
        }

        public void End()
        {
            _started = false;
            _onTurnReady = null;
            _onOptionSelected = null;
            _pendingLine = null;

            if (dialogueRunner != null && dialogueRunner.IsDialogueRunning)
                dialogueRunner.Stop();
        }

        // ---- Callbacks vindos do Yarn Spinner ----

        private readonly List<int> _optionIds = new List<int>();

        // true depois que a conversa iniciada por Begin() entregou algo. Um
        // DialogueComplete antes disso vem de uma conversa anterior.
        private bool _started;

        public override void RunLine(LocalizedLine dialogueLine, Action onDialogueLineFinished)
        {
            // Guarda a fala e avisa o Yarn imediatamente que "terminamos de
            // exibir", para que ele siga adiante e entregue as opcoes. A
            // exibicao de verdade acontece quando o turno completo e emitido.
            _started = true;
            _pendingLine = dialogueLine.TextWithoutCharacterName.Text;
            onDialogueLineFinished?.Invoke();
        }

        public override void RunOptions(DialogueOption[] dialogueOptions, Action<int> onOptionSelected)
        {
            _started = true;
            _onOptionSelected = onOptionSelected;
            _optionIds.Clear();

            var labels = new string[dialogueOptions.Length];
            for (int i = 0; i < dialogueOptions.Length; i++)
            {
                labels[i] = dialogueOptions[i].Line.Text.Text;
                _optionIds.Add(dialogueOptions[i].DialogueOptionID);
            }

            EmitTurn(labels, isEnd: false);
        }

        public override void DialogueComplete()
        {
            if (!_started)
                return;

            // Fim da conversa. Precisa SEMPRE emitir um turno final, mesmo sem
            // fala nova - senao a interface fica presa no "digitando..." para
            // sempre, sem nada para exibir e sem botao para sair.
            //
            // O caso sem fala nova e comum nos .yarn deste projeto: varias
            // opcoes levam so a um comando (ex: <<reveal>>) e encerram o no.
            // Nesses casos reexibimos a ultima fala, para o painel nao ficar
            // vazio enquanto o jogador clica em encerrar.
            if (_pendingLine == null)
                _pendingLine = _lastLine;

            EmitTurn(Array.Empty<string>(), isEnd: true);
        }

        public override void DismissLine(Action onDismissalComplete)
        {
            // Nao ha animacao de saida aqui - a UI unificada cuida da transicao.
            onDismissalComplete?.Invoke();
        }

        private void EmitTurn(string[] options, bool isEnd)
        {
            string line = _pendingLine ?? string.Empty;

            _onTurnReady?.Invoke(new DialogueTurn
            {
                speakerName = speakerName,
                line = line,
                options = options,
                isEnd = isEnd
            });

            if (!string.IsNullOrEmpty(line))
                _lastLine = line;

            _pendingLine = null;
        }
    }
}
