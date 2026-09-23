using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using Convai.Scripts.Runtime.Core;
using Unity.VisualScripting;
using UnityEngine.UI;
using System;

public class SuggestionSystem : MonoBehaviour
{
    [Header("Inventários do jogo")]
    public List<LocalInventory> allInventories; // Inventários dos NPCs
    public LocalInventory playerInventory;
    [Header("Panel de escolha de sugestões do Palpite")]
    public GameObject suggestionPanel; // O painel de palpites
    public TMP_Dropdown personDropdown;
    public TMP_Dropdown weaponDropdown;
    public TMP_Dropdown locationDropdown;
    [Header("Panel de resultado do Palpite do Jogador")]
    public GameObject resultPanel; // Exibe a carta que o NPC vai mostrar
    public TextMeshProUGUI resultText; // Texto do resultado no painel
    [Header("Panel de escolha de resposta para Palpite do NPC")]
    public GameObject playerCardSelectionPanel;
    public TextMeshProUGUI playerCardSelectionNPCNameText;
    public TextMeshProUGUI personSuggestedevidenceName;
    public TextMeshProUGUI weaponSuggestedevidenceName;
    public TextMeshProUGUI roomSuggestedevidenceName;
    public Button confirmPersonSuggestionButton;
    public Button confirmWeaponSuggestionButton;
    public Button confirmRoomSuggestionButton;
    [Header("Panel de resultados de turno de NPC")]
    public GameObject turnResultPanel; // O painel de resultados do turno
    public TextMeshProUGUI turnResultText;
    public TextMeshProUGUI personTurnResultEvidenceName;
    public TextMeshProUGUI weaponTurnResultEvidenceName;
    public TextMeshProUGUI roomTurnResultEvidenceName;
    [Header("Outros")]
    //public ConvaiNPCManager convaiNPCManager; // Gerencia o chat
    public Clue emptyClue;
    /**************************************************************************************************/
    private GameController gameController;
    private InterrogationController interrogationController; // Controla a troca de NPCs
    private TurnController turnController;

    private List<string> npcsWithoutClues = new List<string>(); // Lista de NPCs que não possuem pistas
    private int currentNPCIndex = 0; // Variável para controlar o NPC atual

    // Temporário para armazenar as pistas correspondentes
    private List<Clue> matchingClues = new List<Clue>();
    private List<Clue> lastMatchingCluesPlayer = new List<Clue>();
    private NPCAI lastMatchedNPCPlayer;
    private string lastNPCSuggestionText; // palpite do NPC que aguarda a carta do jogador
    private bool roundSummaryLayoutReady;
    /**************************************************************************************************/

    private void Awake() {
        gameController = GetComponent<GameController>();
        interrogationController = GetComponent<InterrogationController>();
        turnController = GetComponent<TurnController>();
    }
    public void OpenSuggestionPanel()
    {
        // Oculta o chat de conversa durante o palpite
        interrogationController.CloseNPCDialog(interrogationController.GetCurrentIndex()); // Fecha o diálogo do NPC
        // Reseta o índice do NPC para começar pelo primeiro (índice 0)
        currentNPCIndex = 0;
        // Ativa o painel de sugestão
        suggestionPanel.SetActive(true);
    }

    // Função para fechar o painel de sugestão
    public void CloseSuggestionPanel()
    {
        // Restaura o chat de conversa após o palpite
        interrogationController.ResumeNPCDialog(interrogationController.GetCurrentIndex()); // Retoma o diálogo do NPC
        suggestionPanel.SetActive(false); // Oculta o painel
    }

    public void CloseResultPanel()
    {
        // Restaura o chat de conversa após o palpite
        interrogationController.ResumeNPCDialog(interrogationController.GetCurrentIndex()); // Retoma o diálogo do NPC
        resultPanel.SetActive(false); // Oculta o painel de resultado
    }

    private void ShowResultPanel(String resultTextString)
    {
        Detective.Dialogue.SessionLogger.Log("palpite_resultado", ("autor", "Jogador"), ("resultado", resultTextString));
        resultText.text = resultTextString; 
        resultPanel.SetActive(true);
    }

