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

    [Header("Panels para indicar turnos")]
    public GameObject playerSuggestionResultPanel; // UI para indicar o turno do jogador
    public GameObject turnResultPanel; // UI para indicar o turno dos NPCs

    void Awake()
    {
        interrogationController = GetComponent<InterrogationController>();
    }

    void Start()
    {
        StartPlayerTurn(); // Começar com o turno do jogador
    }

    // Função para iniciar o turno do jogador
    public void StartPlayerTurn()
    {
        isPlayerTurn = true;
        turnResultPanel.SetActive(false);
        interrogationController.ResumeNPCDialog(interrogationController.GetCurrentIndex());
    }

    // Função para finalizar o turno do jogador e passar para o NPC
    public void EndPlayerTurn()
    {
        isPlayerTurn = false;
        //Estava comentado antes
        interrogationController.CloseNPCDialog();
        StartNPCTurn(); // Chama o turno do primeiro NPC
    }

    // Função para iniciar o turno do próximo NPC
    public void StartNPCTurn()
    {
        if (currentTurnIndex < npcs.Count)
        {
            NPCAI currentNPC = npcs[currentTurnIndex];
            //Estava comentado antes
            interrogationController.SetNPCByIndex(currentTurnIndex, true); // Atualiza o NPC no InterrogationController
            PlayNPCTurn(currentNPC); // Inicia o turno do NPC
        }
        else
        {
            currentTurnIndex = 0; // Todos os NPCs jogaram, volta para o jogador
            StartPlayerTurn();
        }
    }

    // Função que simula o turno do NPC
    void PlayNPCTurn(NPCAI npc)
    {
        // Chama a função para o NPC fazer um palpite (ou outra ação no turno dele)
        npc.PlayTurn();
    }

    // Função para finalizar o turno do NPC e passar para o próximo
    public void EndNPCTurn()
    {
        //Estava comentado antes
        interrogationController.CloseNPCDialog(); // Fecha o diálogo do NPC atual

        currentTurnIndex++; // Avança para o próximo NPC

        if (currentTurnIndex < npcs.Count)
        {
            // Chama o próximo NPC
            StartNPCTurn();
        }
        else
        {
            // Todos os NPCs jogaram, volta para o jogador
            currentTurnIndex = 0;
            StartPlayerTurn();
        }
    }

    // Função chamada pelo botão "Continuar" para avançar o turno após o jogador terminar
    public void OnNextTurnButtonPressed()
    {
        if (isPlayerTurn)
        {
            Debug.Log("Encerrando turno do jogador");
            playerSuggestionResultPanel.SetActive(false);
            EndPlayerTurn(); // Finaliza o turno do jogador e passa para o NPC
        }
        else
        {
            Debug.Log("Encerrando turno do NPC, ID: " + currentTurnIndex);
            playerSuggestionResultPanel.SetActive(false);
            EndNPCTurn(); // Finaliza o turno do NPC e passa para o próximo
        }
    }
}
