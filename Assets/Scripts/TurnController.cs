using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Convai.Scripts.Runtime.Core;

public class TurnController : MonoBehaviour
{
    [Header("Lista de NPCs no jogo")]
    public List<NPCAI> npcs; // Lista de NPCs que jogam
    [Header("Indice de turno atual")]
    public int currentTurnIndex = 0; // O índice do jogador atual (0 será o jogador humano)
    [Header("É o turno do jogador?")]
    public bool isPlayerTurn = true; // Define se é o turno do jogador
    private InterrogationController interrogationController; // Referência ao InterrogationController
    private SuggestionSystem suggestionSystem;

    [Header("Panels para indicar turnos")]
    public GameObject playerSuggestionResultPanel; // UI para indicar o turno do jogador
    public GameObject turnResultPanel; // UI para indicar o turno dos NPCs

    // Resumo da rodada dos NPCs: uma linha por personagem (quem palpitou, o
    // palpite, quem mostrou carta). Antes cada NPC abria o próprio painel e
    // exigia um clique - 6 cliques por rodada, cerca de 60 numa partida com
    // a regra de uma carta por personagem (diário, item 38).
    public struct NPCTurnRow
    {
        public string autor;
        public string acao;
        public string resultado;
    }
    private readonly List<NPCTurnRow> roundRows = new List<NPCTurnRow>();
    private bool waitingForPlayerCard;
    private bool gameOver;

    void Awake()
    {
        interrogationController = GetComponent<InterrogationController>();
        suggestionSystem = GetComponent<SuggestionSystem>();
    }

    void Start()
    {
        StartPlayerTurn(); // Começar com o turno do jogador
    }

    // Função para iniciar o turno do jogador
    public void StartPlayerTurn()
    {
        isPlayerTurn = true;
        currentTurnIndex = 0;
        turnResultPanel.SetActive(false);
        // Os turnos passam a câmera por cada NPC; volta para o personagem
        // cuja conversa está aberta antes de reexibi-la.
        interrogationController.RestoreConversationView();
        interrogationController.ResumeNPCDialog(interrogationController.GetCurrentIndex());
    }

    // Função para finalizar o turno do jogador e passar para os NPCs
    public void EndPlayerTurn()
    {
        if (gameOver)
            return;

        isPlayerTurn = false;
        interrogationController.CloseNPCDialog();
        roundRows.Clear();
        currentTurnIndex = 0;
        RunNPCTurns();
    }

    // Roda os turnos dos NPCs em sequência, no mesmo quadro. Para quando um
    // NPC precisa que o jogador mostre uma carta (retoma em
    // ResumeAfterPlayerCard) ou quando alguém vence.
    void RunNPCTurns()
    {
        while (currentTurnIndex < npcs.Count)
        {
            if (gameOver)
                return;

            interrogationController.SetNPCByIndex(currentTurnIndex, true); // câmera em quem está jogando
            waitingForPlayerCard = false;
            npcs[currentTurnIndex].PlayTurn();

            if (gameOver || waitingForPlayerCard)
                return;
            currentTurnIndex++;
        }

        ShowRoundSummary();
    }

    // Chamados durante o turno de um NPC (SuggestionSystem, FinalAccusation, NPCAI).
    public void ReportNPCTurn(string autor, string acao, string resultado)
    {
        roundRows.Add(new NPCTurnRow { autor = autor, acao = acao, resultado = resultado });
    }

    public void WaitForPlayerCard() => waitingForPlayerCard = true;

    public void ResumeAfterPlayerCard()
    {
        waitingForPlayerCard = false;
        currentTurnIndex++;
        RunNPCTurns();
    }

    // Fim de jogo (acusação final de alguém): interrompe a rodada.
    public void EndGame() => gameOver = true;

    void ShowRoundSummary()
    {
        currentTurnIndex = 0;
        if (roundRows.Count == 0)
        {
            StartPlayerTurn();
            return;
        }

        Detective.Dialogue.SessionLogger.Log("rodada_npcs",
            ("linhas", roundRows.ConvertAll(r => $"{r.autor} | {r.acao} | {r.resultado}").ToArray()));
        suggestionSystem.ShowRoundSummary(roundRows);
    }

    // Botão "Continuar..." dos dois painéis de resultado:
    // - resultado do palpite do jogador -> passa a vez aos NPCs;
    // - resumo da rodada dos NPCs -> volta a vez ao jogador.
    public void OnNextTurnButtonPressed()
    {
        playerSuggestionResultPanel.SetActive(false);
        if (isPlayerTurn)
        {
            Debug.Log("Encerrando turno do jogador");
            EndPlayerTurn();
        }
        else
        {
            Debug.Log("Encerrando a rodada dos NPCs");
            turnResultPanel.SetActive(false);
            StartPlayerTurn();
        }
    }
}