    // Função de palpite
    public void ConfirmSuggestion()
    {
        Clue guessedPerson = gameController.GetClueByName(personDropdown.options[personDropdown.value].text);
        Clue guessedWeapon = gameController.GetClueByName(weaponDropdown.options[weaponDropdown.value].text);
        Clue guessedLocation = gameController.GetClueByName(locationDropdown.options[locationDropdown.value].text);
        if(guessedPerson == null || guessedWeapon == null || guessedLocation == null)
        {
            Debug.LogWarning("One of the guessed Clues is null");
            if(guessedPerson == null){
                Debug.LogWarning("One of the guessed Clues is null = guessedPerson");
            }

            if(guessedWeapon == null){
                Debug.LogWarning("One of the guessed Clues is null = guessedWeapon");
            }

            if(guessedLocation == null){
                Debug.LogWarning("One of the guessed Clues is null = guessedLocation");
            }
        }

        Detective.Dialogue.SessionLogger.Log("palpite",
            ("autor", "Jogador"),
            ("suspeito", Detective.Dialogue.SessionLogger.Carta(guessedPerson)),
            ("arma", Detective.Dialogue.SessionLogger.Carta(guessedWeapon)),
            ("local", Detective.Dialogue.SessionLogger.Carta(guessedLocation)));

        if (matchingClues == null || npcsWithoutClues == null || suggestionPanel == null || interrogationController == null)
        {
            Debug.LogError("matchingClues == null || npcsWithoutClues == null || suggestionPanel == null || interrogationController == null");
        }

        matchingClues.Clear();
        npcsWithoutClues.Clear(); // Limpa a lista de NPCs sem pistas
        suggestionPanel.SetActive(false); // Oculta o painel de sugestão
        interrogationController.CloseNPCDialog(interrogationController.GetCurrentIndex()); // Fecha o diálogo do NPC


        // Reseta o índice do NPC para começar pelo primeiro
        currentNPCIndex = 0;

        // Verifica se o allInventories está vazio
        if (allInventories == null || allInventories.Count == 0)
        {
            Debug.LogWarning("allInventories está vazio ou não inicializado!");
        }

        // Percorre todos os NPCs até encontrar uma pista ou esgotar as opções
        while (currentNPCIndex < allInventories.Count - 1) //Remove inventário do Player do Count
        {
            LocalInventory npcInventory = allInventories[currentNPCIndex];

            // Ajuste para sincronizar o NPC corretamente usando o InterrogationController
            interrogationController.SetNPCByIndex(currentNPCIndex, true); 

                    // Verifica se o allInventories está vazio
            if (npcInventory == null)
            {
                Debug.LogWarning("npcInventory está vazio ou não inicializado!");
            }

            // Verifica se o NPC tem alguma das pistas do palpite
            matchingClues.Clear(); // Limpa as pistas do NPC anterior
            if (npcInventory.HasClue(guessedPerson)) matchingClues.Add(guessedPerson);
            if (npcInventory.HasClue(guessedWeapon)) matchingClues.Add(guessedWeapon);
            if (npcInventory.HasClue(guessedLocation)) matchingClues.Add(guessedLocation);

            if (matchingClues == null)
            {
                Debug.LogWarning("matchingClues está vazio ou não inicializado!");
            }


            // Se o NPC tem pelo menos uma pista
            if (matchingClues != null && matchingClues.Count > 0)
            {
                
                // Seleciona aleatoriamente uma pista para mostrar ao jogador
                Clue clueToShow = matchingClues[UnityEngine.Random.Range(0, matchingClues.Count)];

                // Anota a carta no painel "Verificar Pistas".
                PlayerCheckClues playerClues = GetComponent<PlayerCheckClues>();
                if (playerClues != null)
                    playerClues.RegisterRevealedClue(clueToShow, npcInventory.GetComponent<ConvaiNPC>().characterName);

                // Formatação do resultado
                string noClueNPCsText = npcsWithoutClues.Count > 0 
                    ? "Esses personagens não tinham pistas correspondentes: " + string.Join(", ", npcsWithoutClues) + "\n" 
                    : "";

                ShowResultPanel(noClueNPCsText + npcInventory.GetComponent<ConvaiNPC>().characterName + " lhe mostrou a pista: " + clueToShow.evidenceName);
                break; // Interrompe o loop ao encontrar um NPC com uma pista
            }
            else
            {
                if (npcsWithoutClues == null)
                {
                    Debug.LogWarning("npcsWithoutClues está vazio ou não inicializado!");
                }
                // Adiciona o nome do NPC à lista de NPCs sem pistas
                npcsWithoutClues.Add(npcInventory.GetComponent<ConvaiNPC>().characterName);
            }

            // Incrementa para o próximo NPC
            currentNPCIndex++;
        }

        // Verifica se o allInventories está vazio
        if (matchingClues == null)
        {
            Debug.LogWarning("matchingClues está vazio ou não inicializado!");
        }

        // Caso nenhum NPC tenha pistas
        if (matchingClues.Count == 0)
        {
            if (npcsWithoutClues == null)
            {
                Debug.LogWarning("npcsWithoutClues está vazio ou não inicializado!");
            }

            string noClueNPCs = string.Join(", ", npcsWithoutClues);
            ShowResultPanel("Nenhum personagem tinha uma carta correspondente.\nPerguntado a: " + noClueNPCs);
        }
    }

