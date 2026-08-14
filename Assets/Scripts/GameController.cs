using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameController : MonoBehaviour
{
    public List<Clue> deck = new List<Clue>(); // Lista original de pistas do jogo.
    private List<Clue> deckCopy; // Cópia do deck para referência no NPCAI.
    public Transform npcParent; // Objeto "NPCs" que contém todos os NPCs
    public LocalInventory playerInventory; // Inventário do jogador
    private List<LocalInventory> npcInventories = new List<LocalInventory>(); // Lista de inventários dos NPCs
    public List<Clue> crimeEnvelope = new List<Clue>(); // Envelope de crime

    [Header("Configurações de Notificação")]
    [SerializeField]
    private bool NotifyNpcsOfInventory = true; // Mutar jogador durante notificação
    
    // Start is called before the first frame update
    void Start()
    {
        // Carrega todos os NPCs da cena
        // Inicializa o deck no NPCAI usando o deck completo
        foreach (Transform npc in npcParent)
        {
            LocalInventory inventory = npc.GetComponent<LocalInventory>();
            if (inventory != null)
            {
                npcInventories.Add(inventory);
            }
            
            NPCAI npcAI = npc.GetComponent<NPCAI>();
            if (npcAI != null)
            {
                npcAI.InitializeClues(deck); // Uma função que você pode adicionar ao NPCAI para carregar as pistas possíveis
            }
        }

        // Embaralha o deck de pistas
        ShuffleDeck();

        // Faz uma cópia do deck original para ser usada no envelope e na distribuição
        deckCopy = new List<Clue>(deck);

        // Separar cartas para o envelope de crime
        PrepareCrimeEnvelope();

        // Distribui as pistas restantes para os NPCs e o jogador
        DistributeClues();

        //Informa NPCs do Inventário
        if(NotifyNpcsOfInventory)
        {
            GetComponent<InventoryNotifier>().NotifyNPCsOfInventory();
        }
    }

    void ShuffleDeck()
    {
        // Método para embaralhar o deck
        for (int i = 0; i < deck.Count; i++)
        {
            Clue temp = deck[i];
            int randomIndex = UnityEngine.Random.Range(0, deck.Count);
            deck[i] = deck[randomIndex];
            deck[randomIndex] = temp;
        }
    }
    
    void PrepareCrimeEnvelope()
    {
        // Separar uma carta de cada tipo (suspeitos, armas, locais) para o envelope de crime
        Clue suspect = GetRandomClueByType(deckCopy, "suspeito");
        Clue weapon = GetRandomClueByType(deckCopy, "arma do crime");
        Clue location = GetRandomClueByType(deckCopy, "local");

        crimeEnvelope.Add(suspect);
        crimeEnvelope.Add(weapon);
        crimeEnvelope.Add(location);

        // Remova do deck apenas para fins de distribuição
        deckCopy.Remove(suspect);
        deckCopy.Remove(weapon);
        deckCopy.Remove(location);
    }

     // Modifique o GetRandomClueByType para aceitar a lista que deseja buscar, evitando impacto no deck original
    Clue GetRandomClueByType(List<Clue> clues, string type)
    {
        List<Clue> cluesOfType = clues.FindAll(clue => clue.type == type);
        return cluesOfType[UnityEngine.Random.Range(0, cluesOfType.Count)];
    }

    void DistributeClues()
    {
        int currentClueIndex = 0;
        int numPlayers = npcInventories.Count + 1; // Número de NPCs + 1 jogador
        int cluesPerPlayer = deckCopy.Count / numPlayers; // Cartas distribuídas igualmente
        
        // Distribuir pistas para o jogador
        for (int i = 0; i < cluesPerPlayer; i++)
        {
            if (currentClueIndex < deck.Count)
            {
                playerInventory.Add(deckCopy[currentClueIndex]);
                currentClueIndex++;
            }
        }

        // Distribuir pistas para os NPCs
        while (currentClueIndex < deckCopy.Count)
        {
            foreach (LocalInventory npcInventory in npcInventories)
            {
                if (currentClueIndex < deckCopy.Count)
                {
                    npcInventory.Add(deckCopy[currentClueIndex]);
                    currentClueIndex++;
                }
                else
                {
                    break;
                }
            }
        }
    }

    // Verifica se a acusação está correta
    public bool IsAccusationCorrect(List<Clue> accusation)
    {
        int correctCount = 0;
        foreach (Clue clue in accusation)
        {
            if (crimeEnvelope.Contains(clue))
            {
                correctCount++;
            }
        }

        return correctCount == 3; // Acusação correta se todas as 3 pistas corresponderem
    }

    // Função para obter uma pista pelo nome
    public Clue GetClueByName(string name)
    {
        foreach (Clue clue in deck)
        {
            if (clue.evidenceName == name)
            {
                return clue;
            }
        }
        return null;
    }

    public List<Clue> GetAllPersons()
    {
        return deck.FindAll(clue => clue.type == "suspeito");
    }

    public List<Clue> GetAllWeapons()
    {
        return deck.FindAll(clue => clue.type == "arma do crime");
    }

    public List<Clue> GetAllLocations()
    {
        return deck.FindAll(clue => clue.type == "local");
    }

    public void returnMainMenu()
    {
        SceneManager.LoadScene("MainMenu");
    }


}
