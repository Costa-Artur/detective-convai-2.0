using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Detective.Dialogue
{
    // A interface unica de dialogo usada pelos 6 NPCs - roteirizados e dinamico.
    // E o coracao do controle experimental: se qualquer diferenca visual, de
    // ritmo ou de formato vazasse entre as duas origens, o jogador poderia
    // identificar o NPC de IA sem julgar a qualidade do dialogo, que e o que o
    // estudo quer medir. Ver docs/arquitetura-npc-dinamico.md §3 e §5.
    public class UnifiedDialogueUI : MonoBehaviour
    {
        [Header("Painel")]
        [Tooltip("Objeto raiz do painel de diálogo - ligado/desligado ao abrir e fechar a conversa.")]
        public GameObject panelRoot;

        [Header("Textos")]
        public TextMeshProUGUI speakerNameText;
        public TextMeshProUGUI lineText;

        [Tooltip("Indicador de que o personagem está formulando a resposta (ex: \"digitando...\"). " +
                 "Precisa ser idêntico para todos os NPCs.")]
        public GameObject typingIndicator;

        [Header("Opções")]
        [Tooltip("Botões de opção pré-criados na cena. A quantidade deve ser >= optionCount " +
                 "da configuração (hoje 2). Os não usados ficam ocultos.")]
        public Button[] optionButtons;

        [Tooltip("Botão mostrado quando a conversa chega ao fim, no lugar das opções.")]
        public Button endConversationButton;

        private IDialogueSource _source;
        private Coroutine _revealRoutine;
        private string[] _shownOptions = System.Array.Empty<string>();

        // Log de sessao: origem de cada fala. Fica so no arquivo - a
        // interface continua sem saber (nem mostrar) qual e qual.
        private string SourceKind =>
            _source is DynamicDialogueSource ? "dinamico" : _source is YarnDialogueSource ? "roteirizado" : "?";

        private void Awake()
        {
            // Cada botao avisa a UI qual indice foi clicado.
            for (int i = 0; i < optionButtons.Length; i++)
            {
                int index = i; // captura por valor - sem isto, todos os botoes
                               // enviariam o ultimo indice do laco
                optionButtons[i].onClick.AddListener(() => OnOptionClicked(index));
            }

            if (endConversationButton != null)
                endConversationButton.onClick.AddListener(Close);

            if (panelRoot != null)
                panelRoot.SetActive(false);
        }

        // Abre a conversa com um NPC. A UI nao sabe (nem precisa saber) se a
        // origem e um arquivo .yarn ou o modelo de linguagem.
        public void OpenConversation(IDialogueSource source)
        {
            // Encerra a conversa anterior antes de abrir outra. Sem isto a
            // origem anterior continua ligada a esta interface: os NPCs
            // roteirizados compartilham o mesmo DialogueRunner, entao a fala do
            // NPC novo chegava tambem pela origem antiga - com o NOME do NPC
            // antigo - e a tela mostrava o que chegasse por ultimo.
            if (_revealRoutine != null)
            {
                StopCoroutine(_revealRoutine);
                _revealRoutine = null;
            }
            if (_source != null)
            {
                SessionLogger.Log("conversa_encerrada", ("npc", _source.SpeakerName), ("origem", SourceKind),
                                  ("motivo", "outro NPC aberto"));
                _source.End();
            }

            _source = source;

            if (panelRoot != null)
                panelRoot.SetActive(true);

            if (speakerNameText != null)
                speakerNameText.text = source.SpeakerName;

            SessionLogger.Log("conversa_aberta",
                ("npc", source.SpeakerName),
                ("origem", SourceKind));

            ShowWaitingState();
            source.Begin(OnTurnReady);
        }

        // Esconde o painel SEM encerrar a conversa - usado quando o jogador abre
        // outro painel (palpite, cartas, acusacao). O historico do NPC dinamico
        // e a posicao na arvore do Yarn sao preservados.
        public void Hide()
        {
            if (panelRoot != null)
                panelRoot.SetActive(false);
        }

        // Volta a exibir a conversa que estava em andamento.
        public void Show()
        {
            if (_source != null && panelRoot != null)
                panelRoot.SetActive(true);
        }

        public void Close()
        {
            if (_revealRoutine != null)
            {
                StopCoroutine(_revealRoutine);
                _revealRoutine = null;
            }

            if (_source != null)
                SessionLogger.Log("conversa_encerrada", ("npc", _source.SpeakerName), ("origem", SourceKind));

            _source?.End();
            _source = null;

            if (panelRoot != null)
                panelRoot.SetActive(false);
        }

        private void OnOptionClicked(int index)
        {
            if (_source == null) return;

            SessionLogger.Log("opcao_escolhida",
                ("npc", _source.SpeakerName),
                ("origem", SourceKind),
                ("indice", index),
                ("texto", index < _shownOptions.Length ? _shownOptions[index] : "?"));

            ShowWaitingState();
            _source.Choose(index);
        }

        private void OnTurnReady(DialogueTurn turn)
        {
            if (_revealRoutine != null)
                StopCoroutine(_revealRoutine);

            // Falas pre-escritas aparecem instantaneamente; sem um atraso que
            // imite a latencia real do NPC dinamico, o "instantaneo" viraria a
            // pista para identifica-lo por eliminacao.
            float delay = 0f;
            if (_source != null && _source.NeedsArtificialDelay)
                delay = GetCalibratedDelaySeconds();

            // Mesma forma de texto para as duas origens: os .yarn antigos poem
            // aspas retas em volta das falas e o modelo as vezes devolve aspas
            // curvas nas opcoes. Diferenca de pontuacao tambem denuncia.
            turn.line = StripWrappingQuotes(turn.line);
            if (turn.options != null)
                for (int i = 0; i < turn.options.Length; i++)
                    turn.options[i] = StripWrappingQuotes(turn.options[i]);

            _shownOptions = turn.isEnd || turn.options == null ? System.Array.Empty<string>() : turn.options;
            SessionLogger.Log("turno_exibido",
                ("npc", turn.speakerName),
                ("origem", SourceKind),
                ("fala", turn.line),
                ("opcoes", _shownOptions),
                ("fim_da_conversa", turn.isEnd),
                ("atraso_artificial_s", delay),
                ("botoes_na_cena", optionButtons != null ? optionButtons.Length : 0));

            _revealRoutine = StartCoroutine(RevealAfterDelay(turn, delay));
        }

        private IEnumerator RevealAfterDelay(DialogueTurn turn, float delaySeconds)
        {
            if (delaySeconds > 0f)
                yield return new WaitForSeconds(delaySeconds);

            _revealRoutine = null;
            RenderTurn(turn);
        }

        private static readonly char[] Quotes = { '"', '\u201C', '\u201D', '\u00AB', '\u00BB', '\'' };

        private static string StripWrappingQuotes(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;
            string t = text.Trim();
            while (t.Length >= 2 && System.Array.IndexOf(Quotes, t[0]) >= 0 &&
                   System.Array.IndexOf(Quotes, t[t.Length - 1]) >= 0)
                t = t.Substring(1, t.Length - 2).Trim();
            return t;
        }

        private static float GetCalibratedDelaySeconds()
        {
            // Latencia medida na rede DESTE jogador, nesta sessao (ver
            // SessionLatencyCalibrator). Sem calibrador na cena, nao ha atraso -
            // o que e uma configuracao incorreta para os testes, entao avisa.
            if (SessionLatencyCalibrator.Instance != null)
                return SessionLatencyCalibrator.Instance.GetRandomDelaySeconds();

            Debug.LogWarning("[UnifiedDialogueUI] Sem SessionLatencyCalibrator na cena - " +
                             "falas roteirizadas aparecerão instantaneamente, o que denuncia o NPC dinâmico.");
            return 0f;
        }

        private void ShowWaitingState()
        {
            if (typingIndicator != null)
                typingIndicator.SetActive(true);

            if (lineText != null)
                lineText.text = string.Empty;

            SetOptionsVisible(0);

            if (endConversationButton != null)
                endConversationButton.gameObject.SetActive(false);
        }

        private void RenderTurn(DialogueTurn turn)
        {
            if (typingIndicator != null)
                typingIndicator.SetActive(false);

            if (speakerNameText != null)
                speakerNameText.text = turn.speakerName;

            if (lineText != null)
                lineText.text = turn.line;

            if (turn.isEnd || turn.options == null || turn.options.Length == 0)
            {
                SetOptionsVisible(0);
                if (endConversationButton != null)
                    endConversationButton.gameObject.SetActive(true);
                return;
            }

            int shown = Mathf.Min(turn.options.Length, optionButtons.Length);
            if (turn.options.Length > optionButtons.Length)
            {
                Debug.LogWarning($"[UnifiedDialogueUI] O turno trouxe {turn.options.Length} opções, " +
                                 $"mas há apenas {optionButtons.Length} botões na cena - as demais foram ocultadas.");
            }

            for (int i = 0; i < shown; i++)
            {
                var label = optionButtons[i].GetComponentInChildren<TextMeshProUGUI>();
                if (label != null)
                    label.text = turn.options[i];
            }

            SetOptionsVisible(shown);

            if (endConversationButton != null)
                endConversationButton.gameObject.SetActive(false);
        }

        private void SetOptionsVisible(int count)
        {
            for (int i = 0; i < optionButtons.Length; i++)
                optionButtons[i].gameObject.SetActive(i < count);
        }
    }
}