    public Clue NPCMakeSuggestion(NPCAI npcAI, Clue guessedPerson, Clue guessedWeapon, Clue guessedLocation)
    {
        List<Clue> matchingClues = new List<Clue>();
        npcsWithoutClues.Clear(); // Limpa a lista de NPCs sem pistas
        string autor = npcAI.GetComponent<ConvaiNPC>().characterName;

        Debug.Log(npcAI.name + " fez o palpite: " + guessedPerson.evidenceName + ", " + guessedWeapon.evidenceName + ", " + guessedLocation.evidenceName);
        Detective.Dialogue.SessionLogger.Log("palpite",
            ("autor", Detective.Dialogue.SessionLogger.NomeNpc(npcAI)),
            ("suspeito", Detective.Dialogue.SessionLogger.Carta(guessedPerson)),
            ("arma", Detective.Dialogue.SessionLogger.Carta(guessedWeapon)),
            ("local", Detective.Dialogue.SessionLogger.Carta(guessedLocation)));

        // Pergunta na ordem dos turnos, a partir de quem vem DEPOIS do autor
        // do palpite (TCC original, 3.1.2.2: só o primeiro que tiver uma carta
        // a mostra). Antes começava sempre do primeiro NPC da lista, então os
        // mesmos personagens respondiam a todos os palpites. allInventories
        // segue a ordem dos turnos, com o jogador por último.
        string palpiteTexto = $"{guessedPerson.evidenceName}, {guessedWeapon.evidenceName}, {guessedLocation.evidenceName}";
        int total = allInventories.Count;
        int inicio = allInventories.IndexOf(npcAI.npcInventory);
        for (int passo = 1; passo <= total; passo++)
        {
            LocalInventory npcInventory = allInventories[((inicio + passo) % total + total) % total];

            // Pular o NPC que está fazendo a sugestão
            if (npcInventory == npcAI.npcInventory)
                continue;

            matchingClues.Clear();
            if (npcInventory.HasClue(guessedPerson)) matchingClues.Add(guessedPerson);
            if (npcInventory.HasClue(guessedWeapon)) matchingClues.Add(guessedWeapon);
            if (npcInventory.HasClue(guessedLocation)) matchingClues.Add(guessedLocation);

            // Verifica se o NPC atual é o jogador
            if (npcInventory == playerInventory)
            {
                if (matchingClues.Count > 0)
                {
                    lastMatchedNPCPlayer = npcAI;
                    Detective.Dialogue.SessionLogger.Log("palpite_resultado",
                        ("autor", Detective.Dialogue.SessionLogger.NomeNpc(npcAI)),
                        ("resultado", "o jogador precisa mostrar uma carta"));
                    // Abre a UI para o jogador escolher qual carta mostrar; a
                    // rodada dos NPCs pausa até ele escolher.
                    lastNPCSuggestionText = palpiteTexto;
                    turnController.WaitForPlayerCard();
                    OpenPlayerCardSelectionPanel(matchingClues, guessedPerson, guessedWeapon, guessedLocation, autor);
                    return emptyClue; // Aguardar o jogador escolher uma carta
                }
                npcsWithoutClues.Add("você");
                continue;
            }

            // Se o NPC tem pelo menos uma pista
            if (matchingClues.Count > 0)
            {
                string quemMostrou = npcInventory.GetComponent<ConvaiNPC>().characterName;
                Debug.Log("Achou um NPC que tem uma pista:" + quemMostrou);
                // Seleciona aleatoriamente uma pista para mostrar ao NPC que fez o palpite
                Clue clueToShow = matchingClues[UnityEngine.Random.Range(0, matchingClues.Count)];
                Detective.Dialogue.SessionLogger.Log("palpite_resultado",
                    ("autor", Detective.Dialogue.SessionLogger.NomeNpc(npcAI)),
                    ("mostrada_por", quemMostrou),
                    ("carta", Detective.Dialogue.SessionLogger.Carta(clueToShow)));

                // Entra no resumo da rodada quem mostrou a carta (sem revelar qual foi)
                turnController.ReportNPCTurn(autor, palpiteTexto, quemMostrou + " mostrou uma carta");
                return clueToShow; //Retorna pista para NPCAI
            }

            // Adiciona o nome do NPC à lista de NPCs sem pistas
            npcsWithoutClues.Add(npcInventory.GetComponent<ConvaiNPC>().characterName);
        }

        // Ninguém refutou - o palpite mais informativo da partida, que antes
        // não abria painel nenhum e travava a sequência de turnos.
        Debug.Log("Nenhum personagem tinha uma carta correspondente.");
        Detective.Dialogue.SessionLogger.Log("palpite_resultado",
            ("autor", Detective.Dialogue.SessionLogger.NomeNpc(npcAI)),
            ("resultado", "ninguem refutou"));
        turnController.ReportNPCTurn(autor, palpiteTexto, "ninguém tinha carta");
        return emptyClue; // Retorna a Clue em branco pública
    }

    // Resumo da rodada dos NPCs no painel "Final do Turno", como tabela: as
    // três colunas do painel (que mostravam um único palpite) viram "quem
    // palpitou | palpite | quem mostrou carta", uma linha por personagem.
    // Ordem das colunas na cena, da esquerda para a direita: Room, Person,
    // Weapon (esta com o botão "Continuar...").
    public void ShowRoundSummary(List<TurnController.NPCTurnRow> rows)
    {
        PrepareRoundSummaryLayout();

        var autores = new List<string>();
        var acoes = new List<string>();
        var resultados = new List<string>();
        foreach (TurnController.NPCTurnRow row in rows)
        {
            autores.Add(row.autor);
            acoes.Add(row.acao);
            resultados.Add(row.resultado);
        }

        turnResultText.text = "Palpites dos personagens nesta rodada:";
        roomTurnResultEvidenceName.text = string.Join("\n", autores);
        personTurnResultEvidenceName.text = string.Join("\n", acoes);
        weaponTurnResultEvidenceName.text = string.Join("\n", resultados);
        turnResultPanel.SetActive(true);
    }

    // Uma vez por partida: títulos das colunas e texto em uma linha por
    // registro (sem quebra, com ajuste automático de tamanho), para as três
    // colunas ficarem alinhadas linha a linha.
    private void PrepareRoundSummaryLayout()
    {
        if (roundSummaryLayoutReady)
            return;
        roundSummaryLayoutReady = true;

        SetupSummaryColumn(roomTurnResultEvidenceName, "Quem palpitou");
        SetupSummaryColumn(personTurnResultEvidenceName, "Palpite");
        SetupSummaryColumn(weaponTurnResultEvidenceName, "Quem mostrou carta");
    }

    private static void SetupSummaryColumn(TextMeshProUGUI field, string title)
    {
        float maxSize = field.fontSize;
        field.textWrappingMode = TextWrappingModes.NoWrap;
        field.enableAutoSizing = true;
        field.fontSizeMin = 10;
        field.fontSizeMax = maxSize;

        // Título da coluna: o outro texto no mesmo grupo vertical.
        foreach (TMP_Text t in field.transform.parent.GetComponentsInChildren<TMP_Text>(true))
        {
            if (t != field && t.transform.parent == field.transform.parent)
                t.text = title;
        }
    }

    public void OpenPlayerCardSelectionPanel(List <Clue> matchingCluesPlayer, Clue guessedPerson, Clue guessedWeapon, Clue guessedLocation, String characterName)
    {
        // Oculta o chat de conversa durante o palpite
        interrogationController.CloseNPCDialog(interrogationController.GetCurrentIndex());

        turnResultPanel.SetActive(false); //new, corrigir bug de sobrepor painel
        

        // Exibe as sugestões no painel de seleção
        playerCardSelectionNPCNameText.text = "Você precisa responder ao palpite do " + characterName +", indicado uma pista que não ocorreu.";
        personSuggestedevidenceName.text = guessedPerson.evidenceName;
        weaponSuggestedevidenceName.text = guessedWeapon.evidenceName;
        roomSuggestedevidenceName.text = guessedLocation.evidenceName;

        // Limpa os botões
        confirmPersonSuggestionButton.interactable = false;
        confirmWeaponSuggestionButton.interactable = false;
        confirmRoomSuggestionButton.interactable = false;

        lastMatchingCluesPlayer = matchingCluesPlayer;

        foreach (Clue clue in matchingCluesPlayer)
        {
            Debug.Log("Evidencia: " + clue.evidenceName);
            Debug.Log("Tipo: " + clue.type);
            switch (clue.type)
            {
                case "suspeito":
                    confirmPersonSuggestionButton.interactable = true;
                    break;
                case "arma do crime":
                    confirmWeaponSuggestionButton.interactable = true;
                    break;
                case "local":
                    confirmRoomSuggestionButton.interactable = true;
                    break;
            }
        }

        playerCardSelectionPanel.SetActive(true);
    }

    public void ConfirmPlayerCardSelection(string chosenClueType)
    {
        Clue chosenClue = null;

        // Verifica se há uma pista correspondente na lista de matchingClues
        foreach (Clue clue in lastMatchingCluesPlayer)
        {
            if (clue.type == chosenClueType)
            {
                chosenClue = clue;
                break; // Saímos do loop assim que encontramos a pista correta
            }
        }

        if (chosenClue != null)
        {
            Debug.Log("Jogador escolheu a carta: " + chosenClue.evidenceName);

            // Atualiza o cluesRevealed no NPCAI que fez o palpite
            // npcAI.cluesRevealed.Add(chosenClue);

            // Desativa o painel de seleção de cartas após a escolha
            playerCardSelectionPanel.SetActive(false);
            matchingClues.Clear(); // Limpa as pistas após a seleção

            // Retorna a carta escolhida (esse valor pode ser passado de volta para a lógica de NPCMakeSuggestion)
            // Aqui, você pode continuar o processamento necessário para mostrar a carta ao NPC

            lastMatchedNPCPlayer.SeePlayerClue(chosenClue);
            Detective.Dialogue.SessionLogger.Log("palpite_resultado",
                ("autor", Detective.Dialogue.SessionLogger.NomeNpc(lastMatchedNPCPlayer)),
                ("mostrada_por", "Jogador"),
                ("carta", Detective.Dialogue.SessionLogger.Carta(chosenClue)));
            turnController.ReportNPCTurn(lastMatchedNPCPlayer.GetComponent<ConvaiNPC>().characterName,
                                         lastNPCSuggestionText, $"você mostrou {chosenClue.evidenceName}");
            // Retoma a rodada dos NPCs de onde parou (o chat só volta no
            // início do turno do jogador).
            turnController.ResumeAfterPlayerCard();
        }
        else
        {
            Debug.LogWarning("Nenhuma carta válida foi encontrada para o tipo selecionado: " + chosenClueType);
        }
    }

}
